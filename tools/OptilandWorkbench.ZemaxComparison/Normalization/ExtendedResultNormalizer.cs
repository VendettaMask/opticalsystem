using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static Axis NativePhysicalFieldAxis(CanonicalAnalysisRequest r) => r.FieldDefinition switch
    {
        "ObjectHeight" => new("ObjectHeight", "Millimeter"),
        "Angle" => new("FieldAngle", "Degree"),
        _ => throw new InvalidDataException("Native field-grid conversion is not verified for " + r.FieldDefinition + "; no angular/image-height substitution")
    };
    private static readonly Axis Defocus = new("Defocus", "Millimeter");
    private static readonly Axis Pupil = new("PupilCoordinate", "Dimensionless");
    private static readonly Axis Wavelength = new("Wavelength", "Micrometer");
    private static readonly Axis Field = new("NormalizedField", "Dimensionless");
    private static readonly Axis ImageHeight = new("ImageHeight", "Micrometer");
    private static readonly Axis Coefficient = new("Coefficient", "Millimeter");
    private static readonly string[] SeidelIds = ["S1", "S2", "S3", "S4", "S5", "CL", "CT"];
    private static readonly string[] WaveIds = ["W040", "W131", "W222", "W220P", "W311", "W020", "W111"];
    private static readonly string[] TransverseIds = ["TSPH", "TSCO", "TTCO", "TAST", "TPFC", "TSFC", "TTFC", "TDIS", "TAXC", "TLAC"];
    private static readonly string[] LongitudinalIds = ["LSPH", "LSCO", "LTCO", "LAST", "LPFC", "LSFC", "LTFC", "LDIS", "LAXC", "LLAC"];

    public static NumericResult Workbench(AnalysisData data, CanonicalAnalysisRequest r)
    {
        if (WorkbenchAngleScan(data, r) is { } angleScan) return angleScan;
        if (WorkbenchGeometry(data, r) is { } geometry) return geometry;
        if (WorkbenchSystemReport(data, r) is { } report) return report;
        if (WorkbenchRmsScan(data, r) is { } scan) return scan;
        if (WorkbenchDiffraction(data, r) is { } diffraction) return diffraction;
        if (WorkbenchEnergy(data, r) is { } energy) return energy;
        if (WorkbenchPupil(data, r) is { } pupil) return pupil;
        var result = new NumericResult { Semantics = $"Explicit {r.CanonicalAnalysisKey} contract; wavelength scope: {r.WavelengthScope}." };
        void Curve(AnalysisSeries s, string id, Axis x, Axis y, double divisor = 1)
        {
            var series = new Series1DResult(id, s.Name, s.Points.Select(p => p.Y / divisor).ToArray(), s.Points.Select(p => p.X).ToArray(),
                x, new(s.XQuantity.ToString(), s.XUnit.ToString()));
            result.Series.Add(PhysicalNormalization.Convert(series, x, y, result.Transformations));
        }
        switch (r.CanonicalAnalysisKey)
        {
            case "Seidel Coefficients":
            case "Seidel Diagram":
                var coefficients = ((JsonElement)data.Values["SeidelCoefficientsMillimeters"]).Deserialize<double[][]>()!;
                AddCoefficients(result, coefficients);
                if (r.CanonicalAnalysisKey == "Seidel Coefficients")
                {
                    AddCoefficientTable(result, ((JsonElement)data.Values["WaveAberrationCoefficients"]).Deserialize<double[][]>()!, WaveIds, new("WaveCoefficient", "Wave"));
                    AddCoefficientTable(result, ((JsonElement)data.Values["TransverseAberrationCoefficientsMillimeters"]).Deserialize<double[][]>()!, TransverseIds, Coefficient);
                    AddCoefficientTable(result, ((JsonElement)data.Values["LongitudinalAberrationCoefficientsMillimeters"]).Deserialize<double[][]>()!, LongitudinalIds, Coefficient);
                }
                break;
            case "Field Curvature":
            case "Field Curvature and Distortion":
                Require(data.PlotSeries.Count == 2, "Expected selected-wavelength tangential/sagittal field curves");
                Curve(data.PlotSeries[0], "tangential", Field, Defocus, r.MaximumFieldRadius);
                Curve(data.PlotSeries[1], "sagittal", Field, Defocus, r.MaximumFieldRadius);
                if (r.CanonicalAnalysisKey == "Field Curvature and Distortion")
                    Curve(data.PlotPanes![1].Series.Single(), "distortion", Field, new("Distortion", "Percent"), r.MaximumFieldRadius);
                break;
            case "Color Focus Shift": Curve(data.PlotSeries.Single(), "focus", Wavelength, Defocus); break;
            case "Lateral Color":
                Require(data.PlotSeries.Count == 3, "Expected extreme-wavelength color and signed primary Airy bounds");
                Curve(data.PlotSeries[0], "color", Field, ImageHeight, r.MaximumFieldRadius);
                Curve(data.PlotSeries[2], "airy-positive", Field, ImageHeight, r.MaximumFieldRadius);
                Curve(data.PlotSeries[1], "airy-negative", Field, ImageHeight, r.MaximumFieldRadius); break;
            case "Axial Aberration":
                Require(data.PlotSeries.Count == r.WavelengthCount, "Axial aberration must retain every defined wavelength");
                for (var i = 0; i < data.PlotSeries.Count; i++) Curve(data.PlotSeries[i], "wavelength:" + (i + 1), Pupil, Defocus);
                break;
            default: throw new InvalidDataException("No Workbench normalizer for " + r.CanonicalAnalysisKey);
        }
        result.Transformations.Add("Exchange plot axes into independent-variable order; field normalization uses the model's maximum field radius. No fitted translation, scale or intensity normalization.");
        return result;
    }

    public static NumericResult Zemax(string path, CanonicalAnalysisRequest r)
    {
        if (ZemaxAngleScan(path, r) is { } angleScan) return angleScan;
        if (ZemaxGeometry(path, r) is { } geometry) return geometry;
        if (ZemaxSystemReport(path, r) is { } report) return report;
        if (ZemaxRmsScan(path, r) is { } scan) return scan;
        if (ZemaxDiffraction(path, r) is { } diffraction) return diffraction;
        if (ZemaxEnergy(path, r) is { } energy) return energy;
        if (ZemaxPupil(path, r) is { } pupil) return pupil;
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement;
        var result = new NumericResult { Semantics = $"Native {r.CanonicalAnalysisKey}; explicit captured settings; wavelength scope: {r.WavelengthScope}." };
        if (r.CanonicalAnalysisKey is "Seidel Coefficients" or "Seidel Diagram")
        {
            var textPath = Path.Combine(Path.GetDirectoryName(path)!, r.CanonicalAnalysisKey == "Seidel Diagram" ? "coefficients.txt" : "data.txt");
            var text = File.ReadAllText(textPath);
            AddCoefficients(result, ParseSeidelTable(text));
            if (r.CanonicalAnalysisKey == "Seidel Coefficients")
            {
                AddCoefficientTable(result, ParseCoefficientTable(text, WaveIds), WaveIds, new("WaveCoefficient", "Wave"));
                AddCoefficientTable(result, ParseCoefficientTable(text, TransverseIds), TransverseIds, Coefficient);
                AddCoefficientTable(result, ParseCoefficientTable(text, LongitudinalIds), LongitudinalIds, Coefficient);
            }
            result.Transformations.Add("Native coefficient table identified by invariant SPHA/COMA/ASTI/FCUR/DIST/CLA/CTR column codes; six printed decimal places, surface order including image and total. Diagram uses a separately captured primary-wavelength Seidel coefficient table.");
            return result;
        }
        var groups = root.GetProperty("dataSeries");
        Require(groups.GetArrayLength() == 1, "Expected exactly one native curve group for this contract");
        var g = groups[0];
        var xs = g.GetProperty("x").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        var rows = g.GetProperty("y").EnumerateArray().Select(e => e.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Require(xs.Length == rows.Length && rows.Length > 1 && rows.All(row => row.Length == rows[0].Length), "Invalid native curve matrix");
        void Curve(int col, string id, Axis x, Axis y, double divisor = 1)
        {
            Require(col < rows[0].Length, "Missing native column " + col);
            result.Series.Add(new(id, "Native column " + col, xs.Select(v => v / divisor).ToArray(), rows.Select(row => row[col]).ToArray(), x, y));
        }
        switch (r.CanonicalAnalysisKey)
        {
            case "Field Curvature":
            case "Field Curvature and Distortion":
                Require(r.FieldDefinition is "ObjectHeight" or "Angle", "Field-coordinate conversion requires an audited native field-type mapping");
                Require(rows[0].Length == 5, "Field curvature native contract has five columns");
                Curve(0, "tangential", Field, Defocus, r.MaximumFieldRadius);
                Curve(1, "sagittal", Field, Defocus, r.MaximumFieldRadius);
                if (r.CanonicalAnalysisKey == "Field Curvature and Distortion") Curve(4, "distortion", Field, new("Distortion", "Percent"), r.MaximumFieldRadius);
                break;
            case "Color Focus Shift": Require(rows[0].Length == 1, "Expected one continuous chromatic-focus column"); Curve(0, "focus", Wavelength, Defocus); break;
            case "Lateral Color":
                Require(rows[0].Length == 3, "Expected extreme color and two signed Airy bounds");
                Curve(0, "color", Field, ImageHeight); Curve(1, "airy-positive", Field, ImageHeight); Curve(2, "airy-negative", Field, ImageHeight); break;
            case "Axial Aberration":
                Require(rows[0].Length == r.WavelengthCount, "Native axial aberration wavelength count differs from lens");
                for (var i = 0; i < rows[0].Length; i++) Curve(i, "wavelength:" + (i + 1), Pupil, Defocus); break;
            default: throw new InvalidDataException("No native normalizer for " + r.CanonicalAnalysisKey);
        }
        return result;
    }

    public static double[][] ParseSeidelTable(string text)
        => ParseCoefficientTable(text, ["SPHA", "COMA", "ASTI", "FCUR", "DIST", "CLA", "CTR"]);

    private static double[][] ParseCoefficientTable(string text, string[] codes)
    {
        var lines = text.Split('\n');
        var header = Array.FindIndex(lines, l => codes.All(l.Contains));
        Require(header >= 0, "Native Seidel column header missing");
        var rows = new List<double[]>();
        foreach (var line in lines.Skip(header + 1))
        {
            var parts = Regex.Split(line.Trim(), @"\s+");
            if (parts.Length == 1 && string.IsNullOrEmpty(parts[0])) { if (rows.Count > 0) break; continue; }
            Require(parts.Length == codes.Length + 1, "Malformed native coefficient row");
            rows.Add(parts.Skip(1).Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray());
        }
        Require(rows.Count >= 2, "Native Seidel table has insufficient rows");
        return rows.ToArray();
    }
    private static void AddCoefficients(NumericResult result, double[][] rows)
        => AddCoefficientTable(result, rows, SeidelIds, Coefficient);

    private static void AddCoefficientTable(NumericResult result, double[][] rows, string[] codes, Axis axis)
    {
        Require(rows.Length >= 2 && rows.All(row => row.Length == codes.Length && row.All(double.IsFinite)), "Invalid coefficient matrix");
        for (var i = 0; i < codes.Length; i++) result.Series.Add(new(codes[i], codes[i],
            Enumerable.Range(1, rows.Length).Select(n => (double)n).ToArray(), rows.Select(row => row[i]).ToArray(), new("SurfaceNumber", "Dimensionless"), axis));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
