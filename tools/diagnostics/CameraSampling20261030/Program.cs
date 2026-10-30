using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--replay-only")
    throw new ArgumentException("Supply frozen experiment path, output directory, and optional --replay-only.");
using var app = WorkbenchApplication.Create();
var original = await app.CameraImaging.OpenExperimentAsync(Path.GetFullPath(args[0]));
var directory = Path.GetFullPath(args[1]); Directory.CreateDirectory(directory);
var region = new CameraSamplingRegion((original.B.Request.Width - 4) / 2, (original.B.Request.Height - 4) / 2);
CameraSamplingReport? report = original.SamplingReport;
foreach (var axis in args.Length == 2 ? Enum.GetValues<CameraSamplingAxis>() : [])
{
    report = await app.CameraImaging.CheckSamplingAsync(original.B, region, axis, report);
    if (report.SavedElectronDifference.MaximumAbsolute != 0 || report.SavedBandDifferences.Any(d => d.MaximumAbsolute != 0))
        throw new InvalidOperationException("Regional baseline changed an original B value.");
    await app.CameraImaging.SaveExperimentAsync(original with { SamplingReport = report }, Path.Combine(directory, "scene-32-sampling.camera-experiment.zip"));
    await app.CameraImaging.ExportSamplingAsync(original.B, report, Path.Combine(directory, "scene-32-sampling.json"));
    Console.WriteLine(JsonSerializer.Serialize(report.Comparisons.Single(c => c.Axis == axis), new JsonSerializerOptions
    { Converters = { new JsonStringEnumConverter() } }));
}
app.Documents.NewBlank();
var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(directory, "scene-32-sampling.camera-experiment.zip"));
var replay = await app.CameraImaging.CheckSamplingAsync(opened.B, region, CameraSamplingAxis.Pixel);
if (replay.SavedElectronDifference.MaximumAbsolute != 0 || replay.SavedBandDifferences.Any(d => d.MaximumAbsolute != 0))
    throw new InvalidOperationException("Reopened baseline changed.");
if (replay.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel).ElectronDifference
    != report!.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel).ElectronDifference)
    throw new InvalidOperationException("Reopened sampling differs.");
var actual = replay.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel);
var expected = report.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel);
if (actual.Status != expected.Status || actual.Refined is { } frame && (expected.Refined is not { } prior
    || !frame.PhotoElectrons.SequenceEqual(prior.PhotoElectrons) || !frame.NoiselessRawDn.SequenceEqual(prior.NoiselessRawDn)
    || frame.Bands.Where((b, i) => !b.PixelPowerWatts.SequenceEqual(prior.Bands[i].PixelPowerWatts)).Any()))
    throw new InvalidOperationException("Reopened per-pixel band/electron/noiseless RAW values differ.");
await app.CameraImaging.ExportSamplingAsync(opened.B, replay, Path.Combine(directory, "scene-32-fresh-pixel-replay.json"));
Console.WriteLine("All five axes attempted; frozen regional baseline and reopened pixel comparison match exactly.");
