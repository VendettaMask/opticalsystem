using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly TextBlock _result = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 12, 0, 0)
    };

    public OptimizationPanel(OptilandConnector connector)
    {
        _connector = connector;

        var runButton = new Button { Content = "Optimize radius", MinWidth = 140 };
        runButton.Click += (_, _) => Run();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _surfacePicker, runButton }
        };

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Variable surface", FontWeight = FontWeight.SemiBold },
                row,
                _result
            }
        };

        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.SurfaceDataChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _surfacePicker.ItemsSource = _connector.Surfaces;
        if (_surfacePicker.SelectedItem is null && _connector.Surfaces.Count > 0)
        {
            _surfacePicker.SelectedIndex = Math.Min(2, _connector.Surfaces.Count - 1);
        }
    }

    private void Run()
    {
        if (_surfacePicker.SelectedItem is not OpticalSurface surface)
        {
            _result.Text = "Select a surface first.";
            return;
        }

        var result = _connector.OptimizeRadius(surface);
        _result.Text = $"{result.Message}{Environment.NewLine}RMS spot: {result.InitialMetric:0.####} -> {result.FinalMetric:0.####} mm";
    }
}
