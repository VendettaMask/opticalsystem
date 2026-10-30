using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

if (args.Length is < 2 or > 3) throw new ArgumentException("Frozen experiment, output directory, optional --replay.");
var replay = args.Length == 3 && args[2] == "--replay";
using var app = WorkbenchApplication.Create();
var original = await app.CameraImaging.OpenExperimentAsync(Path.GetFullPath(args[0]));
var directory = Path.GetFullPath(args[1]); Directory.CreateDirectory(directory);
var region = original.SamplingReport?.Region ?? new CameraSamplingRegion((original.B.Request.Width - 4) / 2, (original.B.Request.Height - 4) / 2);
CameraSamplingReport? report = null;
app.Documents.NewBlank();
var pending = new[] { CameraSamplingAxis.Pupil, CameraSamplingAxis.Scene }.Select(axis =>
    app.CameraImaging.CheckSamplingAsync(original.B, region, axis, completeLadder: true)).ToList();
while (pending.Count > 0)
{
    var task = await Task.WhenAny(pending); pending.Remove(task); var result = await task;
    if (result.SavedElectronDifference.MaximumAbsolute != 0 || result.SavedBandDifferences.Any(d => d.MaximumAbsolute != 0))
        throw new InvalidOperationException("Original frozen B changed.");
    report = report is null ? result : report with { Comparisons = report.Comparisons.Concat(result.Comparisons).OrderBy(c => c.Axis).ToArray() };
    foreach (var level in Flatten(result.Comparisons.Single()))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { level.Axis, level.Status, level.RequestedSettings,
            level.ElectronDifference, level.Refined?.Execution, level.Message }));
        if (level.Status != CameraSamplingComparisonStatus.Compared) throw new InvalidOperationException("Unfinished level: " + level.Message);
    }
    await app.CameraImaging.ExportSamplingAsync(original.B, report, Path.Combine(directory, replay ? "fresh-replay.json" : "convergence.json"));
    if (!replay) await app.CameraImaging.SaveExperimentAsync(original with { SamplingReport = report }, Path.Combine(directory, "convergence.camera-experiment.zip"));
}
if (replay)
{
    var expected = original.SamplingReport ?? throw new InvalidOperationException("No saved report to replay.");
    Equal(expected.Baseline, report!.Baseline);
    foreach (var c in report.Comparisons)
    {
        var old = Flatten(expected.Comparisons.Single(e => e.Axis == c.Axis)).ToArray(); var actual = Flatten(c).ToArray();
        if (old.Length != actual.Length) throw new InvalidOperationException("Level count changed.");
        for (var i = 0; i < old.Length; i++)
        {
            if (old[i].Status != actual[i].Status || old[i].RequestedSettings != actual[i].RequestedSettings
                || old[i].ElectronDifference != actual[i].ElectronDifference || old[i].RawDifference != actual[i].RawDifference
                || !old[i].BandDifferences!.SequenceEqual(actual[i].BandDifferences!)) throw new InvalidOperationException("Saved metrics differ.");
            Equal(old[i].Refined!, actual[i].Refined!);
        }
    }
    Console.WriteLine("REPLAY: every band, electron, noiseless RAW, setting, execution count and adjacent-level metric matches exactly.");
}
else Console.WriteLine("DELIVERY: all pupil and scene levels completed with frozen original B and unchanged prescription/tolerances.");

static IEnumerable<CameraSamplingComparison> Flatten(CameraSamplingComparison comparison) => new[] { comparison }.Concat(comparison.FurtherRefinements ?? []);
static void Equal(CameraSamplingSnapshot a, CameraSamplingSnapshot b)
{
    if (a.Settings != b.Settings || a.LocalBayerPattern != b.LocalBayerPattern || a.Execution != b.Execution
        || !a.PhotoElectrons.SequenceEqual(b.PhotoElectrons) || !a.NoiselessRawDn.SequenceEqual(b.NoiselessRawDn)
        || a.Bands.Where((band, i) => !band.PixelPowerWatts.SequenceEqual(b.Bands[i].PixelPowerWatts)).Any())
        throw new InvalidOperationException("Saved per-pixel output, CFA or work accounting differs.");
}
