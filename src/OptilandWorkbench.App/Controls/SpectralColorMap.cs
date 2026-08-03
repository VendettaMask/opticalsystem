using Avalonia.Media;

namespace OptilandWorkbench.App.Controls;

internal static class SpectralColorMap
{
    private const double VisibleMinimumNanometers = 380;
    private const double VisibleMaximumNanometers = 780;

    private static readonly SpectralStop[] DisplayStops =
    {
        new(380, Color.FromRgb(126, 54, 255)),
        new(430, Color.FromRgb(50, 82, 255)),
        new(486.1, Color.FromRgb(0, 140, 255)),
        new(510, Color.FromRgb(0, 184, 220)),
        new(546.1, Color.FromRgb(0, 184, 140)),
        new(587.6, Color.FromRgb(0, 200, 83)),
        new(610, Color.FromRgb(255, 116, 0)),
        new(656.3, Color.FromRgb(255, 52, 48)),
        new(700, Color.FromRgb(232, 0, 73)),
        new(780, Color.FromRgb(145, 0, 76))
    };

    internal static Color FromNanometers(double wavelengthNanometers)
    {
        if (!double.IsFinite(wavelengthNanometers)
            || wavelengthNanometers < VisibleMinimumNanometers
            || wavelengthNanometers > VisibleMaximumNanometers)
        {
            return Color.FromRgb(126, 132, 145);
        }

        for (var index = 1; index < DisplayStops.Length; index++)
        {
            var upper = DisplayStops[index];
            if (wavelengthNanometers > upper.Nanometers)
            {
                continue;
            }

            var lower = DisplayStops[index - 1];
            var amount = (wavelengthNanometers - lower.Nanometers)
                / (upper.Nanometers - lower.Nanometers);
            return Interpolate(lower.Color, upper.Color, amount);
        }

        return DisplayStops[^1].Color;
    }

    private static Color Interpolate(Color lower, Color upper, double amount)
    {
        var clamped = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            Channel(lower.R, upper.R, clamped),
            Channel(lower.G, upper.G, clamped),
            Channel(lower.B, upper.B, clamped));
    }

    private static byte Channel(byte lower, byte upper, double amount) =>
        (byte)Math.Clamp(Math.Round(lower + ((upper - lower) * amount)), 0, 255);

    private readonly record struct SpectralStop(double Nanometers, Color Color);
}
