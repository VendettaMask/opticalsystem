using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;

var root = Path.GetFullPath(args[0]);
var output = Path.Combine(root, "validation/rechecks/2026-11-20/scene-mtf-sampling");
Directory.CreateDirectory(output);
using var app = WorkbenchApplication.Create();
await app.Documents.OpenAsync(Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx"));
var baseline = new CameraSceneLinearBatchRequest(new(2, 1, 2000, 100_000), Width: 64, Height: 64,
    PixelSamplesPerAxis: 2, PupilRings: 8, PupilArms: 16, RowsPerStrip: 16,
    Mode: SceneLinearBatchMode.ExplicitSources, Sources: [new("d 587.6 nm", 587.6)],
    PupilSampling: CameraPupilSampling.AreaStratifiedDisk);
var cases = new List<(string Name, CameraSceneLinearBatchRequest Request)>();
foreach (var rings in new[] { 4, 8, 16, 24, 32 }) cases.Add(($"area-{rings}", baseline with { PupilRings = rings }));
cases.Add(("pixel-centre", baseline with { PixelSamplesPerAxis = 1 }));
cases.Add(("pixel-4", baseline with { PixelSamplesPerAxis = 4 }));
cases.Add(("angle-7", baseline with { PupilRings = 24, Chart = baseline.Chart! with { RotationZDegrees = 7 } }));
cases.Add(("phase-quarter", baseline with { PupilRings = 24, Chart = baseline.Chart! with { CenterXMillimeters = .75 } }));
cases.Add(("gaussian-24", baseline with { PupilRings = 24, PupilSampling = CameraPupilSampling.GaussianRings }));
cases.Add(("gaussian-32", baseline with { PupilRings = 32, PupilSampling = CameraPupilSampling.GaussianRings }));
cases.Add(("horizontal", baseline with { PupilRings = 24, Chart = baseline.Chart! with { RotationZDegrees = 95 } }));
var reports = new List<object>();
foreach (var (name, request) in cases)
{
    var path = Path.Combine(output, name + ".scene-linear.zip");
    var plan = app.CameraImaging.PrepareSceneLinearBatch(request);
    var watch = Stopwatch.StartNew();
    var artifact = File.Exists(path) ? await app.CameraImaging.OpenSceneLinearBatchAsync(path)
        : await app.CameraImaging.RunSceneLinearBatchAsync(plan, path);
    if (artifact.Manifest.Plan.LensSha256 != plan.LensSha256
        || JsonSerializer.Serialize(artifact.Manifest.Plan.Request) != JsonSerializer.Serialize(plan.Request))
        throw new InvalidDataException($"Frozen input differs for {name}.");
    LinearSfrRoi edge = new("edge", new(4, 0, 56, 64), HalfWindowPixels: 16);
    if (name == "horizontal") edge = edge with { Region = new(0, 4, 64, 56), Orientation = LinearEdgeOrientation.NearHorizontal };
    LinearSfrRoi[] regions = name == "area-24"
        ? [edge, edge with { Id = "window-12", HalfWindowPixels = 12 }, edge with { Id = "window-20", HalfWindowPixels = 20 },
            edge with { Id = "roi-short", Region = new(4, 8, 56, 48) }]
        : [edge];
    var roiRequest = new CameraSceneRoiSfrRequest([0], regions);
    var report = await app.CameraImaging.CompareSceneLinearMtfAsync(artifact, roiRequest, 129);
    var dense = name == "area-24" ? await app.CameraImaging.CompareSceneLinearMtfAsync(artifact, roiRequest, 257) : null;
    var measured = await app.CameraImaging.MeasureSceneLinearRoisAsync(artifact, roiRequest);
    await app.CameraImaging.SaveSceneLinearRoisAsync(measured, Path.Combine(output, name + ".scene-sfr-rois.json"));
    reports.Add(new { Name = name, Request = request, Archive = Path.GetRelativePath(root, path).Replace('\\', '/'),
        ArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))),
        artifact.Manifest.CompletedRays, artifact.Manifest.SurfaceInteractions, Seconds = watch.Elapsed.TotalSeconds,
        report, Dense = dense });
    File.WriteAllText(Path.Combine(output, "sampling.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{name}: {report.Tasks[0].Regions[0].Measurement.Status}; MTF50 " +
        $"{report.Tasks[0].Regions[0].Measurement.Edge?.Measurement.Mtf50CyclesPerPixel:G9}; {watch.Elapsed.TotalSeconds:F1}s");
}
