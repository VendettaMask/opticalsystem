using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OpticalSystemDesign.App.Theming;

internal static class SurfaceRolePalette
{
    // Ordinary, object, stop, image. Role colors are separate from spectral hues.
    private static readonly IBrush[] Light = BrushesFor("#8BA3BF", "#4286D3", "#E9A257", "#40B7A4");
    private static readonly IBrush[] Dark = BrushesFor("#9AAFC8", "#69A6EF", "#F1B465", "#65D1BC");

    internal static IBrush Resolve(int number, bool isLastSurface, bool isStop, bool darkTheme)
    {
        var index = number == 0 ? 1 : isLastSurface ? 3 : isStop ? 2 : 0;
        return (darkTheme ? Dark : Light)[index];
    }

    private static IBrush[] BrushesFor(params string[] colors) =>
        colors.Select(color => (IBrush)new ImmutableSolidColorBrush(Color.Parse(color))).ToArray();
}
