using System.Text.Json;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static bool IsAngleScan(string key) => key is "Angle vs Image Height - Through Pupil" or "Angle vs Image Height - Through Field";
    private static NumericResult? WorkbenchAngleScan(AnalysisData data, CanonicalAnalysisRequest r)
    {
        if (!IsAngleScan(r.CanonicalAnalysisKey)) return null;
        var s = data.PlotSeries.Single();
        Require(s.XQuantity == AnalysisAxisQuantity.ImageHeight && s.XUnit == AnalysisAxisUnit.Millimeter && s.YQuantity == AnalysisAxisQuantity.IncidentAngle && s.YUnit == AnalysisAxisUnit.Degree, "Angle scan requires image-local height in mm and projected angle in degrees");
        return AngleSeries(r, s.Points.Select(p => p.X).ToArray(), s.Points.Select(p => p.Y).ToArray());
    }
    private static NumericResult? ZemaxAngleScan(string path, CanonicalAnalysisRequest r)
    {
        if (!IsAngleScan(r.CanonicalAnalysisKey)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Require(r.FieldDefinition is "ObjectHeight" or "Angle", "Angle-ray inputs require a supported field definition");
        var expected = r.CanonicalAnalysisKey.EndsWith("Pupil", StringComparison.Ordinal)
            ? Enumerable.Range(0, 33).Select(i => new[] { 0d, 0d, 0d, -1 + i / 16d }).ToArray()
            : r.DefinedFields.Select(f => new[] { r.MaximumFieldRadius == 0 ? 0 : f[0] / r.MaximumFieldRadius,
                r.MaximumFieldRadius == 0 ? 0 : f[1] / r.MaximumFieldRadius, 0d, 0d }).ToArray();
        var inputs = doc.RootElement.GetProperty("angleRayInputs").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Require(inputs.Length == expected.Length && inputs.Zip(expected).All(p => p.First.SequenceEqual(p.Second)),
            "Native angle-ray input coordinates differ from the canonical request");
        var rows = doc.RootElement.GetProperty("angleRays").EnumerateArray().ToArray();
        Require(rows.Select((v, i) => v.GetProperty("number").GetInt32() == i + 1).All(v => v), "Native batch ray ordering differs");
        var y = rows.Select(v => v.GetProperty("error").GetInt32() == 0 ? v.GetProperty("y").GetDouble() : double.NaN).ToArray();
        var a = rows.Select(v => v.GetProperty("error").GetInt32() == 0 ? Math.Asin(Math.Clamp(v.GetProperty("m").GetDouble(), -1, 1)) * 180 / Math.PI : double.NaN).ToArray();
        return AngleSeries(r, y, a);
    }
    private static NumericResult AngleSeries(CanonicalAnalysisRequest r, double[] heights, double[] angles)
    {
        Require(heights.Length == (r.CanonicalAnalysisKey.EndsWith("Pupil", StringComparison.Ordinal) ? 33 : r.FieldCount), "Angle scan input count differs");
        var x = Enumerable.Range(0, heights.Length).Select(i => (double)i).ToArray();
        var axis = new Axis("SampleIndex", "Dimensionless");
        return new NumericResult
        {
            Semantics = "Ordered identical ray inputs; compare height and angle separately to preserve nonmonotone height curves. Native batch-ray result, not the built-in three-curve IHT plot.",
            Series = [new("height", "Image local Y", x, heights, axis, new("ImageHeight", "Millimeter")),
                new("angle", "asin(M)", x, angles, axis, new("IncidentAngle", "Degree")),
                new("valid", "Valid ray state", x, heights.Zip(angles).Select(p => double.IsFinite(p.First) && double.IsFinite(p.Second) ? 1d : 0d).ToArray(), axis, new("FiniteState", "Dimensionless"))]
        };
    }
}
