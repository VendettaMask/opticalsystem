using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

/// <summary>The original drawing sheet, shared by the live view and PNG capture.</summary>
internal sealed class UniformDiagramPage : Grid
{
    internal const double SheetWidth = 1200;
    internal const double SheetHeight = 900;
    internal const double CaptionHeight = 160;
    internal const double PlotHeight = SheetHeight - CaptionHeight;
    internal const double ExportScale = 2;
    private readonly Control _plot;
    private readonly Control _caption;
    private readonly Border _overflow;

    internal UniformDiagramPage(Control plot, Control caption)
    {
        Name = "UniformDiagramPage";
        Width = SheetWidth;
        Height = SheetHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        RowDefinitions = new RowDefinitions($"{PlotHeight},{CaptionHeight}");
        ClipToBounds = true;
        UseLayoutRounding = false;
        _plot = plot;
        _caption = caption;
        Children.Add(plot);
        SetRow(caption, 1);
        Children.Add(caption);
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
    }

    internal bool HasOverflow { get; private set; }
    internal Control Plot => _plot;
    internal Control Caption => _caption;

    protected override Size MeasureOverride(Size availableSize)
    {
        _plot.Measure(new Size(SheetWidth, double.PositiveInfinity));
        _caption.Measure(new Size(SheetWidth, double.PositiveInfinity));
        HasOverflow = _plot.DesiredSize.Height > PlotHeight + .5
            || ResponsiveAnalysisPlotHost.MinimumReadableWidth(_plot) > SheetWidth + .5
            || _caption.DesiredSize.Height > CaptionHeight + .5;
        _overflow.IsVisible = HasOverflow;
        base.MeasureOverride(new Size(SheetWidth, SheetHeight));
        return new Size(SheetWidth, SheetHeight);
    }

    internal void ValidateForExport()
    {
        if (HasOverflow)
            throw new InvalidOperationException(UiLocalization.Text("当前内容超出统一图框，请减少同时显示的视场或子图，或减小文字后刷新。"));
    }
}
