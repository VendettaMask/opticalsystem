using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Operand-type colours shared by every matching row and configuration.</summary>
internal static class OperandRowColors
{
    private static readonly Color[] Hues =
    [
        Color.Parse("#4285D4"), Color.Parse("#369B68"), Color.Parse("#9762C5"), Color.Parse("#CD873C"),
        Color.Parse("#BC6187"), Color.Parse("#279BA7"), Color.Parse("#8C963E"), Color.Parse("#AB7351")
    ];

    internal static string Background(int index) => index < 0 ? ThemeResourceBindings.Surface
        : $"MultiConfigurationOperandRow.Background.{index % Hues.Length}";

    internal static int Category(MultiConfigurationParameterKind? kind) => kind switch
    {
        MultiConfigurationParameterKind.Thickness => 0,
        MultiConfigurationParameterKind.Curvature => 1,
        MultiConfigurationParameterKind.Conic => 2,
        MultiConfigurationParameterKind.SemiDiameter => 3,
        MultiConfigurationParameterKind.Material => 4,
        MultiConfigurationParameterKind.ShapeCoefficient or MultiConfigurationParameterKind.GradientIndexCoefficient => 5,
        MultiConfigurationParameterKind.ModelGlassCoefficient => 6,
        _ => -1
    };

    internal static void AddResources(IResourceDictionary resources, Color fallbackSurface)
    {
        var surface = (resources[ThemeResourceBindings.Surface] as ISolidColorBrush)?.Color ?? fallbackSurface;
        var dark = .2126 * surface.R + .7152 * surface.G + .0722 * surface.B < 128;
        for (var index = 0; index < Hues.Length; index++)
        {
            resources[Background(index)] = Tint(surface, Hues[index], dark ? .24 : .18);
        }
    }

    private static IBrush Tint(Color surface, Color hue, double fraction)
    {
        byte Mix(byte background, byte tint) => (byte)Math.Round(background + (tint - background) * fraction);
        return new ImmutableSolidColorBrush(Color.FromRgb(Mix(surface.R, hue.R), Mix(surface.G, hue.G), Mix(surface.B, hue.B)));
    }
}
