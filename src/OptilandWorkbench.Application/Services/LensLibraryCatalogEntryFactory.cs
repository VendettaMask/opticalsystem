using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Application.Services;

public static class LensLibraryCatalogEntryFactory
{
    public static string CreateStableId(string sourceId, string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        return $"{SafeName(sourceId)}-{StableSuffix(
            $"{sourceId}/{Path.GetFileName(sourceFileName)}")}";
    }

    public static LensLibraryEntryDto Create(
        string id,
        string? requestedName,
        string category,
        string sourceName,
        string sourceUrl,
        string license,
        string nativePath,
        string sourcePath,
        Optic optic,
        string? lensType = null,
        string? application = null,
        string? designOrganization = null,
        DateTimeOffset? importedAt = null,
        string? importerVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(optic);

        var wavelengths = optic.Wavelengths.Select(wavelength => wavelength.Nanometers).ToArray();
        var maximumField = optic.Fields
            .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
            .DefaultIfEmpty(0)
            .Max();
        var importedName = string.IsNullOrWhiteSpace(optic.Name)
            || optic.Name.Equals("Imported Zemax ZMX", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : optic.Name;
        var name = string.IsNullOrWhiteSpace(requestedName) ? importedName : requestedName.Trim();
        var fNumber = FiniteOrZero(optic.Paraxial.EstimateFNumber());
        var numericalAperture = NumericalAperture(optic, fNumber);
        var workingDistance = WorkingDistance(optic);

        return new LensLibraryEntryDto(
            id,
            name,
            category,
            sourceName,
            sourceUrl,
            license,
            "ZMX",
            "可用",
            null,
            FiniteOrZero(optic.Paraxial.EstimateEffectiveFocalLength()),
            fNumber,
            optic.Aperture.Kind.ToString(),
            FiniteOrZero(optic.Aperture.Value),
            FiniteOrZero(optic.SurfaceGroup.TotalTrack),
            optic.SurfaceGroup.Items.Count,
            optic.FieldDefinition.ToString(),
            FiniteOrZero(maximumField),
            optic.Fields.Count,
            optic.Wavelengths.Count,
            wavelengths.Length == 0 ? 0 : wavelengths.Min(),
            wavelengths.Length == 0 ? 0 : wavelengths.Max(),
            nativePath.Replace('\\', '/'),
            Path.GetFileName(sourcePath),
            numericalAperture.Value,
            numericalAperture.Basis,
            workingDistance.Value,
            workingDistance.Basis,
            LensElementCount(optic),
            MaximumClearAperture(optic),
            RequiredOrFallback(lensType, InferLensType(category)),
            RequiredOrFallback(application, InferApplication(category)),
            RequiredOrFallback(designOrganization, InferDesignOrganization(sourceName)),
            importedAt,
            RequiredOrFallback(importerVersion, CurrentImporterVersion()));
    }

    public static string SafeName(string value)
    {
        var normalized = new string(value
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrEmpty(normalized) ? "lens" : normalized.ToLowerInvariant();
    }

    private static string StableSuffix(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static (double Value, string Basis) NumericalAperture(Optic optic, double fNumber)
    {
        if (optic.Aperture.Kind == ApertureKind.NumericalAperture)
        {
            return (FiniteOrZero(Math.Abs(optic.Aperture.Value)), "物方定义");
        }

        if (fNumber <= 0)
        {
            return (0, "未提供");
        }

        var imageSpaceAirNa = Math.Sin(Math.Atan(1 / (2 * fNumber)));
        return (FiniteOrZero(imageSpaceAirNa), "像方空气近轴估算");
    }

    private static (double Value, string Basis) WorkingDistance(Optic optic)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.Count < 2)
        {
            return (0, "未提供");
        }

        var wavelength = PrimaryWavelength(optic);
        var physicalIndices = Enumerable.Range(1, Math.Max(0, surfaces.Count - 2))
            .Where(index => HasOpticalMaterial(surfaces[index], wavelength)
                || HasOpticalMaterial(surfaces[index - 1], wavelength))
            .ToArray();
        if (physicalIndices.Length == 0)
        {
            return (0, "未提供");
        }

        var firstPhysical = physicalIndices[0];
        if (!ObjectConjugate.IsInfinite(surfaces[0]))
        {
            var objectDistance = surfaces
                .Take(firstPhysical)
                .Sum(surface => surface.Thickness);
            if (double.IsFinite(objectDistance) && objectDistance >= 0)
            {
                return (objectDistance, "物方工作距离");
            }
        }

        var lastPhysical = physicalIndices[^1];
        var imageDistance = surfaces
            .Skip(lastPhysical)
            .Take(surfaces.Count - 1 - lastPhysical)
            .Sum(surface => surface.Thickness);
        return double.IsFinite(imageDistance) && imageDistance >= 0
            ? (imageDistance, "像方后工作距离")
            : (0, "未提供");
    }

    private static int LensElementCount(Optic optic)
    {
        var wavelength = PrimaryWavelength(optic);
        return optic.SurfaceGroup.Items
            .Skip(1)
            .SkipLast(1)
            .Count(surface => HasOpticalMaterial(surface, wavelength));
    }

    private static double MaximumClearAperture(Optic optic)
    {
        var maximumSemiDiameter = optic.SurfaceGroup.Items
            .Skip(1)
            .SkipLast(1)
            .Select(surface => surface.SemiDiameter)
            .Where(value => double.IsFinite(value) && value > 0)
            .DefaultIfEmpty(0)
            .Max();
        return 2 * maximumSemiDiameter;
    }

    private static bool HasOpticalMaterial(
        OpticalSurface surface,
        double wavelengthNanometers) =>
        surface.MaterialAfter.RefractiveIndex(wavelengthNanometers) > 1.0001;

    private static double PrimaryWavelength(Optic optic) =>
        optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)?.Nanometers
        ?? optic.Wavelengths.FirstOrDefault()?.Nanometers
        ?? 587.6;

    private static string InferLensType(string category) => category switch
    {
        "显微物镜" => "显微物镜",
        "工业镜头" => "工业成像镜头",
        _ => "未分类光学系统"
    };

    private static string InferApplication(string category) => category switch
    {
        "显微物镜" => "显微成像",
        "工业镜头" => "工业成像",
        _ => "公开研究与教学"
    };

    private static string InferDesignOrganization(string sourceName)
    {
        if (sourceName.Contains("STAR Labs", StringComparison.OrdinalIgnoreCase))
        {
            return "S.T.A.R. Labs";
        }

        if (sourceName.Contains("TI ", StringComparison.OrdinalIgnoreCase)
            || sourceName.Contains("Texas Instruments", StringComparison.OrdinalIgnoreCase))
        {
            return "Texas Instruments";
        }

        return "未注明";
    }

    private static string CurrentImporterVersion()
    {
        var assembly = typeof(ZemaxZmxImporter).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion
               ?? assembly.GetName().Version?.ToString(3)
               ?? "未知";
    }

    private static string RequiredOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
