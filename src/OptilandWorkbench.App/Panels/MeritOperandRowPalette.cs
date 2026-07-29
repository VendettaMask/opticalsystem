using Avalonia.Media;

namespace OptilandWorkbench.App.Panels;

internal static class MeritOperandRowPalette
{
    internal static Color Resolve(string? operandType, bool hasError = false)
    {
        if (hasError)
        {
            return Color.FromRgb(255, 198, 198);
        }

        var type = operandType?.Trim().ToUpperInvariant() ?? string.Empty;
        return type switch
        {
            "BLNK" => Colors.White,
            "DMFS" => Color.FromRgb(247, 177, 239),

            "TTHI" or "PETZ" or "MNCA" or "MXEG" or "CONF" or "RANG"
                => Color.FromRgb(188, 188, 248),

            "OPLT" or "CTGT" or "PROD"
                => Color.FromRgb(231, 237, 224),

            "EFFL" or "FNUM" or "TOTR" or "MNEG" or "REAX" or "REAY"
                => Color.FromRgb(190, 218, 242),

            "PMAG" or "REAR" or "DIMX" or "RADI" or "THIC"
                => Color.FromRgb(246, 243, 190),

            "CONS" or "SINE"
                => Color.FromRgb(211, 231, 232),

            "DIVI"
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
    }
}
