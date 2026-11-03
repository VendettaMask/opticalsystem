using Avalonia.Media;
using System.Collections.Concurrent;

namespace OpticalSystemDesign.App.Theming;

internal enum ScientificIconFinish { Flat, SteelHud }

/// <summary>Semantic artwork colours owned by the theme, independent of plotted optical data.</summary>
internal sealed record ScientificIconPalette(Color Structure, Color Signal, Color Secondary)
{
    internal ScientificIconFinish Finish { get; init; }
    internal IBrush GlowOuter { get; } = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(22, Signal.R, Signal.G, Signal.B));
    internal IBrush GlowInner { get; } = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(30, Signal.R, Signal.G, Signal.B));
    private static readonly IBrush HudHot = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#E2FBFF"));
    private static readonly IBrush HudReferenceFill = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#D6AC6B"));
    private static readonly IBrush Steel = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Absolute),
        EndPoint = new Avalonia.RelativePoint(32, 32, Avalonia.RelativeUnit.Absolute),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#A2BDCC"), 0), new GradientStop(Color.Parse("#456878"), .42),
            new GradientStop(Color.Parse("#A3BAC6"), .66), new GradientStop(Color.Parse("#4D6D7B"), 1)
        }
    }.ToImmutable();

    internal IBrush Brush(int role, bool filled)
    {
        if (Finish == ScientificIconFinish.SteelHud)
        {
            if (role == 0) return Steel;
            if (role == 3) return HudHot;
            if (role == 2 && filled) return HudReferenceFill;
        }
        return Brushes[role == 3 ? 1 : role];
    }
    internal IReadOnlyList<IBrush> Brushes { get; } = new IBrush[]
    {
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Structure),
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Signal),
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Secondary)
    };

    internal static ScientificIconPalette Light { get; } = new(
        Color.Parse("#8298A8"), Color.Parse("#398FF0"), Color.Parse("#28ADAF"));

    internal static ScientificIconPalette LumenHud { get; } = new(
        Color.Parse("#6C8D9D"), Color.Parse("#78DCFF"), Color.Parse("#C4F5FF"))
        { Finish = ScientificIconFinish.SteelHud };
    internal static ScientificIconPalette IsekaiArcane { get; } = new(
        Color.Parse("#A18B5F"), Color.Parse("#EACC8B"), Color.Parse("#A6B48B"));

    private static readonly ConcurrentDictionary<Color, ScientificIconPalette> MonochromeCache = new();
    internal static ScientificIconPalette Monochrome(Color colour) =>
        MonochromeCache.GetOrAdd(colour, static value => new(value, value, value));

    internal static double Contrast(Color first, Color second)
    {
        static double L(Color colour)
        {
            static double C(byte value) { var s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * C(colour.R) + .7152 * C(colour.G) + .0722 * C(colour.B);
        }
        var a = L(first); var b = L(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
