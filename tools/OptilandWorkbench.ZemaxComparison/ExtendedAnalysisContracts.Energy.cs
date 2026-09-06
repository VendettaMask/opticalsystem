using System.Globalization;

namespace OptilandWorkbench.ZemaxComparison;

public static partial class ExtendedAnalysisContracts
{
    public static bool IsEnergyContract(string key) => key is "Encircled Energy" or "Diffraction Encircled Energy" or "Geometric Line Edge Spread" or "RMS Field Map" or "Zernike" or "Relative Illumination";
    private static bool ConfigureEnergy(ref CanonicalAnalysisRequest r, Dictionary<string, object> s)
    {
        var key = r.CanonicalAnalysisKey;
        if (!IsEnergyContract(key)) return false;
        if (key == "Encircled Energy") r = r with { Field = 0 };
        void Set(params object[] pairs) { for (var i = 0; i < pairs.Length; i += 2) s.Add((string)pairs[i], pairs[i + 1]); }
        Set("Wavelength", r.Wavelength);
        if (key != "Relative Illumination") Set("Field", key == "Encircled Energy" ? 0 : r.Field, "Surface", -1);
        if (key == "RMS Field Map")
        {
            r = r with { Reference = "ChiefRay" };
            Set("Data", "Wavefront", "MethodType", "GaussQuad", "RayDensity", "RayDens_6", "ReferTo", "Centroid", "ShowAs", "FalseColor", "UsePolarization", false,
                "RemoveVignettingFactors", true, "X_FieldSampling", 11, "Y_FieldSampling", 11, "X_FieldSize", r.MaximumFieldRadius / Math.Sqrt(2), "Y_FieldSize", r.MaximumFieldRadius / Math.Sqrt(2), "PlotScale", 0d, "ContourFormat", "");
        }
        else if (key == "Zernike")
            Set("SampleSize", $"S_{r.PupilSampling}x{r.PupilSampling}", "ReferenceOBDToVertex", false, "Sx", 0d, "Sy", 0d, "Sr", 1d, "MaximumNumberOfTerms", 37);
        else if (key == "Relative Illumination")
        {
            r = r with { FieldScanDirection = "+y" };
            Set("ScanType", "Plus_Y", "RayDensity", 20, "FieldDensity", 20, "UsePolarization", false, "LogScale", false, "RemoveVignettingFactors", true);
        }
        else
        {
            r = r with { Reference = "Centroid" };
            Set("SampleSize", $"S_{r.PupilSampling}x{r.PupilSampling}", "UsePolarization", false, "RadiusMaximum", 5d);
            if (key == "Geometric Line Edge Spread") Set("Type", "LineEdge");
            else Set("Type", "Encircled", "ReferTo", "Centroid", "ScatterRays", false, "ShowDiffractionLimit", key == "Diffraction Encircled Energy", "UseDashes", false, "UseHuygensPSF", false, "HuygensSample", $"S_{r.ImageSampling}x{r.ImageSampling}", "HuygensDelta", 0d);
        }
        return true;
    }
    private static bool MapEnergy(CanonicalAnalysisRequest r, Dictionary<string, string> s)
    {
        var key = r.CanonicalAnalysisKey;
        if (!IsEnergyContract(key)) return false;
        void Set(string key, object value) => s[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        Set("Sampling", r.PupilSampling); Set("PupilSampling", r.PupilSampling); Set("ImageSampling", r.PupilSampling * 2); Set("ZernikeTerms", 37);
        Set("NumRays", r.PupilSampling); Set("Distribution", "uniform"); Set("NumPoints", key == "Geometric Line Edge Spread" ? 101 : 401);
        Set("Reference", "centroid"); Set("MaximumDistanceMicrometers", 5); Set("MaximumRadiusMicrometers", 5); Set("MultiplyByDiffractionLimit", false);
        if (key == "Encircled Energy") Set("ZemaxCompatibleOutput", true);
        if (key == "RMS Field Map")
        {
            Set("XFieldSamples", 11); Set("YFieldSamples", 11); Set("XFieldWidth", r.MaximumFieldRadius / Math.Sqrt(2)); Set("YFieldWidth", r.MaximumFieldRadius / Math.Sqrt(2));
            Set("NumRings", 6); Set("Method", "GQ"); Set("Data", "wavefront"); Set("Reference", "chief"); Set("RemoveVignetting", true);
            Set("GaussianAzimuthalSamples", 12);
        }
        if (key == "Relative Illumination") { Set("RayDensity", 20); Set("FieldDensity", 21); Set("ScanDirection", r.FieldScanDirection); Set("RemoveVignettingFactors", true); }
        if (key == "Geometric Line Edge Spread") { Set("Orientation", "X"); Set("Display", "line and edge"); }
        return true;
    }
}
