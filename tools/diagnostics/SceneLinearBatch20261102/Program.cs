using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "validation/rechecks/2026-11-02/scene-linear-batch"); Directory.CreateDirectory(output);
var lensPath = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx");
using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(lensPath);
var chart = new EmissiveCheckerboardSettings(CellSizeMillimeters: 2000, DistanceMillimeters: 100000,
    CenterXMillimeters: -Math.Sin(Math.PI / 36) * 1000, CenterYMillimeters: Math.Cos(Math.PI / 36) * 1000);
var monochrome = app.CameraImaging.PrepareSceneLinearBatch(new(chart, Width: 512, Height: 512, PupilRings: 2, PupilArms: 4, RowsPerStrip: 17));
var finite = app.CameraImaging.PrepareSceneLinearBatch(new(chart, Width: 128, Height: 128, PupilRings: 2, PupilArms: 4,
    Mode: SceneLinearBatchMode.ArticleSourceShapes, RowsPerStrip: 17));
var rejected = app.CameraImaging.PrepareSceneLinearBatch(new(chart, Width: 5000, Height: 3000, PixelPitchMicrometers: .5));
// Run both against their shared, frozen original lens while a different document is current.
await app.Documents.OpenAsync(Path.Combine(root, "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX"));
var activeRevision = app.Documents.GetSnapshot().Revision;
var summaries = new List<object>();
foreach (var (label, plan) in new[] { ("double-gauss-512-lens-wavelengths", monochrome), ("double-gauss-128-article-source-shapes", finite) })
{
    try
    {
        var timer = Stopwatch.StartNew(); var path = Path.Combine(output, label + ".scene-linear.zip");
        var artifact = args.Length > 1 && args[1] == "verify" ? await app.CameraImaging.OpenSceneLinearBatchAsync(path)
            : await app.CameraImaging.RunSceneLinearBatchAsync(plan, path,
                new Callback<CameraSceneLinearBatchProgress>(p => { if (p.Phase != "Tracing" || p.CompletedRows == p.Height) Console.WriteLine($"{label}: {p.Phase}, task {p.TaskIndex + 1}, {p.CompletedRays}/{p.TotalRays} rays"); }));
        var opened = await app.CameraImaging.OpenSceneLinearBatchAsync(path);
        var tasks = new List<object>(); bool? legacyEqual = null;
        for (var task = 0; task < plan.Tasks.Count; task++)
        {
            var image = await app.CameraImaging.ReadSceneLinearTaskAsync(opened, task);
            tasks.Add(new { Name = image.Name, Pixels = image.PowerWatts.Count, SumPowerWatts = image.PowerWatts.Sum(), MinimumPower = image.PowerWatts.Min(), MaximumPower = image.PowerWatts.Max(),
                FirstPixel = image.PowerWatts[0], LastPixel = image.PowerWatts[^1], MaximumIrradiance = image.IrradianceWattsPerSquareMeter.Max() });
            if (task == 0 && plan.Request.Mode == SceneLinearBatchMode.CurrentLensWavelengths)
            {
                // Compare the identical frozen physical snapshot. A fresh import may resolve material
                // catalogues differently from the application document and is not a strip-equivalence reference.
                var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(opened.Manifest.Plan.LensSnapshotJson,
                    new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!);
                if (optic.Apodization is UniformApodization or ZemaxApodization { Type: ZemaxApodizationType.Uniform }) optic.Apodization = null;
                var n = plan.Tasks[0].Nodes.Single();
                var scene = EmissiveCheckerboard.Create(chart, [new(n.WavelengthNanometers, n.BrightRadiance, n.DarkRadiance)], optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z);
                var unpartitioned = DirectCameraRenderer.Render(optic, scene, new(512, 512, 3, new(1), 2, 4));
                legacyEqual = unpartitioned.DetectorPower.Bands[0].PixelBandPowerWatts.SequenceEqual(image.PowerWatts);
            }
        }
        // Link evidence PNGs directly to the authoritative ZIP, rather than retain duplicate exchange images.
        summaries.Add(new { label, Status = "complete", opened.Manifest.Plan.LensSha256, Plan = PlanReceipt(opened.Manifest.Plan), artifact.Manifest.CompletedRays,
            artifact.Manifest.SurfaceInteractions, artifact.Manifest.Png, Tasks = tasks, UnpartitionedFirstBandBitExact = legacyEqual,
            UnchangedActiveDocumentRevision = app.Documents.GetSnapshot().Revision == activeRevision,
            Operation = args.Length > 1 && args[1] == "verify" ? "verify-existing-archive" : "generate-and-verify",
            MeasuredOperationSeconds = timer.Elapsed.TotalSeconds,
            ArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            Scope = "Complete declared native sensor, one immutable lens/chart, all source tasks and nodes, global existing budget. No Ansys ROM/original 5000x3000, formal MTF or convergence claim." });
        Console.WriteLine($"{label}: complete, full-frame comparison {legacyEqual}");
    }
    catch (Exception ex) { summaries.Add(new { label, Status = "failed", Exception = ex.GetType().Name, ex.Message }); Console.WriteLine($"{label}: {ex}"); }
}
File.WriteAllText(Path.Combine(output, "native-batch-summary.json"), JsonSerializer.Serialize(new
{
    SourceFileSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(lensPath))), SharedLensSnapshot = monochrome.LensSha256 == finite.LensSha256,
    Results = summaries, OriginalRasterPreflight = PlanReceipt(rejected),
    OriginalResources = "../ansys-scene-sfr/original-reference-inventory.json"
}, new JsonSerializerOptions { WriteIndented = true }));

static object PlanReceipt(CameraSceneLinearBatchPlan plan) => new
{
    plan.Format, plan.SchemaVersion, plan.Algorithm, plan.SourceDocument, plan.LensSha256,
    plan.Request, plan.Tasks, plan.Work, plan.Float64Values, plan.WithinBudget, plan.BudgetFailure
};

sealed class Callback<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
