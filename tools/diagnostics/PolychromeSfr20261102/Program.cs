using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var radius = int.Parse(args.FirstOrDefault(a => a.StartsWith("--radius="))?.Split('=')[1] ?? "32");
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
foreach (var (name, path) in new[] {
    ("double-gauss", "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx"),
    ("123456", "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX") })
{
    var file = Path.Combine(root, path); var optic = OpticalFormatCatalog.Import(File.ReadAllText(file), ".zmx");
    var before = JsonSerializer.Serialize(optic.ToSnapshot(), json);
    foreach (var field in args.Contains("--all-fields") ? Enumerable.Range(1, optic.Fields.Count) : new[] { 1, optic.Fields.Count }.Distinct())
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var run = MonochromeSlantedEdge.Run(optic, new(FieldNumber: field, PsfRadiusPixels: radius, UseLensWavelengths: true));
            var baseline = run.Baseline;
            var report = new { Algorithm = SlantedEdgeExperiment.PolychromeAlgorithm, Source = path,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), PrescriptionUnchanged = before == JsonSerializer.Serialize(optic.ToSnapshot(), json),
                baseline.Settings, baseline.Spectrum, baseline.ReferenceWavelengthNanometers, run.SamplingConverged,
                baseline.Measurement.Mtf50CyclesPerPixel, baseline.RelativePsfReadoutIntegral,
                baseline.MaximumSfrReferenceDifference, baseline.MaximumSfrFullPupilDifference, baseline.MaximumWindowFullPupilDifference,
                Curve = baseline.Measurement.Curve.Select((p, i) => new { p.CyclesPerPixel, p.CyclesPerMillimeter, Sfr = p.Response,
                    WindowMtf = baseline.OpticalMtf[i].Response, WindowPixelMtf = baseline.PixelIntegratedMtf[i].Response,
                    FullMtf = baseline.FullPupilMtf![i].Response, FullPixelMtf = baseline.FullPupilPixelIntegratedMtf![i].Response }),
                Sampling = run.Sampling.Select(c => new { c.Axis, c.WithinCriteria, c.MaximumImageDifference, c.MaximumSfrDifference,
                    c.MaximumMtfDifference, c.MaximumFullPupilMtfDifference, c.RelativeReadoutIntegralDifference, c.Message }), Seconds = timer.Elapsed.TotalSeconds };
            await File.WriteAllTextAsync(Path.Combine(output, $"{name}-field-{field}.json"), JsonSerializer.Serialize(report, json));
            Console.WriteLine($"{name} Field {field}: converged={run.SamplingConverged}, SFR/full={baseline.MaximumSfrFullPupilDifference:G8}, window/full={baseline.MaximumWindowFullPupilDifference:G8}, captured={baseline.RelativePsfReadoutIntegral:G8}, {timer.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception e) { Console.WriteLine($"{name} Field {field}: {e}"); }
    }
}
