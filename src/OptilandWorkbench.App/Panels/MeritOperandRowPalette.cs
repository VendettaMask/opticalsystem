using Avalonia.Media;

namespace OptilandWorkbench.App.Panels;

internal static class MeritOperandRowPalette
{
    private static readonly Color LightForeground = Color.FromRgb(24, 24, 27);
    private static readonly Color DarkForeground = Color.FromRgb(236, 240, 246);

    internal static Color Resolve(string? operandType, bool hasError = false)
    {
        return ResolveBackground(operandType, hasError, darkTheme: false);
    }

    internal static MeritOperandRowVisual ResolveVisual(
        string? operandType,
        bool hasError = false,
        bool darkTheme = false)
    {
        return new MeritOperandRowVisual(
            ResolveBackground(operandType, hasError, darkTheme),
            darkTheme ? DarkForeground : LightForeground);
    }

    internal static IEnumerable<MeritOperandRowVisual> ContrastSamples()
    {
        var sampleTypes = new[]
        {
            "BLNK",
            "DMFS",
            "TTHI",
            "OPLT",
            "EFFL",
            "PMAG",
            "POWR",
            "MNIN",
            "CONS",
            "DIVI",
            "EQUA",
            "OSUM",
            "MNEA",
            "MNCG",
            "RSCE",
            "TRAC",
            "UNKNOWN"
        };
        foreach (var darkTheme in new[] { false, true })
        {
            foreach (var sampleType in sampleTypes)
            {
                yield return ResolveVisual(sampleType, darkTheme: darkTheme);
            }

            yield return ResolveVisual("EFFL", hasError: true, darkTheme: darkTheme);
        }
    }

    private static Color ResolveBackground(string? operandType, bool hasError, bool darkTheme)
    {
        if (hasError)
        {
            return darkTheme ? Color.FromRgb(92, 44, 44) : Color.FromRgb(255, 198, 198);
        }

        var type = operandType?.Trim().ToUpperInvariant() ?? string.Empty;
        var light = type switch
        {
            "BLNK" => Colors.White,
            "DMFS" => Color.FromRgb(247, 177, 239),

            "TTHI" or "PETZ" or "MNCA" or "MXEG" or "CONF" or "RANG"
                => Color.FromRgb(188, 188, 248),

            "OPLT" or "CTGT" or "PROD" or "PROB" or "EQUA"
                => Color.FromRgb(231, 237, 224),

            "EFFL" or "FNUM" or "TOTR" or "MNEG" or "REAX" or "REAY" or "POWR"
                => Color.FromRgb(190, 218, 242),

            "PMAG" or "REAR" or "DIMX" or "RADI" or "THIC" or "MNIN" or "MXIN" or "MNAB" or "MXAB"
                => Color.FromRgb(246, 243, 190),

            "CONS" or "SINE" or "OSUM" or "QSUM"
                => Color.FromRgb(211, 231, 232),

            "DIVI" or "DIVB"
                => Color.FromRgb(255, 198, 198),

            "MNEA"
                => Color.FromRgb(255, 221, 221),

            "MNCG" or "MXCG"
                => Color.FromRgb(208, 251, 211),

            _ when type.StartsWith("RS", StringComparison.Ordinal)
                || type.StartsWith("RW", StringComparison.Ordinal)
                || type.StartsWith("OPD", StringComparison.Ordinal)
                => Color.FromRgb(218, 216, 250),

            _ when type.StartsWith("TR", StringComparison.Ordinal)
                || type.StartsWith("AN", StringComparison.Ordinal)
                || type.StartsWith("MEC", StringComparison.Ordinal)
                => Color.FromRgb(211, 231, 232),

            _ => Color.FromRgb(236, 244, 241)
        };
        return darkTheme ? DarkEquivalent(light) : light;
    }

    private static Color DarkEquivalent(Color light)
    {
        if (light == Colors.White)
        {
            return Color.FromRgb(28, 32, 38);
        }

        return Color.FromRgb(
            Darken(light.R),
            Darken(light.G),
            Darken(light.B));
    }

    private static byte Darken(byte channel)
    {
        return (byte)Math.Clamp(24 + (channel * 0.21), 24, 86);
    }
}

internal sealed record MeritOperandRowVisual(Color Background, Color Foreground);
