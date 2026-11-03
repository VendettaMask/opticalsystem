using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Services;

public enum ScientificColorMap { Viridis, RedBlue, Rainbow, Inferno, Gray }

/// <summary>Presentation-only scalar colours. Never used for curves, RGB images or physical wavelengths.</summary>
public static class ScientificColorMaps
{
    private static readonly List<WeakReference<Control>> Plots = [];
    public static ScientificColorMap Current { get; private set; } = ScientificColorMap.Viridis;

    public static ScientificColorMap Parse(string? value) =>
        Enum.TryParse<ScientificColorMap>(value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : ScientificColorMap.Viridis;

    internal static void Track(Control plot)
    {
        Plots.RemoveAll(reference => !reference.TryGetTarget(out _));
        Plots.Add(new(plot));
    }

    public static void Configure(AppSettings settings)
    {
        var next = Parse(settings.AnalysisColorMap);
        if (Current == next) return;
        Current = next;
        Plots.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in Plots)
            if (reference.TryGetTarget(out var plot)) plot.InvalidateVisual();
    }

    public static Color ColorFor(ScientificColorMap map, double value)
    {
        if (!double.IsFinite(value)) return Colors.Transparent;
        value = Math.Clamp(value, 0, 1);
        if (map == ScientificColorMap.RedBlue)
        {
            // Blue -> neutral white -> red; preserve the plot's existing normalization.
            var low = value <= .5 ? Color.FromRgb(33, 102, 172) : Colors.White;
            var high = value <= .5 ? Colors.White : Color.FromRgb(178, 24, 43);
            var fraction = value <= .5 ? value * 2 : (value - .5) * 2;
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
            return Color.FromRgb(Mix(low.R, high.R), Mix(low.G, high.G), Mix(low.B, high.B));
        }
        return AnalysisPlotControl.ColorFor(map switch
        {
            ScientificColorMap.Rainbow => AnalysisColorMap.Jet,
            ScientificColorMap.Inferno => AnalysisColorMap.Inferno,
            ScientificColorMap.Gray => AnalysisColorMap.Gray,
            _ => AnalysisColorMap.Viridis
        }, value);
    }

    internal static Color ScalarColor(AnalysisColorMap sourceMap, double value) =>
        sourceMap is AnalysisColorMap.Gray or AnalysisColorMap.InverseGray
            ? AnalysisPlotControl.ColorFor(sourceMap, value)
            : ColorFor(Current, value);
}
