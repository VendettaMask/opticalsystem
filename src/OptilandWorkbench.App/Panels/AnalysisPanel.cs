using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly ComboBox _analysisPicker = new()
    {
        MinWidth = 220,
        SelectedIndex = 0
    };
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
        _analysisPicker.ItemsSource = _connector.AnalysisNames;

        var runButton = new Button
        {
            Content = "Run analysis",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 120
        };
        runButton.Click += (_, _) => Refresh();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                _analysisPicker,
                runButton
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_report);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var name = _analysisPicker.SelectedItem as string ?? _connector.AnalysisNames.FirstOrDefault() ?? "Prescription Report";
        _report.Text = _connector.BuildAnalysisReport(name);
    }
}
