using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Application.Services;

internal sealed class LensLibraryService : ILensLibraryService
{
    private const int SupportedCatalogVersion = 2;
    private const int SupportedCommercialCatalogVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string? _localStockModelDirectory;
    private readonly Dictionary<string, string> _stockModelPacks = new(StringComparer.Ordinal);
    private LensLibraryCatalogDocument? _catalog;
    private CommercialLensCatalogDocument? _commercialCatalog;
    private IReadOnlyList<CommercialLensEntryDto>? _commercialEntries;

    public LensLibraryService(string libraryDirectory, string? localStockModelDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        LibraryDirectory = Path.GetFullPath(libraryDirectory);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _localStockModelDirectory = localStockModelDirectory ?? (string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "OpticalSystemDesign", "StockLensModels"));
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
                    .Select(WithLocalModelAvailability)
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
        return entry is null
            ? null
            : ResolveNativeProjectPath(LibraryDirectory, entry.NativePath)
                ?? ResolveNativeProjectPath(_localStockModelDirectory, entry.LocalModelPath);
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
        var project = await LoadCommercialProjectAsync(lensId, cancellationToken).ConfigureAwait(false);
        return project is null
            ? null
            : await BuildPreviewFromProjectAsync(project.Value.Document, project.Value.Source, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(StarOptProjectDocument Document, string Source)?> LoadCommercialProjectAsync(
        string lensId,
        CancellationToken cancellationToken)
    {
        var entry = GetCommercialEntry(lensId);
        if (entry?.HasPackedModel == true)
        {
            var archive = _stockModelPacks[lensId];
            var document = await Task.Run(() => StockLensModelPack.LoadProject(archive, lensId), cancellationToken)
                .ConfigureAwait(false);
            return (document, $"{entry.PartNumber}.staropt");
        }

        var nativePath = GetCommercialNativeProjectPath(lensId);
        return nativePath is null
            ? null
            : (await StarOptProjectStore.LoadAsync(nativePath, cancellationToken).ConfigureAwait(false), nativePath);
    }

    private static async Task<SceneDto> BuildPreviewFromProjectAsync(
        string nativePath,
        CancellationToken cancellationToken)
    {
        var project = await StarOptProjectStore.LoadAsync(nativePath, cancellationToken).ConfigureAwait(false);
        return await BuildPreviewFromProjectAsync(project, nativePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SceneDto> BuildPreviewFromProjectAsync(
        StarOptProjectDocument project,
        string source,
        CancellationToken cancellationToken)
    {
        var optic = project.Configurations[project.ActiveConfigurationIndex];
        var (scene, warning) = await Task.Run(
            () => BuildPreviewScene(optic),
            cancellationToken).ConfigureAwait(false);
        var summary = CreateSummary(optic, source);
        return new SceneDto(0, SceneDimension.TwoDimensional, WorkbenchMapper.ToScene2Dto(scene), null, summary,
            Warnings: warning is null ? Array.Empty<string>() : new[] { warning });
    }

    internal static (Layout2DScene Scene, string? Warning) BuildPreviewScene(Optic optic)
    {
        var builder = new Layout2DBuilder(optic);
        try
        {
            return (builder.Build(
                options: new LayoutBuildOptions(
                    IncludeAllWavelengths: false,
                    RayCount: 3,
                    MarginalAndChiefOnly: true)), null);
        }
        catch (InvalidOperationException exception) when (exception is RayAimingException or FieldAimingException)
        {
            return (builder.Build(
                options: new LayoutBuildOptions(
                    IncludeAllWavelengths: false,
                    RayCount: 0,
                    MarginalAndChiefOnly: true)), $"仅显示镜片几何；预览光线追迹失败：{exception.Message}");
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

    private CommercialLensEntryDto WithLocalModelAvailability(CommercialLensEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(entry.LocalModelPath))
        {
            return entry;
        }

        return ResolveNativeProjectPath(_localStockModelDirectory, entry.LocalModelPath) is null
            ? entry with { LocalModelPath = null }
            : entry with { ModelStatus = "本机 STAROPT 模型可载入" };
    }

    private static string? ResolveNativeProjectPath(string? root, string? relativePath)
    {
        if (root is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            var nativePath = SafeChildPath(root, relativePath);
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

        CommercialLensCatalogDocument catalog;
        try
        {
            var loaded = CommercialLensCatalogStore.LoadDirectory(LibraryDirectory);
            catalog = loaded is { Version: SupportedCommercialCatalogVersion }
                ? loaded
                : EmptyCommercialCatalog();
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            catalog = EmptyCommercialCatalog();
        }

        var entries = catalog.Entries.ToList();
        var builtAt = catalog.BuiltAt;
        foreach (var manufacturer in StockLensCatalogPolicy.Manufacturers)
        {
            var fileName = manufacturer + StockLensModelPack.Extension;
            var bundled = Path.Combine(LibraryDirectory, CommercialLensCatalogStore.DirectoryName, fileName);
            var path = File.Exists(bundled) || _localStockModelDirectory is null
                ? bundled
                : Path.Combine(_localStockModelDirectory, fileName);
            if (!File.Exists(path)) continue;

            try
            {
                var packed = StockLensModelPack.ReadCatalog(path);
                var existingIds = entries.Where(entry => !entry.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
                if (packed.Entries.Any(entry => existingIds.Contains(entry.Id))) continue;

                entries.RemoveAll(entry => entry.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase));
                entries.AddRange(packed.Entries);
                foreach (var entry in packed.Entries.Where(entry => entry.HasPackedModel))
                    _stockModelPacks.Add(entry.Id, path);
                if (packed.BuiltAt > builtAt) builtAt = packed.BuiltAt;
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Other manufacturers and the shipped directory remain available.
            }
        }

        return _commercialCatalog = new CommercialLensCatalogDocument(SupportedCommercialCatalogVersion, builtAt, entries);
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
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(prefix, comparison) &&
            !candidate.Equals(fullRoot, comparison))
        {
            throw new InvalidDataException("镜头库索引包含越界路径。");
        }

        // Reject links within the trusted library, including the final file.
        var current = fullRoot;
        foreach (var segment in Path.GetRelativePath(fullRoot, candidate).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("镜头库路径不能包含符号链接或目录联接。");
        }
        return candidate;
    }

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
