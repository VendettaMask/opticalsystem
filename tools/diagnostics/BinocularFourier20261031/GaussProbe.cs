using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class GaussProbe
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
        var gaussian = typeof(ApertureSampler).GetMethod("GenerateHuygensGaussianQuadrature", flags, [typeof(int), typeof(int), typeof(double)])!;
        var results = new List<object>();
        foreach (var w in optic.Wavelengths)
        foreach (var n in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 32, 64 })
        foreach (var mapping in new[] { "along", "across", "circle" })
        {
            var pupils = (IReadOnlyList<PupilSample>)gaussian.Invoke(null, new object[] { n, 4, 0d })!;
            var rule = pupils.Where((_, i) => i % 4 == 0).Select(p => (U: p.X * p.X, W: p.Weight * 4)).ToArray();
            var shift = 2 * 3.2 / (double)cutoff.Invoke(null, new object[] { optic, w })!;
            var half = shift / 2;
            var coordinates = new List<(double, double)>(); var weights = new List<double>();
            if (mapping == "circle")
            {
                var polar = ApertureSampler.GenerateGaussianQuadrature(n, 4 * n);
                foreach (var p in polar)
                {
                    if (Math.Pow(Math.Abs(p.X) + half, 2) + p.Y * p.Y > 1) continue;
                    coordinates.Add((p.X - half, p.Y)); coordinates.Add((p.X + half, p.Y)); weights.Add(p.Weight);
                }
            }
            else foreach (var a in rule) foreach (var b in rule)
            {
                double x, y, jac;
                if (mapping == "along")
                {
                    x = a.U * (1 - half); var height = Math.Sqrt(1 - (x + half) * (x + half));
                    y = b.U * height; jac = (1 - half) * height;
                }
                else
                {
                    y = a.U * Math.Sqrt(1 - half * half); var width = Math.Sqrt(1 - y * y) - half;
                    x = b.U * width; jac = Math.Sqrt(1 - half * half) * width;
                }
                foreach (var sx in new[] { -1, 1 }) foreach (var sy in new[] { -1, 1 })
                {
                    coordinates.Add((sx * x - half, sy * y)); coordinates.Add((sx * x + half, sy * y));
                    weights.Add(a.W * b.W * jac / Math.PI);
                }
            }
            var wf = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), w, coordinates, 0,
                optic.Wavelengths.First(x => x.IsPrimary), optic.RayAimingEnabled);
            Complex P(WavefrontSample s) => (Complex)value.Invoke(null, new object[] { s, w, 0d, wf })!;
            var sum = Complex.Zero;
            for (var i = 0; i < weights.Count; i++) sum += weights[i] * P(wf.Samples[2 * i + 1]) * Complex.Conjugate(P(wf.Samples[2 * i]));
            results.Add(new { w.Micrometers, n, mapping, count = weights.Count, sum.Real, sum.Imaginary, sum.Magnitude });
        }
        JsonFiles.Write(Path.Combine(root, "gaussian-rules.json"), results);
        Console.WriteLine("Gaussian overlap probe complete");
    }
}
