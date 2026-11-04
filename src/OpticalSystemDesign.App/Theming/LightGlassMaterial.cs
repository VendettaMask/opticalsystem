using Avalonia;
using Avalonia.Media;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Paint-only glass layers. The window's blue backdrop and content remain separate.</summary>
internal static class LightGlassMaterial
{
    public static IBrush Sheen { get; } = Gradient(new RelativePoint(1, 1, RelativeUnit.Relative),
        (0, "#48FFFFFF"), (.38, "#0CFFFFFF"), (.7, "#28FFFFFF"), (1, "#12FFFFFF"));
    public static IBrush Rim { get; } = Gradient(new RelativePoint(0, 1, RelativeUnit.Relative),
        (0, "#F0FFFFFF"), (.4, "#B0FFFFFF"), (1, "#80A9CEF2"));
    public static IBrush SectionSurface { get; } = Gradient(new RelativePoint(.65, 1, RelativeUnit.Relative),
        (0, "#94FFFFFF"), (.5, "#54FFFFFF"), (1, "#80FFFFFF"));
    public static IBrush EditorHeaderSurface { get; } = Gradient(new RelativePoint(0, 1, RelativeUnit.Relative),
        (0, "#E5F2FF"), (.28, "#D5E9FD"), (1, BlueThemeTokens.LensEditorHeaderBackground.ToString()));
    public static BoxShadows SectionShadow { get; } = BoxShadows.Parse(
        "0 1 3 0 #08396EA8, inset 0 1 0 0 #D8FFFFFF, inset 0 -1 0 0 #6078B5E8");
    public static BoxShadows FrameShadow { get; } = BoxShadows.Parse(
        "inset 0 2 5 0 #20FFFFFF, inset 0 -1 4 0 #0C538DC5");

    private static IBrush Gradient(RelativePoint end, params (double Offset, string Color)[] stops)
    {
        var gradientStops = new GradientStops();
        foreach (var stop in stops) gradientStops.Add(new GradientStop(Color.Parse(stop.Color), stop.Offset));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = end,
            GradientStops = gradientStops
        }.ToImmutable();
    }

}
