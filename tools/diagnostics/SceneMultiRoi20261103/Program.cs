using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "validation/rechecks/2026-11-03/scene-multi-roi-sfr"); Directory.CreateDirectory(output);
using var app = WorkbenchApplication.Create();
var receipts = new List<object>();
foreach (var name in new[] { "double-gauss-512-lens-wavelengths", "double-gauss-128-article-source-shapes" })
{
    var path = Path.Combine(root, "validation/rechecks/2026-11-02/scene-linear-batch", name + ".scene-linear.zip");
    var artifact = await app.CameraImaging.OpenSceneLinearBatchAsync(path);
    LinearSfrRoi[] rois = artifact.Manifest.Plan.Request.Width == 512
        ? [new("V top", new(160, 8, 192, 128)), new("V middle", new(160, 192, 192, 128)),
           new("V bottom", new(160, 360, 192, 128)), new("H candidate", new(8, 144, 144, 128), LinearEdgeOrientation.NearHorizontal)]
        : [new("V centre", new(0, 0, 128, 128)), new("H candidate", new(0, 0, 128, 128), LinearEdgeOrientation.NearHorizontal)];
    await Measure(artifact, name, rois);
}
// A separately declared native control exposes both checker directions in one full sensor.
// It retains the actual prescription/image surface, uses one explicitly selected wavelength and no refocus.
var lens = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx");
await app.Documents.OpenAsync(lens); var primary = app.Documents.GetSnapshot();
var plan = app.CameraImaging.PrepareSceneLinearBatch(new(new(2, 2, 2000, 100000), Width: 128, Height: 128,
    PixelSamplesPerAxis: 2, PupilRings: 8, PupilArms: 16, RowsPerStrip: 17,
    Mode: SceneLinearBatchMode.ExplicitSources, Sources: [new("d 587.6 nm", 587.6, Provenance: "Explicit native monochrome control; current Double Gauss d line") ]));
var crossPath = Path.Combine(output, "double-gauss-cross-128.scene-linear.zip");
var cross = args.Length > 1 && args[1] == "verify" ? await app.CameraImaging.OpenSceneLinearBatchAsync(crossPath)
    : await app.CameraImaging.RunSceneLinearBatchAsync(plan, crossPath,
        new Callback<CameraSceneLinearBatchProgress>(p => { if (p.CompletedRows == p.Height) Console.WriteLine($"cross: {p.Phase}, {p.CompletedRays} rays"); }));
await Measure(cross, "double-gauss-cross-128", [new("V top", new(24, 0, 80, 48), HalfWindowPixels: 12),
    new("H left", new(0, 24, 48, 80), LinearEdgeOrientation.NearHorizontal, 12),
    new("V bottom", new(24, 80, 80, 48), HalfWindowPixels: 12),
    new("H right", new(80, 24, 48, 80), LinearEdgeOrientation.NearHorizontal, 12),
    new("crossing", new(16, 16, 96, 96), HalfWindowPixels: 16)]);
await File.WriteAllTextAsync(Path.Combine(output, "native-roi-summary.json"), JsonSerializer.Serialize(new
{
    Date = "2026-11-03", Scope = "Native image-only ROI measurement and quality gates. No formal MTF/independent-tool accuracy or convergence claim.",
    UnchangedLiveRevision = app.Documents.GetSnapshot().Revision == primary.Revision,
    Receipts = receipts
}, new JsonSerializerOptions { WriteIndented = true }));

async Task Measure(CameraSceneLinearBatchArtifact artifact, string label, LinearSfrRoi[] rois)
{
    var report = await app.CameraImaging.MeasureSceneLinearRoisAsync(artifact,
        new(Enumerable.Range(0, artifact.Manifest.Plan.Tasks.Count).ToArray(), rois));
    var path = Path.Combine(output, label + ".scene-sfr-rois.json"); await app.CameraImaging.SaveSceneLinearRoisAsync(report, path);
    var replay = await app.CameraImaging.OpenSceneLinearRoisAsync(artifact, path);
    var equal = JsonSerializer.Serialize(report) == JsonSerializer.Serialize(replay);
    var results = report.Tasks.Select(t => new { t.TaskIndex, t.Name, t.IrradianceSha256, Regions = t.Regions.Select(r => new
    {
        r.Roi, r.Status, r.Failure, r.Fit, r.Geometry, Mtf50 = r.Edge?.Measurement.Mtf50CyclesPerPixel,
        Mtf10 = r.Edge?.Measurement.Mtf10CyclesPerPixel
    }) }).ToArray();
    receipts.Add(new { label, InputArchive = Path.GetRelativePath(root, artifact.ArchivePath),
        InputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(artifact.ArchivePath))), report.SourceManifestSha256,
        report.LensSha256, report.Width, report.Height, report.PixelPitchMicrometers,
        artifact.Manifest.CompletedRays, artifact.Manifest.SurfaceInteractions,
        ReportPath = Path.GetRelativePath(root, path), ReportSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
        ReplayedExactly = equal, Results = results });
    Console.WriteLine($"{label}: {report.Tasks.Sum(t => t.Regions.Count(r => r.Edge is not null))}/{report.Tasks.Sum(t => t.Regions.Count)} measured; replay {equal}");
    foreach (var task in report.Tasks) foreach (var r in task.Regions)
        Console.WriteLine($"task {task.TaskIndex + 1}, {r.Roi.Id}: {r.Status}; rms {r.Fit?.EdgeFitRmsPixels:G5}; MTF50 {r.Edge?.Measurement.Mtf50CyclesPerPixel:G7}");
}
sealed class Callback<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
