using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private const string Number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][-+]?\d+)?";
    private static Axis DiffractionAxis(string key) => key.Contains("Through Focus", StringComparison.Ordinal) ? Defocus
        : key.Contains("vs Field", StringComparison.Ordinal) ? Field : key == "Geometric MTF" ? new("SpatialFrequency", "CyclesPerMillimeter") : ImageHeight;
    private static Axis DiffractionValueAxis(string key) => ExtendedAnalysisContracts.IsMtfContract(key) ? new("Modulation", "Dimensionless") : new("Irradiance", "Dimensionless");
    private static NumericResult? WorkbenchDiffraction(AnalysisData data, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (!ExtendedAnalysisContracts.IsMtfContract(key) && !ExtendedAnalysisContracts.IsPsfProfile(key)) return null;
        var result = new NumericResult { Semantics = "Explicit scalar diffraction/geometric OTF contract; selected wavelength; native physical normalization." };
        for (var i = 0; i < data.PlotSeries.Count; i++)
        {
            var s = data.PlotSeries[i];
            result.Series.Add(PhysicalNormalization.Convert(new("curve:" + i, s.Name, s.Points.Select(p => p.X).ToArray(), s.Points.Select(p => p.Y).ToArray(),
                new(s.XQuantity.ToString(), s.XUnit.ToString()), new(s.YQuantity.ToString(), s.YUnit.ToString())), DiffractionAxis(key), DiffractionValueAxis(key), result.Transformations));
        }
        return result;
    }
    private static NumericResult? ZemaxDiffraction(string path, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (!ExtendedAnalysisContracts.IsMtfContract(key) && !ExtendedAnalysisContracts.IsPsfProfile(key)) return null;
        var result = new NumericResult { Semantics = "Native scalar diffraction/OTF export with explicit selected settings; no fitted intensity or coordinate correction." };
        if (ExtendedAnalysisContracts.IsPsfProfile(key))
        {
            var rows = File.ReadAllLines(Path.ChangeExtension(path, ".txt")).Select(l => Regex.Match(l, @"^\s*(\d+)\s+(" + Number + @")\s+(" + Number + @")\s*$")).Where(m => m.Success)
                .Select(m => new[] { double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Parse(m.Groups[2].Value), Parse(m.Groups[3].Value) }).ToArray();
            Require(rows.Length >= 2 && rows.Select((v, i) => v[0] == i).All(v => v), "Native profile sample indices are missing or discontinuous");
            result.Series.Add(new("curve:0", "Native X profile", rows.Select(v => v[1]).ToArray(), rows.Select(v => v[2]).ToArray(), ImageHeight, DiffractionValueAxis(key)));
            return result;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var groups = doc.RootElement.GetProperty("dataSeries");
        Require(groups.GetArrayLength() == (key.Contains("vs Field", StringComparison.Ordinal) ? 6 : 1), "Native MTF group count differs from explicit selected scope");
        var index = 0;
        foreach (var g in groups.EnumerateArray())
        {
            var x = g.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var rows = g.GetProperty("y").EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
            Require(rows.Length == x.Length && rows.All(v => v.Length == 2), "Native MTF requires tangential and sagittal columns");
            for (var col = 0; col < 2; col++) result.Series.Add(new("curve:" + index++, "Native " + (col == 0 ? "tangential" : "sagittal"), x, rows.Select(v => v[col]).ToArray(), DiffractionAxis(key), DiffractionValueAxis(key)));
        }
        return result;
    }
}
