using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class MaterialCatalogService : IMaterialCatalogService
{
    private readonly WorkspaceCoordinator _workspace;
    private readonly string? _userCatalogDirectory;

    public MaterialCatalogService(
        WorkspaceCoordinator workspace,
        string? userCatalogDirectory)
    {
        _workspace = workspace;
        _userCatalogDirectory = userCatalogDirectory;
    }

    private object _gate => _workspace.Gate;

    private WorkbenchRuntime _runtime => _workspace.Runtime;

    public IReadOnlyList<MaterialCatalogDto> GetCatalogs()
    {
        return GetGlasses()
            .GroupBy(glass => glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MaterialCatalogDto(group.Key, group.Count()))
            .OrderBy(catalog => catalog.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetCatalogNames()
    {
        lock (_gate)
        {
            return _runtime.CurrentOptic.Materials.GlassManufacturers.ToArray();
        }
    }

    public IReadOnlyList<GlassMaterialDto> GetGlasses()
    {
        lock (_gate)
        {
            return CatalogGlasses()
                .Select(ToGlassMaterialDto)
                .OrderBy(glass => glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(glass => glass.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public AnalysisViewDto Analyze(MaterialAnalysisRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var glasses = CatalogGlasses()
                .Where(glass => string.IsNullOrWhiteSpace(request.Manufacturer)
                    || glass.Manufacturer.Equals(request.Manufacturer, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var selected = SelectGlass(glasses, request.GlassName);
            return request.Kind switch
            {
                MaterialAnalysisKind.DispersionDiagram => BuildDispersionDiagram(
                    selected,
                    request.SampleCount),
                MaterialAnalysisKind.GlassMap => BuildGlassMap(glasses, selected),
                MaterialAnalysisKind.AthermalGlassMap => BuildAthermalGlassMap(glasses, selected),
                MaterialAnalysisKind.InternalTransmission => BuildInternalTransmission(
                    selected,
                    request.ThicknessMillimeters),
                MaterialAnalysisKind.DispersionVsWavelength => BuildDispersionVsWavelength(
                    selected,
                    request.SampleCount),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Kind,
                    "The material analysis kind is invalid.")
            };
        }
    }

    private IReadOnlyList<CatalogGlassMaterial> CatalogGlasses()
    {
        var materials = _runtime.CurrentOptic.Materials;
        var glasses = new Dictionary<string, CatalogGlassMaterial>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in materials.Names)
        {
            CatalogGlassMaterial? glass;
            if (materials.TryResolveExternalGlass(name, preferredManufacturers: null, out var externalGlass))
            {
                glass = externalGlass;
            }
            else if (materials.TryResolve(name, preferredManufacturers: null, out var resolved) &&
                resolved is CatalogGlassMaterial catalogGlass)
            {
                glass = catalogGlass;
            }
            else
            {
                continue;
            }

            glasses[$"{glass.Manufacturer}:{glass.CatalogName}"] = glass;
        }

        return glasses.Values.ToArray();
    }

    private static GlassMaterialDto ToGlassMaterialDto(CatalogGlassMaterial glass)
    {
        const double wavelengthF = 486.1327;
        const double wavelengthD = 587.5618;
        const double wavelengthC = 656.2725;
        var refractiveIndexD = glass.ZemaxData is { ReferenceIndexD: > 0 } zemaxData
            ? zemaxData.ReferenceIndexD
            : glass.RefractiveIndex(wavelengthD);
        var abbeNumber = glass.ZemaxData is { ReferenceAbbeNumber: > 0 } referenceData
            ? referenceData.ReferenceAbbeNumber
            : CalculateAbbeNumber(glass, refractiveIndexD, wavelengthF, wavelengthC);
        return new GlassMaterialDto(
            glass.CatalogName,
            glass.Manufacturer,
            glass.Formula,
            refractiveIndexD,
            abbeNumber,
            glass.MinimumWavelengthNanometers / 1000.0,
            glass.MaximumWavelengthNanometers / 1000.0,
            glass.Coefficients.ToArray(),
            glass.RefractiveIndices.Count,
            glass.ExtinctionCoefficients.Count,
            glass.ZemaxData?.DispersionFormulaNumber,
            GlassStatus(glass.ZemaxData?.Status),
            glass.ZemaxData?.Comment ?? string.Empty,
            glass.ZemaxData?.ExcludeSubstitution ?? false,
            glass.ZemaxData?.MeltFrequency ?? 0,
            glass.ZemaxData?.ThermalExpansionLow,
            glass.ZemaxData?.ThermalExpansionHigh,
            glass.ZemaxData?.Density,
            glass.ZemaxData?.RelativePartialDispersionDeviation,
            glass.ZemaxData?.ThermalCoefficients.ToArray() ?? Array.Empty<double>(),
            glass.ZemaxData?.MechanicalData.ToArray() ?? Array.Empty<double>(),
            glass.ZemaxData?.OtherData.ToArray() ?? Array.Empty<double>(),
            glass.ZemaxData?.InternalTransmissions.Count ?? 0,
            glass.ZemaxData?.StressData.Count ?? 0);
    }

    private static CatalogGlassMaterial? SelectGlass(
        IReadOnlyList<CatalogGlassMaterial> glasses,
        string? glassName)
    {
        if (!string.IsNullOrWhiteSpace(glassName))
        {
            var match = glasses.FirstOrDefault(glass =>
                glass.CatalogName.Equals(glassName, StringComparison.OrdinalIgnoreCase)
                || GlassLabel(glass).Equals(glassName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return glasses.FirstOrDefault(glass =>
                glass.CatalogName.Equals("N-BK7", StringComparison.OrdinalIgnoreCase))
            ?? glasses.OrderBy(glass => glass.CatalogName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public async Task<MaterialCatalogImportResultDto> ImportZemaxCatalogAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (_userCatalogDirectory is null)
        {
            throw new InvalidOperationException("No user glass-catalog directory is configured.");
        }

        var document = await ZemaxAgfCatalogReader.ImportFileAsync(path, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_userCatalogDirectory);
        var fileName = string.Concat(document.CatalogName.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var destination = Path.Combine(
            _userCatalogDirectory,
            $"{fileName}{OptilandGlassCatalogStore.Extension}");
        await OptilandGlassCatalogStore.SaveAsync(document, destination, cancellationToken).ConfigureAwait(false);
        ExternalGlassCatalogDatabase.Register(document);
        return new MaterialCatalogImportResultDto(document.CatalogName, document.Glasses.Count, destination);
    }

    internal static void LoadUserCatalogs(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
            directory,
            $"*{OptilandGlassCatalogStore.Extension}",
            SearchOption.TopDirectoryOnly))
        {
            try
            {
                ExternalGlassCatalogDatabase.Register(OptilandGlassCatalogStore.Load(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
            }
        }
    }

    private static string GlassStatus(int? status) => status switch
    {
        0 => "标准",
        1 => "首选",
        2 => "废弃",
        3 => "特殊",
        4 => "熔融",
        _ => "内置只读"
    };

}
