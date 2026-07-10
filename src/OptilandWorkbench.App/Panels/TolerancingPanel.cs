using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class TolerancingPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly NumericUpDown _radiusSigma = new()
    {
        Minimum = 0,
        Maximum = 1_000,
        Increment = 0.1m,
        Value = 0.1m,
        Width = 100
    };
    private readonly NumericUpDown _thicknessSigma = new()
    {
        Minimum = 0,
        Maximum = 1_000,
        Increment = 0.1m,
        Value = 0.05m,
        Width = 100
    };
    private readonly NumericUpDown _trials = new()
    {
        Minimum = 1,
        Maximum = 10_000,
        Increment = 10,
        Value = 50,
        Width = 92
    };
    private readonly NumericUpDown _seed = new()
    {
        Minimum = 1,
        Maximum = 1_000_000,
        Increment = 1,
        Value = 1234,
        Width = 104
    };
    private readonly NumericUpDown _compensationIterations = new()
    {
        Minimum = 0,
        Maximum = 500,
        Increment = 5,
        Value = 20,
        Width = 92
    };
    private readonly DataGrid _sensitivityGrid = CreateGrid();
    private readonly DataGrid _monteCarloGrid = CreateGrid();
    private readonly TextBlock _summary = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 8, 0, 0)
    };

    public TolerancingPanel(OptilandConnector connector)
    {
        _connector = connector;
        ConfigureGrids();

        var runButton = new Button { Content = "运行公差", MinWidth = 100 };
        runButton.Click += (_, _) => Run();

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                Label("表面"),
                _surfacePicker,
                Label("半径 sigma"),
                _radiusSigma,
                Label("厚度 sigma"),
                _thicknessSigma,
                Label("次数"),
                _trials,
                Label("种子"),
                _seed,
                Label("补偿迭代"),
                _compensationIterations,
                runButton
            }
        };

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "灵敏度", Content = _sensitivityGrid },
                new TabItem { Header = "Monte Carlo", Content = _monteCarloGrid }
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_summary);
        root.Children.Add(tabs);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.SurfaceDataChanged += (_, _) => Refresh();
        Refresh();
    }

    private void ConfigureGrids()
    {
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "扰动", Binding = new Binding(nameof(TolerancingSensitivityRow.Perturbation)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "评价函数变化", Binding = new Binding(nameof(TolerancingSensitivityRow.DeltaMerit)), Width = new DataGridLength(140) });

        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "试验", Binding = new Binding(nameof(TolerancingTrialRow.Trial)), Width = new DataGridLength(80) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "评价函数", Binding = new Binding(nameof(TolerancingTrialRow.Merit)), Width = new DataGridLength(140) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "补偿后评价函数", Binding = new Binding(nameof(TolerancingTrialRow.CompensatedMerit)), Width = new DataGridLength(160) });
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
        var view = _connector.RunTolerancing(
            _surfacePicker.SelectedItem as OpticalSurface,
            DoubleValue(_radiusSigma, 0.1),
            DoubleValue(_thicknessSigma, 0.05),
            IntValue(_trials, 50),
            IntValue(_seed, 1234),
            IntValue(_compensationIterations, 20));
        _sensitivityGrid.ItemsSource = view.SensitivityRows;
        _monteCarloGrid.ItemsSource = view.TrialRows;
        _summary.Text = $"{view.Summary}    {view.Details}";
    }

    private static DataGrid CreateGrid()
    {
        return new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowBackground = Brushes.White,
            MinHeight = 260
        };
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(10, 0, 4, 0)
        };
    }

    private static double DoubleValue(NumericUpDown input, double fallback)
    {
        return input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;
    }

    private static int IntValue(NumericUpDown input, int fallback)
    {
        return input.Value.HasValue ? Decimal.ToInt32(input.Value.Value) : fallback;
    }
}
