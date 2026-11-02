using Avalonia.Automation;
using Avalonia.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

/// <summary>One display-only scale policy shared by analysis and layout viewports.</summary>
internal sealed class DiagramViewportModeButton : DropDownButton
{
    private ResponsiveAnalysisPlotHost? _host;
    private bool _fit = true;
    private readonly MenuItem _fitItem = new() { ToggleType = MenuItemToggleType.Radio, IsChecked = true };
    private readonly MenuItem _actualItem = new() { ToggleType = MenuItemToggleType.Radio };
    protected override Type StyleKeyOverride => typeof(DropDownButton);

    internal DiagramViewportModeButton(string name)
    {
        Name = name;
        var menu = new MenuFlyout(); menu.Items.Add(_fitItem); menu.Items.Add(_actualItem); Flyout = menu;
        _fitItem.Click += (_, _) => SetFit(true);
        _actualItem.Click += (_, _) => SetFit(false);
        menu.Opening += (_, _) => UpdateLabels();
        UpdateLabels();
    }
    internal bool IsFitToViewport => _fit;
    internal void BindHost(ResponsiveAnalysisPlotHost? host)
    {
        _host = host;
        IsVisible = host?.Child is UniformDiagramPage;
        if (host is not null) host.FitToViewport = _fit;
        UpdateLabels();
    }
    private void SetFit(bool fit)
    {
        _fit = fit;
        if (_host is not null) _host.FitToViewport = fit;
        _fitItem.IsChecked = fit; _actualItem.IsChecked = !fit;
        UpdateLabels();
    }
    private void UpdateLabels()
    {
        Content = UiLocalization.Text(_fit ? "适应窗口" : "100%");
        _fitItem.Header = UiLocalization.Text("适应窗口");
        _actualItem.Header = UiLocalization.Text("原始大小（100%）");
        AutomationProperties.SetName(this, UiLocalization.Text("图纸显示比例"));
        AutomationProperties.SetName(_fitItem, "适应窗口");
        AutomationProperties.SetName(_actualItem, "原始大小（100%）");
        ToolTip.SetTip(this, UiLocalization.Text("仅调整屏幕显示比例，原图、数据和导出尺寸保持不变"));
    }
}
