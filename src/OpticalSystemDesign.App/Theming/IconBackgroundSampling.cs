using Avalonia;
using Avalonia.Media;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Samples an opaque UI surface in that surface's own coordinates.</summary>
internal static class IconBackgroundSampling
{
    internal static bool TrySample(IBrush? brush, Point position, Size size, out Color colour)
    {
        colour = default;
        if (brush is null || brush.Opacity < .98) return false;
        if (brush is ISolidColorBrush solid)
        {
            colour = solid.Color;
            return colour.A >= 250;
        }
        if (brush is not ILinearGradientBrush gradient || gradient.SpreadMethod != GradientSpreadMethod.Pad)
            return false;
        var stops = gradient.GradientStops.OrderBy(stop => stop.Offset).ToArray();
        if (stops.Length == 0) return false;
        var start = gradient.StartPoint.ToPixels(size);
        var direction = gradient.EndPoint.ToPixels(size) - start;
        var delta = position - start;
        var lengthSquared = direction.X * direction.X + direction.Y * direction.Y;
        var offset = lengthSquared > 0 ? Math.Clamp((delta.X * direction.X + delta.Y * direction.Y) / lengthSquared, 0, 1) : 0;
        var lower = stops[0]; var upper = stops[^1];
        if (offset <= lower.Offset) upper = lower;
        else if (offset >= upper.Offset) lower = upper;
        else
            for (var index = 1; index < stops.Length; index++)
                if (offset <= stops[index].Offset) { lower = stops[index - 1]; upper = stops[index]; break; }
        var fraction = upper.Offset > lower.Offset ? (offset - lower.Offset) / (upper.Offset - lower.Offset) : 0;
        byte Mix(byte first, byte second) => (byte)Math.Round(first + (second - first) * fraction);
        colour = Color.FromArgb(Mix(lower.Color.A, upper.Color.A), Mix(lower.Color.R, upper.Color.R),
            Mix(lower.Color.G, upper.Color.G), Mix(lower.Color.B, upper.Color.B));
        return colour.A >= 250;
    }
}
