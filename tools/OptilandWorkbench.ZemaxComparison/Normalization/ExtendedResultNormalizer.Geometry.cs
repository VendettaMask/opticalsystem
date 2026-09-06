using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static readonly Axis Surface = new("SurfaceNumber", "Dimensionless");
    private static readonly Axis Coordinate = new("Coordinate", "Millimeter");
    private static readonly Axis Angle = new("IncidentAngle", "Degree");
    private static readonly string[] CardinalIds = ["focal-length", "focal-plane", "principal-plane", "anti-principal-plane", "nodal-plane", "anti-nodal-plane"];
    private static readonly HashSet<string> GeometryKeys = ["Single Ray Trace", "Grid Distortion", "Full Field Aberration", "Cardinal Points Data", "Y-Ybar", "Angle vs Image Height"];

    private static NumericResult? WorkbenchGeometry(AnalysisData data, CanonicalAnalysisRequest r)
    {
        if (!GeometryKeys.Contains(r.CanonicalAnalysisKey)) return null;
        var result = new NumericResult { Semantics = "Physical geometry under explicit captured settings; unrounded Workbench data." };
        double Value(string key) => ((JsonElement)data.Values[key]).GetDouble();
        double[][] Matrix(string key) => ((JsonElement)data.Values[key]).Deserialize<double[][]>()!;
        switch (r.CanonicalAnalysisKey)
        {
            case "Single Ray Trace": AddRayTable(result, Matrix("RealRayData"), r.SurfaceCount); break;
            case "Cardinal Points Data": AddCardinal(result, Matrix("CardinalPositionsMillimeters")); break;
            case "Y-Ybar":
                var count = (int)Value("SurfaceCount");
                foreach (var kind in new[] { "Chief", "Marginal" })
                    result.Series.Add(new(kind, kind, Enumerable.Range(1, count).Select(i => (double)i).ToArray(),
                        Enumerable.Range(1, count).Select(i => Value($"Surface {i} {kind}")).ToArray(), Surface, new("RayHeight", "Millimeter")));
                break;
            case "Angle vs Image Height":
                Require(data.PlotSeries.Count == 3, "Incident angle requires the lower, chief and upper ray curves");
                for (var i = 0; i < 3; i++) result.Series.Add(new("ray:" + i, "ray:" + i,
                    data.PlotSeries[i].Points.Select(p => p.X).ToArray(), data.PlotSeries[i].Points.Select(p => p.Y).ToArray(), new("ImageHeight", "Millimeter"), Angle));
                break;
            case "Grid Distortion":
                var n = (int)Value("GridSize"); var points = data.PlotSeries.Last().Points;
                Require(n == 13 && points.Count == n * n, "Grid distortion contract requires a 13 by 13 field grid");
                var a = Value("MappingA"); var b = Value("MappingB"); var c = Value("MappingC"); var d = Value("MappingD");
                // The product draws in object coordinates. Its explicit linear optical map restores
                // image coordinates; no parameters are fitted to the native comparison data.
                var actualX = points.Select(p => a * p.X + b * p.Y).ToArray();
                var actualY = points.Select(p => c * p.X + d * p.Y).ToArray();
                AddGridDistortion(result, actualX, actualY, n);
                result.Transformations.Add("Map Workbench's displayed object-coordinate grid to relative image coordinates using its own MappingA/B/C/D, scale=1 and on-axis reference field=1.");
                break;
            case "Full Field Aberration":
                var s = data.PlotSeries.Single();
                var x = s.Points.Select(p => p.X).Distinct().Order().ToArray(); var y = s.Points.Select(p => p.Y).Distinct().Order().ToArray();
                var z = y.Select(_ => new double?[x.Length]).ToArray();
                foreach (var p in s.Points) z[Array.BinarySearch(y, p.Y)][Array.BinarySearch(x, p.X)] = p.Value;
                result.Grids.Add(new("defocus", x, y, z, new(s.XQuantity.ToString(), s.XUnit.ToString()), new(s.YQuantity.ToString(), s.YUnit.ToString()), new("WavefrontError", "Wave")));
                break;
        }
        return result;
    }

    private static NumericResult? ZemaxGeometry(string path, CanonicalAnalysisRequest r)
    {
        if (!GeometryKeys.Contains(r.CanonicalAnalysisKey)) return null;
        var result = new NumericResult { Semantics = "Native geometric output. Report settings are verified before numerical acceptance." };
        var text = File.ReadAllText(Path.ChangeExtension(path, ".txt"));
        switch (r.CanonicalAnalysisKey)
        {
            case "Single Ray Trace":
                var lines = text.Split('\n'); var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("OBJ", StringComparison.Ordinal));
                Require(start >= 0, "Native real-ray object row missing");
                var rayRows = new List<double[]>();
                foreach (var line in lines.Skip(start + 1))
                {
                    if (string.IsNullOrWhiteSpace(line)) break;
                    var tokens = Regex.Split(line.Trim(), @"\s+");
                    Require(tokens.Length >= 12, "Malformed real-ray row");
                    rayRows.Add(tokens.Take(12).Select(Parse).ToArray());
                }
                AddRayTable(result, rayRows.ToArray(), r.SurfaceCount); break;
            case "Cardinal Points Data":
                VerifyPrimaryRange(text, r, true);
                var cardinal = Regex.Matches(text, @"^.*?:\s+([-+\d.Ee]+)\s+([-+\d.Ee]+)\s*$", RegexOptions.Multiline)
                    .Select(m => new[] { Parse(m.Groups[1].Value), Parse(m.Groups[2].Value) }).ToArray();
                AddCardinal(result, cardinal); break;
            case "Y-Ybar":
                VerifyPrimaryRange(text, r, false);
                var yybar = NumericRows(text, 3);
                Require(yybar.Length == r.SurfaceCount - 1 && yybar.Select(row => row[0]).SequenceEqual(Enumerable.Range(1, r.SurfaceCount - 1).Select(i => (double)i)), "Y-Ybar surface range mismatch");
                for (var i = 1; i <= 2; i++) result.Series.Add(new(i == 1 ? "Chief" : "Marginal", "Native paraxial ray", yybar.Select(row => row[0]).ToArray(), yybar.Select(row => row[i]).ToArray(), Surface, new("RayHeight", "Millimeter")));
                break;
            case "Angle vs Image Height":
                var wave = Regex.Matches(text, @":\s*([\d.]+)\s*(?:µm|μm|微米)").Select(m => Parse(m.Groups[1].Value)).ToArray();
                Require(wave.Length == 1 && Math.Abs(wave[0] - r.PrimaryWavelengthMicrometers) <= 0.00005, "Incident-angle primary wavelength readback differs");
                var angles = NumericRows(text, 4);
                Require(angles.Length == 21, "Incident-angle field-density readback differs from 20 intervals");
                for (var i = 0; i < 3; i++) result.Series.Add(new("ray:" + i, "Native ray " + i, angles.Select(row => row[0]).ToArray(), angles.Select(row => row[i + 1]).ToArray(), new("ImageHeight", "Millimeter"), Angle));
                result.Transformations.Add("Primary wavelength and 21 samples verified from native text; native +Y field scan and signed incidence, lower/chief/upper column order.");
                break;
            case "Grid Distortion":
                Require(r.Field == 1, "Grid distortion image-coordinate comparison currently requires the on-axis reference field");
                var gridRows = text.Split('\n').Select(line => Regex.Split(line.Trim(), @"\s+")).Where(row => row.Length == 10 && row[^1].EndsWith('%'))
                    .Select(row => row.Select(value => Parse(value.TrimEnd('%'))).ToArray()).OrderBy(row => row[1]).ThenBy(row => row[0]).ToArray();
                Require(gridRows.Length == 169, "Native grid distortion requires 13 by 13 samples");
                Require(gridRows.Select(row => (row[0], row[1])).Distinct().Count() == 169, "Native grid contains duplicate lattice coordinates");
                AddGridDistortion(result, gridRows.Select(row => row[7]).ToArray(), gridRows.Select(row => row[8]).ToArray(), 13); break;
            case "Full Field Aberration":
                using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    var grids = doc.RootElement.GetProperty("dataGrids"); Require(grids.GetArrayLength() == 1, "Expected native defocus grid");
                    var g = grids[0]; var n = g.GetProperty("nx").GetInt32(); var m = g.GetProperty("ny").GetInt32();
                    Require(n == 11 && m == 11, "Native full-field grid sampling differs");
                    var x = Enumerable.Range(0, n).Select(i => g.GetProperty("minX").GetDouble() + i * g.GetProperty("dx").GetDouble()).ToArray();
                    var y = Enumerable.Range(0, m).Select(i => g.GetProperty("minY").GetDouble() + i * g.GetProperty("dy").GetDouble()).ToArray();
                    var z = g.GetProperty("values").EnumerateArray().Select((row, j) => row.EnumerateArray().Select((v, i) =>
                        x[i] * x[i] + y[j] * y[j] > r.MaximumFieldRadius * r.MaximumFieldRadius * (1 + 1e-12) ? null : (double?)v.GetDouble()).ToArray()).ToArray();
                    var axis = NativePhysicalFieldAxis(r);
                    result.Grids.Add(new("defocus", x, y, z, axis, axis, new("WavefrontError", "Wave")));
                    result.Transformations.Add("Mask samples outside the explicitly selected elliptical field boundary; valid zero-valued coefficients are retained.");
                }
                break;
        }
        return result;
    }

    private static void VerifyPrimaryRange(string text, CanonicalAnalysisRequest r, bool direction)
    {
        var header = Regex.Matches(text, @"^[^:\r\n]+:\s*([-+\d.Ee]+)\s*$", RegexOptions.Multiline).Take(3).Select(m => Parse(m.Groups[1].Value)).ToArray();
        Require(header.Length == 3 && header[0] == 1 && header[1] == r.SurfaceCount - 1 && Math.Abs(header[2] - r.PrimaryWavelengthMicrometers) <= 0.0000005,
            "Native report range or primary wavelength differs from the explicit full-system contract");
        if (direction) Require(Regex.IsMatch(text, @":\s*Y-Z\s*$", RegexOptions.Multiline), "Native cardinal orientation is not Y-Z");
    }
    private static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static double[][] NumericRows(string text, int width) => text.Split('\n').Select(line => Regex.Split(line.Trim(), @"\s+"))
        .Where(row => row.Length == width && row.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        .Select(row => row.Select(Parse).ToArray()).ToArray();
    private static void AddCardinal(NumericResult result, double[][] rows)
    {
        Require(rows.Length == 6 && rows.All(row => row.Length == 2), "Cardinal report requires six object/image pairs");
        for (var i = 0; i < 2; i++) result.Series.Add(new(i == 0 ? "object" : "image", string.Join(",", CardinalIds),
            Enumerable.Range(0, 6).Select(j => (double)j).ToArray(), rows.Select(row => row[i]).ToArray(), new("Coordinate", "Dimensionless"), Coordinate));
    }
    private static void AddRayTable(NumericResult result, double[][] rows, int surfaces)
    {
        Require(rows.Length == surfaces - 1 && rows.All(row => row.Length == 12) && rows.Select(row => row[0]).SequenceEqual(Enumerable.Range(1, surfaces - 1).Select(i => (double)i)), "Incomplete or unordered real-ray surface table");
        string[] names = ["x", "y", "z", "l", "m", "n", "normal-x", "normal-y", "normal-z", "incidence", "path-length"];
        for (var i = 1; i < 12; i++) result.Series.Add(new(names[i - 1], names[i - 1], rows.Select(row => row[0]).ToArray(), rows.Select(row => row[i]).ToArray(), Surface,
            i <= 3 || i == 11 ? Coordinate : i == 10 ? Angle : new("DirectionCosine", "Dimensionless")));
    }
    private static void AddGridDistortion(NumericResult result, double[] x, double[] y, int n)
    {
        var axis = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        foreach (var (values, name) in new[] { (x, "image-x"), (y, "image-y") }) result.Grids.Add(new(name, axis, axis,
            Enumerable.Range(0, n).Select(row => values.Skip(row * n).Take(n).Select(v => (double?)v).ToArray()).ToArray(),
            new("FieldGridColumn", "Dimensionless"), new("FieldGridRow", "Dimensionless"), new("ImageHeight", "Millimeter")));
    }
}
