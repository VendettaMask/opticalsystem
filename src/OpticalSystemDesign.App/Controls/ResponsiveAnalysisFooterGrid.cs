using Avalonia;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

internal sealed class ResponsiveAnalysisFooterGrid : Grid
{
    private readonly Control _summary;
    private readonly Control _document;
    private bool _stacked;

    public ResponsiveAnalysisFooterGrid(Control summary, Control document)
    {
        _summary = summary;
        _document = document;
        Children.Add(summary);
        Children.Add(document);
        ApplyLayout(false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = availableSize.Width < DisplayTypography.Scale(600);
        if (stacked != _stacked) ApplyLayout(stacked);
        return base.MeasureOverride(availableSize);
    }

    private void ApplyLayout(bool stacked)
    {
        _stacked = stacked;
        ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "2*,*");
        RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "*");
        SetRow(_summary, 0);
        SetColumn(_summary, 0);
        SetRow(_document, stacked ? 1 : 0);
        SetColumn(_document, stacked ? 0 : 1);
    }
}
