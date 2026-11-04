using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using Avalonia.VisualTree;

namespace OpticalSystemDesign.App.Controls;

/// <summary>One external drawing frame; its inset never changes the source sheet.</summary>
internal sealed class AnalysisImageFrame : Border
{
    internal const double LineWidth = 1;
    private readonly FrameCanvas _canvas = new() { IsHitTestVisible = false };
    internal bool IntegrateWithResultBinding { get; init; }
    internal bool ResultBookOnly { get; init; }
    internal bool IntegrateWithWorkspaceBinding { get; init; }
    internal bool IsIntegrated => (IntegrateWithResultBinding
        && this.FindAncestorOfType<AnalysisResultTabs>()?.UsesBookFrame == true)
        || (IntegrateWithWorkspaceBinding && this.FindAncestorOfType<AnalysisBookWorkspace>()?.UsesBookFrame == true);
    internal double DrawingInset => IsIntegrated || ResultBookOnly && ThemeDrawingSurface.Material(this) is null
        ? 0 : ThemeDrawingSurface.FrameInset(this);
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
        _canvas.Material = DrawingInset == 0 ? null : ThemeDrawingSurface.Material(this);
        var line = DrawingInset == 0 || _canvas.Material is IsekaiDrawingMaterial ? 0 : LineWidth;
        BorderThickness = new Thickness(line);
        Padding = new Thickness(Math.Max(0, DrawingInset - line));
        CornerRadius = default; // Material alpha, not a neat rectangle, defines a parchment edge.
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); UpdateMaterial();
    }

    private sealed class FrameCanvas : Control
    {
        internal IThemeDrawingMaterial? Material { get; set; }
        public override void Render(DrawingContext context) => Material?.DrawFrame(context, new Rect(Bounds.Size));
    }
}
