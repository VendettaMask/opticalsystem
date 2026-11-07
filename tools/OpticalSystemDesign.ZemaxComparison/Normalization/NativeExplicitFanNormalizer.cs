using System.Text.Json;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.ZemaxComparison.Normalization;

/// <summary>Independent explicit batch reference; never fills or replaces a built-in plot.</summary>
public static class NativeExplicitFanNormalizer
{
    public static NumericResult Read(string path, AnalysisComparisonEntry entry, CanonicalAnalysisRequest request,
        PupilVignetting? displayedPupil = null, bool allowGeometricChief = false)
    {
        var root = JsonFiles.Read<JsonElement>(path);
        if (root.GetProperty("contract").GetString() != "NativeExplicitPointsV1"
            || root.GetProperty("units").GetString() != "Millimeters"
            || root.GetProperty("kind").GetString() != "fan"
            || root.GetProperty("wavelength").GetInt32() != request.Wavelength
            || root.GetProperty("builtInPlotReplaced").GetBoolean()
            || root.GetProperty("savedInput").GetBoolean())
            throw new InvalidDataException("Explicit native fan identity differs.");
        var opd = request.CanonicalAnalysisKey == "Optical Path Difference";
        if (!opd && request.CanonicalAnalysisKey != "Ray Fan") throw new InvalidDataException("Explicit fan route requires a fan request.");
        if (opd && root.GetProperty("opdMode").GetString() != "CurrentAndChief") throw new InvalidDataException("Unverified OPD reference mode.");
        var inputs = root.GetProperty("inputs").EnumerateArray().ToArray();
        var surfaces = root.GetProperty("surfaces"); var image = surfaces[surfaces.GetArrayLength() - 1].GetProperty("rays");
        if (inputs.Length is < 5 or > 10000 || image.GetArrayLength() != inputs.Length
            || inputs[0].GetProperty("role").GetString() != "chief" || N(inputs[0], "px") != 0 || N(inputs[0], "py") != 0
            || !(Valid(image[0]) || allowGeometricChief && GeometricIntercept(image[0])))
            throw new InvalidDataException("Explicit fan requires a geometric chief intercept and complete bounded rays.");
        var result = new NumericResult();
        if (!Valid(image[0])) result.Transformations.Add("Use the vignetted chief only as a geometric datum; the independent physical mask still clips that ray.");
        foreach (var axis in new[] { "tangential", "sagittal" })
        {
            var indices = Enumerable.Range(1, inputs.Length - 1).Where(i => inputs[i].GetProperty("role").GetString() == axis).ToArray();
            if (indices.Length < 2) throw new InvalidDataException("Explicit fan axis is incomplete.");
            var x = new double[indices.Length]; var y = new double[indices.Length];
            var component = axis == "tangential" ? "y" : "x";
            for (var k = 0; k < indices.Length; k++)
            {
                var i = indices[k]; var p = inputs[i]; x[k] = N(p, "coordinate");
                if (!double.IsFinite(x[k]) || Math.Abs(x[k]) > 1 || k > 0 && x[k] <= x[k - 1]
                    || N(p, "hx") != N(inputs[0], "hx") || N(p, "hy") != N(inputs[0], "hy")
                    || N(p, axis == "tangential" ? "py" : "px") != x[k] || N(p, axis == "tangential" ? "px" : "py") != 0)
                    throw new InvalidDataException("Explicit fan coordinates or ordering differ.");
                y[k] = !Valid(image[i]) ? double.NaN : opd ? N(image[i], "opdWaves") : 1000 * (N(image[i], component) - N(image[0], component));
                if (Valid(image[i]) && !double.IsFinite(y[k])) throw new InvalidDataException("Accepted native fan ray is nonfinite.");
            }
            if (displayedPupil is { } pupil)
                x = x.Select(t => axis == "tangential" ? pupil.ScaleAndShift(0, t).Y : pupil.ScaleAndShift(t, 0).X).ToArray();
            result.Series.Add(new(axis, "Native explicit batch", x, y, entry.XAxis!, entry.YAxis!) { HasClippingMask = true });
        }
        if (1 + result.Series.Sum(s => s.X.Length) != inputs.Length) throw new InvalidDataException("Unrecognized explicit fan rays.");
        return result;
    }
    private static double N(JsonElement p, string key) => p.GetProperty(key).GetDouble();
    private static bool Valid(JsonElement p) => p.GetProperty("error").GetInt32() == 0 && p.GetProperty("vignette").GetInt32() == 0 && N(p, "intensity") > 0;

    private static bool GeometricIntercept(JsonElement p)
    {
        if (p.GetProperty("error").GetInt32() != 0) return false;
        var l = N(p, "l"); var m = N(p, "m"); var n = N(p, "n");
        return Math.Abs(l * l + m * m + n * n - 1) <= 1e-8
            && double.IsFinite(N(p, "x")) && double.IsFinite(N(p, "y")) && double.IsFinite(N(p, "z"));
    }
}
