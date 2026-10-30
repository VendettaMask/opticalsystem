using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class AnalysisPanel
{
    private DropDownButton CreateCurveWidthButton()
    {
        var menu = new MenuFlyout();
        foreach (var (width, label) in new[]
        {
            (AnalysisCurveWidth.Thin, "细"),
            (AnalysisCurveWidth.Standard, "标准"),
            (AnalysisCurveWidth.Thick, "粗")
        })
        {
            var item = new MenuItem
            {
                Header = label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = width == AnalysisCurveWidth.Standard,
                Tag = width
            };
            AutomationProperties.SetName(item, label);
            item.Click += (_, _) =>
            {
                // Inherited rendering state also reaches plots created by a refresh.
                // Never replace result series or reset their interactive viewport.
                _resultHost.SetValue(AnalysisPlotControl.CurveWidthProperty, width);
                foreach (var choice in menu.Items.OfType<MenuItem>())
                    choice.IsChecked = Equals(choice.Tag, width);
            };
            menu.Items.Add(item);
        }

        var button = new DropDownButton
        {
            Name = "AnalysisCurveWidth",
            Flyout = menu,
            IsVisible = false,
            Content = new TextBlock { Text = "线宽", VerticalAlignment = VerticalAlignment.Center }
        };
        StyleToolbarButton(button, iconOnly: false);
        AutomationProperties.SetName(button, "线宽");
        ToolTip.SetTip(button, "曲线线宽：细、标准、粗");
        return button;
    }

    private void UpdateCurveWidthButton()
    {
        static bool HasCurve(AnalysisSeriesDto series) => series.Points.Count > 0
            && series.Kind is AnalysisSeriesKind.Line or AnalysisSeriesKind.ColoredLine;
        _curveWidthButton.IsVisible = ImageExportPage() is not null && _view is { } view
            && (view.Series.Any(HasCurve) || view.PlotPanes.Any(pane => pane.Series.Any(HasCurve)));
        _curveWidthButton.IsEnabled = !_isRunning && !_disposed;
        ControlAvailability.Explain(_curveWidthButton, _isRunning
            ? "正在计算，完成后可调整线宽" : "曲线线宽：细、标准、粗");
    }
}
