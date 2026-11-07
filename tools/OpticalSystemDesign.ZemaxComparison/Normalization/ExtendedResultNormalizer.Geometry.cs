using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.ZemaxComparison.Normalization;

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
            case "Single Ray Trace": AddRayTable(result, Matrix("RealRayData"), ActiveRaySurfaces(r.SurfaceCount, data)); break;
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

    private static NumericResult? ZemaxGeometry(string path, CanonicalAnalysisRequest r, string? workbenchPath,
        IReadOnlyList<int>? sourceSurfaceNumbers)
    {
        if (!GeometryKeys.Contains(r.CanonicalAnalysisKey)) return null;
        var result = new NumericResult { Semantics = "Native geometric output. Report settings are verified before numerical acceptance." };
        var text = File.ReadAllText(Path.ChangeExtension(path, ".txt"));
        switch (r.CanonicalAnalysisKey)
        {
            case "Single Ray Trace":
                var proof = workbenchPath is null ? null : JsonFiles.Read<AnalysisData>(workbenchPath);
                var expectedSurfaces = ActiveRaySurfaces(r.SurfaceCount, proof);
                var omitted = Enumerable.Range(1, r.SurfaceCount - 1).Except(expectedSurfaces).ToArray();
                Require(sourceSurfaceNumbers is null || sourceSurfaceNumbers.Count == r.SurfaceCount,
                    "Imported surface map length differs from the request");
                var nativeOmitted = sourceSurfaceNumbers is null ? omitted : omitted.Select(i => sourceSurfaceNumbers[i]).ToArray();
                var firstNativeSurface = Enumerable.Range(1, r.SurfaceCount - 1).Except(nativeOmitted).First();
                var rayRows = NativeRealRayRows(text, firstNativeSurface);
                var modelPath = Path.Combine(Path.GetDirectoryName(path)!, "model.json");
                Require(omitted.Length == 0 || File.Exists(modelPath), "Ignored native rows require a captured surface model");
                if (File.Exists(modelPath))
                {
                    using var model = JsonDocument.Parse(File.ReadAllText(modelPath));
                    var signs = NativeReflectionConvention.PropagationSigns(model.RootElement, nativeOmitted);
                    NativeReflectionConvention.ConvertRayReport(rayRows, signs, model.RootElement);
                    if (signs.Any(sign => sign < 0)) result.Transformations.Add(
                        "Convert native signed-index direction, incident normal and segment length to physical propagation using explicit MIRROR rows; raw report retained.");
                    var originalCount = rayRows.Length;
                    rayRows = NativeRaySurfaceMap.ToImportedSurfaces(rayRows, model.RootElement, r.SurfaceCount,
                        nativeOmitted, sourceSurfaceNumbers);
                    if (rayRows.Length != originalCount) result.Transformations.Add(
                        "Fold native CoordinateBreak rows into imported physical-surface numbering using the captured model; retain the next physical hit's local coordinates, direction and normal, and sum physical segment lengths. Raw report retained.");
                    if (NormalizeGradient9MirrorDirections(rayRows, model.RootElement, proof, r.SurfaceCount))
                        result.Transformations.Add(
                            "Convert the verified zero-gap Gradient 9 mirror pair from the native axial-index boundary to directions in the local GRIN medium. Raw report retained.");
                    var unreported = UnreportedGradientPaths(rayRows, model.RootElement, proof, r.SurfaceCount);
                    if (unreported.Length != 0)
                    {
                        result = result with { UnreportedNativeRayPathSurfaces = unreported };
                        result.Transformations.Add("Zero length reported at the end of a verified nonzero gradient-index body segment denotes unreported geometric length. Raw rows and other columns are retained.");
                    }
                }
                AddRayTable(result, rayRows, expectedSurfaces);
                if (omitted.Length > 0) result.Transformations.Add(
                    $"Compare all active native surface numbers; imported IGNR explicitly omits {string.Join(", ", omitted)}.");
                break;
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

    private static bool NormalizeGradient9MirrorDirections(double[][] rows, JsonElement model, AnalysisData? proof, int surfaceCount)
    {
        if (proof?.Values.GetValueOrDefault("Gradient9MirrorIndexRatios") is not JsonElement metadata
            || metadata.Deserialize<double[][]>() is not { Length: > 0 } ratios)
            return false;
        var surfaces = model.GetProperty("surfaces");
        if (surfaces.GetArrayLength() != surfaceCount) return false;
        var changed = false;
        foreach (var entry in ratios)
        {
            if (entry.Length != 2 || entry[0] != Math.Truncate(entry[0]) || entry[0] < 2
                || entry[0] >= surfaceCount - 1 || entry[1] is not (> 0 and < 1))
                continue;
            var surface = (int)entry[0];
            var ratio = entry[1];
            var mirror = surfaces[surface];
            var entrance = surfaces[surface - 1];
            var returnFace = surfaces[surface + 1];
            if (mirror.GetProperty("Material").GetString() != "MIRROR"
                || mirror.GetProperty("Thickness").ValueKind != JsonValueKind.Number
                || mirror.GetProperty("Thickness").GetDouble() != 0
                || entrance.GetProperty("Type").GetString() != "Gradient9"
                || returnFace.GetProperty("Type").GetString() != "Gradient9"
                || entrance.GetProperty("Material").GetString() != returnFace.GetProperty("Material").GetString())
                continue;
            var reflected = rows.FirstOrDefault(row => row[0] == surface);
            var returned = rows.FirstOrDefault(row => row[0] == surface + 1);
            if (reflected is null || returned is null || Math.Abs(returned[11]) > 1e-9
                || (Vector(reflected, 1) - Vector(returned, 1)).Length > 1e-9)
                continue;

            // The native report inserts the axial-index direction between the
            // coincident surfaces. Confirm both its angular law and the actual
            // transmitted vector before interpreting any of its columns.
            const double radians = Math.PI / 180;
            var sinMirror = Math.Sin(reflected[10] * radians);
            var sinReturn = Math.Sin(returned[10] * radians);
            if (Math.Abs(sinReturn - ratio * sinMirror) > 1e-8
                || sinReturn / ratio > 1)
                continue;
            var normal = Vector(reflected, 7);
            if (Math.Abs(normal.Length - 1) > 1e-8 || (normal + Vector(returned, 7)).Length > 1e-8)
                continue;
            var direction = Vector(reflected, 4);
            var normalComponent = direction.X * normal.X + direction.Y * normal.Y + direction.Z * normal.Z;
            var tangent = (direction - normal * normalComponent) / ratio;
            var normalSquared = 1 - (tangent.X * tangent.X + tangent.Y * tangent.Y + tangent.Z * tangent.Z);
            if (normalSquared < 0) continue;
            var physical = tangent + normal * Math.CopySign(Math.Sqrt(normalSquared), normalComponent);
            if ((physical - Vector(returned, 4)).Length > 1e-8) continue;

            reflected[4] = physical.X;
            reflected[5] = physical.Y;
            reflected[6] = physical.Z;
            returned[10] = Math.Asin(sinReturn / ratio) / radians;
            changed = true;
        }
        return changed;

        static Vector3D Vector(double[] row, int offset) => new(row[offset], row[offset + 1], row[offset + 2]);
    }

    private static void VerifyPrimaryRange(string text, CanonicalAnalysisRequest r, bool direction)
    {
        var header = Regex.Matches(text, @"^[^:\r\n]+:\s*([-+\d.Ee]+)\s*$", RegexOptions.Multiline).Take(3).Select(m => Parse(m.Groups[1].Value)).ToArray();
        Require(header.Length == 3 && header[0] == 1 && header[1] == r.SurfaceCount - 1 && Math.Abs(header[2] - r.PrimaryWavelengthMicrometers) <= 0.0000005,
            "Native report range or primary wavelength differs from the explicit full-system contract");
        if (direction) Require(Regex.IsMatch(text, @":\s*Y-Z\s*$", RegexOptions.Multiline), "Native cardinal orientation is not Y-Z");
    }
    private static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static double[][] NativeRealRayRows(string text, int firstSurface)
    {
        // The object marker is localized and its coordinates can be infinite.
        // Identify the unique real-ray table by its numeric schema, then retain
        // AddRayTable's complete, ordered physical-surface contract. Paraxial
        // rows have fewer columns and must never supply real-ray values.
        var tables = new List<double[][]>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (ReadRow(lines[index]) is not { } first || first[0] != firstSurface) continue;
            Require(index == 0 || ReadRow(lines[index - 1]) is null, "Native real-ray table begins before the first active surface");
            var rows = new List<double[]> { first };
            while (index + 1 < lines.Length)
            {
                var nextLine = lines[index + 1];
                if (ReadRow(nextLine) is { } next)
                {
                    rows.Add(next);
                    index++;
                    continue;
                }
                // An NSC internal interaction has eleven physical columns and
                // no sequential row number. The next numbered port already
                // reports that accumulated path; do not add it a second time.
                var tokens = Regex.Split(nextLine.Trim(), @"\s+");
                if (tokens.Length >= 11 && !Regex.IsMatch(tokens[0], @"^\d+$")
                    && tokens.Take(11).All(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)))
                {
                    index++;
                    continue;
                }
                break;
            }
            tables.Add(rows.ToArray());
        }
        Require(tables.Count == 1, "Native real-ray numeric table missing or ambiguous");
        return tables[0];

        static double[]? ReadRow(string line)
        {
            var tokens = Regex.Split(line.Trim(), @"\s+");
            if (tokens.Length < 12 || !Regex.IsMatch(tokens[0], @"^\d+$")) return null;
            var values = new double[12];
            for (var i = 0; i < values.Length; i++)
            {
                if (i >= 7 && tokens[i] == "-") { values[i] = double.NaN; continue; }
                if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || !double.IsFinite(values[i])) return null;
            }
            return values;
        }
    }
    private static double[][] NumericRows(string text, int width) => text.Split('\n').Select(line => Regex.Split(line.Trim(), @"\s+"))
        .Where(row => row.Length == width && row.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        .Select(row => row.Select(Parse).ToArray()).ToArray();
    private static void AddCardinal(NumericResult result, double[][] rows)
    {
        Require(rows.Length == 6 && rows.All(row => row.Length == 2), "Cardinal report requires six object/image pairs");
        for (var i = 0; i < 2; i++) result.Series.Add(new(i == 0 ? "object" : "image", string.Join(",", CardinalIds),
            Enumerable.Range(0, 6).Select(j => (double)j).ToArray(), rows.Select(row => row[i]).ToArray(), new("Coordinate", "Dimensionless"), Coordinate));
    }
    private static int[] ActiveRaySurfaces(int surfaceCount, AnalysisData? proof)
    {
        Require(surfaceCount >= 2, "Single-ray surface count is invalid");
        var ignored = proof?.Values.GetValueOrDefault("IgnoredSurfaceNumbers") is JsonElement value
            ? value.Deserialize<int[]>() ?? throw new InvalidDataException("Ignored single-ray surfaces are missing")
            : [];
        Require(ignored.Distinct().Count() == ignored.Length && ignored.All(n => n > 0 && n < surfaceCount - 1),
            "Ignored single-ray surface numbers are invalid");
        return Enumerable.Range(1, surfaceCount - 1).Except(ignored).ToArray();
    }

    private static void AddRayTable(NumericResult result, double[][] rows, IReadOnlyList<int> expectedSurfaces)
    {
        Require(rows.Length == expectedSurfaces.Count && rows.All(row => row.Length == 12) && rows.Select(row => row[0]).SequenceEqual(expectedSurfaces.Select(i => (double)i)), "Incomplete or unordered real-ray surface table");
        string[] names = ["x", "y", "z", "l", "m", "n", "normal-x", "normal-y", "normal-z", "incidence", "path-length"];
        for (var i = 1; i < 12; i++)
        {
            var samples = i == 11 && result.UnreportedNativeRayPathSurfaces is { } omitted
                ? rows.Where(row => !omitted.Contains((int)row[0])).ToArray() : rows;
            if (samples.Length == 0) continue;
            result.Series.Add(new(names[i - 1], names[i - 1], samples.Select(row => row[0]).ToArray(),
                samples.Select(row => row[i]).ToArray(), Surface,
                i <= 3 || i == 11 ? Coordinate : i == 10 ? Angle : new("DirectionCosine", "Dimensionless")));
        }
    }

    private static int[] UnreportedGradientPaths(double[][] rows, JsonElement model, AnalysisData? proof, int surfaceCount)
    {
        if (proof?.Values.GetValueOrDefault("GradientSegmentEndSurfaces") is not JsonElement segmentMetadata
            || segmentMetadata.Deserialize<int[]>() is not { Length: > 0 } ends)
            return [];
        var surfaces = model.GetProperty("surfaces");
        if (surfaces.GetArrayLength() != surfaceCount) return [];

        return rows.Where(row => row[0] >= 2 && row[11] == 0 && ends.Contains((int)row[0])
                && surfaces[(int)row[0] - 1].TryGetProperty("Type", out var type)
                && type.GetString() is "GridGradient" or "Gradient1" or "Gradient9"
                && surfaces[(int)row[0] - 1].TryGetProperty("Thickness", out var thickness)
                && thickness.ValueKind == JsonValueKind.Number && Math.Abs(thickness.GetDouble()) > 0)
            .Select(row => (int)row[0]).ToArray();
    }
    private static void AddGridDistortion(NumericResult result, double[] x, double[] y, int n)
    {
        var axis = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        foreach (var (values, name) in new[] { (x, "image-x"), (y, "image-y") }) result.Grids.Add(new(name, axis, axis,
            Enumerable.Range(0, n).Select(row => values.Skip(row * n).Take(n).Select(v => (double?)v).ToArray()).ToArray(),
            new("FieldGridColumn", "Dimensionless"), new("FieldGridRow", "Dimensionless"), new("ImageHeight", "Millimeter")));
    }
}
