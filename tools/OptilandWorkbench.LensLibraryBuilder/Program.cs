using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: OptilandWorkbench.LensLibraryBuilder <manifest.json> <output-directory>");
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
            var id = $"{SafeName(item.Source.Id)}-{StableSuffix(
                $"{item.Source.Id}/{Path.GetFileName(item.Path)}")}";
            var projectPath = Path.Combine(projectsDirectory, $"{id}.staropt");
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(result.Configurations, result.ActiveConfigurationIndex),
                projectPath);
            var wavelengths = optic.Wavelengths.Select(wavelength => wavelength.Nanometers).ToArray();
            var maximumField = optic.Fields
                .Select(field => Math.Sqrt((field.X * field.X) + (field.Y * field.Y)))
                .DefaultIfEmpty(0)
                .Max();
            var name = string.IsNullOrWhiteSpace(optic.Name) ||
                optic.Name.Equals("Imported Zemax ZMX", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(item.Path)
                    : optic.Name;
            entries.Add(new LensLibraryEntryDto(
                id,
                name,
                item.Source.Category,
                item.Source.Name,
                item.Source.SourceUrl,
                item.Source.License,
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
                Path.GetRelativePath(stagingDirectory, projectPath),
                Path.GetFileName(item.Path)));
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
        1,
        DateTimeOffset.UnixEpoch,
        entries
            .OrderBy(entry => entry.Category, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    await File.WriteAllTextAsync(
        Path.Combine(stagingDirectory, "index.json"),
        JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    PublishLibrary(stagingDirectory, outputDirectory);
    Console.WriteLine($"Output: {outputDirectory}");
    Console.WriteLine($"Lenses: {entries.Count}");
    Console.WriteLine($"Failed: {failures.Count}");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

return failures.Count == 0 ? 0 : 1;

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

static void PublishLibrary(string stagingDirectory, string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var legacyCatalogDirectory = Path.Combine(outputDirectory, "catalogs");
    if (Directory.Exists(legacyCatalogDirectory))
    {
        Directory.Delete(legacyCatalogDirectory, recursive: true);
    }

    foreach (var name in new[] { "projects" })
    {
        var destination = Path.Combine(outputDirectory, name);
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        var source = Path.Combine(stagingDirectory, name);
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
    }

    File.Move(
        Path.Combine(stagingDirectory, "index.json"),
        Path.Combine(outputDirectory, "index.json"),
        overwrite: true);
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

static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;

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
    IReadOnlyList<string>? ExcludeFiles = null);
