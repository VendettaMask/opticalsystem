using System.Text.Json;
using System.Text.Encodings.Web;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Application.Services;

public static class CommercialLensCatalogStore
{
    public const string DirectoryName = "StockCatalogs";
    private const int SupportedVersion = 1;
    private const int MaximumManufacturerFiles = 32;

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

        var files = Directory
            .EnumerateFiles(catalogDirectory, "*.json", SearchOption.TopDirectoryOnly)
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
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manufacturerGroup in catalog.Entries
                     .GroupBy(entry => entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
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

        foreach (var staleFile in Directory.EnumerateFiles(
                     fullOutputDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            if (!expectedFiles.Contains(Path.GetFileName(staleFile)))
            {
                File.Delete(staleFile);
            }
        }
    }

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
