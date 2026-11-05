using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;

var root = Path.GetFullPath(args[0]);
var output = Path.Combine(root, "validation/rechecks/2026-11-05/scene-mtf");
Directory.CreateDirectory(output);
using var app = WorkbenchApplication.Create();

if (args.Contains("render"))
{
    var lens = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx");
    await app.Documents.OpenAsync(lens);
    var baseline = new CameraSceneLinearBatchRequest(new(2, 1, 2000, 100000), Width: 64, Height: 64,
        PixelSamplesPerAxis: 2, PupilRings: 4, PupilArms: 16, RowsPerStrip: 16,
        Mode: SceneLinearBatchMode.ExplicitSources, Sources: [new("d 587.6 nm", 587.6)]);
    foreach (var (name, settings) in new (string, CameraSceneLinearBatchRequest)[]
    {
        ("pupil-low", baseline with { PupilRings = 2, PupilArms = 8 }),
        ("baseline", baseline),
        ("pupil-high", baseline with { PupilRings = 8 }),
        ("pupil-16", baseline with { PupilRings = 16 }),
        ("pupil-24", baseline with { PupilRings = 24 }),
        ("pupil-32", baseline with { PupilRings = 32 }),
        ("pupil-azimuth-32", baseline with { PupilRings = 16, PupilArms = 32 }),
        ("pixel-centre", baseline with { PixelSamplesPerAxis = 1 }),
        ("pixel-high", baseline with { PixelSamplesPerAxis = 4 }),
        ("joint", baseline with { PupilRings = 8, PixelSamplesPerAxis = 4 }),
        ("joint-phase", baseline with { PupilRings = 8, PixelSamplesPerAxis = 4,
            Chart = baseline.Chart! with { CenterXMillimeters = .75 } }),
        ("joint-angle", baseline with { PupilRings = 8, PixelSamplesPerAxis = 4,
            Chart = baseline.Chart! with { RotationZDegrees = 7 } })
    })
    {
        var path = Path.Combine(output, name + ".scene-linear.zip");
        var plan = app.CameraImaging.PrepareSceneLinearBatch(settings);
        var artifact = File.Exists(path) ? await app.CameraImaging.OpenSceneLinearBatchAsync(path)
            : await app.CameraImaging.RunSceneLinearBatchAsync(plan, path);
        if (artifact.Manifest.Plan.LensSha256 != plan.LensSha256
            || JsonSerializer.Serialize(artifact.Manifest.Plan.Request) != JsonSerializer.Serialize(plan.Request))
            throw new InvalidDataException($"Frozen lens or render settings differ for {name}.");
        Console.WriteLine($"{name}: {artifact.Manifest.CompletedRays} rays");
    }
}
else
{
    var reports = new List<object>();
    const string old = "validation/rechecks/2026-11-03/scene-multi-roi-sfr";
    const string m2 = "validation/rechecks/2026-11-02/scene-linear-batch";
    LinearSfrRoi[] cross = [new("V top", new(24, 0, 80, 48), HalfWindowPixels: 12),
        new("H left", new(0, 24, 48, 80), LinearEdgeOrientation.NearHorizontal, 12),
        new("V bottom", new(24, 80, 80, 48), HalfWindowPixels: 12),
        new("H right", new(80, 24, 48, 80), LinearEdgeOrientation.NearHorizontal, 12),
        new("crossing", new(16, 16, 96, 96), HalfWindowPixels: 16)];
    await Compare("cross", $"{old}/double-gauss-cross-128.scene-linear.zip", cross, [0], true);
    await Compare("coarse-512", $"{m2}/double-gauss-512-lens-wavelengths.scene-linear.zip",
        [new("V top", new(160, 8, 192, 128)), new("V middle", new(160, 192, 192, 128)),
         new("V bottom", new(160, 360, 192, 128)), new("H candidate", new(8, 144, 144, 128), LinearEdgeOrientation.NearHorizontal)], [0, 1, 2]);
    await Compare("coarse-spectra", $"{m2}/double-gauss-128-article-source-shapes.scene-linear.zip",
        [new("V centre", new(0, 0, 128, 128)),
         new("H candidate", new(0, 0, 128, 128), LinearEdgeOrientation.NearHorizontal)], [0, 1, 2]);
    foreach (var name in new[] { "pupil-low", "baseline", "pupil-high", "pupil-16", "pupil-24", "pupil-32", "pupil-azimuth-32", "pixel-centre", "pixel-high", "joint", "joint-phase", "joint-angle" })
    {
        LinearSfrRoi edge = new("edge", new(4, 0, 56, 64), HalfWindowPixels: 16);
        LinearSfrRoi[] rois = name == "joint"
            ? [edge, edge with { Id = "window-12", HalfWindowPixels = 12 },
                edge with { Id = "window-20", HalfWindowPixels = 20 },
                edge with { Id = "roi-short", Region = new(4, 8, 56, 48) },
                edge with { Id = "roi-narrow", Region = new(8, 0, 48, 64) }]
            : [edge];
        await Compare(name, Path.GetRelativePath(root, Path.Combine(output, name + ".scene-linear.zip")), rois, [0], name == "joint");
    }

    await File.WriteAllTextAsync(Path.Combine(output, "formal-mtf.json"),
        JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));

    // Keep the production estimator's default intact and vary only ESF bin density on the same original ROI.
    var joint = await app.CameraImaging.OpenSceneLinearBatchAsync(Path.Combine(output, "joint.scene-linear.zip"));
    var image = await app.CameraImaging.ReadSceneLinearTaskAsync(joint, 0);
    const int roiWidth = 56, roiHeight = 64, firstColumn = 4;
    var crop = Enumerable.Range(0, roiHeight).SelectMany(y => Enumerable.Range(firstColumn, roiWidth)
        .Select(x => image.IrradianceWattsPerSquareMeter[y * image.Width + x])).ToArray();
    var peak = crop.Max();
    var bins = new List<object>();
    foreach (var density in new[] { 4, 8, 16 })
    {
        try
        {
            var m = SlantedEdgeSfr.MeasureAtBinDensity(crop.Select(v => v / peak).ToArray(), roiWidth, roiHeight,
                new(0, 0, roiWidth, roiHeight), image.PixelPitchMicrometers, 16, density);
            bins.Add(new { Density = density, m.Curve, m.Mtf50CyclesPerPixel });
        }
        catch (InvalidOperationException ex) { bins.Add(new { Density = density, Failure = ex.Message }); }
    }
    await File.WriteAllTextAsync(Path.Combine(output, "bin-sensitivity.json"),
        JsonSerializer.Serialize(bins, new JsonSerializerOptions { WriteIndented = true }));

    async Task Compare(string name, string relative, LinearSfrRoi[] rois, int[] taskIndices, bool checkReferenceDensity = false)
    {
        var path = Path.Combine(root, relative);
        var artifact = await app.CameraImaging.OpenSceneLinearBatchAsync(path);
        var request = new CameraSceneRoiSfrRequest(taskIndices, rois);
        var report = await app.CameraImaging.CompareSceneLinearMtfAsync(artifact, request);
        var dense = checkReferenceDensity ? await app.CameraImaging.CompareSceneLinearMtfAsync(artifact, request, 257) : null;
        reports.Add(new
        {
            Name = name, Archive = relative.Replace('\\', '/'),
            ArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))),
            artifact.Manifest.CompletedRays, artifact.Manifest.SurfaceInteractions,
            artifact.Manifest.Plan.Request.Width, artifact.Manifest.Plan.Request.Height,
            artifact.Manifest.Plan.Request.PixelPitchMicrometers, artifact.Manifest.Plan.Request.Chart,
            artifact.Manifest.Plan.Request.PupilRings, artifact.Manifest.Plan.Request.PupilArms, artifact.Manifest.Plan.Request.PixelSamplesPerAxis,
            report.ReferenceAlgorithm, report.MeasurementAlgorithm, report.SourceManifestSha256, report.LensSha256,
            Tasks = report.Tasks.Select((task, ti) => new { task.TaskIndex, task.Name, task.IrradianceSha256,
                Regions = task.Regions.Select((region, ri) => new
                {
                    region.Measurement.Roi, region.Measurement.Status, region.Measurement.Failure, region.Measurement.Fit,
                    region.Measurement.Geometry, region.Reference, region.ReferenceFailure,
                    Curve = region.Measurement.Edge?.Measurement.Curve,
                    Mtf50 = region.Measurement.Edge?.Measurement.Mtf50CyclesPerPixel,
                    Mtf10 = region.Measurement.Edge?.Measurement.Mtf10CyclesPerPixel,
                    DenseReference = dense?.Tasks[ti].Regions[ri].Reference,
                    DenseReferenceFailure = dense?.Tasks[ti].Regions[ri].ReferenceFailure
                }) })
        });
        foreach (var task in report.Tasks)
        foreach (var region in task.Regions)
            Console.WriteLine($"{name}/{task.TaskIndex}/{region.Measurement.Roi.Id}: {region.Measurement.Status}, " +
                $"{region.Measurement.Edge?.Measurement.Mtf50CyclesPerPixel:G6} / " +
                $"{(region.Reference is null ? region.ReferenceFailure : SlantedEdgeSfr.Crossing(region.Reference.WithPixelAperture, .5)?.ToString("G6"))}");
    }
}
