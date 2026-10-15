using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.ZemaxComparison.Workbench;

public sealed record WorkbenchRecaptureOptions(string Source, string SettingsManifest, string Output,
    int StartIndex = 1, int EndIndex = int.MaxValue)
{
    public static WorkbenchRecaptureOptions Parse(string[] args)
    {
        if (args.Length is < 3 or > 5)
            throw new ArgumentException("Usage: --recapture-workbench <source.zmx> <settings-manifest.json> <output-directory> [start-index] [end-index]");
        int Index(int position) => int.TryParse(args[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : throw new ArgumentException("Recapture indices must be positive one-based integers.");
        return new WorkbenchRecaptureOptions(args[0], args[1], args[2],
            args.Length >= 4 ? Index(3) : 1, args.Length == 5 ? Index(4) : int.MaxValue).Normalize();
    }

    public WorkbenchRecaptureOptions Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(SettingsManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(Output);
        if (StartIndex < 1 || EndIndex < StartIndex)
            throw new ArgumentException("Recapture indices must satisfy 1 <= start <= end.");
        if (!Path.GetExtension(Source).Equals(".zmx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Recapture source must be a ZMX file.");
        return this with { Source = Path.GetFullPath(Source), SettingsManifest = Path.GetFullPath(SettingsManifest), Output = Path.GetFullPath(Output) };
    }
}

/// <summary>Historical AnalysisView/report schema, executed by the same Application analysis factory.</summary>
public static class WorkbenchRecapture
{
    private static readonly JsonSerializerOptions ViewJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> RunAsync(WorkbenchRecaptureOptions options, CancellationToken cancellationToken = default)
    {
        options = options.Normalize();
        ProtectInputs(options);
        var sourceHash = JsonFiles.Hash(await File.ReadAllBytesAsync(options.Source, cancellationToken));
        var settingsBytes = await File.ReadAllBytesAsync(options.SettingsManifest, cancellationToken);
        var settingsManifest = JsonSerializer.Deserialize<SettingsManifest>(settingsBytes, ViewJson)
            ?? throw new InvalidDataException("The settings manifest could not be read.");
        if (settingsManifest.Analyses is null || settingsManifest.Analyses.Any(item => item is null || item.Settings is null))
            throw new InvalidDataException("The settings manifest must contain analyses with settings.");
        var settingsByName = settingsManifest.Analyses.GroupBy(item => WorkbenchAnalysisCatalog.CanonicalKey(item.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (group.FirstOrDefault(item => item.Name == group.Key) ?? group.First()).Settings, StringComparer.Ordinal);
        var optic = await new ZemaxZmxImporter().ImportFileAsync(options.Source, cancellationToken);
        var workspace = new WorkbenchRuntime(optic);
        if (options.StartIndex > workspace.AnalysisNames.Count
            || options.EndIndex != int.MaxValue && options.EndIndex > workspace.AnalysisNames.Count)
            throw new ArgumentException($"Recapture index exceeds the current {workspace.AnalysisNames.Count}-entry catalog.");
        var codeFingerprint = string.Join(":", new[] { typeof(WorkbenchRuntime).Assembly, typeof(OpticalSystemDesign.Core.Optic).Assembly }
            .Select(assembly => JsonFiles.Hash(File.ReadAllBytes(assembly.Location))));
        var manifestPath = Path.Combine(options.Output, "current-manifest.json");
        var previous = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<CurrentManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), ViewJson)
                ?? throw new InvalidDataException("Existing recapture manifest is empty.")
            : null;
        if (previous is null && Directory.Exists(options.Output) && Directory.EnumerateFileSystemEntries(options.Output).Any())
            throw new IOException("Use a fresh output directory or an existing recapture manifest.");
        if (previous is not null && (previous.SourceSha256 != sourceHash || previous.CodeFingerprint != codeFingerprint))
            throw new IOException("Source or computation binaries changed; use a fresh output directory.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(options.Output);
        using var outputLock = new FileStream(Path.Combine(options.Output, ".recapture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var started = DateTimeOffset.UtcNow;
        var runs = new List<AnalysisRun>();
        var cancelled = false;
        try
        {
            foreach (var (name, zeroBasedIndex) in workspace.AnalysisNames.Select((name, index) => (name, index)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = zeroBasedIndex + 1;
                var settings = workspace.MergeAnalysisSettings(name, settingsByName.GetValueOrDefault(name));
                var relativeOutput = $"current/{index:D3}-{Slug(name)}.json";
                var outputPath = Path.Combine(options.Output, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
                var selected = index >= options.StartIndex && index <= options.EndIndex;
                var prior = previous?.Analyses?.FirstOrDefault(run => run.Name == name && run.Output == relativeOutput);
                var reusable = !selected && CanReuse(prior, settings, outputPath);
                if (!selected && !reusable)
                {
                    runs.Add(new(index, name, "not-requested", 0, settings, 0, 0, 0, null, null,
                        "Outside the requested interval; no matching reusable result."));
                    continue;
                }
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    if (reusable && JsonFiles.Hash(File.ReadAllBytes(outputPath)) != prior!.OutputSha256)
                        throw new InvalidDataException($"Reusable result checksum mismatch: {relativeOutput}");
                    var view = reusable
                        ? JsonSerializer.Deserialize<AnalysisView>(await File.ReadAllTextAsync(outputPath, cancellationToken), ViewJson)
                            ?? throw new InvalidDataException($"Could not read {outputPath}.")
                        : workspace.BuildAnalysisView(name, settings, cancellationToken);
                    if (!reusable) JsonFiles.Write(outputPath, view, ViewJson);
                    var run = CompletedRun(index, name, settings, relativeOutput, outputPath, view,
                        reusable ? prior!.ElapsedMilliseconds : stopwatch.ElapsedMilliseconds, reusable);
                    runs.Add(run);
                    Console.WriteLine($"{index:D3}/{workspace.AnalysisNames.Count:D3} {name}: {run.Status}{(reusable ? " (reused)" : "")}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    runs.Add(new(index, name, "failed", stopwatch.ElapsedMilliseconds, settings, 0, 0, 0, null, exception.ToString()));
                    Console.WriteLine($"{index:D3}/{workspace.AnalysisNames.Count:D3} {name}: FAILED");
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            // Cancellation stops computation, but cannot erase prior unvisited
            // results. Retain only matching settings and verified saved bytes.
            for (var index = runs.Count + 1; index <= workspace.AnalysisNames.Count; index++)
            {
                var name = workspace.AnalysisNames[index - 1];
                var settings = workspace.MergeAnalysisSettings(name, settingsByName.GetValueOrDefault(name));
                var relativeOutput = $"current/{index:D3}-{Slug(name)}.json";
                var outputPath = Path.Combine(options.Output, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
                var prior = previous?.Analyses?.FirstOrDefault(run => run.Name == name && run.Output == relativeOutput);
                if (!CanReuse(prior, settings, outputPath))
                {
                    runs.Add(new(index, name, "not-requested", 0, settings, 0, 0, 0, null, null,
                        "Cancelled before execution; no matching reusable result."));
                    continue;
                }
                try
                {
                    if (JsonFiles.Hash(File.ReadAllBytes(outputPath)) != prior!.OutputSha256)
                        throw new InvalidDataException($"Reusable result checksum mismatch: {relativeOutput}");
                    var view = JsonSerializer.Deserialize<AnalysisView>(await File.ReadAllTextAsync(outputPath), ViewJson)
                        ?? throw new InvalidDataException($"Could not read {outputPath}.");
                    runs.Add(CompletedRun(index, name, settings, relativeOutput, outputPath, view,
                        prior.ElapsedMilliseconds, reused: true));
                }
                catch (Exception exception)
                {
                    runs.Add(new(index, name, "failed", 0, settings, 0, 0, 0, null, exception.ToString()));
                }
            }
            throw;
        }
        finally
        {
            JsonFiles.Write(manifestPath, new CurrentManifest(DateTimeOffset.UtcNow, options.Source, sourceHash,
                optic.SurfaceGroup.Items.Count, optic.Fields.Count, optic.Wavelengths.Count, runs, started,
                DateTimeOffset.UtcNow, codeFingerprint, JsonFiles.Hash(settingsBytes), cancelled), ViewJson);
        }
        var failed = runs.Count(run => run.Status == "failed");
        Console.WriteLine($"Completed {runs.Count}; captured={runs.Count(run => run.Status == "captured")}; not-requested={runs.Count(run => run.Status == "not-requested")}; failed={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static bool CanReuse(AnalysisRun? prior, IReadOnlyDictionary<string, string> settings, string outputPath) =>
        File.Exists(outputPath) && prior?.Settings is { } priorSettings
        && !string.IsNullOrWhiteSpace(prior.OutputSha256)
        && priorSettings.Count == settings.Count
        && settings.All(pair => priorSettings.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static AnalysisRun CompletedRun(int index, string name, IReadOnlyDictionary<string, string> settings,
        string relativeOutput, string outputPath, AnalysisView view, long elapsedMilliseconds, bool reused)
    {
        var series = view.PlotPanes.Count > 0 ? view.PlotPanes.SelectMany(pane => pane.Series).ToArray() : view.SeriesList.ToArray();
        return new(index, name, CaptureStatus(view), elapsedMilliseconds, settings,
            series.Length, view.PlotPanes.Count, series.Sum(item => item.Points.Count), relativeOutput,
            null, view.OutcomeReason, reused, JsonFiles.Hash(File.ReadAllBytes(outputPath)));
    }

    private static void ProtectInputs(WorkbenchRecaptureOptions options)
    {
        bool Contains(string root, string path)
        {
            var relative = Path.GetRelativePath(root, path);
            return !Path.IsPathRooted(relative) && relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        foreach (var input in new[] { options.Source, options.SettingsManifest })
        {
            if (Contains(options.Output, input)) throw new IOException("Output must not contain the source or settings manifest.");
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(input)!); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "manifest.json"))
                    && Directory.Exists(Path.Combine(directory.FullName, "analyses")) && Contains(directory.FullName, options.Output))
                    throw new IOException("Native baseline directories are read-only; use a separate recapture output directory.");
        }
    }

    private static string CaptureStatus(AnalysisView view) => view.Outcome switch
    {
        AnalysisOutcome.Success => "captured",
        AnalysisOutcome.Unavailable => "unavailable",
        AnalysisOutcome.NotApplicable => "not-applicable",
        _ => throw new InvalidDataException("Unknown analysis outcome.")
    };

    private static string Slug(string name) => string.Join('-', new string(name.ToLowerInvariant()
        .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray())
        .Split('-', StringSplitOptions.RemoveEmptyEntries));

    private sealed record SettingsManifest(IReadOnlyList<SettingsAnalysis> Analyses);
    private sealed record SettingsAnalysis(string Name, Dictionary<string, string> Settings);
    private sealed record AnalysisRun(int Index, string Name, string Status, long ElapsedMilliseconds,
        IReadOnlyDictionary<string, string> Settings, int SeriesCount, int PaneCount, int PointCount,
        string? Output, string? Error, string? OutcomeReason = null, bool Reused = false, string? OutputSha256 = null);
    private sealed record CurrentManifest(DateTimeOffset CreatedUtc, string SourceFile, string SourceSha256,
        int SurfaceCount, int FieldCount, int WavelengthCount, IReadOnlyList<AnalysisRun> Analyses,
        DateTimeOffset StartedUtc, DateTimeOffset CompletedUtc, string CodeFingerprint = "",
        string SettingsManifestSha256 = "", bool Cancelled = false);
}
