using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Analysis;

if (args.Length is < 3 or > 5)
{
    Console.Error.WriteLine(
        "Usage: OptilandWorkbench.AccuracyCapture <source.zmx> <settings-manifest.json> " +
        "<output-directory> [start-index] [end-index]");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var settingsManifestPath = Path.GetFullPath(args[1]);
var outputDirectory = Path.GetFullPath(args[2]);
var startIndex = args.Length >= 4
    ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)
    : 1;
var endIndex = args.Length == 5
    ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture)
    : int.MaxValue;
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
var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
var codeFingerprint = string.Join(":", new[] { typeof(WorkbenchRuntime).Assembly, typeof(OptilandWorkbench.Core.Optic).Assembly }
    .Select(assembly => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant()));
var previousManifestPath = Path.Combine(outputDirectory, "current-manifest.json");
var previous = File.Exists(previousManifestPath)
    ? JsonSerializer.Deserialize<CurrentManifest>(await File.ReadAllTextAsync(previousManifestPath), jsonOptions)
    : null;
var optic = await new ZemaxZmxImporter().ImportFileAsync(sourcePath);
var workspace = new WorkbenchRuntime(optic);
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
var analysisCount = workspace.AnalysisNames.Count;
foreach (var (name, zeroBasedIndex) in workspace.AnalysisNames.Select((name, index) => (name, index)))
{
    var index = zeroBasedIndex + 1;
    var settings = settingsByName.TryGetValue(name, out var saved)
        ? workspace.MergeAnalysisSettings(name, saved)
        : workspace.MergeAnalysisSettings(name, null);
    var slug = Slug(name);
    var relativeOutput = $"current/{index:D3}-{slug}.json";
    var outputPath = Path.Combine(outputDirectory, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
    var prior = previous?.Analyses.FirstOrDefault(run => run.Name == name && run.Output == relativeOutput);
    if ((index < startIndex || index > endIndex) && File.Exists(outputPath)
        && previous?.SourceSha256 == sourceHash && previous.CodeFingerprint == codeFingerprint
        && prior is not null && prior.Settings.Count == settings.Count
        && settings.All(pair => prior.Settings.TryGetValue(pair.Key, out var value) && value == pair.Value))
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
            CaptureStatus(existing),
            prior.ElapsedMilliseconds,
            settings,
            existingSeries.Length,
            existing.PlotPanes.Count,
            existingSeries.Sum(item => item.Points.Count),
            relativeOutput,
            null,
            existing.OutcomeReason,
            Reused: true));
        Console.WriteLine($"{index:D3}/{analysisCount:D3} {name}: reused ({CaptureStatus(existing)})");
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
            CaptureStatus(view),
            stopwatch.ElapsedMilliseconds,
            settings,
            series.Length,
            view.PlotPanes.Count,
            series.Sum(item => item.Points.Count),
            relativeOutput,
            null,
            view.OutcomeReason));
        Console.WriteLine(
            $"{index:D3}/{analysisCount:D3} {name}: {CaptureStatus(view)}, {series.Length} series, " +
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
        Console.WriteLine($"{index:D3}/{analysisCount:D3} {name}: FAILED, {stopwatch.Elapsed.TotalSeconds:F2}s");
    }
}

var manifest = new CurrentManifest(
    DateTimeOffset.UtcNow,
    sourcePath,
    sourceHash,
    optic.SurfaceGroup.Items.Count,
    optic.Fields.Count,
    optic.Wavelengths.Count,
    runs,
    started,
    DateTimeOffset.UtcNow,
    codeFingerprint);
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "current-manifest.json"),
    JsonSerializer.Serialize(manifest, jsonOptions));

var failed = runs.Count(run => run.Status == "failed");
Console.WriteLine($"Completed {runs.Count}; numerical={runs.Count(run => run.Status == "captured")}; " +
    $"unavailable={runs.Count(run => run.Status == "unavailable")}; not-applicable={runs.Count(run => run.Status == "not-applicable")}; failed={failed}");
return failed == 0 ? 0 : 1;

static string CaptureStatus(AnalysisView view) => view.Outcome switch
{
    AnalysisOutcome.Success => "captured",
    AnalysisOutcome.Unavailable => "unavailable",
    AnalysisOutcome.NotApplicable => "not-applicable",
    _ => throw new ArgumentOutOfRangeException(nameof(view))
};

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
    string? Error,
    string? OutcomeReason = null,
    bool Reused = false);

internal sealed record CurrentManifest(
    DateTimeOffset CreatedUtc,
    string SourceFile,
    string SourceSha256,
    int SurfaceCount,
    int FieldCount,
    int WavelengthCount,
    IReadOnlyList<AnalysisRun> Analyses,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string CodeFingerprint = "");
