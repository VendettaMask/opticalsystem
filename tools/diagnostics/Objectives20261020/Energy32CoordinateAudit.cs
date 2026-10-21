using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Offline arithmetic isolation. Native directions and reference classifications
// are diagnostics only; no native quantity enters a product computation.
internal static class Energy32CoordinateAudit
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var cones = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-residuals-20261021/native-cones-v3-mm/cones.json"));
        using var baseline = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-residuals-20261021/grid-v3/summary.json"));
        var cases = new List<object>();
        var predictions = new List<Prediction>();
        foreach (var control in baseline.RootElement.EnumerateArray().Where(c => c.TryGetProperty("variant", out var variant)
            && variant.GetString() == "baseline" && c.GetProperty("ideal").GetBoolean()))
        {
            var lens = control.GetProperty("lens").GetString()!;
            var expected = control.GetProperty("max").GetDouble() < 1e-5 ? control.GetProperty("pupilCount").GetInt32()
                : control.GetProperty("pupilCount").GetInt32() == 97 ? 101 : 97;
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var wave = optic.Wavelengths[request.Wavelength - 1];
            object?[] args = [optic, (0d, 0d), wave, optic.RayAimingEnabled, 1d, null];
            typeof(DiffractionEngine).GetMethod("TryWorkingPupilDirections", Static)!.Invoke(null, args);
            var core = (Vector3D[])args[5]!;
            var capture = cones.RootElement.EnumerateArray().Single(c => c.GetProperty("lens").GetString() == lens);
            var native = capture.GetProperty("waves")[request.Wavelength - 1].GetProperty("rays").EnumerateArray().Take(5)
                .Select(r => new Vector3D(r.GetProperty("l").GetDouble(), r.GetProperty("m").GetDouble(), r.GetProperty("n").GetDouble())).ToArray();
            var coreF = DiffractionEngine.WorkingFNumber(optic, (0, 0), wave, optic.RayAimingEnabled);
            var nativeF = capture.GetProperty("waves")[0].GetProperty("workingFNumber").GetDouble();
            var rms = typeof(DiffractionEngine).GetMethod("CumulativeEnergyRootMeanSquare", Static)!;
            var pupilRadius = optic.Paraxial.EstimateEntrancePupilDiameter() / 2;
            var exitRadius = optic.Paraxial.EstimateExitPupilDiameter(wave.Micrometers) / 2;
            cases.Add(new { lens, expected, coreF, nativeF, wavelength = wave.Micrometers,
                pupilRadius, exitRadius,
                core = core.Select(d => new { d.X, d.Y, d.Z, d.Length }), native = native.Select(d => new { d.X, d.Y, d.Z, d.Length }) });
            foreach (var (scaleName, scale) in new[] { ("entrance-radius", pupilRadius), ("entrance-diameter", pupilRadius * 2),
                ("exit-radius", exitRadius), ("exit-diameter", exitRadius * 2) })
            foreach (var arithmetic in new[] { "coordinate-first", "step-first" })
            foreach (var mask in new[] { "normalized-square", "physical-square", "physical-sqrt" })
            {
                var sx = control.GetProperty("sx").GetDouble(); var sy = control.GetProperty("sy").GetDouble();
                var x = arithmetic == "coordinate-first" ? (4 * sx) * scale : 4 * (sx * scale);
                var y = arithmetic == "coordinate-first" ? (4 * sy) * scale : 4 * (sy * scale);
                var inside = mask switch
                {
                    "physical-square" => x * x + y * y <= scale * scale,
                    "physical-sqrt" => Math.Sqrt(x * x + y * y) <= scale,
                    _ => (x / scale) * (x / scale) + (y / scale) * (y / scale) <= 1
                };
                predictions.Add(new(string.Join("/", "physical-baseline", scaleName, arithmetic, mask), lens, expected, inside ? 101 : 97, x / scale, y / scale, coreF));
            }
            foreach (var (source, directions, systemF) in new[] { ("core", core, coreF), ("native", native, nativeF) })
            foreach (var naMethod in new[] { "sin-acos", "cross-normalized", "cross", "cos-squared", "transverse" })
            {
                var chief = directions[0];
                var nas = directions.Skip(1).Select(d => naMethod switch
                {
                    "sin-acos" => Math.Sin(Math.Acos(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z)),
                    "cross-normalized" => optic.Backend.Current.Cross(chief, d).Length / (chief.Length * d.Length),
                    "cross" => optic.Backend.Current.Cross(chief, d).Length,
                    "cos-squared" => Math.Sqrt(1 - Math.Pow(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, 2)),
                    _ => Math.Sqrt(d.X * d.X + d.Y * d.Y)
                }).ToArray();
                foreach (var moment in new[] { "naive-rms", "exact-rms", "mean", "system-f", "combined-f" })
                {
                    var mean = moment switch
                    {
                        "exact-rms" => (double)rms.Invoke(null, [nas])!,
                        "mean" => nas.Average(),
                        "system-f" => .5 / systemF,
                        "combined-f" => .5 / Combine(.5 / Math.Sqrt(nas.Take(2).Average(n => n * n)), .5 / Math.Sqrt(nas.Skip(2).Average(n => n * n))),
                        _ => Math.Sqrt(nas.Average(n => n * n))
                    };
                    var fs = .5 / mean;
                    var fx = .5 / ((nas[2] + nas[3]) / 2);
                    var fy = .5 / ((nas[0] + nas[1]) / 2);
                    foreach (var axis in new[] { "directional", "common" })
                    foreach (var units in new[] { "um", "mm", "nm-mm", "um-div-mm", "nm-div-mm" })
                    {
                        var lambda = units switch
                        {
                            "um" => wave.Micrometers,
                            "mm" => wave.Micrometers * .001,
                            "um-div-mm" => wave.Micrometers / 1000,
                            "nm-div-mm" => wave.Nanometers / 1000000,
                            _ => wave.Nanometers * 1e-6
                        };
                        foreach (var pitchMethod in new[] { "multiply", "divide", "cutoff", "reciprocal-cutoff", "frequency-first", "cutoff-sqrt-product", "frequency-target", "default-fft-ratio", "width-first", "width-sqrt-first", "width-frequency", "width-divide", "width-default" })
                        {
                            var pitch = pitchMethod switch
                            {
                                "multiply" => lambda * fs / Math.Sqrt(32),
                                "divide" => lambda / Math.Sqrt(32) * fs,
                                "cutoff" => Math.Sqrt(1d / 32) / (2 * mean / lambda),
                                "frequency-first" => 1 / (32 * ((1 / (lambda * fs)) / Math.Sqrt(32))),
                                "cutoff-sqrt-product" => 1 / (Math.Sqrt(32) * (1 / (lambda * fs))),
                                "frequency-target" => Math.Sqrt(32) / (32 * (1 / (lambda * fs))),
                                "default-fft-ratio" => (lambda * fs * 30 / 64) * (2 * Math.Sqrt(32) / 30),
                                "width-first" => lambda * fs * Math.Sqrt(32) / 32,
                                "width-sqrt-first" => Math.Sqrt(32) * lambda * fs / 32,
                                "width-frequency" => (Math.Sqrt(32) / (1 / (lambda * fs))) / 32,
                                "width-divide" => (lambda * fs) / (32 / Math.Sqrt(32)),
                                "width-default" => (lambda * fs * 30 / 2) / (30 / Math.Sqrt(32)) / 32,
                                _ => Math.Sqrt(1d / 32) / (1 / (lambda * fs))
                            };
                            foreach (var coordinateMethod in new[] { "conjugate-step", "extent-step", "extent-coordinate", "extent-f-first", "frequency-step", "frequency-ratio", "frequency-na-ratio", "cutoff-product", "inverse-extent", "inverse-fft-spacing", "fft-spacing-ratio" })
                            {
                                double Coordinate(double f)
                                {
                                    if (axis == "common") f = fs;
                                    var extent = lambda * f * 30 / 64 / pitch;
                                    return coordinateMethod switch
                                    {
                                        "inverse-extent" => 4 / (15 / extent),
                                        "inverse-fft-spacing" => 4 / (pitch / (lambda * f / 32) ),
                                        "fft-spacing-ratio" => 4 * ((lambda * f / 32) / pitch),
                                        "conjugate-step" => 4 * (1 / (32 * pitch / lambda / f)),
                                        "extent-step" => 4 * (extent / 15),
                                        "extent-coordinate" => extent * (4d / 15),
                                        "extent-f-first" => (lambda * 30 / 64 / pitch * f) * (4d / 15),
                                        "frequency-ratio" => 4 * ((1 / (32 * pitch)) / (1 / (lambda * f))),
                                        "frequency-na-ratio" => 4 * ((1 / (32 * pitch)) / (2 * (.5 / f) / lambda)),
                                        "cutoff-product" => 4 * (1 / (32 * pitch * (1 / (lambda * f)))),
                                        _ => 4 * (lambda / (32 * pitch) * f)
                                    };
                                }
                                var x = Coordinate(fx); var y = Coordinate(fy);
                                var count = x * x + y * y <= 1 ? 101 : 97;
                                var key = string.Join("/", source, naMethod, moment, axis, units, pitchMethod, coordinateMethod);
                                predictions.Add(new(key, lens, expected, count, x, y, fs));
                            }
                        }
                    }
                }
            }
        }
        var ranked = predictions.GroupBy(p => p.Method).Select(g => new { method = g.Key, matched = g.Count(p => p.Expected == p.Actual), count = g.Count(),
            rows = g.Select(p => new { p.Lens, p.Expected, p.Actual, p.X, p.Y, p.F }) }).OrderByDescending(g => g.matched).ThenBy(g => g.method).ToArray();
        JsonFiles.Write(Path.Combine(output, "inputs.json"), cases);
        JsonFiles.Write(Path.Combine(output, "methods.json"), ranked);
        foreach (var source in new[] { "physical-baseline", "core", "native" })
        {
            var best = ranked.Where(r => r.method.StartsWith(source + "/", StringComparison.Ordinal)).Take(20);
            foreach (var row in best) Console.WriteLine($"{row.matched}/{row.count}: {row.method}");
        }
        static double Combine(double x, double y) => 1 / Math.Sqrt((1 / (x * x) + 1 / (y * y)) / 2);
    }
    private sealed record Prediction(string Method, string Lens, int Expected, int Actual, double X, double Y, double F);
}
