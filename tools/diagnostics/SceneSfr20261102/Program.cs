using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using SkiaSharp;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "validation/rechecks/2026-11-02/ansys-scene-sfr"); Directory.CreateDirectory(output);
var sourceZip = Path.Combine(root, "local-data/references/ansys-scene-sfr-2025r1/Data_MTF_KB.zip");
var summaries = new List<object>();
using var app = WorkbenchApplication.Create();
foreach (var label in new[] { "double-gauss", "lens-710-original" })
{
    var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zmx");
    string path; string sourceIdentity;
    try
    {
        if (label == "double-gauss")
        {
            path = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx");
            sourceIdentity = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }
        else
        {
            if (!File.Exists(sourceZip)) { summaries.Add(new { label, Status = "not-run", Reason = "Original reference ZIP unavailable" }); continue; }
            using var zip = ZipFile.OpenRead(sourceZip);
            var entry = zip.GetEntry("Data - MTF KB/ZOS/Lens 710.ZMX")!;
            using var source = entry.Open(); using var memory = new MemoryStream(); source.CopyTo(memory);
            var bytes = memory.ToArray(); sourceIdentity = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllBytes(temporary, bytes); path = temporary;
        }
        await app.Documents.OpenAsync(path);
        var before = app.Documents.GetSnapshot();
        var timer = Stopwatch.StartNew();
        var request = new CameraSceneSfrRequest(new(CellSizeMillimeters: 2000, DistanceMillimeters: 100000,
                CenterYMillimeters: Math.Cos(5 * Math.PI / 180) * 1000, CenterXMillimeters: -Math.Sin(5 * Math.PI / 180) * 1000),
            Width: 96, Height: 96, PixelSamplesPerAxis: 2, Edge: new(new(0, 0, 96, 96), HalfWindowPixels: 24));
        if (args.Length > 1) request = request with { PupilRings = int.Parse(args[1]), PupilArms = int.Parse(args[2]) };
        var result = await app.CameraImaging.SimulateSceneSlantedEdgeAsync(request);
        timer.Stop();
        var archive = Path.Combine(output, label + ".scene-sfr.zip");
        await app.CameraImaging.ExportSceneSlantedEdgeAsync(result, archive);
        var linear = result.Frame.ToLinearImage().Bands[0].WattsPerSquareMeter; var peak = linear.Max();
        using (var bitmap = new SKBitmap(result.Frame.Width, result.Frame.Height))
        {
            for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            { var v = (byte)Math.Round(Math.Clamp(peak > 0 ? linear[y * bitmap.Width + x] / peak : 0, 0, 1) * 255); bitmap.SetPixel(x, y, new(v, v, v)); }
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(Path.Combine(output, label + "-linear-preview.png")); png.SaveTo(file);
        }
        var reopened = await app.CameraImaging.OpenSceneSlantedEdgeAsync(archive);
        var replay = await app.CameraImaging.RecomputeSceneSlantedEdgeAsync(reopened);
        var exact = result.Frame.Bands.Zip(replay.Frame.Bands).All(pair => pair.First.PixelPowerWatts.SequenceEqual(pair.Second.PixelPowerWatts));
        summaries.Add(new { label, Status = "rendered", SourceSha256 = sourceIdentity, result.Algorithm, result.LensSha256,
            result.Request, result.Frame.LaunchedRays, result.Frame.PassedLensRays, result.Frame.SceneHitRays,
            result.Frame.SurfaceInteractions, TotalPowerWatts = result.Frame.ToLinearImage().Power.TotalPowerWatts,
            result.Frame.FirstLensVertexZMillimeters, result.Frame.SensorPlaneZMillimeters,
            result.EdgeFailure, Mtf50CyclesPerPixel = result.Edge?.Measurement.Mtf50CyclesPerPixel,
            EdgeFitRmsPixels = result.Edge?.Measurement.EdgeFitRmsPixels,
            MeasurementSucceeded = result.Edge is not null, ExactReplay = exact,
            UnchangedDocumentRevision = app.Documents.GetSnapshot().Revision == before.Revision,
            ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Scope = "Native geometric chart at declared control conditions; 96x96 complete control sensor. NOT original 5000x3000 camera/ROM parity, formal MTF agreement or sampling convergence." });
        Console.WriteLine($"{label}: rendered; ROI {(result.Edge is null ? result.EdgeFailure : "measured")}; exact replay {exact}");
    }
    catch (Exception ex)
    {
        summaries.Add(new { label, Status = "failed", Exception = ex.GetType().Name, ex.Message }); Console.WriteLine($"{label}: {ex.Message}");
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
}
File.WriteAllText(Path.Combine(output, "native-scene-summary.json"), JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true }));
