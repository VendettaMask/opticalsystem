using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static readonly string[] FootprintNames = ["RayXMinimumMillimeters", "RayXMaximumMillimeters", "RayYMinimumMillimeters", "RayYMaximumMillimeters", "MaximumRayRadiusMillimeters"];
    private static NumericResult? WorkbenchPupil(AnalysisData data, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (key is not ("Footprint Diagram" or "Jones Pupil" or "Contrast Loss Map")) return null;
        var result = new NumericResult { Semantics = key == "Jones Pupil" ? "Y-input Jones projection: component magnitudes only; global phase and the other input state are not equated." : key == "Footprint Diagram" ? "Native report's five footprint extents only; no native ray-point correspondence." : "Moore-Elliott loss at explicitly requested 5% of cutoff; circular OPD phase represented by sine/cosine." };
        if (key == "Footprint Diagram")
        {
            foreach (var name in FootprintNames) result.Scalars.Add(new(name, ((JsonElement)data.Values[name]).GetDouble(), "Millimeter"));
            return result;
        }
        if (key == "Jones Pupil")
        {
            var rows = ((JsonElement)data.Values["ImagePlaneYInputMagnitudes"]).Deserialize<double[][]>(JsonFiles.Options)!;
            var axis = rows.Select(v => v[0]).Distinct().Order().ToArray();
            for (var col = 0; col < 2; col++)
            {
                result.Grids.Add(new("amplitude:" + col, axis, axis, axis.Select(y => axis.Select(x =>
                {
                    var value = rows.Single(v => v[0] == x && v[1] == y)[col + 2];
                    return double.IsFinite(value) ? (double?)value : null;
                }).ToArray()).ToArray(), Pupil, Pupil, new("Coefficient", "Dimensionless")));
            }
            result.Transformations.Add("Project propagated Y-input electric field to local image-plane X/Y; do not equate orthonormal transverse Jones entries with native Ex/Ey.");
            return result;
        }
        var panes = data.PlotPanes!;
        var originalPhase = ((JsonElement)data.Values["UnshiftedPupilPhaseSeries"]).Deserialize<AnalysisSeries[]>(JsonFiles.Options)!;
        Require(originalPhase.Length == 2, "Two original pupil phase grids are required");
        for (var axis = 0; axis < 2; axis++)
        {
            result.Grids.Add(PupilGrid("loss:" + axis, panes[axis].Series.Single(), new("Modulation", "Dimensionless")));
            var phase = originalPhase[axis];
            Require(phase.ValueQuantity == AnalysisAxisQuantity.WavefrontError && phase.ValueUnit == AnalysisAxisUnit.Wave,
                "Original pupil phase requires typed wave units");
            foreach (var component in new[] { "sin", "cos" })
            {
                var s = phase with { Points = phase.Points.Select(p => p with { Value = p.Value.HasValue ? component == "sin" ? Math.Sin(2 * Math.PI * p.Value.Value) : Math.Cos(2 * Math.PI * p.Value.Value) : null }).ToArray() };
                result.Grids.Add(PupilGrid("phase:" + axis + ":" + component, s, new("PhaseComponent", "Dimensionless")));
            }
        }
        result.Transformations.Add("Native exported phase arrays match the original pupil wavefront in the captured 2026 R1 files. Compare that separate observable; the documented GUI mean-shifted-ray OPD indicator is excluded.");
        return result;
    }
    private static Grid2DResult PupilGrid(string id, AnalysisSeries s, Axis value)
    {
        var x = s.Points.Select(p => p.X).Distinct().Order().ToArray(); var y = s.Points.Select(p => p.Y).Distinct().Order().ToArray();
        return new(id, x, y, y.Select(v => s.Points.Where(p => p.Y == v).OrderBy(p => p.X).Select(p => p.Value.HasValue && double.IsFinite(p.Value.Value) ? p.Value : null).ToArray()).ToArray(), Pupil, Pupil, value);
    }
    private static NumericResult? ZemaxPupil(string path, CanonicalAnalysisRequest r)
    {
        var key = r.CanonicalAnalysisKey;
        if (key is not ("Footprint Diagram" or "Jones Pupil" or "Contrast Loss Map")) return null;
        var result = new NumericResult { Semantics = "Audited native pupil report/grid, retained settings and explicit common-output boundary." };
        var text = File.ReadAllText(Path.ChangeExtension(path, ".txt"));
        if (key == "Footprint Diagram")
        {
            Require(Regex.IsMatch(text, @"(?:表面|Surface)\s*[:：]\s*" + (r.SurfaceCount - 1) + @"\b"), "Native footprint surface differs");
            var values = Regex.Matches(text, @"=\s*(" + Number + @")").Select(m => Parse(m.Groups[1].Value)).ToArray();
            Require(values.Length == 11 && Math.Abs(values[9] - r.PrimaryWavelengthMicrometers) < 0.000051, "Native footprint primary wavelength/report shape differs");
            for (var i = 0; i < 5; i++) result.Scalars.Add(new(FootprintNames[i], values[i], "Millimeter"));
            return result;
        }
        if (key == "Jones Pupil")
        {
            Require(r.DefinedFields.Length > 0 && r.DefinedFields[0].All(v => v == 0), "Jones projection requires on-axis first field");
            var header = text.Split('\n').TakeWhile(l => !Regex.IsMatch(l, @"^\s*Px\s+Py\b")).ToArray();
            var hv = header.Select(l => Regex.Match(l, @"[:：]\s*(" + Number + @")\s*(?:mm|µm|μm)?\s*$")).Where(m => m.Success).Select(m => Parse(m.Groups[1].Value)).ToArray();
            Require(hv.Length == 8 && Math.Abs(hv[0] - r.PrimaryWavelengthMicrometers) < 0.000051 && hv[1] == 0 && hv[2] == 0 && hv[3] == 1 && hv[4] == 0 && hv[5] == 0 && hv[6] == r.Configuration && hv[7] == r.SurfaceCount - 1, "Native Jones input polarization, field, wavelength, configuration or surface differs");
            var rows = text.Split('\n').Select(l => Regex.Matches(l, Number).Select(m => Parse(m.Value)).ToArray()).Where(v => v.Length == 7).ToArray();
            Require(rows.Length == 197 && rows.Select(v => (v[0], v[1])).Distinct().Count() == 197, "Native Jones pupil grid differs from 17 by 17 circular grid");
            var axis = Enumerable.Range(0, 17).Select(i => -1 + i / 8d).ToArray();
            for (var col = 0; col < 2; col++) result.Grids.Add(new("amplitude:" + col, axis, axis, axis.Select(y => axis.Select(x => rows.FirstOrDefault(v => v[0] == x && v[1] == y) is { } row ? (double?)row[col + 2] : null).ToArray()).ToArray(), Pupil, Pupil, new("Coefficient", "Dimensionless")));
            result.Transformations.Add("Compare Y-input field magnitudes projected onto local image X/Y against native Ex/Ey; no full Jones-matrix or phase-equivalence claim.");
            return result;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var grids = doc.RootElement.GetProperty("dataGrids");
        Require(grids.GetArrayLength() == 4, "Native contrast output needs four ordered phase/loss grids");
        for (var axis = 0; axis < 2; axis++)
        {
            var start = axis == 0 ? 2 : 0;
            foreach (var type in new[] { "loss", "sin", "cos" })
            {
                var g = grids[start + (type == "loss" ? 1 : 0)]; var nx = g.GetProperty("nx").GetInt32(); var ny = g.GetProperty("ny").GetInt32(); Require(nx == 13 && ny == 13, "Native contrast sampling differs");
                var x = Enumerable.Range(0, nx).Select(i => g.GetProperty("minX").GetDouble() + i * g.GetProperty("dx").GetDouble()).ToArray();
                var y = Enumerable.Range(0, ny).Select(i => g.GetProperty("minY").GetDouble() + i * g.GetProperty("dy").GetDouble()).ToArray(); var v = g.GetProperty("values").Deserialize<double?[][]>()!;
                for (var row = 0; row < ny; row++) for (var col = 0; col < nx; col++)
                {
                    var px = x[col]; var py = y[row]; var sx = axis == 0 ? 0.05 : 0; var sy = axis == 1 ? 0.05 : 0;
                    if (Math.Pow(px - sx, 2) + Math.Pow(py - sy, 2) > 1 + 1e-12 || Math.Pow(px + sx, 2) + Math.Pow(py + sy, 2) > 1 + 1e-12) v[row][col] = null;
                    else if (v[row][col].HasValue && type != "loss") v[row][col] = type == "sin" ? Math.Sin(v[row][col]!.Value * Math.PI / 180) : Math.Cos(v[row][col]!.Value * Math.PI / 180);
                }
                result.Grids.Add(new(type == "loss" ? "loss:" + axis : "phase:" + axis + ":" + type, x, y, v, Pupil, Pupil, new(type == "loss" ? "Modulation" : "PhaseComponent", "Dimensionless")));
            }
        }
        result.Transformations.Add("Explicit frequency=0 means 5% cutoff, hence pupil separation 0.1; mask only the intersection of the two shifted unit pupils. Native OPD degrees become sine/cosine to compare circular phase.");
        return result;
    }
}
