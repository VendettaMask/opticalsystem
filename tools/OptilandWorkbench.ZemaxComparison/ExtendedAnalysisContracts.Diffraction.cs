using System.Globalization;

namespace OptilandWorkbench.ZemaxComparison;

public static partial class ExtendedAnalysisContracts
{
    public static bool IsMtfContract(string key) => key is "Through Focus MTF" or "Fourier Through Focus MTF" or "Huygens Through Focus MTF" or "Geometric Through Focus MTF" or "Fourier MTF vs Field" or "Huygens MTF vs Field" or "Geometric MTF vs Field" or "Geometric MTF";
    public static bool IsPsfProfile(string key) => key is "FFT PSF Cross Section" or "Huygens PSF Cross Section" or "FFT Line Edge Spread";
    private static bool ConfigureDiffraction(ref CanonicalAnalysisRequest r, Dictionary<string, object> s)
    {
        var key = r.CanonicalAnalysisKey;
        if (!IsMtfContract(key) && !IsPsfProfile(key)) return false;
        void Set(params object[] pairs) { for (var i = 0; i < pairs.Length; i += 2) s.Add((string)pairs[i], pairs[i + 1]); }
        Set("Wavelength", r.Wavelength, "UsePolarization", false);
        if (key.Contains("vs Field", StringComparison.Ordinal))
        {
            r = r with { FieldScanDirection = "+y" };
            Set("ScanType", "Plus_Y", "RemoveVignetting", true, "FieldDensity", 10);
            for (var i = 1; i <= 6; i++) Set("Freq_" + i, 10d * i);
        }
        else Set("Field", r.Field);
        if (key.StartsWith("Huygens", StringComparison.Ordinal) && !key.Contains("vs Field", StringComparison.Ordinal))
            Set("PupilSampleSize", $"S_{r.PupilSampling}x{r.PupilSampling}", "ImageSampleSize", $"S_{r.ImageSampling}x{r.ImageSampling}", "Configuration", r.Configuration);
        else Set("SampleSize", $"S_{r.PupilSampling}x{r.PupilSampling}");
        if (key.StartsWith("Huygens", StringComparison.Ordinal)) Set("ImageDelta", r.ImageDeltaMicrometers);
        if (IsMtfContract(key))
        {
            Set("UseDashes", false);
            if (key.StartsWith("Geometric", StringComparison.Ordinal)) Set("MultiplyByDiffractionLimit", false, "ScatterRays", false);
            else if (!key.Contains("vs Field", StringComparison.Ordinal)) Set("Type", "Modulation");
            if (key.Contains("Through Focus", StringComparison.Ordinal))
            {
                r = r with { FocusMinimum = -0.01, FocusMaximum = 0.01 };
                Set("DeltaFocus", 0.01, "Frequency", r.MaximumFrequency, "NumberOfSteps", 5);
            }
            if (key == "Geometric MTF") Set("MaximumFrequency", r.MaximumFrequency);
        }
        else
        {
            Set("Type", "X_Linear");
            if (key == "FFT Line Edge Spread") Set("Spread", "Line", "UseCoherentPSF", false, "PlotScale", 0d);
            else Set("Normalize", false, "RowCol", 0);
            if (key == "FFT PSF Cross Section") Set("PlotScale", 0d);
            if (key == "Huygens PSF Cross Section") Set("UseCentroid", false);
        }
        return true;
    }
    private static bool MapDiffraction(CanonicalAnalysisRequest r, Dictionary<string, string> s)
    {
        var key = r.CanonicalAnalysisKey;
        if (!IsMtfContract(key) && !IsPsfProfile(key)) return false;
        void Set(string key, object value) => s[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        Set("Sampling", r.PupilSampling); Set("PupilSampling", r.PupilSampling); Set("ImageSampling", r.ImageSampling);
        Set("ImageDeltaMicrometers", r.ImageDeltaMicrometers); Set("Normalized", false); Set("UseCentroid", false); Set("UsePolarization", false);
        Set("Frequency", r.MaximumFrequency); Set("SpatialFrequency", r.MaximumFrequency); Set("MaximumFrequency", r.MaximumFrequency);
        Set("DeltaFocus", 0.01); Set("NumberOfSteps", 5); Set("Steps", 5); Set("UseDashes", false);
        Set("FieldDensity", 10); Set("FieldPointCount", 10); Set("ScanType", r.FieldScanDirection); Set("RemoveVignettingFactors", true);
        for (var i = 1; i <= 6; i++) Set("Frequency" + i, i * 10);
        Set("NumRays", r.PupilSampling); Set("Distribution", "uniform"); Set("ScaleByDiffractionLimit", false); Set("PlotPointCount", 128);
        if (IsPsfProfile(key)) { Set("Type", "X-线性"); Set("ProfileType", "X"); Set("Row", "中心"); Set("GraphScaleMicrometers", 0); Set("Spread", "线"); Set("UseCoherentPsf", false); }
        return true;
    }
}
