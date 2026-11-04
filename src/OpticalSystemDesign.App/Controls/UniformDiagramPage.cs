using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>The selected drawing sheet, shared by live, PNG and report rendering.</summary>
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
    private readonly Border _overflow;
    private Size _viewport;
    private double _automaticWidth;

    internal UniformDiagramPage(Control plot, Control caption, bool preservePlotGeometry = false, double captionHeight = CaptionHeight)
    {
        Name = "UniformDiagramPage";
        Width = DiagramSheetLayout.Width;
        Height = SheetHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _captionHeight = captionHeight;
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
        ThemeDrawingSurface.AddPaper(this);
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
        this.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.PlotBackground);
        AttachedToVisualTree += (_, _) =>
        {
            DiagramSheetLayout.Changed += UpdateSheet;
            UpdateSheet();
        };
        DetachedFromVisualTree += (_, _) => DiagramSheetLayout.Changed -= UpdateSheet;
    }

    private void UpdateSheet()
    {
        if (DiagramSheetLayout.AspectRatio == DiagramSheetLayout.Automatic && _viewport.Width > 0 && _viewport.Height > 0)
        {
            _automaticWidth = DiagramSheetLayout.AutomaticWidth(_viewport, _automaticWidth);
            Width = _automaticWidth;
        }
        else
        {
            _automaticWidth = 0;
            Width = DiagramSheetLayout.Width;
        }
    }

    internal void SetViewport(Size viewport)
    {
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width <= 0 || viewport.Height <= 0)
            return;
        _viewport = viewport;
        UpdateSheet();
    }

    internal bool HasOverflow { get; private set; }
    internal Control Plot => _plot;
    internal Control Caption => _caption;

    protected override Size MeasureOverride(Size availableSize)
    {
        _plotHost.Measure(new Size(Width, double.PositiveInfinity));
        _caption.Measure(new Size(Width, double.PositiveInfinity));
        HasOverflow = _plot.DesiredSize.Height > SheetHeight - _captionHeight + .5
            || ResponsiveAnalysisPlotHost.MinimumReadableWidth(_plot) > (_plotHost == _plot ? Width : SheetWidth) + .5
            || _caption.DesiredSize.Height > _captionHeight + .5;
        _overflow.IsVisible = HasOverflow;
        base.MeasureOverride(new Size(Width, SheetHeight));
        return new Size(Width, SheetHeight);
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
