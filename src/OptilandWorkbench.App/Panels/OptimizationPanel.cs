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
    private readonly ComboBox _optimizerPicker = new()
    {
        MinWidth = 180,
        SelectedIndex = 0
    };
    private readonly NumericUpDown _iterationsInput = new()
    {
        Minimum = 1,
        Maximum = 1000,
        Increment = 10,
        Value = 80,
        Width = 100
    };
    private readonly TextBlock _result = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 12, 0, 0)
    };

    public OptimizationPanel(OptilandConnector connector)
    {
        _connector = connector;
        _optimizerPicker.ItemsSource = _connector.OptimizerNames;

        var runButton = new Button { Content = "Run", MinWidth = 86 };
        runButton.Click += (_, _) => Run();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _surfacePicker, _optimizerPicker, _iterationsInput, runButton }
        };

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Radius variable", FontWeight = FontWeight.SemiBold },
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

        if (_optimizerPicker.SelectedItem is null && _connector.OptimizerNames.Count > 0)
        {
            _optimizerPicker.SelectedIndex = 0;
        }
    }

    private void Run()
    {
        if (_surfacePicker.SelectedItem is not OpticalSurface surface)
        {
            _result.Text = "Select a surface first.";
            return;
        }

        var optimizerName = _optimizerPicker.SelectedItem as string
            ?? _connector.OptimizerNames.FirstOrDefault()
            ?? "Orthogonal Descent";
        var iterations = _iterationsInput.Value.HasValue
            ? Decimal.ToInt32(_iterationsInput.Value.Value)
            : 80;
        var initialRadius = surface.Radius;
        var result = _connector.OptimizeSurfaceRadius(surface, optimizerName, iterations);
        _result.Text =
            $"{result.Message}{Environment.NewLine}" +
            $"Merit: {result.InitialMerit:0.######} -> {result.FinalMerit:0.######}{Environment.NewLine}" +
            $"Radius: {initialRadius:0.###} -> {surface.Radius:0.###}{Environment.NewLine}" +
            $"Iterations: {result.Iterations}";
    }
}
