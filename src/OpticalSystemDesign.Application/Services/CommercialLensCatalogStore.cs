using System.Text.Encodings.Web;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Application.Services;

public static class CommercialLensCatalogStore
{
    public const string DirectoryName = "StockCatalogs";
    private const int SupportedVersion = 1;
    private const int MaximumManufacturerFiles = 32;
    private const string IndexFileName = ".stock-catalog-index";
    private const string IndexKind = "OpticalSystemDesign.StockCatalogIndex";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static CommercialLensCatalogDocument LoadDirectory(string libraryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        var catalogDirectory = Path.Combine(Path.GetFullPath(libraryDirectory), DirectoryName);
        if (!Directory.Exists(catalogDirectory))
        {
            return EmptyCatalog();
        }

        var index = ReadIndex(catalogDirectory);
        var files = (index is null
            ? Directory.EnumerateFiles(catalogDirectory, "*.json", SearchOption.TopDirectoryOnly)
            : index.Files.Select(name => Path.Combine(catalogDirectory, name)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length > MaximumManufacturerFiles)
        {
            throw new InvalidDataException("Stock-lens catalog contains too many manufacturer files.");
        }

        var generatedAt = DateTimeOffset.MinValue;
        var entries = new List<CommercialLensEntryDto>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var catalog = LoadFile(path);
            generatedAt = generatedAt > catalog.BuiltAt ? generatedAt : catalog.BuiltAt;
            foreach (var entry in catalog.Entries)
            {
                if (!Path.GetFileNameWithoutExtension(path).Equals(
                        entry.Manufacturer,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Stock-lens file '{Path.GetFileName(path)}' contains another manufacturer.");
                }
                if (!ids.Add(entry.Id))
                {
                    throw new InvalidDataException($"Stock-lens catalog contains duplicate ID '{entry.Id}'.");
                }
                entries.Add(entry);
            }
        }

        return new CommercialLensCatalogDocument(SupportedVersion, generatedAt, entries);
    }

    public static CommercialLensCatalogDocument LoadFile(string path)
    {
        var json = BoundedFile.ReadAllText(
            path,
            BoundedFile.MaximumCatalogBytes,
            "Stock-lens manufacturer catalog");
        var catalog = JsonSerializer.Deserialize<CommercialLensCatalogDocument>(json, ReadOptions)
            ?? throw new InvalidDataException("Stock-lens manufacturer catalog is empty.");
        if (catalog.Version != SupportedVersion || catalog.Entries is null)
        {
            throw new InvalidDataException("Stock-lens manufacturer catalog schema is unsupported.");
        }
        return catalog;
    }

    public static async Task SaveDirectoryAsync(
        CommercialLensCatalogDocument catalog,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (catalog.Version != SupportedVersion || catalog.Entries is null)
        {
            throw new InvalidDataException("Stock-lens catalog schema is unsupported.");
        }

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        _ = ReadIndex(fullOutputDirectory); // Reject an unrelated/corrupt reserved index before writing.
        var groups = catalog.Entries.GroupBy(entry => entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length > MaximumManufacturerFiles)
            throw new InvalidDataException("Stock-lens catalog contains too many manufacturers.");
        // Preflight every destination before changing any file. A coincidentally
        // named user JSON is not permission to replace it with a catalog.
        foreach (var group in groups)
        {
            var target = Path.Combine(fullOutputDirectory, ManufacturerFileName(group.Key));
            if (!File.Exists(target)) continue;
            try
            {
                var existing = LoadFile(target);
                if (existing.Entries.Any(entry => !string.Equals(entry.Manufacturer, group.Key, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Manufacturer identity differs.");
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new IOException($"Existing file '{Path.GetFileName(target)}' is not this manufacturer's catalog and was preserved.", exception);
            }
        }
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manufacturerGroup in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = ManufacturerFileName(manufacturerGroup.Key);
            expectedFiles.Add(fileName);
            var manufacturerCatalog = new CommercialLensCatalogDocument(
                SupportedVersion,
                catalog.BuiltAt,
                manufacturerGroup
                    .OrderBy(entry => entry.PartNumber, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                    .ToArray());
            await BoundedFile.WriteAllTextAtomicAsync(
                    Path.Combine(fullOutputDirectory, fileName),
                    JsonSerializer.Serialize(manufacturerCatalog, WriteOptions),
                    BoundedFile.MaximumCatalogBytes,
                    $"{manufacturerGroup.Key} stock-lens catalog",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Select active catalog files explicitly. Never sweep or delete a user's
        // JSON files; previously exported manufacturers remain intact but inactive.
        await BoundedFile.WriteAllTextAtomicAsync(Path.Combine(fullOutputDirectory, IndexFileName),
            JsonSerializer.Serialize(new CatalogIndex(IndexKind, expectedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray()), WriteOptions),
            BoundedFile.MaximumCatalogBytes, "Stock-lens catalog index", cancellationToken).ConfigureAwait(false);
    }

    private static CatalogIndex? ReadIndex(string directory)
    {
        var path = Path.Combine(directory, IndexFileName);
        if (!File.Exists(path)) return null;
        var index = JsonSerializer.Deserialize<CatalogIndex>(
            BoundedFile.ReadAllText(path, 16 * 1024, "Stock-lens catalog index"), ReadOptions);
        if (index is null || index.Kind != IndexKind || index.Files is null
            || index.Files.Length > MaximumManufacturerFiles
            || index.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Files.Length
            || index.Files.Any(name => string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)
                || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Stock-lens catalog index is invalid.");
        return index;
    }

    private sealed record CatalogIndex(string Kind, string[] Files);

    private static string ManufacturerFileName(string manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) ||
            manufacturer.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"Invalid stock-lens manufacturer name '{manufacturer}'.");
        }
        return $"{manufacturer}.json";
    }

    private static CommercialLensCatalogDocument EmptyCatalog() => new(
        SupportedVersion,
        DateTimeOffset.MinValue,
        Array.Empty<CommercialLensEntryDto>());
}
