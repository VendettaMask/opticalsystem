using System.Globalization;
using OptilandWorkbench.ZemaxComparison.Configuration;

namespace OptilandWorkbench.ZemaxComparison;

public static partial class ExtendedAnalysisContracts
{
    public static void ValidateRmsFieldSampling(string key, AnalysisConfiguration a)
    {
        if (key != "RMS vs Field")
        {
            if (a.RmsFieldMethod != "GQ" || a.RmsFieldRayDensity != 6 || !a.RmsFieldRemoveVignettingFactors)
                throw new ArgumentException("RMS field sampling controls apply only to RMS vs Field");
            return;
        }
        if (a.RmsFieldMethod == "GQ" && a.RmsFieldRayDensity is >= 1 and <= 20) return;
        if (a.RmsFieldMethod == "RA" && a.RmsFieldRayDensity is 32 or 64 or 128 or 256) return;
        throw new ArgumentException("RMS vs Field requires GQ density 1..20 or RA grid 32, 64, 128, 256");
    }

    public static CanonicalAnalysisRequest ConfigureRmsFieldSampling(CanonicalAnalysisRequest r, AnalysisConfiguration a)
    {
        ValidateRmsFieldSampling(r.CanonicalAnalysisKey, a);
        if (r.CanonicalAnalysisKey != "RMS vs Field") return r;
        var settings = new Dictionary<string, object>(r.ZemaxSettings, StringComparer.Ordinal)
        {
            ["Method"] = a.RmsFieldMethod == "RA" ? "RectArray" : "GaussQuad",
            ["RayDensity"] = a.RmsFieldMethod == "RA"
                ? $"RayDens_{a.RmsFieldRayDensity}x{a.RmsFieldRayDensity}" : $"RayDens_{a.RmsFieldRayDensity}",
            ["RemoveVignettingFactors"] = a.RmsFieldRemoveVignettingFactors
        };
        return r with { ZemaxSettings = settings };
    }

    public static bool IsRmsScan(string key) => key is "RMS vs Field" or "RMS Wavefront vs Field" or "RMS vs Wavelength" or "RMS vs Focus";
    private static bool ConfigureScans(ref CanonicalAnalysisRequest r, Dictionary<string, object> settings)
    {
        if (!IsRmsScan(r.CanonicalAnalysisKey)) return false;
        void Set(params object[] pairs) { for (var i = 0; i < pairs.Length; i += 2) settings.Add((string)pairs[i], pairs[i + 1]); }
        var spot = r.CanonicalAnalysisKey is "RMS vs Field" or "RMS vs Wavelength";
        r = r with { Reference = spot ? "Centroid" : "ChiefRay" };
        // In the installed 26.1 interface, ReferTo ordinal 0 is labelled ChiefRay but
        // the actual native report uses centroid; ordinal 1 uses chief. Verify the report.
        Set("Method", "GaussQuad", "RayDensity", "RayDens_6", "ReferTo", spot ? "ChiefRay" : "Centroid",
            "Data", spot ? "SpotRadius" : "Wavefront", "ShowDiffractionLimit", false, "UseDashes", false, "UsePolarization", false, "PlotScale", 0d);
        switch (r.CanonicalAnalysisKey)
        {
            case "RMS vs Field":
            case "RMS Wavefront vs Field":
                var edge = r.DefinedFields.OrderByDescending(f => f[0] * f[0] + f[1] * f[1]).FirstOrDefault() ?? new[] { 0d, 1d };
                var axisX = Math.Abs(edge[0]) > Math.Abs(edge[1]); var negative = edge[axisX ? 0 : 1] < 0;
                r = r with { FieldScanDirection = (negative ? "-" : "+") + (axisX ? "x" : "y") };
                // Field/WaveDensity also use ordinal+1 intervals in 26.1, despite enum names
                // suggesting multiples of five. Ordinal 14 gives 15 intervals; verify output.
                Set("Wavelength", r.Wavelength, "FieldDensity", "FieldDens_75", "Orientation", (negative ? "Minus_" : "Plus_") + (axisX ? "X" : "Y"), "RemoveVignettingFactors", true); break;
            case "RMS vs Wavelength":
                Set("Field", r.Field, "WaveDensity", "WaveDens_100");
                r = r with { Wavelength = 0, WavelengthScope = "ContinuousBetweenDefinedExtrema" }; break;
            case "RMS vs Focus":
                Set("Wavelength", r.Wavelength, "FocusDensity", "FocusDens_15", "MinimumFocus", -0.01, "MaximumFocus", 0.01);
                r = r with { FocusMinimum = -0.01, FocusMaximum = 0.01 }; break;
        }
        return true;
    }
    private static bool MapScans(CanonicalAnalysisRequest r, Dictionary<string, string> settings)
    {
        if (!IsRmsScan(r.CanonicalAnalysisKey)) return false;
        void Set(string name, object value) => settings[name] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        var spot = r.CanonicalAnalysisKey is "RMS vs Field" or "RMS vs Wavelength";
        var rectangular = Convert.ToString(r.ZemaxSettings["Method"], CultureInfo.InvariantCulture) == "RectArray";
        var densityName = Convert.ToString(r.ZemaxSettings["RayDensity"], CultureInfo.InvariantCulture)!;
        var density = int.Parse(densityName["RayDens_".Length..].Split('x')[0], CultureInfo.InvariantCulture);
        Set("Method", rectangular ? "RA" : "GQ"); Set("NumRings", density); Set("RayDensity", density); Set("Reference", spot ? "centroid" : "chief");
        Set("GaussianAzimuthalSamples", rectangular ? 12 : 2 * density);
        Set("Data", spot ? "spot" : "wavefront"); Set("ShowDiffractionLimit", false); Set("UsePolarization", false);
        var removeVignetting = !r.ZemaxSettings.TryGetValue("RemoveVignettingFactors", out var remove)
            || bool.Parse(Convert.ToString(remove, CultureInfo.InvariantCulture)!);
        Set("RemoveVignetting", removeVignetting); Set("RemoveVignettingFactors", removeVignetting); Set("FieldDensity", 15); Set("WaveDensity", 21);
        Set("FocusDensity", 16); Set("MinimumFocus", r.FocusMinimum); Set("MaximumFocus", r.FocusMaximum); Set("ScanType", r.FieldScanDirection); Set("ScanDirection", r.FieldScanDirection);
        return true;
    }
}
