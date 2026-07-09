using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly TextBox _report = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 300
    };

    public AnalysisPanel(OptilandConnector connector)
    {
        _connector = connector;

        var runButton = new Button
        {
            Content = "Run analysis",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 120
        };
        runButton.Click += (_, _) => Refresh();

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(runButton, Dock.Top);
        root.Children.Add(runButton);
        root.Children.Add(_report);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _report.Text = _connector.BuildAnalysisReport();
    }
}
