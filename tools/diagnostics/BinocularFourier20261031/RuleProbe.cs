using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Independent quadrature diagnostics only. All rays, OPD and pupil fields are
// evaluated by Core. These experiments never supply product analysis results.
internal static class RuleProbe
{
    internal static void Run(string prior, string root)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(prior, "cases.json")));
        var row = cases.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "m3-fourier");
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var cutoff = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!
            .GetMethod("AfocalCutoffFrequencyCyclesPerMilliradian", flags)!;
        var value = typeof(DiffractionEngine).GetMethod("PupilValue", flags)!;
        var results = new List<object>();
        foreach (var w in optic.Wavelengths)
        foreach (var n in new[] { 8, 9, 15, 16, 17, 24, 31, 32, 33, 64 })
        foreach (var rule in new[] { "midpoint", "endpoint", "native" })
        foreach (var bounds in new[] { "full", "overlap" })
        {
            var f = 3.2;
            var shift = 2 * f / (double)cutoff.Invoke(null, new object[] { optic, w })!;
            var halfX = bounds == "full" ? 1 : 1 - shift / 2;
            var halfY = bounds == "full" ? 1 : Math.Sqrt(1 - shift * shift / 4);
            var coordinates = new List<(double, double)>();
            var weights = new List<double>();
            var indices = new List<(int X, int Y)>();
            double Grid(int i) => rule == "midpoint" ? -1 + (2d * i + 1) / n
                : rule == "endpoint" ? -1 + 2d * i / (n - 1) : (i - n / 2d) / (n / 2d - 1);
            double divisor = rule == "midpoint" ? n : rule == "endpoint" ? n - 1 : n - 2;
            for (var iy = 0; iy < n; iy++) for (var ix = 0; ix < n; ix++)
            {
                var x = Grid(ix) * halfX; var y = Grid(iy) * halfY;
                var left = (X: x - shift / 2, Y: y); var right = (X: x + shift / 2, Y: y);
                if (left.X * left.X + y * y > 1 + 1e-12 || right.X * right.X + y * y > 1 + 1e-12) continue;
                coordinates.Add(left); coordinates.Add(right);
                weights.Add(4 * halfX * halfY / (divisor * divisor));
                indices.Add((ix, iy));
            }
            var wf = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), w, coordinates, 0,
                optic.Wavelengths.First(x => x.IsPrimary), optic.RayAimingEnabled);
            Complex P(WavefrontSample s) => (Complex)value.Invoke(null, new object[] { s, w, 0d, wf })!;
            var analyticArea = 2 * Math.Acos(shift / 2) - shift * Math.Sqrt(1 - shift * shift / 4);
            var products = Enumerable.Range(0, weights.Count).Select(i => P(wf.Samples[2 * i + 1]) * Complex.Conjugate(P(wf.Samples[2 * i]))).ToArray();
            foreach (var pattern in new[] { "all", "even-checker", "odd-checker", "even-even", "odd-odd", "even-x", "odd-x", "even-y", "odd-y" })
            {
                bool Accept(int i) => pattern switch
                {
                    "even-checker" => (indices[i].X + indices[i].Y) % 2 == 0,
                    "odd-checker" => (indices[i].X + indices[i].Y) % 2 != 0,
                    "even-even" => indices[i].X % 2 == 0 && indices[i].Y % 2 == 0,
                    "odd-odd" => indices[i].X % 2 != 0 && indices[i].Y % 2 != 0,
                    "even-x" => indices[i].X % 2 == 0, "odd-x" => indices[i].X % 2 != 0,
                    "even-y" => indices[i].Y % 2 == 0, "odd-y" => indices[i].Y % 2 != 0, _ => true
                };
                var sum = Complex.Zero; var apertureArea = 0d;
                for (var i = 0; i < weights.Count; i++) if (Accept(i)) { sum += weights[i] * products[i]; apertureArea += weights[i]; }
                var multiplier = pattern == "all" ? 1 : pattern is "even-even" or "odd-odd" ? 4 : 2;
                results.Add(new { w.Micrometers, n, rule, bounds, pattern,
                    raw = multiplier * sum.Magnitude / Math.PI,
                    areaCorrected = apertureArea == 0 ? 0 : sum.Magnitude / apertureArea * analyticArea / Math.PI });
            }
        }
        JsonFiles.Write(Path.Combine(root, "quadrature-patterns.json"), results);
        Console.WriteLine("Independent rule probe complete");
    }
}
