using System.Security.Cryptography;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;

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
        Optic optic)
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
            FiniteOrZero(optic.Paraxial.EstimateFNumber()),
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
            Path.GetFileName(sourcePath));
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

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
