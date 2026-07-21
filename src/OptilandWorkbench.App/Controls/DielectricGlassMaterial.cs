using Avalonia.Media;

namespace OptilandWorkbench.App.Controls;

internal readonly record struct DielectricGlassSample(
    double Reflectance,
    double Transmission,
    Color Color);

internal static class DielectricGlassMaterial
{
    private const double MinimumCosine = 0.08;
    private const double AttenuationDistanceMillimeters = 180;

    public static DielectricGlassSample Sample(
        double refractiveIndex,
        double viewCosine,
        double keyLight,
        double rimLight,
        double thicknessMillimeters,
        bool isSideWall)
    {
        var ior = Math.Clamp(refractiveIndex, 1.0001, 2.5);
        var cosine = Math.Clamp(Math.Abs(viewCosine), 0, 1);
        var r0 = Math.Pow((ior - 1) / (ior + 1), 2);
        var reflectance = r0 + ((1 - r0) * Math.Pow(1 - cosine, 5));

        var opticalPath = Math.Max(0, thicknessMillimeters)
            / Math.Max(MinimumCosine, cosine);
        var transmission = Math.Exp(-opticalPath / AttenuationDistanceMillimeters);
        var absorption = 1 - transmission;
        var key = Math.Clamp(keyLight, 0, 1);
        var rim = Math.Clamp(rimLight, 0, 1);
        var studioHighlight = Math.Clamp((key * 0.72) + (rim * 0.55), 0, 1);

        var reflectionWeight = Math.Clamp(reflectance + (studioHighlight * 0.22), 0, 1);
        var bodyWeight = Math.Clamp(absorption * 2.4, 0, 0.58);
        var sideWeight = isSideWall ? 0.16 : 0;
        var alpha = Math.Clamp(
            0.035 + (reflectionWeight * 0.72) + bodyWeight + sideWeight,
            0.04,
            isSideWall ? 0.82 : 0.68);

        var neutral = 226 + (24 * studioHighlight);
        var red = neutral - (bodyWeight * 92) - (sideWeight * 95);
        var green = neutral + (bodyWeight * 8) - (sideWeight * 24);
        var blue = neutral + (bodyWeight * 20) - (sideWeight * 6);
        return new DielectricGlassSample(
            reflectance,
            transmission,
            Color.FromArgb(
                ToByte(alpha * 255),
                ToByte(red),
                ToByte(green),
                ToByte(blue)));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
