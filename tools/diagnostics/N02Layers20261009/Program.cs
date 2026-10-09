using System.Reflection;
using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

if (args.Length == 3 && args[0] == "--prepared-controls")
{
    var controls = new List<object>();
    foreach (var (name, sampling) in new[] { ("ms-l7-32", 32), ("ms-l7-64", 64), ("cooke-32", 32), ("tessar-32", 32) })
    {
        var directory = Path.Combine(args[1], name);
        var controlOptic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(directory, "source.ZMX")), ".zmx");
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "probe/model.json")));
        var aiming = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        controlOptic.RayAimingEnabled = aiming;
        var wave = controlOptic.Wavelengths[0];
        var wf = WavefrontEngine.GenerateChiefRayUniform(controlOptic, (0, 0), wave, sampling,
            cellCentered: true, aimAtStop: aiming, pupilGridStretch: Math.Sqrt(sampling / 32d), zemaxCentered: true, referenceWavelength: wave);
        using var rays = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"pupil-{sampling}/pupil-grid.json")));
        var inputs = rays.RootElement.GetProperty("inputs"); var rows = rays.RootElement.GetProperty("rows");
        var nativeRows = Enumerable.Range(0, rows.GetArrayLength()).ToDictionary(i =>
            (inputs[i].GetProperty("px").GetDouble(), inputs[i].GetProperty("py").GetDouble()), i => rows[i]);
        var prepared = wf with { Samples = wf.Samples.Select(s => s with {
            OpdWaves = nativeRows[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("opd").GetDouble(),
            Intensity = nativeRows[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("intensity").GetDouble()
        }).ToArray() };
        var psf = DiffractionEngine.ComputeFftPsf(controlOptic, (0, 0), wave, sampling, 2 * sampling,
            cellCenteredPupil: true, zemaxFftSampling: true, preparedWavefront: prepared, aimAtStop: aiming, referenceWavelength: wave);
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"psf-{sampling}/data.json")));
        var g = data.RootElement.GetProperty("dataGrids")[0]; var dx = g.GetProperty("dx").GetDouble();
        var minX = g.GetProperty("minX").GetDouble(); var minY = g.GetProperty("minY").GetDouble();
        var dy = g.GetProperty("dy").GetDouble(); var size = psf.GridSize;
        var bilinear = typeof(PsfAnalysis).GetMethod("BilinearSample", BindingFlags.NonPublic | BindingFlags.Static)!;
        var squared = 0d; var physicalSquared = 0d; var referenceSquared = 0d; var max = 0d;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var reference = g.GetProperty("values")[y][x].GetDouble();
            var row = ((int)Math.Round(minY / dy + y) + size / 2 + size) % size;
            var column = ((int)Math.Round(minX / dx + x) + size / 2 + size) % size;
            var difference = psf.Values[row, column] / 100 - reference;
            var physical = (double)bilinear.Invoke(null, [psf, minX + x * dx, minY + y * dy])! / 100 - reference;
            squared += difference * difference; physicalSquared += physical * physical;
            referenceSquared += reference * reference; max = Math.Max(max, Math.Abs(difference));
        }
        controls.Add(new { name, sampling, psf.SampleSpacingMicrometers, nativeSpacingMicrometers = dx,
            indexRelativeL2 = Math.Sqrt(squared / referenceSquared), indexMaximumAbsolute = max,
            physicalRelativeL2 = Math.Sqrt(physicalSquared / referenceSquared) });
    }
    JsonFiles.Write(args[2], controls);
    return;
}
if (args.Length == 3 && args[0] == "--inputs")
{
    var size = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    var grid = new PupilGridSpecification(size, true, true, Math.Sqrt(Math.Max(1, size / 32d)));
    JsonFiles.Write(args[2], Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x => new {
        hx = 0d, hy = 0d, px = grid.Coordinate(x), py = grid.Coordinate(y), fieldIndex = 0, pupilIndex = y * size + x
    })).Where(p => p.px * p.px + p.py * p.py <= 1).ToArray());
    return;
}
if (args.Length == 4 && args[0] == "--complex-control")
{
    Directory.CreateDirectory(args[2]);
    ComplexPupilDiagnosis.Run(args[1], args[2], int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
    return;
}
if (args.Length != 3) throw new ArgumentException("repository-root native-run-directory fresh-output-directory");
var root = Path.GetFullPath(args[0]);
var native = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("A fresh output directory is required.");
Directory.CreateDirectory(output);
var fixture = Path.Combine(root, "validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06");
var source = Path.Combine(fixture, "source.ZMX");
var sourceHash = JsonFiles.Hash(File.ReadAllBytes(source));
var pupilGrid = new PupilGridSpecification(32, true, true, 1);
var pupilInputs = Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 32).Select(x => new {
    hx = 0d, hy = 0d, px = pupilGrid.Coordinate(x), py = pupilGrid.Coordinate(y), fieldIndex = 0, pupilIndex = y * 32 + x
})).Where(p => p.px * p.px + p.py * p.py <= 1).ToArray();
JsonFiles.Write(Path.Combine(output, "pupil-inputs.json"), pupilInputs);
if (sourceHash != JsonFiles.Hash(File.ReadAllBytes(Path.Combine(native, "input/source.ZMX"))))
    throw new InvalidDataException("Capture input is not the frozen source.");
using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "diffraction-encircled-energy-c1/captured-settings.json")));
var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
var expected = ExtendedResultNormalizer.Zemax(Path.Combine(fixture, "diffraction-encircled-energy-c1/data.json"), request);
var fresh = ExtendedResultNormalizer.Zemax(Path.Combine(native, "dee-32/data.json"), request);
var frozenMatch = expected.Series.Zip(fresh.Series, (a, b) => new {
    a.Id, count = a.X.Length, xExact = a.X.SequenceEqual(b.X), yExact = a.Y.SequenceEqual(b.Y)
}).ToArray();
if (frozenMatch.Any(r => !r.xExact || !r.yExact)) throw new InvalidDataException("Native DEE changed; investigate before interpreting layer controls.");
var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
optic.RayAimingEnabled = request.UseRayAiming;
var settings = AnalysisComparisonRegistry.MapWorkbench(entry, request);
var current = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, settings);
current = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(current, JsonFiles.Options), JsonFiles.Options)!;
var actual = ExtendedResultNormalizer.Workbench(current, request);
JsonFiles.Write(Path.Combine(output, "formal-dee.json"), current);
var nativePoints = ReadNativeGrid(Path.Combine(native, "psf-32/data.json"));
var layerRows = new List<object>();
var energyRows = new List<object>();
var pupilControls = new List<object>();
if (File.Exists(Path.Combine(native, "psf-real-32/data.json")) && File.Exists(Path.Combine(native, "psf-imaginary-32/data.json")))
    ComplexPupilDiagnosis.Run(native, output);
foreach (var ideal in new[] { false, true })
{
    var psf = new PsfAnalysis(optic, 32, 64, wavelengthNumber: 1, fieldNumber: 1,
        type: "linear", displayAs: "heatmap", zemaxCompatible: true, ignoreOpd: ideal).GenerateData();
    var heatmap = psf.PlotSeries.Single(p => p.Kind == AnalysisSeriesKind.Heatmap);
    var points = heatmap.Points.Select(p => new WeightedEnergyPoint(p.X, p.Y, p.Value!.Value)).ToArray();
    JsonFiles.Write(Path.Combine(output, ideal ? "formal-ideal-psf.json" : "formal-actual-psf.json"), psf);
    var wave = optic.Wavelengths[0];
    var raw = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
        cellCenteredPupil: true, zemaxFftSampling: true, ignoreOpd: ideal,
        aimAtStop: request.UseRayAiming, referenceWavelength: wave);
    var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
        cellCentered: true, aimAtStop: raw.UseRayAiming, pupilGridStretch: raw.PupilGridStretch,
        zemaxCentered: true, referenceWavelength: wave);
    JsonFiles.Write(Path.Combine(output, ideal ? "ideal-pupil.json" : "actual-pupil.json"), wavefront);
    JsonFiles.Write(Path.Combine(output, ideal ? "ideal-raw-fft.json" : "actual-raw-fft.json"), new {
        raw.SampleSpacingMicrometers, raw.WorkingFNumber, raw.PupilGridStretch, raw.UseRayAiming,
        values = Enumerable.Range(0, 64).Select(y => Enumerable.Range(0, 64).Select(x => raw.Values[y, x] / 100).ToArray()).ToArray()
    });
    var curve = expected.Series[ideal ? 0 : 1];
    var formalCurve = actual.Series.Single(s => s.Id == curve.Id);
    var formalMetric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
        ComparisonMetrics.Align(formalCurve, curve, []), entry.DefaultTolerances[curve.YAxis.Quantity]);
    var gridMetric = ideal ? null : GridCompare(points, nativePoints);
    var nativePupilPath = Path.Combine(native, "pupil-32/pupil-grid.json");
    if (!ideal && File.Exists(nativePupilPath))
    {
        using var nativePupil = JsonDocument.Parse(File.ReadAllText(nativePupilPath));
        var rayInputs = nativePupil.RootElement.GetProperty("inputs");
        var rayRows = nativePupil.RootElement.GetProperty("rows");
        var paired = Enumerable.Range(0, rayRows.GetArrayLength()).ToDictionary(i =>
            (rayInputs[i].GetProperty("px").GetDouble(), rayInputs[i].GetProperty("py").GetDouble()),
            i => rayRows[i]);
        var opdDifferences = wavefront.Samples.Select(s => s.OpdWaves - paired[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("opd").GetDouble()).ToArray();
        var intensityDifferences = wavefront.Samples.Select(s => s.Intensity - paired[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("intensity").GetDouble()).ToArray();
        var bilinear = typeof(PsfAnalysis).GetMethod("BilinearSample", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var phaseFromNative in new[] { false, true })
            foreach (var intensityFromNative in new[] { false, true })
            {
                var prepared = wavefront with { Samples = wavefront.Samples.Select(s => s with {
                    OpdWaves = phaseFromNative ? paired[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("opd").GetDouble() : s.OpdWaves,
                    Intensity = intensityFromNative ? paired[(s.NormalizedPupilX, s.NormalizedPupilY)].GetProperty("intensity").GetDouble() : s.Intensity
                }).ToArray() };
                var control = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
                    cellCenteredPupil: true, zemaxFftSampling: true, preparedWavefront: prepared,
                    aimAtStop: raw.UseRayAiming, referenceWavelength: wave);
                var controlPoints = points.Select(p => p with { Weight = (double)bilinear.Invoke(null, [control, p.X, p.Y])! / 100 }).ToArray();
                var comparison = GridCompare(controlPoints, nativePoints);
                pupilControls.Add(new { phaseFromNative, intensityFromNative, comparison,
                    maxOpdDifferenceWaves = opdDifferences.Max(Math.Abs),
                    maxIntensityDifference = intensityDifferences.Max(Math.Abs) });
                Console.WriteLine($"native-phase={phaseFromNative}, native-intensity={intensityFromNative}: {JsonSerializer.Serialize(comparison)}");
            }
    }
    layerRows.Add(new { ideal, formalMetric, gridMetric, pupilCount = wavefront.Samples.Count,
        pupilIlluminated = wavefront.Samples.Count(s => s.Intensity > 0), wavefront.VignettedRayCount,
        raw.SampleSpacingMicrometers, raw.WorkingFNumber, raw.PupilGridStretch,
        nativeIdealGridAvailable = false });
    Console.WriteLine($"ideal={ideal}: DEE NRMSE={formalMetric.Nrmse:R}; PSF={JsonSerializer.Serialize(gridMetric)}");
    foreach (var (label, gridPoints) in new[] { ("formal", points), ("native", nativePoints) })
    {
        if (ideal && label == "native") continue;
        foreach (var size in new[] { 32, 64 })
        {
            var window = Window(gridPoints, size);
            var centroid = new GeometricEnergyDistribution(window, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
            foreach (var (centerName, center) in new[] { ("window-centroid", centroid),
                ("reported-native-centroid", ideal ? (0.005766, 0.005766) : (0.006955, 0.006955)), ("chief", (0d, 0d)) })
            {
                var distribution = new DiffractionEnergyDistribution(window, center, EnergyRegion.Circle);
                // The plot's formal PsfPixelEnergyGrid permits radii extending beyond
                // the finite square. Invoke that same integrator; do not copy it or
                // relax the operand API's stricter complete-circle coverage check.
                var integrator = typeof(DiffractionEnergyDistribution).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(distribution)!;
                var fraction = integrator.GetType().GetMethod("Fraction")!;
                var values = curve.X.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
                var metric = Compare(values, curve);
                var knotAxis = Enumerable.Range(0, 32).Select(i => curve.X[^1] * i / 31).ToArray();
                var knotValues = knotAxis.Select(r => (double)fraction.Invoke(integrator, [r])!).ToArray();
                var spline = typeof(MtfThroughFocusAnalysis).GetMethod("CubicSplineInterpolate", BindingFlags.Static | BindingFlags.NonPublic)!;
                var sampled = (double[])spline.Invoke(null, [knotAxis, knotValues, curve.X])!;
                var previous = 0d;
                sampled = sampled.Select(v => previous = Math.Clamp(Math.Max(previous, v), 0, 1)).ToArray();
                var sampledMetric = Compare(sampled, curve);
                energyRows.Add(new { ideal, source = label, size, centerName, centerX = center.Item1, centerY = center.Item2,
                    totalPixelWeight = window.Sum(p => p.Weight), retainedFraction = window.Sum(p => p.Weight) / gridPoints.Sum(p => p.Weight),
                    distribution.MaximumCoveredDistanceMicrometers, metric, values, knotAxis, knotValues, sampledMetric, sampled });
                Console.WriteLine($"{label} ideal={ideal} window={size} center={centerName}: NRMSE={metric.Nrmse:R}");
                if (centerName == "window-centroid") Console.WriteLine($"32-knot formal-integral spline control: NRMSE={sampledMetric.Nrmse:R}");
            }
        }
    }
}
if (sourceHash != JsonFiles.Hash(File.ReadAllBytes(source))) throw new InvalidDataException("Frozen input changed.");
JsonFiles.Write(Path.Combine(output, "layer-summary.json"), new {
    sourceSha256 = sourceHash, coreAssembly = typeof(DiffractionEngine).Assembly.Location,
    coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(DiffractionEngine).Assembly.Location)),
    request, settings, frozenMatch, layerRows, energyRows, pupilControls,
    semantics = "Native original-settings capture plus formal Core diagnostics. Native FFT PSF and DEE share source/configuration/field/wavelength/32 sampling/automatic pitch. PSF values are compared without fitting, shifting or peak normalization. Native ideal 2D PSF/internal integration grid/total-energy denominator are not exposed by the audited settings interfaces; no ideal native grid is fabricated. Reported native centers are rounded text values in mm converted to micrometers. Pixel weight sums are diagnostic finite-grid sums, not native physical total-energy certification."
});

ComparisonMetric Compare(double[] values, Series1DResult reference) => ComparisonMetrics.Calculate(reference.Id, reference.YAxis.Unit,
    ComparisonMetrics.Align(reference with { Y = values }, reference, []), entry.DefaultTolerances[reference.YAxis.Quantity]);

static WeightedEnergyPoint[] ReadNativeGrid(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var grid = document.RootElement.GetProperty("dataGrids")[0];
    var n = grid.GetProperty("nx").GetInt32(); var m = grid.GetProperty("ny").GetInt32();
    var dx = grid.GetProperty("dx").GetDouble(); var dy = grid.GetProperty("dy").GetDouble();
    var xmin = grid.GetProperty("minX").GetDouble(); var ymin = grid.GetProperty("minY").GetDouble();
    return Enumerable.Range(0, m).SelectMany(y => Enumerable.Range(0, n).Select(x =>
        new WeightedEnergyPoint(xmin + x * dx, ymin + y * dy, grid.GetProperty("values")[y][x].GetDouble()))).ToArray();
}

static WeightedEnergyPoint[] Window(WeightedEnergyPoint[] points, int size)
{
    var xs = points.Select(p => p.X).Distinct().Order().ToArray(); var ys = points.Select(p => p.Y).Distinct().Order().ToArray();
    var startX = (xs.Length - size) / 2; var startY = (ys.Length - size) / 2;
    var selectedX = xs.Skip(startX).Take(size).ToHashSet(); var selectedY = ys.Skip(startY).Take(size).ToHashSet();
    return points.Where(p => selectedX.Contains(p.X) && selectedY.Contains(p.Y)).ToArray();
}

static object GridCompare(WeightedEnergyPoint[] actual, WeightedEnergyPoint[] expected)
{
    if (actual.Length != expected.Length) throw new InvalidDataException("Different grid sizes.");
    var a = actual.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray(); var b = expected.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
    var maxCoordinateDifference = a.Zip(b, (p, q) => Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y))).Max();
    if (maxCoordinateDifference > 1e-8) throw new InvalidDataException($"Coordinates differ: {maxCoordinateDifference:R}");
    var squared = a.Zip(b, (p, q) => Math.Pow(p.Weight - q.Weight, 2)).Sum();
    var referenceSquared = b.Sum(p => p.Weight * p.Weight);
    return new { count = a.Length, maxCoordinateDifference, relativeL2 = Math.Sqrt(squared / referenceSquared),
        maxAbsolute = a.Zip(b, (p, q) => Math.Abs(p.Weight - q.Weight)).Max(),
        actualSum = a.Sum(p => p.Weight), expectedSum = b.Sum(p => p.Weight) };
}
