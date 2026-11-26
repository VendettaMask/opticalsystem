using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>The drawing sheet; 2D screen fitting can follow the viewport independently of the print aspect.</summary>
internal sealed class UniformDiagramPage : Grid
{
    internal const double SheetWidth = 1200;
    internal const double SheetHeight = 900;
    internal const double CaptionHeight = 160;
    internal const double PlotHeight = SheetHeight - CaptionHeight;
    internal static double ExportScale => ImageExportResolution.Scale;
    private readonly Control _plot;
    private readonly Control _plotHost;
    private readonly Control _caption;
    private readonly double _captionHeight;
    private readonly bool _fillViewportAspect;
    private readonly bool _fitDrawingToViewport;
    private bool _fitToViewport = true;
    private double _frameInset;
    private readonly Border _overflow;
    private Size _viewport;
    private double _automaticWidth;

    internal UniformDiagramPage(Control plot, Control caption, bool preservePlotGeometry = false,
        double captionHeight = CaptionHeight, IBrush? background = null, bool fillViewportAspect = false,
        bool fitDrawingToViewport = false)
    {
        Name = "UniformDiagramPage";
        Width = DiagramSheetLayout.Width;
        Height = SheetHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _captionHeight = captionHeight;
        _fillViewportAspect = fillViewportAspect;
        _fitDrawingToViewport = fitDrawingToViewport;
        RowDefinitions = new RowDefinitions($"{SheetHeight - captionHeight},{captionHeight}");
        ClipToBounds = true;
        UseLayoutRounding = false;
        _plot = plot;
        _plotHost = preservePlotGeometry ? new OriginalPlotLayout(plot) : plot;
        _caption = caption;
        Children.Add(_plotHost);
        SetRow(caption, 1);
        Children.Add(caption);
        ThemeDrawingSurface.ApplyResources(this, transparentPlot: true);
        if (background is null) ThemeDrawingSurface.AddPaper(this);
        _overflow = new Border
        {
            IsVisible = false,
            Padding = new Thickness(32),
            Child = new TextBlock
            {
                Text = UiLocalization.Text("当前内容超出统一图框，请减少同时显示的视场或子图，或减小文字后刷新。"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = DisplayTypography.Body,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _overflow.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.PlotBackground);
        SetRowSpan(_overflow, 2);
        Children.Add(_overflow);
        if (background is null) this.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.PlotBackground);
        else Background = background;
        AttachedToVisualTree += (_, _) =>
        {
            DiagramSheetLayout.Changed += UpdateSheet;
            UpdateSheet();
        };
        DetachedFromVisualTree += (_, _) => DiagramSheetLayout.Changed -= UpdateSheet;
    }

    private void UpdateSheet()
    {
        if (_fitDrawingToViewport)
        {
            // Screen fitting uses the available aspect, not the print sheet's
            // aspect. Keep logical text/caption dimensions and isotropic drawing.
            var aspect = _viewport.Height > 0 ? _viewport.Width / _viewport.Height : Width / Height;
            var width = _fitToViewport ? Math.Clamp(Math.Max(SheetWidth, (SheetHeight + 2 * _frameInset) * aspect - 2 * _frameInset), SheetWidth, 7200)
                : DiagramSheetLayout.Width;
            var height = _fitToViewport ? Math.Clamp(Math.Max(SheetHeight, (SheetWidth + 2 * _frameInset) / aspect - 2 * _frameInset), SheetHeight, 7200)
                : SheetHeight;
            Width = width;
            if (Height != height)
            {
                Height = height;
                RowDefinitions[0].Height = new GridLength(height - _captionHeight);
            }
            return;
        }
        if (DiagramSheetLayout.AspectRatio == DiagramSheetLayout.Automatic && _viewport.Width > 0 && _viewport.Height > 0)
        {
            _automaticWidth = _fillViewportAspect
                ? Math.Round(Math.Clamp(SheetHeight * _viewport.Width / _viewport.Height, 320, 7200))
                : DiagramSheetLayout.AutomaticWidth(_viewport, _automaticWidth);
            Width = _automaticWidth;
        }
        else
        {
            _automaticWidth = 0;
            Width = DiagramSheetLayout.Width;
        }
    }

    internal bool FitsDrawingToViewport => _fitDrawingToViewport;

    internal void SetViewport(Size viewport, bool fitToViewport = true, double frameInset = 0)
    {
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width <= 0 || viewport.Height <= 0)
            return;
        _viewport = viewport;
        _fitToViewport = fitToViewport;
        _frameInset = Math.Max(0, frameInset);
        UpdateSheet();
    }

    internal bool HasOverflow { get; private set; }
    internal Control Plot => _plot;
    internal Control Caption => _caption;

    protected override Size MeasureOverride(Size availableSize)
    {
        _plotHost.Measure(new Size(Width, double.PositiveInfinity));
        _caption.Measure(new Size(Width, double.PositiveInfinity));
        HasOverflow = _plot.DesiredSize.Height > Height - _captionHeight + .5
            || ResponsiveAnalysisPlotHost.MinimumReadableWidth(_plot) > (_plotHost == _plot ? Width : SheetWidth) + .5
            || _caption.DesiredSize.Height > _captionHeight + .5;
        _overflow.IsVisible = HasOverflow;
        base.MeasureOverride(new Size(Width, Height));
        return new Size(Width, Height);
    }

    internal void ValidateForExport()
    {
        if (HasOverflow)
            throw new InvalidOperationException(UiLocalization.Text("当前内容超出统一图框，请减少同时显示的视场或子图，或减小文字后刷新。"));
    }

    // Equal-aspect axes derive their numerical range from the inner rectangle.
    // Keep that complete rectangle unchanged; the wider sheet adds side space.
    private sealed class OriginalPlotLayout : Decorator
    {
        internal OriginalPlotLayout(Control plot)
        {
            Child = plot;
            UseLayoutRounding = false;
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            Child!.Measure(new Size(SheetWidth, availableSize.Height));
            return new Size(SheetWidth, Child.DesiredSize.Height);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Child!.Arrange(new Rect((finalSize.Width - SheetWidth) / 2, 0, SheetWidth, finalSize.Height));
            return finalSize;
        }
    }
}
