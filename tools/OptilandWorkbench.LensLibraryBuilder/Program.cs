using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.LensLibraryBuilder;

if (args.Length == 2 && args[0].Equals("--reindex", StringComparison.OrdinalIgnoreCase))
{
    return await ReindexExistingLibraryAsync(Path.GetFullPath(args[1]));
}

if (args.Length is 3 or 4 && args[0].Equals("--stock-catalog", StringComparison.OrdinalIgnoreCase))
{
    var catalog = await StockLensCatalogConverter.ConvertAsync(
        args[1],
        args[2],
        args.Length == 4 ? args[3] : null);
    Console.WriteLine($"Output: {Path.GetFullPath(args[2])}");
    Console.WriteLine($"Stock lenses: {catalog.Entries.Count}");
    return 0;
}

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: OptilandWorkbench.LensLibraryBuilder <manifest.json> <output-directory>\n"
        + "   or: OptilandWorkbench.LensLibraryBuilder --reindex <library-directory>\n"
        + "   or: OptilandWorkbench.LensLibraryBuilder --stock-catalog <zmf-directory> <output-directory> [seed-catalog.json]");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
var manifest = JsonSerializer.Deserialize<LensLibraryBuildManifest>(
    await File.ReadAllTextAsync(manifestPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("Lens-library build manifest is empty.");
if (manifest.Version != 1 || manifest.Sources.Count == 0)
{
    throw new InvalidDataException("Lens-library build manifest is empty or unsupported.");
}

var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    $"staropt-lens-library-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryDirectory);
var stagingDirectory = Path.Combine(temporaryDirectory, "output");
var projectsDirectory = Path.Combine(stagingDirectory, "projects");
Directory.CreateDirectory(projectsDirectory);
var failures = new List<string>();
var importedAt = DateTimeOffset.UtcNow;
try
{
    var sourceFiles = new List<(LensLibraryBuildSource Source, string Path)>();
    foreach (var source in manifest.Sources)
    {
        if (source.Files.Count == 0)
        {
            continue;
        }

        foreach (var configuredPath in source.Files)
        {
            var sourcePath = Path.GetFullPath(Path.Combine(manifestDirectory, configuredPath));
            var inputs = File.Exists(sourcePath)
                ? new[] { sourcePath }
                : Directory.Exists(sourcePath)
                    ? Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToArray()
                    : throw new FileNotFoundException(
                        $"Lens-library source path was not found: {sourcePath}",
                        sourcePath);
            foreach (var inputPath in inputs)
            {
                if (IsExcluded(source, inputPath))
                {
                    continue;
                }

                var extension = Path.GetExtension(inputPath);
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extractionDirectory = Path.Combine(
                        temporaryDirectory,
                        $"{SafeName(source.Id)}-{StableSuffix(inputPath)}");
                    ExtractZip(inputPath, extractionDirectory);
                    sourceFiles.AddRange(Directory
                        .EnumerateFiles(extractionDirectory, "*", SearchOption.AllDirectories)
                        .Where(path => IsSelectedBuildInput(source, path))
                        .Select(path => (source, path)));
                }
                else if (IsSelectedBuildInput(source, inputPath))
                {
                    sourceFiles.Add((source, inputPath));
                }
            }
        }
    }

    var entries = new List<LensLibraryEntryDto>();
    var importer = new ZemaxZmxImporter();
    foreach (var item in sourceFiles
        .Where(item => Path.GetExtension(item.Path).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
        .OrderBy(item => item.Source.Id, StringComparer.Ordinal)
        .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
    {
        try
        {
            var result = await importer.ImportConfigurationSetFileAsync(item.Path);
            var optic = result.ActiveOptic;
            var id = LensLibraryCatalogEntryFactory.CreateStableId(
                item.Source.Id,
                Path.GetFileName(item.Path));
            var projectPath = Path.Combine(projectsDirectory, $"{id}.staropt");
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(result.Configurations, result.ActiveConfigurationIndex),
                projectPath);
            entries.Add(LensLibraryCatalogEntryFactory.Create(
                id,
                null,
                item.Source.Category,
                item.Source.Name,
                item.Source.SourceUrl,
                item.Source.License,
                Path.GetRelativePath(stagingDirectory, projectPath),
                item.Path,
                optic,
                item.Source.LensType,
                item.Source.Application,
                item.Source.DesignOrganization,
                importedAt));
            Console.WriteLine($"Lens: {entries[^1].Category} / {entries[^1].Name}");
        }
        catch (Exception exception)
        {
            var failure = $"{Path.GetFileName(item.Path)}: {exception.Message}";
            failures.Add(failure);
            Console.Error.WriteLine($"FAILED: {failure}");
        }
    }

    var catalog = new LensLibraryCatalogDocument(
        2,
        importedAt,
        entries
            .OrderBy(entry => entry.Category, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    await File.WriteAllTextAsync(
        Path.Combine(stagingDirectory, "index.json"),
        JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    LensLibraryPublisher.Publish(stagingDirectory, outputDirectory);
    Console.WriteLine($"Output: {outputDirectory}");
    Console.WriteLine($"Lenses: {entries.Count}");
    Console.WriteLine($"Failed: {failures.Count}");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

return failures.Count == 0 ? 0 : 1;

static async Task<int> ReindexExistingLibraryAsync(string libraryDirectory)
{
    var catalogPath = Path.Combine(libraryDirectory, "index.json");
    if (!File.Exists(catalogPath))
    {
        throw new FileNotFoundException("Lens-library index was not found.", catalogPath);
    }

    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var existing = JsonSerializer.Deserialize<LensLibraryCatalogDocument>(
        await File.ReadAllTextAsync(catalogPath),
        options) ?? throw new InvalidDataException("Lens-library index is empty.");
    if (existing.Version is not (1 or 2))
    {
        throw new InvalidDataException(
            $"Lens-library index version {existing.Version} cannot be reindexed.");
    }

    var entries = new List<LensLibraryEntryDto>(existing.Entries.Count);
    foreach (var oldEntry in existing.Entries)
    {
        var projectPath = SafeChildPath(libraryDirectory, oldEntry.NativePath);
        var project = await StarOptProjectStore.LoadAsync(projectPath);
        var optic = project.Configurations[project.ActiveConfigurationIndex];
        var enriched = LensLibraryCatalogEntryFactory.Create(
            oldEntry.Id,
            oldEntry.Name,
            oldEntry.Category,
            oldEntry.SourceName,
            oldEntry.SourceUrl,
            oldEntry.License,
            oldEntry.NativePath,
            oldEntry.SourcePath,
            optic,
            NullIfHistoricalValue(oldEntry.LensType),
            NullIfHistoricalValue(oldEntry.Application),
            NullIfHistoricalValue(oldEntry.DesignOrganization),
            oldEntry.ImportedAt,
            string.IsNullOrWhiteSpace(oldEntry.ImporterVersion)
                ? "历史版本（未记录）"
                : oldEntry.ImporterVersion) with
        {
            SourceFormat = oldEntry.SourceFormat,
            ImportStatus = oldEntry.ImportStatus,
            ImportMessage = oldEntry.ImportMessage
        };
        entries.Add(enriched);
    }

    var catalog = new LensLibraryCatalogDocument(
        2,
        DateTimeOffset.UtcNow,
        entries
            .OrderBy(entry => entry.Category, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray());
    var temporaryPath = $"{catalogPath}.{Guid.NewGuid():N}.tmp";
    try
    {
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, catalogPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    Console.WriteLine($"Reindexed: {entries.Count}");
    Console.WriteLine($"Output: {catalogPath}");
    return 0;
}

static string? NullIfHistoricalValue(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;

static bool IsBuildInput(string path)
{
    var extension = Path.GetExtension(path);
    return extension.Equals(".zmx", StringComparison.OrdinalIgnoreCase);
}

static bool IsSelectedBuildInput(LensLibraryBuildSource source, string path)
{
    if (!IsBuildInput(path) || IsExcluded(source, path))
    {
        return false;
    }

    var fileName = Path.GetFileName(path);
    var hasAllowList = source.IncludeFiles is { Count: > 0 } ||
                       source.IncludeFilePrefixes is { Count: > 0 };
    return !hasAllowList ||
           source.IncludeFiles?.Contains(fileName, StringComparer.OrdinalIgnoreCase) == true ||
           source.IncludeFilePrefixes?.Any(prefix =>
               fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) == true;
}

static bool IsExcluded(LensLibraryBuildSource source, string path) =>
    source.ExcludeFiles?.Contains(
        Path.GetFileName(path),
        StringComparer.OrdinalIgnoreCase) == true;

static void ExtractZip(string archivePath, string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    using var archive = ZipFile.OpenRead(archivePath);
    foreach (var entry in archive.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name))
        {
            continue;
        }

        var outputPath = SafeChildPath(outputDirectory, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        entry.ExtractToFile(outputPath, overwrite: true);
    }
}

static string SafeChildPath(string root, string relativePath)
{
    var fullRoot = Path.GetFullPath(root);
    var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
    var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
        ? fullRoot
        : $"{fullRoot}{Path.DirectorySeparatorChar}";
    if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
    {
        throw new InvalidDataException("Archive contains a path outside its extraction directory.");
    }

    return candidate;
}

static string SafeName(string value)
{
    var normalized = new string(value
        .Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
        .ToArray())
        .Trim('-');
    return string.IsNullOrEmpty(normalized) ? "lens" : normalized.ToLowerInvariant();
}

static string StableSuffix(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes.AsSpan(0, 6)).ToLowerInvariant();
}

internal sealed record LensLibraryBuildManifest(
    int Version,
    IReadOnlyList<LensLibraryBuildSource> Sources);

internal sealed record LensLibraryBuildSource(
    string Id,
    string Name,
    string Category,
    string SourceUrl,
    string License,
    IReadOnlyList<string> Files,
    IReadOnlyList<string>? IncludeFiles = null,
    IReadOnlyList<string>? IncludeFilePrefixes = null,
    IReadOnlyList<string>? ExcludeFiles = null,
    string? LensType = null,
    string? Application = null,
    string? DesignOrganization = null);
