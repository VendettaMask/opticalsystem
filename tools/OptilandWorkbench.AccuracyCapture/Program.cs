using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core.FileIO;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine(
        "Usage: OptilandWorkbench.AccuracyCapture <source.zmx> <settings-manifest.json> " +
        "<output-directory> [start-index]");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var settingsManifestPath = Path.GetFullPath(args[1]);
var outputDirectory = Path.GetFullPath(args[2]);
var startIndex = args.Length == 4
    ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)
    : 1;
var currentDirectory = Path.Combine(outputDirectory, "current");
Directory.CreateDirectory(currentDirectory);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
};
jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
var optic = await new ZemaxZmxImporter().ImportFileAsync(sourcePath);
var workspace = new OpticalWorkspaceModel(optic);
var settingsManifest = JsonSerializer.Deserialize<SettingsManifest>(
    await File.ReadAllTextAsync(settingsManifestPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("The settings manifest could not be read.");
var settingsByName = settingsManifest.Analyses.ToDictionary(
    analysis => analysis.Name,
    analysis => (IReadOnlyDictionary<string, string>)analysis.Settings,
    StringComparer.Ordinal);

var started = DateTimeOffset.UtcNow;
var runs = new List<AnalysisRun>();
foreach (var (name, zeroBasedIndex) in workspace.AnalysisNames.Select((name, index) => (name, index)))
{
    var index = zeroBasedIndex + 1;
    var settings = settingsByName.TryGetValue(name, out var saved)
        ? workspace.MergeAnalysisSettings(name, saved)
        : workspace.MergeAnalysisSettings(name, null);
    var slug = Slug(name);
    var relativeOutput = $"current/{index:D3}-{slug}.json";
    var outputPath = Path.Combine(outputDirectory, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
    if (index < startIndex && File.Exists(outputPath))
    {
        var existing = JsonSerializer.Deserialize<AnalysisView>(
            await File.ReadAllTextAsync(outputPath),
            jsonOptions) ?? throw new InvalidOperationException($"Could not read {outputPath}.");
        var existingSeries = existing.PlotPanes.Count > 0
            ? existing.PlotPanes.SelectMany(pane => pane.Series).ToArray()
            : existing.SeriesList.ToArray();
        runs.Add(new AnalysisRun(
            index,
            name,
            "captured",
            0,
            settings,
            existingSeries.Length,
            existing.PlotPanes.Count,
            existingSeries.Sum(item => item.Points.Count),
            relativeOutput,
            null));
        Console.WriteLine($"{index:D3}/069 {name}: reused");
        continue;
    }

    var stopwatch = Stopwatch.StartNew();
    try
    {
        var view = workspace.BuildAnalysisView(name, settings);
        stopwatch.Stop();
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(view, jsonOptions));
        var series = view.PlotPanes.Count > 0
            ? view.PlotPanes.SelectMany(pane => pane.Series).ToArray()
            : view.SeriesList.ToArray();
        runs.Add(new AnalysisRun(
            index,
            name,
            "captured",
            stopwatch.ElapsedMilliseconds,
            settings,
            series.Length,
            view.PlotPanes.Count,
            series.Sum(item => item.Points.Count),
            relativeOutput,
            null));
        Console.WriteLine(
            $"{index:D3}/069 {name}: captured, {series.Length} series, " +
            $"{series.Sum(item => item.Points.Count)} points, {stopwatch.Elapsed.TotalSeconds:F2}s");
    }
    catch (Exception exception)
    {
        stopwatch.Stop();
        runs.Add(new AnalysisRun(
            index,
            name,
            "failed",
            stopwatch.ElapsedMilliseconds,
            settings,
            0,
            0,
            0,
            null,
            exception.ToString()));
        Console.WriteLine($"{index:D3}/069 {name}: FAILED, {stopwatch.Elapsed.TotalSeconds:F2}s");
    }
}

var manifest = new CurrentManifest(
    DateTimeOffset.UtcNow,
    sourcePath,
    Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
    optic.SurfaceGroup.Items.Count,
    optic.Fields.Count,
    optic.Wavelengths.Count,
    runs,
    started,
    DateTimeOffset.UtcNow);
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "current-manifest.json"),
    JsonSerializer.Serialize(manifest, jsonOptions));

var failed = runs.Count(run => run.Status != "captured");
Console.WriteLine($"Completed {runs.Count - failed}/{runs.Count}; failed={failed}");
return failed == 0 ? 0 : 1;

static string Slug(string value)
{
    var characters = value.ToLowerInvariant().Select(character =>
        char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();
    return string.Join(
        '-',
        new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
}

internal sealed record SettingsManifest(IReadOnlyList<SettingsAnalysis> Analyses);

internal sealed record SettingsAnalysis(
    string Name,
    Dictionary<string, string> Settings);

internal sealed record AnalysisRun(
    int Index,
    string Name,
    string Status,
    long ElapsedMilliseconds,
    IReadOnlyDictionary<string, string> Settings,
    int SeriesCount,
    int PaneCount,
    int PointCount,
    string? Output,
    string? Error);

internal sealed record CurrentManifest(
    DateTimeOffset CreatedUtc,
    string SourceFile,
    string SourceSha256,
    int SurfaceCount,
    int FieldCount,
    int WavelengthCount,
    IReadOnlyList<AnalysisRun> Analyses,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc);
