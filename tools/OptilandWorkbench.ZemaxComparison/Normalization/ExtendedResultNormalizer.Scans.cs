using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static Axis RmsScanAxis(CanonicalAnalysisRequest r) => r.CanonicalAnalysisKey switch
    {
        "RMS vs Wavelength" => Wavelength,
        "RMS vs Focus" => Defocus,
        _ => Field
    };
    private static Axis RmsMetricAxis(CanonicalAnalysisRequest r) => r.CanonicalAnalysisKey is "RMS vs Field" or "RMS vs Wavelength"
        ? new("Radius", "Micrometer") : new("WavefrontError", "Wave");
    private static NumericResult? WorkbenchRmsScan(AnalysisData data, CanonicalAnalysisRequest r)
    {
        if (!ExtendedAnalysisContracts.IsRmsScan(r.CanonicalAnalysisKey)) return null;
        var result = new NumericResult { Semantics = "GQ density 6; spot about centroid, wavefront piston-only chief reference; explicit scan settings." };
        var expected = r.CanonicalAnalysisKey == "RMS vs Focus" ? r.FieldCount : 1;
        Require(data.PlotSeries.Count == expected, "Unexpected Workbench RMS scan curve count");
        for (var i = 0; i < data.PlotSeries.Count; i++)
        {
            var s = data.PlotSeries[i]; var fieldScan = r.CanonicalAnalysisKey is "RMS vs Field" or "RMS Wavefront vs Field";
            var curve = new Series1DResult("curve:" + i, "RMS curve " + i,
                s.Points.Select(p => fieldScan ? p.X / r.MaximumFieldRadius * (r.FieldScanDirection.StartsWith('-') ? -1 : 1) : p.X).ToArray(), s.Points.Select(p => p.Y).ToArray(),
                RmsScanAxis(r), new(s.YQuantity.ToString(), s.YUnit.ToString()));
            result.Series.Add(PhysicalNormalization.Convert(curve, RmsScanAxis(r), RmsMetricAxis(r), result.Transformations));
        }
        return result;
    }
    private static NumericResult? ZemaxRmsScan(string path, CanonicalAnalysisRequest r)
    {
        if (!ExtendedAnalysisContracts.IsRmsScan(r.CanonicalAnalysisKey)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var groups = doc.RootElement.GetProperty("dataSeries");
        Require(groups.GetArrayLength() == 1, "Expected one native RMS scan group");
        var x = groups[0].GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var rows = groups[0].GetProperty("y").EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        var width = r.CanonicalAnalysisKey == "RMS vs Focus" ? r.FieldCount : 1;
        Require(rows.Length == x.Length && rows.All(row => row.Length == width), "Native RMS scan curve count differs");
        var result = new NumericResult { Semantics = "Native configured RMS data; normalized field or physical wavelength/focus axis; no fitted normalization." };
        var text = File.ReadAllText(Path.ChangeExtension(path, ".txt"));
        var centroid = r.CanonicalAnalysisKey is "RMS vs Field" or "RMS vs Wavelength";
        var reference = centroid ? @"(?:质心|中心|Centroid)" : @"(?:主光线|Chief Ray)";
        Require(Regex.IsMatch(text, @"(?:参考|Reference)\s*:\s*" + reference, RegexOptions.IgnoreCase), "Actual native RMS reference differs from canonical request (26.1 enum-name/readback mismatch)");
        Require(x.Length == (r.CanonicalAnalysisKey == "RMS vs Wavelength" ? 21 : 16), "Actual native RMS scan count differs from canonical request");
        if (r.CanonicalAnalysisKey is "RMS vs Field" or "RMS Wavefront vs Field")
        {
            Require(Math.Abs(Math.Abs(x[^1]) - r.MaximumFieldRadius) <= 1e-7 * Math.Max(1, r.MaximumFieldRadius), "Native RMS field extent differs from the selected scan direction");
            x = x.Select(v => v / r.MaximumFieldRadius).ToArray();
            Require(x.All(v => v >= 0), "Native RMS field axis must be distance along the configured orientation");
            result.Transformations.Add("Field coordinate is distance along " + r.FieldScanDirection + ", divided by the captured field extent; Workbench signed coordinates converted using the requested orientation.");
        }
        result.Transformations.Add("26.1 RMS enum-name discrepancy: physical reference and interval count verified from native report/data, not inferred from interface enum display names.");
        for (var i = 0; i < width; i++) result.Series.Add(new("curve:" + i, "Native RMS column " + i, x, rows.Select(row => row[i]).ToArray(), RmsScanAxis(r), RmsMetricAxis(r)));
        return result;
    }
}
