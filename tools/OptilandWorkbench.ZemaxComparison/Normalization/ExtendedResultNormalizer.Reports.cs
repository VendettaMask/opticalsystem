using System.Text.Json;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.ZemaxComparison.Normalization;

public static partial class ExtendedResultNormalizer
{
    private static bool IsSystemReport(CanonicalAnalysisRequest r) => r.CanonicalAnalysisKey is "Prescription Report" or "System Data Report";
    private static NumericResult? WorkbenchSystemReport(AnalysisData data, CanonicalAnalysisRequest r)
    {
        if (!IsSystemReport(r)) return null;
        var result = new NumericResult { Semantics = "Common report numbers only; localized prose, material names and unsupported native report sections are retained for review, not counted as numerical equivalence." };
        foreach (var key in new[] { "SurfaceCount", "StopSurface", "PrimaryWavelengthMicrometers" })
            result.Scalars.Add(new(key, ((JsonElement)data.Values[key]).GetDouble(), key.StartsWith("Primary", StringComparison.Ordinal) ? "Micrometer" : "Dimensionless"));
        if (r.CanonicalAnalysisKey == "System Data Report")
            foreach (var key in new[] { "EffectiveFocalLength", "FNumber", "EntrancePupilDiameter", "ExitPupilDiameter" })
                result.Scalars.Add(new(key, ((JsonElement)data.Values[key]).GetDouble(), key == "FNumber" ? "Dimensionless" : "Millimeter"));
        else AddPrescription(result, ((JsonElement)data.Values["SurfacePrescription"]).Deserialize<double[][]>(JsonFiles.Options)!);
        return result;
    }
    private static NumericResult? ZemaxSystemReport(string path, CanonicalAnalysisRequest r)
    {
        if (!IsSystemReport(r)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "model.json")));
        var model = doc.RootElement; var surfaces = model.GetProperty("surfaces").EnumerateArray().ToArray();
        var result = new NumericResult { Semantics = "Common report numbers from native LDE/SystemData and explicit MFE operands; includes the object surface in SurfaceCount." };
        result.Scalars.Add(new("SurfaceCount", model.GetProperty("surfaceCount").GetInt32(), "Dimensionless"));
        result.Scalars.Add(new("StopSurface", surfaces.Single(s => s.GetProperty("IsStop").GetBoolean()).GetProperty("SurfaceNumber").GetInt32(), "Dimensionless"));
        var waves = model.GetProperty("wavelengths").EnumerateArray().ToArray();
        result.Scalars.Add(new("PrimaryWavelengthMicrometers", waves[r.Wavelength - 1].GetProperty("data").GetProperty("Wavelength").GetDouble(), "Micrometer"));
        if (r.CanonicalAnalysisKey == "System Data Report")
        {
            using var data = JsonDocument.Parse(File.ReadAllText(path)); var scalars = data.RootElement.GetProperty("scalars");
            foreach (var key in new[] { "EffectiveFocalLength", "FNumber", "EntrancePupilDiameter", "ExitPupilDiameter" })
                result.Scalars.Add(new(key, scalars.GetProperty(key).GetDouble(), key == "FNumber" ? "Dimensionless" : "Millimeter"));
        }
        else
        {
            AddPrescription(result, surfaces.Select(s => new[]
            {
                (double)s.GetProperty("SurfaceNumber").GetInt32(),
                s.GetProperty("Radius").ValueKind != JsonValueKind.Number || s.GetProperty("Radius").GetDouble() == 0 ? 0 : 1 / s.GetProperty("Radius").GetDouble(),
                s.GetProperty("Thickness").ValueKind == JsonValueKind.Number ? s.GetProperty("Thickness").GetDouble() : double.PositiveInfinity,
                s.GetProperty("SemiDiameter").GetDouble(), s.GetProperty("Conic").GetDouble()
            }).ToArray());
        }
        return result;
    }
    private static void AddPrescription(NumericResult result, double[][] rows)
    {
        string[] names = ["curvature", "thickness", "semi-diameter", "conic"];
        Axis[] axes = [new("Curvature", "InverseMillimeter"), Coordinate, Coordinate, new("Conic", "Dimensionless")];
        for (var i = 1; i <= 4; i++)
        {
            // Image-surface thickness is undefined in the native LDE. It is not a
            // propagation segment; Workbench stores zero there. Compare actual gaps.
            var applicable = i == 2 ? rows[..^1] : rows;
            var valid = applicable.Where(row => double.IsFinite(row[i])).ToArray();
            result.Series.Add(new(names[i - 1], names[i - 1], valid.Select(row => row[0]).ToArray(), valid.Select(row => row[i]).ToArray(), Surface, axes[i - 1]));
            result.Series.Add(new(names[i - 1] + ":finite-state", "Finite/infinite state", applicable.Select(row => row[0]).ToArray(),
                applicable.Select(row => double.IsFinite(row[i]) ? 0d : double.IsPositiveInfinity(row[i]) ? 1d : double.IsNegativeInfinity(row[i]) ? -1d : 2d).ToArray(), Surface, new("FiniteState", "Dimensionless")));
            if (valid.Length != applicable.Length) result.Transformations.Add($"{names[i - 1]}: signed infinity/finite state checked separately at each surface; finite-error metrics cover finite entries only.");
        }
        result.Transformations.Add("Image-surface thickness excluded on both sides because there is no following propagation segment.");
    }
}
