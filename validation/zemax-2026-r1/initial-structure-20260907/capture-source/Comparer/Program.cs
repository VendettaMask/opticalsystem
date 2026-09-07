using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.InitialStructure.Contracts;
if (args.Length != 2) throw new ArgumentException("Supply a frozen input directory and a new output directory.");
if (Directory.Exists(args[1]) && Directory.EnumerateFileSystemEntries(args[1]).Any())
    throw new IOException("Existing output is never overwritten.");
Directory.CreateDirectory(args[1]);
var options = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var rows = new List<object>();
var success = true;
foreach (var n in new[] { 1, 2, 3 })
{
    var directory = Path.Combine(args[0], $"{n}-element");
    var candidate = JsonSerializer.Deserialize<CandidateSnapshot>(await File.ReadAllTextAsync(Path.Combine(directory, "candidate.json")), options)!;
    var optic = Optic.FromSnapshot(candidate.Optic);
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "native-rays.json")));
    var root = document.RootElement;
    if (root.GetProperty("WavelengthCount").GetInt32() != 1 || optic.Wavelengths.Count != 1) throw new InvalidDataException("These frozen representatives are monochromatic.");
    var inputs = root.GetProperty("Inputs").EnumerateArray().ToArray();
    var rays = root.GetProperty("Rays").EnumerateArray().ToArray();
    if (rays.Length != 582 || inputs.Length != rays.Length) throw new InvalidDataException("Incomplete native rays.");
    var fields = new List<object>();
    var maximumCoordinateError = 0.0;
    var maximumSpotError = 0.0;
    foreach (var field in new[] { 0, .25, .5, .7, .85, 1 })
    {
        var indices = Enumerable.Range(0, inputs.Length).Where(i => inputs[i][0].GetDouble() == field).ToArray();
        var samples = indices.Select(i => new PupilSample(inputs[i][1].GetDouble(), inputs[i][2].GetDouble(), 1)).ToArray();
        if (indices.Any(i => rays[i].GetProperty("Error").GetInt32() != 0 || rays[i].GetProperty("Vignette").GetInt32() != 0))
            throw new InvalidDataException("Native error or clipped ray in a representative.");
        var actual = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field, samples, 1, reference: "absolute", includeSurfaceTransmission: false);
        if (actual.VignettedRayCount != 0 || actual.RayCount != 97) throw new InvalidDataException("Workbench ray bundle differs.");
        var traced = actual.Wavelengths[0].Rays;
        var x = indices.Select(i => rays[i].GetProperty("X").GetDouble()).ToArray();
        var y = indices.Select(i => rays[i].GetProperty("Y").GetDouble()).ToArray();
        var error = Enumerable.Range(0, 97).SelectMany(i => new[] { Math.Abs(x[i] - traced[i].X), Math.Abs(y[i] - traced[i].Y) }).Max();
        // Independent statistics of native image coordinates belong only to this offline validation program.
        var cx = x.Average(); var cy = y.Average();
        var squared = x.Zip(y).Select(point => Math.Pow(point.First - cx, 2) + Math.Pow(point.Second - cy, 2)).ToArray();
        var rms = Math.Sqrt(squared.Average()); var maximum = Math.Sqrt(squared.Max());
        var metric = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field, samples, 1, reference: "centroid", includeSurfaceTransmission: false).Metrics!;
        var spotError = Math.Max(Math.Abs(rms - metric.RmsSpotRadius), Math.Abs(maximum - metric.MaximumSpotRadius));
        maximumCoordinateError = Math.Max(maximumCoordinateError, error); maximumSpotError = Math.Max(maximumSpotError, spotError);
        fields.Add(new { Field = field, RayCount = 97, MaximumCoordinateErrorMillimeters = error, NativeRms = rms, WorkbenchRms = metric.RmsSpotRadius,
            NativeMaximumRadius = maximum, WorkbenchMaximumRadius = metric.MaximumSpotRadius, MaximumSpotErrorMillimeters = spotError });
    }
    var eflError = Math.Abs(root.GetProperty("EFL").GetDouble() - optic.Paraxial.EstimateEffectiveFocalLength());
    var pass = maximumCoordinateError <= 1e-8 && maximumSpotError <= 1e-8 && eflError <= 1e-8;
    success &= pass;
    rows.Add(new { Elements = n, CandidateFingerprint = candidate.OpticFingerprint, NativeVersion = root.GetProperty("Version").GetInt32(),
        Pass = pass, AbsoluteToleranceMillimeters = 1e-8, MaximumCoordinateErrorMillimeters = maximumCoordinateError,
        MaximumSpotErrorMillimeters = maximumSpotError, EflErrorMillimeters = eflError, Fields = fields });
    Console.WriteLine($"{n}: pass={pass}, ray error={maximumCoordinateError:R}, spot error={maximumSpotError:R}, EFL error={eflError:R} mm");
}
await File.WriteAllTextAsync(Path.Combine(args[1], "dense-ray-comparison.json"), JsonSerializer.Serialize(new { Scope = "Three generated monochromatic spherical representatives, 582 rays each; no claim for other inputs", Pass = success, Results = rows }, options));
return success ? 0 : 1;
