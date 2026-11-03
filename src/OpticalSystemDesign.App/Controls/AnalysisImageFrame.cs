using Avalonia;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

/// <summary>One external drawing frame; its inset never changes the source sheet.</summary>
internal sealed class AnalysisImageFrame : Border
{
    internal const double LineWidth = 1;
    public AnalysisImageFrame()
    {
        BorderThickness = new Thickness(LineWidth);
        CornerRadius = default;
        UseLayoutRounding = false;
        this.BindThemeResource(BorderBrushProperty, ThemeResourceBindings.PlotAxis);
    }
}
