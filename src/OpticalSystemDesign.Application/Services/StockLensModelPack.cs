using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Application.Services;

public static class StockLensModelPack
{
    public const string Extension = ".stocklens";
    private const long MaximumPackBytes = 256L * 1024 * 1024;
    private const int MaximumModels = 32_768;
    private const string CatalogEntryName = "catalog.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Task SaveAsync(
        string path,
        CommercialLensCatalogDocument catalog,
        IReadOnlyDictionary<string, string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog, Path.GetFileNameWithoutExtension(path));
        var ids = catalog.Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        if (projectPaths.Keys.Any(id => !ids.Contains(id)))
            throw new InvalidDataException("A stock-lens project has no catalog record.");

        return BoundedFile.WriteAtomicAsync(path, MaximumPackBytes, "Stock-lens model pack", async (stream, token) =>
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            using (var metadata = archive.CreateEntry(CatalogEntryName, CompressionLevel.Optimal).Open())
                await JsonSerializer.SerializeAsync(metadata, catalog, JsonOptions, token).ConfigureAwait(false);

            foreach (var (id, projectPath) in projectPaths.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                using var source = BoundedFile.OpenRead(projectPath, BoundedFile.MaximumOpticalDocumentBytes, "Stock-lens project");
                using var destination = archive.CreateEntry(ProjectEntryName(id), CompressionLevel.NoCompression).Open();
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    internal static CommercialLensCatalogDocument ReadCatalog(string path)
    {
        using var stream = BoundedFile.OpenRead(path, MaximumPackBytes, "Stock-lens model pack");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > MaximumModels + 1)
            throw new InvalidDataException("The stock-lens model pack contains too many files.");
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
            if (!files.TryAdd(entry.FullName, entry))
                throw new InvalidDataException("The stock-lens model pack has duplicate entry names.");
        if (!files.TryGetValue(CatalogEntryName, out var metadata))
            throw new InvalidDataException("The stock-lens model pack has no catalog.");

        var catalog = JsonSerializer.Deserialize<CommercialLensCatalogDocument>(
            ReadEntry(metadata, BoundedFile.MaximumCatalogBytes), JsonOptions)
            ?? throw new InvalidDataException("The stock-lens model pack catalog is empty.");
        ValidateCatalog(catalog, Path.GetFileNameWithoutExtension(path));
        return catalog with
        {
            Entries = catalog.Entries.Select(entry => entry with
            {
                HasPackedModel = files.ContainsKey(ProjectEntryName(entry.Id)),
                NativePath = null,
                LocalModelPath = null
            }).ToArray()
        };
    }

    internal static StarOptProjectDocument LoadProject(string path, string id)
    {
        using var stream = BoundedFile.OpenRead(path, MaximumPackBytes, "Stock-lens model pack");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var model = archive.GetEntry(ProjectEntryName(id))
            ?? throw new InvalidDataException("The stock-lens model is missing from the pack.");
        return StarOptProjectStore.Deserialize(ReadEntry(model, BoundedFile.MaximumOpticalDocumentBytes));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumBytes)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
            throw new InvalidDataException($"Invalid stock-lens pack entry size: {entry.FullName}.");
        var bytes = new byte[(int)entry.Length];
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"Unexpected extra data in stock-lens pack entry: {entry.FullName}.");
        return bytes;
    }

    private static void ValidateCatalog(CommercialLensCatalogDocument catalog, string manufacturer)
    {
        if (catalog.Version != 1 || catalog.Entries is null || catalog.Entries.Count is 0 or > MaximumModels ||
            catalog.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.PartNumber) ||
                !manufacturer.Equals(entry.Manufacturer, StringComparison.OrdinalIgnoreCase)) ||
            catalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Entries.Count)
            throw new InvalidDataException("The stock-lens model pack catalog has an invalid version, manufacturer or record.");
    }

    private static string ProjectEntryName(string id) =>
        $"models/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))}.staropt";
}
