using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>One external drawing frame; its inset never changes the source sheet.</summary>
internal sealed class AnalysisImageFrame : Border
{
    internal const double LineWidth = 1;
    private readonly FrameCanvas _canvas = new() { IsHitTestVisible = false };
    public AnalysisImageFrame()
    {
        BorderThickness = new Thickness(LineWidth);
        CornerRadius = default;
        UseLayoutRounding = false;
        this.BindThemeResource(BorderBrushProperty, ThemeResourceBindings.PlotAxis);
        ActualThemeVariantChanged += (_, _) => UpdateMaterial();
        SizeChanged += (_, _) => UpdateMaterial();
        UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        Padding = new Thickness(ThemeDrawingSurface.FrameInset(this) - LineWidth);
        _canvas.Material = ThemeDrawingSurface.Material(this);
        CornerRadius = _canvas.Material is { } material ? new CornerRadius(material.FrameInset) : default;
        if (_canvas.Material is null) Background = null;
        else
        {
            var size = new Size(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));
            _canvas.Width = size.Width;
            _canvas.Height = size.Height;
            _canvas.Measure(size);
            _canvas.Arrange(new Rect(size));
            _canvas.InvalidateVisual();
            Background = new VisualBrush(_canvas) { Stretch = Stretch.Fill };
        }
        InvalidateVisual();
    }

    private sealed class FrameCanvas : Control
    {
        internal IThemeDrawingMaterial? Material { get; set; }
        public override void Render(DrawingContext context) => Material?.DrawFrame(context, new Rect(Bounds.Size));
    }
}
