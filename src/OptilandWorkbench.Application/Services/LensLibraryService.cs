using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Application.Services;

internal sealed class LensLibraryService : ILensLibraryService
{
    private const int SupportedCatalogVersion = 2;
    private const int SupportedCommercialCatalogVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private LensLibraryCatalogDocument? _catalog;
    private CommercialLensCatalogDocument? _commercialCatalog;
    private IReadOnlyList<CommercialLensEntryDto>? _commercialEntries;

    public LensLibraryService(string libraryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        LibraryDirectory = Path.GetFullPath(libraryDirectory);
    }

    public string LibraryDirectory { get; }

    public IReadOnlyList<LensLibraryEntryDto> GetLenses()
    {
        lock (_gate)
        {
            return LoadCatalog().Entries
                .OrderBy(entry => entry.Category, StringComparer.Ordinal)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<CommercialLensEntryDto> GetCommercialLenses()
    {
        lock (_gate)
        {
            return _commercialEntries ??= LoadCommercialCatalog().Entries
                    .Where(entry => StockLensCatalogPolicy.IncludesManufacturer(entry.Manufacturer))
                    .OrderBy(entry => entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.PartNumber, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
    }

    public string? GetNativeProjectPath(string lensId)
    {
        var entry = GetEntry(lensId);
        if (entry is null || string.IsNullOrWhiteSpace(entry.NativePath))
        {
            return null;
        }

        try
        {
            var nativePath = SafeChildPath(LibraryDirectory, entry.NativePath);
            return Path.GetExtension(nativePath).Equals(".staropt", StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(nativePath)
                ? nativePath
                : null;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public string? GetCommercialNativeProjectPath(string lensId)
    {
        var entry = GetCommercialEntry(lensId);
        return entry is null ? null : ResolveNativeProjectPath(entry.NativePath);
    }

    public async Task<SceneDto?> BuildPreviewAsync(
        string lensId,
        CancellationToken cancellationToken = default)
    {
        var nativePath = GetNativeProjectPath(lensId);
        if (nativePath is null)
        {
            return null;
        }

        return await BuildPreviewFromProjectAsync(nativePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SceneDto?> BuildCommercialPreviewAsync(
        string lensId,
        CancellationToken cancellationToken = default)
    {
        var nativePath = GetCommercialNativeProjectPath(lensId);
        return nativePath is null
            ? null
            : await BuildPreviewFromProjectAsync(nativePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SceneDto> BuildPreviewFromProjectAsync(
        string nativePath,
        CancellationToken cancellationToken)
    {
        var project = await StarOptProjectStore.LoadAsync(nativePath, cancellationToken).ConfigureAwait(false);
        var optic = project.Configurations[project.ActiveConfigurationIndex];
        var scene = await Task.Run(
            () => BuildPreviewScene(optic),
            cancellationToken).ConfigureAwait(false);
        var summary = CreateSummary(optic, nativePath);
        return new SceneDto(0, SceneDimension.TwoDimensional, WorkbenchMapper.ToScene2Dto(scene), null, summary);
    }

    private static Layout2DScene BuildPreviewScene(Optic optic)
    {
        var builder = new Layout2DBuilder(optic);
        try
        {
            return builder.Build(
                options: new LayoutBuildOptions(
                    IncludeAllWavelengths: false,
                    RayCount: 3,
                    MarginalAndChiefOnly: true));
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith(
                "Cannot find rays to yield requested real image height",
                StringComparison.Ordinal))
        {
            return builder.Build(
                options: new LayoutBuildOptions(
                    IncludeAllWavelengths: false,
                    RayCount: 0,
                    MarginalAndChiefOnly: true));
        }
    }

    private LensLibraryEntryDto? GetEntry(string id)
    {
        lock (_gate)
        {
            return LoadCatalog().Entries.FirstOrDefault(entry =>
                entry.Id.Equals(id, StringComparison.Ordinal));
        }
    }

    private CommercialLensEntryDto? GetCommercialEntry(string id)
    {
        lock (_gate)
        {
            return GetCommercialLenses().FirstOrDefault(entry =>
                entry.Id.Equals(id, StringComparison.Ordinal));
        }
    }

    private string? ResolveNativeProjectPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            var nativePath = SafeChildPath(LibraryDirectory, relativePath);
            return Path.GetExtension(nativePath).Equals(".staropt", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(nativePath)
                ? nativePath
                : null;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private LensLibraryCatalogDocument LoadCatalog()
    {
        if (_catalog is not null)
        {
            return _catalog;
        }

        var path = Path.Combine(LibraryDirectory, "index.json");
        if (!File.Exists(path))
        {
            return _catalog = EmptyCatalog();
        }

        try
        {
            var json = BoundedFile.ReadAllText(path, BoundedFile.MaximumCatalogBytes, "Lens-library catalog");
            var catalog = JsonSerializer.Deserialize<LensLibraryCatalogDocument>(json, JsonOptions);
            return _catalog = catalog is { Version: SupportedCatalogVersion }
                ? catalog
                : EmptyCatalog();
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return _catalog = EmptyCatalog();
        }
    }

    private CommercialLensCatalogDocument LoadCommercialCatalog()
    {
        if (_commercialCatalog is not null)
        {
            return _commercialCatalog;
        }

        try
        {
            var catalog = CommercialLensCatalogStore.LoadDirectory(LibraryDirectory);
            return _commercialCatalog = catalog is { Version: SupportedCommercialCatalogVersion }
                ? catalog
                : EmptyCommercialCatalog();
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return _commercialCatalog = EmptyCommercialCatalog();
        }
    }

    private static LensLibraryCatalogDocument EmptyCatalog() => new(
        SupportedCatalogVersion,
        DateTimeOffset.MinValue,
        Array.Empty<LensLibraryEntryDto>());

    private static CommercialLensCatalogDocument EmptyCommercialCatalog() => new(
        SupportedCommercialCatalogVersion,
        DateTimeOffset.MinValue,
        Array.Empty<CommercialLensEntryDto>());

    private static OpticalDocumentSnapshot CreateSummary(Optic optic, string path) => new(
        optic.Name,
        path,
        0,
        "镜头库预览",
        false,
        false,
        FiniteOrZero(optic.Paraxial.EstimateEffectiveFocalLength()),
        FiniteOrZero(optic.Paraxial.EstimateFNumber()),
        FiniteOrZero(optic.Aperture.Value),
        FiniteOrZero(optic.SurfaceGroup.TotalTrack),
        optic.SurfaceGroup.Items.Count,
        optic.Fields.Count,
        optic.Wavelengths.Count);

    private static string SafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : $"{fullRoot}{Path.DirectorySeparatorChar}";
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal) &&
            !candidate.Equals(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("镜头库索引包含越界路径。");
        }

        return candidate;
    }

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
