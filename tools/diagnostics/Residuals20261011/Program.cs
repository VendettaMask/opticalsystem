using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

if (args.Length != 3) throw new ArgumentException("Usage: <repository-root> <fresh-output-directory> <mode>");
if (!new[] { "energy", "huygens", "knots", "dense", "full-dense", "energy-grids", "cubic-grid", "reproduce", "inputs", "rays" }.Contains(args[2]))
    throw new ArgumentException("Unknown diagnostic mode.");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh output directory.");
Directory.CreateDirectory(output);
var source = Path.Combine(root, "validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX");
if (Hash(source) != "8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8")
    throw new InvalidDataException("Frozen source identity differs.");
var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
optic.RayAimingEnabled = true;
var wave = optic.Wavelengths[0];
if (args.Length < 3 || args[2] == "energy") Energy();
if (args.Length < 3 || args[2] == "huygens") Huygens();
if (args.Length >= 3 && args[2] == "knots") Knots();
if (args.Length >= 3 && args[2] == "dense") Dense();
if (args.Length >= 3 && args[2] == "full-dense") Dense();
if (args.Length >= 3 && args[2] == "energy-grids") Dense();
if (args.Length >= 3 && args[2] == "cubic-grid") CubicGrid();
if (args.Length >= 3 && args[2] == "reproduce") Reproduce();
if (args.Length >= 3 && args[2] == "inputs") Inputs();
if (args.Length >= 3 && args[2] == "rays") Rays();
File.WriteAllText(Path.Combine(output, "provenance.json"), JsonSerializer.Serialize(new {
    generatedUtc = DateTime.UtcNow, mode = args.Length >= 3 ? args[2] : "all-original-controls", source,
    sourceSha256 = Hash(source), coreAssembly = typeof(DiffractionEngine).Assembly.Location,
    coreSha256 = Hash(typeof(DiffractionEngine).Assembly.Location), toolSha256 = Hash(Assembly.GetExecutingAssembly().Location),
    frozen = new[] {
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json",
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Planar/data.json",
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Spherical/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-32/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-range-2p5/data.json",
        "validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/dee-range-10/data.json",
        "validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/ms-l7-32/pupil-32/pupil-grid.json"
    }.Select(path => new { path, sha256 = Hash(Path.Combine(root, path)) }),
    semantics = "Offline diagnostic controls, not native certification. No reference values, tolerances, settings or product calculations are changed. Dense/control interpolation results are not substituted for captured-settings recalculation."
}, jsonOptions));

string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

void Rays()
{
    var nativePath = Path.Combine(root, "validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/ms-l7-32/pupil-32/pupil-grid.json");
    using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
    var inputs = native.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
    var nativeRows = native.RootElement.GetProperty("rows").EnumerateArray().ToArray();
    var pupils = inputs.Select(p => new PupilSample(p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(), 1)).ToArray();
    var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils, true));
    var wf = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, pupils.Select(p => (p.X, p.Y)).ToArray(), referenceWavelength: wave);
    var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
    var rows = Enumerable.Range(0, pupils.Length).Select(i =>
    {
        if (nativeRows[i].GetProperty("error").GetInt32() != 0 || nativeRows[i].GetProperty("vignette").GetInt32() != 0 || rays[i] is null)
            throw new InvalidDataException("This valid-ray control must preserve propagation/vignetting masks.");
        var ray = rays[i]!;
        var position = frame.ToLocalPoint(ray.Position);
        var direction = frame.ToLocalDirection(ray.Direction);
        double Value(string name) => nativeRows[i].GetProperty(name).GetDouble();
        return new { pupils[i].X, pupils[i].Y,
            dx = position.X - Value("x"), dy = position.Y - Value("y"), dz = position.Z - Value("z"),
            dl = direction.X - Value("l"), dm = direction.Y - Value("m"), dn = direction.Z - Value("n"),
            dOpdWaves = wf.Samples[i].OpdWaves - Value("opd"), dIntensity = ray.Intensity - Value("intensity") };
    }).ToArray();
    var summary = new { count = rows.Length, nativeSha256 = Hash(nativePath),
        maximumPositionDifferenceMillimeters = rows.Max(r => Math.Max(Math.Max(Math.Abs(r.dx), Math.Abs(r.dy)), Math.Abs(r.dz))),
        maximumDirectionDifference = rows.Max(r => Math.Max(Math.Max(Math.Abs(r.dl), Math.Abs(r.dm)), Math.Abs(r.dn))),
        maximumOpdDifferenceWaves = rows.Max(r => Math.Abs(r.dOpdWaves)), maximumIntensityDifference = rows.Max(r => Math.Abs(r.dIntensity)),
        semantics = "Same normalized native pupil inputs; native image-local position/direction compared through the formal image coordinate system. Reference-sphere OPD remains distinct from cumulative optical path.", rows };
    File.WriteAllText(Path.Combine(output, "rays.json"), JsonSerializer.Serialize(summary, jsonOptions));
    Console.WriteLine($"rays={summary.count}; position={summary.maximumPositionDifferenceMillimeters:R} mm; direction={summary.maximumDirectionDifference:R}; OPD={summary.maximumOpdDifferenceWaves:R} waves; intensity={summary.maximumIntensityDifference:R}");
}

void Reproduce()
{
    var rows = new List<object>();
    foreach (var (key, slug) in new[] { ("Huygens PSF Cross Section", "huygens-psf-cross-section"), ("Diffraction Encircled Energy", "diffraction-encircled-energy") })
    {
        var directory = Path.Combine(root, $"validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/{slug}-c1");
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "captured-settings.json")));
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var entry = AnalysisComparisonRegistry.Get(key);
        var runtime = new WorkbenchRuntime(OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx"));
        var data = runtime.BuildAnalysisData(key, AnalysisComparisonRegistry.MapWorkbench(entry, request));
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var actual = ExtendedResultNormalizer.Workbench(data, request);
        var expected = ExtendedResultNormalizer.Zemax(Path.Combine(directory, "data.json"), request);
        foreach (var curve in expected.Series)
        {
            var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []), entry.DefaultTolerances[curve.YAxis.Quantity]);
            rows.Add(new { key, request, capturedSettingsSha256 = Hash(Path.Combine(directory, "captured-settings.json")), nativeDataSha256 = Hash(Path.Combine(directory, "data.json")), metric });
            Console.WriteLine($"formal {key}/{curve.Id}: NRMSE={metric.Nrmse:R}; max={metric.MaxAbsolute:R}; {metric.Conclusion}");
        }
    }
    File.WriteAllText(Path.Combine(output, "formal.json"), JsonSerializer.Serialize(rows, JsonFiles.Options));
}

void Inputs()
{
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: true);
        var pupil = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(row => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33)
                .Select(column => new PupilSample((column - 16) / 16d, (row - 16) / 16d, 1))).Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var inputs = pupil.Select((p, i) => new { hx = 0d, hy = 0d, px = p.X, py = p.Y, fieldIndex = 0, pupilIndex = i }).ToArray();
        File.WriteAllText(Path.Combine(output, nodes + ".json"), JsonSerializer.Serialize(inputs, JsonFiles.Options));
        Console.WriteLine($"{nodes}: {inputs.Length} exact normalized pupil inputs");
    }
}

void CubicGrid()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            ignoreOpd: ideal, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true, referenceWavelength: wave);
        foreach (var kernel in new[] { "linear", "catmull-rom", "keys", "lagrange" })
        foreach (var factor in new[] { 4, 8 })
        {
            double Interpolate(double xx, double yy)
            {
                var c = xx / psf.SampleSpacingMicrometers + 32;
                var r = yy / psf.SampleSpacingMicrometers + 32;
                var column = (int)Math.Floor(c); var row = (int)Math.Floor(r);
                var total = 0d;
                for (var j = -1; j <= 2; j++)
                for (var i = -1; i <= 2; i++) total += psf.Values[row + j, column + i] * Weight(c - column, i) * Weight(r - row, j);
                return Math.Max(0, total);
            }
            double Weight(double t, int node)
            {
                if (kernel == "lagrange")
                {
                    var value = 1d;
                    for (var other = -1; other <= 2; other++) if (other != node) value *= (t - other) / (node - other);
                    return value;
                }
                var x = Math.Abs(t - node);
                if (kernel == "linear") return Math.Max(0, 1 - x);
                var a = kernel == "keys" ? -1d : -.5;
                return x < 1 ? (a + 2) * x * x * x - (a + 3) * x * x + 1
                    : x < 2 ? a * x * x * x - 5 * a * x * x + 8 * a * x - 4 * a : 0;
            }
            var size = 32 * factor;
            var pitch = psf.SampleSpacingMicrometers / factor;
            var points = Enumerable.Range(0, size).SelectMany(row => Enumerable.Range(0, size).Select(column =>
            {
                var xx = (column - size / 2d + .5) * pitch;
                var yy = (row - size / 2d + .5) * pitch;
                return new WeightedEnergyPoint(xx, yy, Interpolate(xx, yy));
            })).ToArray();
            var distribution = new DiffractionEnergyDistribution(points, (0, 0), EnergyRegion.Circle);
            var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
            var fraction = integrator.GetType().GetMethod("Fraction")!;
            foreach (var range in new[] { 2.5, 5, 10 })
            {
                var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
                using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                    $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
                var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
                var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
                var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
                var knots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
                knots[1] = knots[2] / 4;
                var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
                var values = (double[])spline.Invoke(null, [axis, knots, x])!;
                var previous = 0d;
                values = values.Select(v => previous = Math.Clamp(Math.Max(previous, v), 0, 1)).ToArray();
                var metric = Metric(values, y);
                records.Add(new { ideal, kernel, factor, range, knots, metric, values });
                Console.WriteLine($"cubic ideal={ideal} kernel={kernel} factor={factor} range={range}: {metric.relativeL2:R}; first={knots[1]:R}, second={knots[2]:R}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output, "cubic-grid.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Dense()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    foreach (var imageSize in args[2] == "energy-grids" ? new[] { 64, 256 } : args[2] == "full-dense" ? new[] { 64, 256, 512 } : new[] { 32, 64, 128, 256, 512 })
    foreach (var smoothed in args[2] == "full-dense" && !ideal ? new[] { true, false } : new[] { true })
    foreach (var zemax in args[2] == "energy-grids" ? new[] { false, true } : new[] { true })
    foreach (var cellCentered in args[2] == "energy-grids" && !zemax ? new[] { false, true } : new[] { true })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, imageSize,
            ignoreOpd: ideal, cellCenteredPupil: cellCentered, zemaxFftSampling: zemax, aimAtStop: true, referenceWavelength: wave);
        if (!smoothed)
        {
            var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, true, true, zemaxCentered: true, referenceWavelength: wave);
            var builder = typeof(DiffractionEngine).GetMethod("BuildComplexPupilCore", BindingFlags.NonPublic | BindingFlags.Static)!;
            var pupil = (Complex[,])builder.Invoke(null, [wf, null, 32, wave, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber, true, 0d, 1d, ideal, true])!;
            var padded = new Complex[imageSize, imageSize];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++) padded[row + (imageSize - 32) / 2, column + (imageSize - 32) / 2] = pupil[row, column];
            typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [padded, false]);
            var count = pupil.Cast<Complex>().Count(c => c.Magnitude > 0);
            for (var row = 0; row < imageSize; row++)
            for (var column = 0; column < imageSize; column++)
                psf.Values[row, column] = Math.Pow(padded[(row + imageSize / 2) % imageSize, (column + imageSize / 2) % imageSize].Magnitude / count, 2) * 100;
        }
        var spacing = psf.SampleSpacingMicrometers;
        // Keep exactly the original physical half-width and pupil rays. Only the
        // independent integration quadrature changes; this is not a new capture.
        var size = args[2] == "full-dense" || imageSize == 32 ? imageSize : imageSize / 2;
        var start = (imageSize - size) / 2;
        var points = Enumerable.Range(0, size).SelectMany(row => Enumerable.Range(0, size).Select(column =>
            new WeightedEnergyPoint((column - size / 2d) * spacing, (row - size / 2d) * spacing, psf.Values[row + start, column + start]))).ToArray();
        var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
        var distribution = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
        var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
        var fraction = integrator.GetType().GetMethod("Fraction")!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
            var knots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
            foreach (var originCorrection in new[] { false, true })
            {
                var corrected = knots.ToArray();
                if (originCorrection) corrected[1] = corrected[2] / 4;
                var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
                var values = (double[])spline.Invoke(null, [axis, corrected, x])!;
                var metric = Metric(values, y);
                records.Add(new { ideal, imageSize, smoothed, zemax, cellCentered, range, spacing, center, originCorrection, knots = corrected, metric, values });
                Console.WriteLine($"dense ideal={ideal} image={imageSize} smoothed={smoothed} zemax={zemax} centered={cellCentered} range={range} origin={originCorrection}: {metric.relativeL2:R}; first={corrected[1]:R}, second={corrected[2]:R}");
            }
        }
    }
    File.WriteAllText(Path.Combine(output, "dense.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Knots()
{
    var records = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var heatmap = new PsfAnalysis(optic, 32, 64, 1, 1, type: "linear", displayAs: "heatmap", zemaxCompatible: true, ignoreOpd: ideal)
            .GenerateData().PlotSeries.First(s => s.Kind == AnalysisSeriesKind.Heatmap);
        var points = heatmap.Points.Select(p => new WeightedEnergyPoint(p.X, p.Y, p.Value!.Value)).ToArray();
        var xs = points.Select(p => p.X).Distinct().Order().Skip(16).Take(32).ToHashSet();
        var ys = points.Select(p => p.Y).Distinct().Order().Skip(16).Take(32).ToHashSet();
        var window = points.Where(p => xs.Contains(p.X) && ys.Contains(p.Y)).ToArray();
        var center = new GeometricEnergyDistribution(window, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
        var distribution = new DiffractionEnergyDistribution(window, center, EnergyRegion.Circle);
        var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
        var fraction = integrator.GetType().GetMethod("Fraction")!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            // Each polynomial is reconstructed solely to identify the native display
            // interpolation. These values never feed the formal product calculation.
            double NativeAt(double radius)
            {
                var segment = Math.Min(30, (int)Math.Floor(radius / range * 31));
                var start = (int)Math.Ceiling(segment * 400 / 31d) + 2;
                start = Math.Min(397, start);
                var total = 0d;
                for (var a = start; a < start + 4; a++)
                {
                    var factor = y[a];
                    for (var b = start; b < start + 4; b++) if (a != b) factor *= (radius - x[b]) / (x[a] - x[b]);
                    total += factor;
                }
                return total;
            }
            var axis = Enumerable.Range(0, 32).Select(i => range * i / 31).ToArray();
            var nativeKnots = axis.Select(NativeAt).ToArray();
            var formalKnots = axis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
            var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
            var restored = (double[])spline.Invoke(null, [axis, nativeKnots, x])!;
            records.Add(new { ideal, range, center, total = window.Sum(p => p.Weight), peak = window.Max(p => p.Weight), axis, nativeKnots, formalKnots, restoredMetric = Metric(restored, y), restored, nativeValues = y });
            Console.WriteLine($"knots ideal={ideal} range={range} centroid={center} total={window.Sum(p => p.Weight):R} peak={window.Max(p => p.Weight):R} spline={Metric(restored, y).relativeL2:R}");
            for (var i = 0; i < 8; i++) Console.WriteLine($"r={axis[i]:R}, native={nativeKnots[i]:R}, formal={formalKnots[i]:R}");
        }
    }
    File.WriteAllText(Path.Combine(output, "knots.json"), JsonSerializer.Serialize(records, jsonOptions));
}

void Energy()
{
    var rows = new List<object>();
    foreach (var ideal in new[] { true, false })
    {
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            ignoreOpd: ideal, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true, referenceWavelength: wave);
        var bilinear = typeof(PsfAnalysis).GetMethod("BilinearSample", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var range in new[] { 2.5, 5, 10 })
        {
            var name = range == 5 ? "dee-32" : range == 2.5 ? "dee-range-2p5" : "dee-range-10";
            using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
                $"validation/zemax/2026-r1/n02-energy-layer-controls-2026-10-09/{name}/data.json")));
            var series = native.RootElement.GetProperty("dataSeries")[ideal ? 0 : 1];
            var x = series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var y = series.GetProperty("y").EnumerateArray().Select(v => v[0].GetDouble()).ToArray();
            foreach (var count in new[] { 16, 32, 64, 128 })
            foreach (var extent in new[] { 1d, 2d })
            foreach (var centered in new[] { false, true })
            {
                var pitch = range * extent / count;
                var points = Enumerable.Range(0, count).SelectMany(row => Enumerable.Range(0, count).Select(column =>
                {
                    var xx = (column - count / 2d + (centered ? .5 : 0)) * pitch;
                    var yy = (row - count / 2d + (centered ? .5 : 0)) * pitch;
                    return new WeightedEnergyPoint(xx, yy, (double)bilinear.Invoke(null, [psf, xx, yy])!);
                })).ToArray();
                var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
                var distribution = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
                var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
                var fraction = integrator.GetType().GetMethod("Fraction")!;
                var values = x.Select(radius => (double)fraction.Invoke(integrator, [radius])!).ToArray();
                var metric = Metric(values, y);
                rows.Add(new { ideal, range, count, extent, centered, pitch, center, metric, values });
                if (metric.relativeL2 < .025) Console.WriteLine($"energy ideal={ideal} range={range} n={count} extent={extent} centered={centered}: {metric.relativeL2:R}");
            }
            File.WriteAllText(Path.Combine(output, $"native-{ideal}-{range}.json"), JsonSerializer.Serialize(new { x, y }));
        }
    }
    File.WriteAllText(Path.Combine(output, "energy-controls.json"), JsonSerializer.Serialize(rows, jsonOptions));
}

void Huygens()
{
    using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json")));
    var grid = native.RootElement.GetProperty("dataGrids")[0];
    var target = grid.GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    var dx = grid.GetProperty("dx").GetDouble(); var dy = grid.GetProperty("dy").GetDouble();
    var minX = grid.GetProperty("minX").GetDouble(); var minY = grid.GetProperty("minY").GetDouble();
    var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers, true)).Single()!;
    var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, true);
    var k = 2 * Math.PI / (wave.Micrometers * .001);
    var records = new List<object>();
    var pixelGridType = typeof(DiffractionEnergyDistribution).Assembly.GetType("OptilandWorkbench.Core.Analysis.PsfPixelEnergyGrid")!;
    var pixelType = pixelGridType.GetNestedType("Pixel", BindingFlags.NonPublic)!;
    var areaFraction = pixelGridType.GetMethod("AreaFraction", BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (var aiming in new[] { true, false })
    foreach (var nodes in new[] { "endpoints", "centers", "zemax", "halfopen", "closed32intervals" })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: nodes == "centers", zemaxCentered: nodes == "zemax", aimAtStop: aiming);
        var pupil = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(row => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33)
                .Select(column => new PupilSample((column - 16) / 16d, (row - 16) / 16d, 1))).Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupil, aiming));
        var pitch = nodes == "zemax" ? 1 / 15d : nodes == "endpoints" ? 2 / 31d : 1 / 16d;
        var areas = pupil.Select(p => (double)areaFraction.Invoke(null, [Activator.CreateInstance(pixelType,
            [p.X - pitch / 2, p.X + pitch / 2, p.Y - pitch / 2, p.Y + pitch / 2, 1d])!, 1d, "encircled"])!).ToArray();
        foreach (var weight in new[] { "uniform", "sqrtcos", "cos", "invsqrtcos", "invcos", "obliquity", "area", "area-sqrtcos", "area-cos" })
        {
            double Amplitude(int i)
            {
                var ray = rays[i];
                if (ray is null || ray.Intensity <= 0) return 0;
                var cos = ray.Direction.Z / chief.Direction.Z;
                var factor = weight switch { "sqrtcos" => Math.Sqrt(cos), "cos" => cos, "invsqrtcos" => 1 / Math.Sqrt(cos), "invcos" => 1 / cos, "obliquity" => (1 + cos) / 2, _ => 1 };
                if (weight.StartsWith("area")) factor = areas[i] * (weight == "area-cos" ? cos : weight == "area-sqrtcos" ? Math.Sqrt(cos) : 1);
                return Math.Sqrt(ray.Intensity) * factor;
            }
            var peak = Math.Pow(Enumerable.Range(0, pupil.Length).Sum(Amplitude), 2);
            var values = new double[1024];
            for (var row = 0; row < 32; row++)
            for (var column = 0; column < 32; column++)
            {
                var point = frame.Center + frame.TangentX * ((minX + column * dx) / 1000) + frame.TangentY * ((minY + row * dy) / 1000);
                var sum = Complex.Zero;
                for (var i = 0; i < rays.Count; i++)
                {
                    if (rays[i] is not { } ray) continue;
                    var offset = point - ray.Position;
                    var propagation = ray.Direction.X * offset.X + ray.Direction.Y * offset.Y + ray.Direction.Z * offset.Z;
                    var path = ray.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength + wf.ImageRefractiveIndex * propagation;
                    sum += Complex.FromPolarCoordinates(Amplitude(i), k * path);
                }
                values[row * 32 + column] = sum.Magnitude * sum.Magnitude / peak;
            }
            var metric = Metric(values, target);
            records.Add(new { aiming, nodes, weight, metric, values });
            Console.WriteLine($"huygens aiming={aiming} nodes={nodes} weight={weight}: L2={metric.relativeL2:R}, max={metric.maximum:R}");
        }
    }
    var formal = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 32, .00025, aimAtStop: true);
    Console.WriteLine($"formal Huygens L2={Metric(formal.Values.Cast<double>().Select(v => v / 100).ToArray(), target).relativeL2:R}");
    File.WriteAllText(Path.Combine(output, "huygens-controls.json"), JsonSerializer.Serialize(records, jsonOptions));
}

static (double relativeL2, double maximum) Metric(double[] actual, double[] expected) =>
    (Math.Sqrt(actual.Zip(expected, (a, b) => Math.Pow(a - b, 2)).Sum() / expected.Sum(v => v * v)),
        actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max());
