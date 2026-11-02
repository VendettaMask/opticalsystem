using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

var json = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = false };
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var paths = new[] {
 "local-data/lens-library/originals/user-zmx/project/samples/lenses/Double Gauss 28 degree field_2.zmx",
 "local-data/lens-library/originals/user-zmx/project/samples/lenses/double-gauss-50mm.zmx",
 "validation/zemax/2026-r1/objectives-repair-2026-10-23/captures/dense-energy/1634-052-double-gauss-28-degree-field-c1-primary-f1-w2-diffraction-encircled-energy-p256/source.zmx",
 "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX" };
var rows = new List<object>();
foreach (var relative in paths)
{
    var file = Path.Combine(root, relative); var bytes = File.ReadAllBytes(file);
    var optic = OpticalFormatCatalog.Import(File.ReadAllText(file), ".zmx");
    var initial = JsonSerializer.Serialize(optic.ToSnapshot(), json);
    var wave = new Wavelength { Micrometers = .55, IsPrimary = true, Weight = 1 };
    var pupilChecks = new List<object>();
    foreach (var n in new[] { 32, 64, 128, 256 })
    {
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, n, cellCentered: true,
            aimAtStop: optic.RayAimingEnabled, allowGeometricReference: true);
        var map = wf.Samples.Where(p => p.Intensity > 0).ToDictionary(p => (
            (int)Math.Round((p.NormalizedPupilX + 1) * n / 2 - .5), (int)Math.Round((p.NormalizedPupilY + 1) * n / 2 - .5)), p => p.OpdWaves);
        var steps = map.SelectMany(kv => new[] { (kv.Key.Item1 + 1, kv.Key.Item2), (kv.Key.Item1, kv.Key.Item2 + 1) }
            .Where(map.ContainsKey).Select(k => Math.Abs(map[k] - kv.Value))).OrderBy(v => v).ToArray();
        pupilChecks.Add(new { n, MinimumOpd = map.Values.Min(), MaximumOpd = map.Values.Max(), MaximumAdjacentWaves = steps.Max(), P95AdjacentWaves = steps[(int)(steps.Length * .95)] });
    }
    var mono = optic.Clone(); mono.Wavelengths.Clear(); mono.Wavelengths.Add(wave);
    var spot = SpotMetricEvaluator.Evaluate(mono, rayDensity: 16, fieldNumber: 1, wavelengthNumber: 1);
    var focus = FocusMetricEvaluator.Evaluate(mono, focusStep: 10, focusPlaneCount: 7, rayDensity: 12, fieldNumber: 1, wavelengthNumber: 1);
    object result;
    try
    {
        var run = MonochromeSlantedEdge.Run(optic, new());
        result = new { run.SamplingConverged, run.Baseline.Measurement.Mtf50CyclesPerPixel, run.Baseline.RelativePsfReadoutIntegral,
            run.Baseline.MaximumSfrReferenceDifference, Checks = run.Sampling.Select(c => new { c.Axis, c.WithinCriteria, c.MaximumImageDifference,
                c.MaximumSfrDifference, c.MaximumMtfDifference, c.RelativeReadoutIntegralDifference, c.Message }) };
    }
    catch (Exception ex) { result = new { Error = ex.Message }; }
    var row = new { Path = relative, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), optic.Name,
        ObjectDistanceMillimeters = double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness) ? "Infinity" : optic.SurfaceGroup.Items[0].Thickness.ToString("G17"),
        Spot = spot, FocusDiagnostic = focus, PupilChecks = pupilChecks, Sfr = result,
        PrescriptionUnchanged = initial == JsonSerializer.Serialize(optic.ToSnapshot(), json) };
    rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
}
await File.WriteAllTextAsync(Path.Combine(output, "diagnostic.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
