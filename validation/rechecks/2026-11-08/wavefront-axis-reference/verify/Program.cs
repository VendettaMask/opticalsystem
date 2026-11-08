using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var root = Path.GetFullPath(args[1]);
var previous = Path.GetFullPath(args[2]);
if (args[0] == "prepare-oap")
{
    var jobsPath = Path.Combine(root, "native-jobs.json");
    var jobs = JsonNode.Parse(File.ReadAllText(jobsPath))!.AsArray();
    foreach (var rings in new[] { 6, 10, 16 })
    {
        var id = $"2605-rays-{rings}";
        var requestPath = Path.Combine(root, id + ".json");
        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "2605-operands.json")))!.AsObject();
        request.Remove("modelOperands");
        request["adapter"] = "point-comparison";
        request["pointKind"] = "gaussian-axis-rms-spot-radius";
        request["originalKeys"] = new JsonArray("RMS vs Field");
        request["pointInputs"] = JsonSerializer.SerializeToNode(ApertureSampler
            .GenerateGaussianQuadrature(rings, 2 * rings, useChebyshevAzimuths: true)
            .Select(p => new { hx = 0, hy = 0, px = p.X, py = p.Y, weight = p.Weight }));
        File.WriteAllText(requestPath, request.ToJsonString());
        jobs.Add(new JsonObject { ["id"] = id, ["request"] = requestPath, ["output"] = Path.Combine(root, id) });
    }
    File.WriteAllText(jobsPath, jobs.ToJsonString());
    return;
}

var frozen = JsonFiles.Read<JsonElement[]>(Path.Combine(previous, "rows.json"));
Tolerances Gate(int index, string quantity) => frozen[index - 1].GetProperty("settings")
    .GetProperty("quantities").GetProperty(quantity).Deserialize<Tolerances>(JsonFiles.Options)!;
string Previous(int index, string file) => Path.Combine(previous, "analyses", index.ToString(), file);
JsonElement Native(string job, string file) => JsonFiles.Read<JsonElement>(Path.Combine(root, job, file));
CanonicalAnalysisRequest Request(string directory) => JsonFiles.Read<WorkbenchJob>(Path.Combine(directory, "request.json")).Request;
Grid2DResult CoreGrid(string dir)
{
    var request = Request(dir);
    return ResultNormalizer.Workbench(Path.Combine(dir, "data.json"),
        AnalysisComparisonRegistry.Get("Wavefront"), request).Grids.Single();
}

var rms = new List<object>();
var rmsT = Gate(2605, "Radius");
var rmsOperand = Native("2605-operands", "model-controls.json").GetProperty("rows");
var nativeRadii = new List<double>();
var coreRadii = new List<double>();
foreach (var (rings, index) in new[] { 6, 10, 16 }.Select((n, i) => (n, i)))
{
    var data = Native($"2605-rays-{rings}", "point-reference.json");
    var image = data.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
    var nodes = data.GetProperty("inputs").EnumerateArray().ToArray();
    if (nodes.Length != image.Length || !image.Select((ray, i) => ray.GetProperty("number").GetInt32() == i + 1).All(v => v))
        throw new InvalidDataException("Gaussian node order changed.");
    var coreDir = Path.Combine(root, $"core-2605-rms-{rings}");
    var workbenchJob = JsonFiles.Read<WorkbenchJob>(Path.Combine(coreDir, "request.json"));
    var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(workbenchJob.SnapshotPath));
    var pupil = nodes.Select(p => new PupilSample(N(p, "px"), N(p, "py"), N(p, "weight"))).ToArray();
    var wavelength = optic.Wavelengths[workbenchJob.Request.Wavelength - 1].Micrometers;
    var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wavelength, pupil, aimAtStop: false);
    var target = optic.SurfaceGroup.Items.Count - 1;
    var virtualCount = optic.SequentialRayTracer.TraceGaussianPupil(bundle)
        .Count(history => history.Last() is { Vignetted: false, Intensity: > 0 } ray && ray.SurfaceNumber == target);
    var physicalRays = optic.SequentialRayTracer.TraceFinalSamples(bundle);
    var physicalMask = physicalRays.Select(ray => ray is { Vignetted: false, Intensity: > 0 } && ray.SurfaceNumber == target).ToArray();
    var maskMismatches = physicalMask.Zip(image).Count(p => p.First != Valid(p.Second));
    var weighted = nodes.Zip(image).Where(p => Valid(p.Second))
        .Select(p => (X: N(p.Second, "x"), Y: N(p.Second, "y"), Weight: N(p.First, "weight"))).ToArray();
    var radiusUm = Radius(weighted);
    var imageFrame = optic.SurfaceGroup.Items[target].CoordinateSystem;
    var corePhysicalRadius = Radius(Enumerable.Range(0, physicalMask.Length).Where(i => physicalMask[i]).Select(i =>
    {
        var point = imageFrame.ToLocalPoint(physicalRays[i]!.Position);
        return (point.X, point.Y, N(nodes[i], "weight"));
    }).ToArray());
    var core = ResultNormalizer.Workbench(Path.Combine(coreDir, "data.json"), AnalysisComparisonRegistry.Get("RMS vs Field"), Request(coreDir)).Series.Single();
    if (!core.IsPointDomain || core.X.Single() != 0)
        throw new InvalidDataException("RMS output is not the original zero-extent physical domain.");
    var compare = CompareRms(core.Y.Single(), radiusUm, rmsT);
    nativeRadii.Add(radiusUm);
    coreRadii.Add(core.Y.Single());
    rms.Add(new { rings, count = nodes.Length, nativeTransmitted = weighted.Length, corePhysical = physicalMask.Count(v => v), maskMismatches,
        coreVirtual = virtualCount, nativeRadiusUm = radiusUm, corePhysicalRadius,
        physicalPair = CompareRms(corePhysicalRadius, radiusUm, rmsT),
        nativeOperandUm = 1000 * N(rmsOperand[index], "value"), coreRadiusUm = core.Y.Single(), comparisonOfDistinctMasks = compare,
        coreProvenance = Native($"core-2605-rms-{rings}", "provenance.json") });
}

var wavefront = new List<object>();
foreach (var index in new[] { 1070, 1246 })
{
    var checks = new List<object>();
    var entry = AnalysisComparisonRegistry.Get("Wavefront");
    foreach (var sampling in new[] { 128, 256 })
    {
        var name = $"{index}-entrance-{sampling}";
        var native = Native(name, "data.json");
        var grids = native.GetProperty("dataGrids").GetArrayLength();
        var messages = native.GetProperty("messages");
        if (grids != 1 || messages.GetArrayLength() != 0)
        {
            checks.Add(new { sampling, grids, messages });
            continue;
        }
        var coreDir = Path.Combine(root, "core-" + name);
        var workbenchPath = Path.Combine(coreDir, "data.json");
        try
        {
            var reference = ResultNormalizer.Zemax(Path.Combine(root, name, "data.json"), entry, Request(coreDir), workbenchPath);
            var metrics = Compare(CoreGrid(coreDir), reference.Grids.Single(), Gate(index, "WavefrontError"));
            checks.Add(new { sampling, grids, messages, metrics });
        }
        catch (InvalidDataException error)
        {
            checks.Add(new { sampling, grids, messages, error = error.Message });
        }
    }
    var meridian = Native($"{index}-meridian", "point-reference.json");
    var inputs = meridian.GetProperty("inputs").EnumerateArray().ToArray();
    var rays = meridian.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
    if (inputs.Length != rays.Length || !inputs.All(p => N(p, "px") == 0 && N(p, "hx") == 0 && N(p, "hy") == 0))
        throw new InvalidDataException("Native meridian identity differs.");
    var originalDirectory = Path.GetDirectoryName(Previous(index, "data.json"))!;
    var original = CoreGrid(originalDirectory);
    var meridianX = original.X.ToList().IndexOf(0);
    if (original.Z.Any(row => row.Where((_, i) => i != meridianX).Any(v => v.HasValue)))
        throw new InvalidDataException("Original projected output is not wholly on the meridian.");
    var nativeGrid = original with { Z = original.Y.Select(y =>
    {
        var row = new double?[original.X.Length];
        var k = Array.FindIndex(inputs, p => Math.Abs(y - N(p, "py")) < 1e-12);
        if (k >= 0 && Valid(rays[k])) row[meridianX] = N(rays[k], "opdWaves");
        return row;
    }).ToArray() };
    var exactMap = ResultNormalizer.Zemax(Path.Combine(root, $"{index}-entrance-64", "data.json"),
        entry, Request(originalDirectory), Previous(index, "data.json")).Grids.Single();
    var entranceMeridian = exactMap with
    {
        Z = exactMap.Z.Select(row => row.Select((v, i) => i == meridianX ? v : null).ToArray()).ToArray()
    };
    var coreValues = JsonFiles.Read<JsonElement>(Previous(index, "data.json")).GetProperty("values");
    var fNumbers = Native($"{index}-working-f-number", "model-controls.json").GetProperty("rows");
    var nativeFNumberX = N(fNumbers[0], "value");
    var nativeFNumberY = N(fNumbers[1], "value");
    var gate = Gate(index, "WavefrontError");
    wavefront.Add(new
    {
        index,
        entranceChecks = checks,
        nativeMeridian = Compare(nativeGrid, entranceMeridian, gate),
        coreMeridian = Compare(original, nativeGrid, gate),
        nativeFNumberX,
        nativeFNumberY,
        coreFNumberX = N(coreValues, "ExitPupilWorkingFNumberX"),
        coreFNumberY = N(coreValues, "ExitPupilWorkingFNumberY"),
        nativeProjectionX = nativeFNumberY / nativeFNumberX,
        coreProjectionX = N(coreValues, "ExitPupilDisplayScaleX")
    });
}
JsonFiles.Write(Path.Combine(root, "comparisons.json"), new
{
    rmsTolerance = rmsT,
    rms,
    nativeRmsConvergence = CompareRms(nativeRadii[^2], nativeRadii[^1], rmsT),
    coreRmsConvergence = CompareRms(coreRadii[^2], coreRadii[^1], rmsT),
    originalCoreAgainstFinestPhysicalReference = CompareRms(coreRadii[0], nativeRadii[^1], rmsT),
    wavefront
});

static double N(JsonElement value, string name) => value.GetProperty(name).GetDouble();
static bool Valid(JsonElement ray) => N(ray, "error") == 0 && N(ray, "vignette") == 0 && N(ray, "intensity") > 0;
static double Radius((double X, double Y, double Weight)[] points)
{
    var weight = points.Sum(p => p.Weight);
    var cx = points.Sum(p => p.Weight * p.X) / weight;
    var cy = points.Sum(p => p.Weight * p.Y) / weight;
    return 1000 * Math.Sqrt(points.Sum(p => p.Weight * (Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2))) / weight);
}
static ComparisonMetric CompareRms(double core, double reference, Tolerances gate) =>
    ComparisonMetrics.Calculate("axis-radius", "Micrometer", new([0], null, [core], [reference], 1), gate);
static ComparisonMetric Compare(Grid2DResult core, Grid2DResult native, Tolerances gate)
{
    var values = ComparisonMetrics.Align(core, native, []);
    return ComparisonMetrics.Calculate(core.Id, native.ValueAxis.Unit, values, gate);
}
