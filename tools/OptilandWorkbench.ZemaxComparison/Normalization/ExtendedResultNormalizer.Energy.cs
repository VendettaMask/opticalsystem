using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static Axis EnergyAxis(string key) => key == "Relative Illumination" ? Field : key == "Geometric Line Edge Spread" ? ImageHeight : new("Radius", "Micrometer");
    private static NumericResult? WorkbenchEnergy(AnalysisData data, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (!ExtendedAnalysisContracts.IsEnergyContract(key) && key != "Extended Source Encircled Energy") return null;
        var result = new NumericResult
        {
            Semantics = key == "Extended Source Encircled Energy"
                ? "Captured 2026 R1 plot convention: 100 cumulative knots display CDF at radius minus maximum/99, then monotone cubic output at 396 coordinates. Traced ray positions and weights are unchanged."
                : "Explicit selected-wavelength energy/field contract; geometric and diffraction energy remain distinct methods."
        };
        if (key == "Zernike")
        {
            var numbers = ((JsonElement)data.Values["CoefficientNumbers"]).Deserialize<double[]>()!;
            var values = ((JsonElement)data.Values["CoefficientsWaves"]).Deserialize<double[]>()!;
            result.Series.Add(new("fringe", "Fringe coefficients", numbers, values, new("CoefficientIndex", "Dimensionless"), new("WaveCoefficient", "Wave")));
            return result;
        }
        if (key == "RMS Field Map")
        {
            var s = data.PlotSeries.Single(); var x = s.Points.Select(p => p.X).Distinct().Order().ToArray(); var y = s.Points.Select(p => p.Y).Distinct().Order().ToArray();
            result.Grids.Add(new("rms", x, y, y.Select(v => s.Points.Where(p => p.Y == v).OrderBy(p => p.X).Select(p => p.Value).ToArray()).ToArray(),
                new(s.XQuantity.ToString(), s.XUnit.ToString()), new(s.YQuantity.ToString(), s.YUnit.ToString()), new("WavefrontError", "Wave")));
            return result;
        }
        for (var i = 0; i < data.PlotSeries.Count; i++)
        {
            var s = data.PlotSeries[i];
            var x = s.Points.Select(p => key == "Relative Illumination" ? p.X / r.MaximumFieldRadius : p.X).ToArray();
            result.Series.Add(new("curve:" + i, s.Name, x, s.Points.Select(p => p.Y).ToArray(), EnergyAxis(key), new(s.YQuantity.ToString(), s.YUnit.ToString())));
        }
        return result;
    }
    private static NumericResult? ZemaxEnergy(string path, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (!ExtendedAnalysisContracts.IsEnergyContract(key) && key != "Extended Source Encircled Energy") return null;
        var result = new NumericResult { Semantics = "Native configured energy/field contract. No fitted normalization." };
        if (key == "Zernike")
        {
            var rows = File.ReadAllLines(Path.ChangeExtension(path, ".txt")).Select(l => Regex.Match(l, @"^\s*Z\s*(\d+)\s+(" + Number + @")\b")).Where(m => m.Success).ToArray();
            Require(rows.Length == 37 && rows.Select((m, i) => Parse(m.Groups[1].Value) == i + 1).All(v => v), "Expected 37 sequential native Fringe coefficients");
            result.Series.Add(new("fringe", "Native Fringe coefficients", rows.Select(m => Parse(m.Groups[1].Value)).ToArray(), rows.Select(m => Parse(m.Groups[2].Value)).ToArray(), new("CoefficientIndex", "Dimensionless"), new("WaveCoefficient", "Wave")));
            return result;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (key == "RMS Field Map")
        {
            var grids = doc.RootElement.GetProperty("dataGrids"); Require(grids.GetArrayLength() == 1, "Expected one native RMS grid"); var g = grids[0];
            var axis = NativePhysicalFieldAxis(r);
            var x = Enumerable.Range(0, g.GetProperty("nx").GetInt32()).Select(i => g.GetProperty("minX").GetDouble() + i * g.GetProperty("dx").GetDouble()).ToArray();
            var y = Enumerable.Range(0, g.GetProperty("ny").GetInt32()).Select(i => g.GetProperty("minY").GetDouble() + i * g.GetProperty("dy").GetDouble()).ToArray();
            var values = g.GetProperty("values").Deserialize<double?[][]>()!;
            Require(x.Length == 11 && y.Length == 11, "Native RMS field sampling differs");
            result.Grids.Add(new("rms", x, y, values, axis, axis, new("WavefrontError", "Wave"))); return result;
        }
        var groups = doc.RootElement.GetProperty("dataSeries");
        var expected = key == "Diffraction Encircled Energy" ? 2 : key == "Encircled Energy" ? r.FieldCount : 1;
        Require(groups.GetArrayLength() == expected, "Native energy/illumination curve count differs");
        var id = 0;
        foreach (var g in groups.EnumerateArray())
        {
            var x = g.GetProperty("x").EnumerateArray().Select(v => v.GetDouble() / (key == "Relative Illumination" ? r.MaximumFieldRadius : 1)).ToArray();
            var rows = g.GetProperty("y").Deserialize<double[][]>()!;
            var width = key == "Geometric Line Edge Spread" ? 4 : key == "Relative Illumination" ? 2 : 1;
            Require(rows.Length == x.Length && rows.All(v => v.Length == width), "Native energy/illumination column count differs");
            // Workbench exposes one line orientation per request. For its X-line,
            // the independent displacement is Y, hence native Y LSF/ERF columns.
            var columns = key == "Relative Illumination" ? new[] { 0 } : key == "Geometric Line Edge Spread" ? new[] { 2, 3 } : new[] { 0 };
            foreach (var col in columns)
            {
                var valueAxis = key is "Relative Illumination" or "Geometric Line Edge Spread" ? new Axis("Irradiance", "Dimensionless") : new Axis("EnergyFraction", "Dimensionless");
                result.Series.Add(new("curve:" + id++, "Native column " + col, x, rows.Select(v => v[col]).ToArray(), EnergyAxis(key), valueAxis));
            }
        }
        return result;
    }
}
