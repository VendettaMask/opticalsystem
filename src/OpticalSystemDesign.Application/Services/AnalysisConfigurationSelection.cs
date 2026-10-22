using System.Globalization;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Application.Services;

/// <summary>Stable settings values, independent of translated labels and configuration count.</summary>
public static class AnalysisConfigurationSelection
{
    public const string Key = "Configuration";
    public const string Current = "Current";
    public const string All = "All";

    // Explicit capabilities: never infer composition from a localized name or
    // from the shape of a result. Huygens coherent sums are not curve overlays.
    public static AnalysisConfigurationDisplay AllDisplay(string analysisName) =>
        WorkbenchAnalysisCatalog.CanonicalKey(analysisName) switch
        {
            "Configuration Matrix Spot Diagram" => AnalysisConfigurationDisplay.Matrix,
            "Spot Diagram" or "Full Field Spot Diagram" or "Matrix Spot Diagram"
                or "Through Focus" or "Ray Fan" or "Optical Path Difference"
                or "Footprint Diagram" or "Field Curvature and Distortion" or "Field Curvature"
                or "Color Focus Shift" or "Lateral Color" or "Axial Aberration"
                or "Encircled Energy" or "Diffraction Encircled Energy"
                or "Geometric Line Edge Spread" or "Extended Source Encircled Energy"
                or "RMS vs Field" or "RMS vs Wavelength" or "RMS vs Focus"
                or "RMS Wavefront vs Field" or "Zernike vs Field"
                or "Fourier Through Focus MTF" or "Geometric Through Focus MTF"
                or "Fourier MTF vs Field" or "Geometric MTF vs Field"
                or "Angle vs Image Height" or "Angle vs Image Height - Through Pupil"
                or "Angle vs Image Height - Through Field" or "Vignetting Diagram"
                or "Relative Illumination" or "Y-Ybar" or "FFT PSF Cross Section"
                or "FFT Line Edge Spread" or "MTF" or "Geometric MTF" or "Sampled MTF"
                => AnalysisConfigurationDisplay.Overlay,
            _ => AnalysisConfigurationDisplay.Single
        };

    internal static string Default(string analysisName) =>
        AllDisplay(analysisName) == AnalysisConfigurationDisplay.Matrix ? All : Current;

    internal static AnalysisParameterDescriptor Descriptor(string analysisName, int count)
    {
        var choices = new List<string>();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (AllDisplay(analysisName) != AnalysisConfigurationDisplay.Single)
        {
            choices.Add(All);
            labels[All] = "所有";
        }
        choices.Add(Current);
        labels[Current] = "当前的";
        for (var number = 1; number <= count; number++)
        {
            var value = number.ToString(CultureInfo.InvariantCulture);
            choices.Add(value);
            labels[value] = $"{number} / {count}";
        }
        return new(Key, "结构", AnalysisParameterKind.Choice, Default(analysisName),
            0, 1, 1, choices, labels);
    }

    internal static string Read(string analysisName, IReadOnlyDictionary<string, string>? settings) =>
        settings?.GetValueOrDefault(Key)?.Trim() ?? Default(analysisName);

    internal static int Resolve(string selection, int activeIndex, int count)
    {
        if (selection is Current or All) return activeIndex;
        if (int.TryParse(selection, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= count) return number - 1;
        throw new ArgumentException($"所选结构 {selection} 不存在；此文件共有 {count} 个结构。", Key);
    }

    internal static void Validate(string analysisName, string selection, int activeIndex, int count)
    {
        _ = Resolve(selection, activeIndex, count);
        if (selection == All && AllDisplay(analysisName) == AnalysisConfigurationDisplay.Single)
            throw new ArgumentException("此分析支持当前的或单个结构；不支持将所有结构作为普通曲线叠加。", Key);
    }
}
