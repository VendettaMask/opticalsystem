using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class TolerancingPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly ITolerancingService _tolerancing;
    private readonly IWorkspaceEventStream _events;
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly NumericUpDown _radiusSigma = Number(0.1m, 0, 1000, 0.1m, 100);
    private readonly NumericUpDown _thicknessSigma = Number(0.05m, 0, 1000, 0.1m, 100);
    private readonly NumericUpDown _trials = Number(50, 1, 10_000, 10, 92);
    private readonly NumericUpDown _seed = Number(1234, 1, 1_000_000, 1, 104);
    private readonly NumericUpDown _compensationIterations = Number(20, 0, 500, 5, 92);
    private readonly DataGrid _sensitivityGrid = CreateGrid();
    private readonly DataGrid _monteCarloGrid = CreateGrid();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
    private CancellationTokenSource? _runCancellation;
    private int _generation;
    private bool _disposed;

    public TolerancingPanel(
        IPrescriptionService prescription,
        ITolerancingService tolerancing,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _tolerancing = tolerancing;
        _events = events;
        ConfigureGrids();
        var runButton = new Button { Content = new LocalIconLabel("play", "运行公差"), MinWidth = 100 };
        runButton.Click += async (_, _) => await RunAsync();
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                Label("表面"), _surfacePicker,
                Label("半径 sigma"), _radiusSigma,
                Label("厚度 sigma"), _thicknessSigma,
                Label("次数"), _trials,
                Label("种子"), _seed,
                Label("补偿迭代"), _compensationIterations,
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
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_summary, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_summary);
        root.Children.Add(tabs);
        Content = root;
        _events.Changed += OnWorkspaceChanged;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _events.Changed -= OnWorkspaceChanged;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
    }

    private void ConfigureGrids()
    {
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "扰动", Binding = new Binding(nameof(TolerancingSensitivityRowDto.Perturbation)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "评价函数变化", Binding = new Binding(nameof(TolerancingSensitivityRowDto.DeltaMerit)), Width = new DataGridLength(140) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "试验", Binding = new Binding(nameof(TolerancingTrialRowDto.Trial)), Width = new DataGridLength(80) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "评价函数", Binding = new Binding(nameof(TolerancingTrialRowDto.Merit)), Width = new DataGridLength(140) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "补偿后评价函数", Binding = new Binding(nameof(TolerancingTrialRowDto.CompensatedMerit)), Width = new DataGridLength(160) });
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var selected = (_surfacePicker.SelectedItem as SurfaceEditorRow)?.Number;
        var surfaces = _prescription.GetSurfaces().Select(surface => new SurfaceEditorRow(surface)).ToArray();
        _surfacePicker.ItemsSource = surfaces;
        _surfacePicker.SelectedItem = surfaces.FirstOrDefault(surface => surface.Number == selected)
            ?? surfaces.ElementAtOrDefault(Math.Min(2, Math.Max(0, surfaces.Length - 1)));
    }

    private async Task RunAsync()
    {
        if (_surfacePicker.SelectedItem is not SurfaceEditorRow surface)
        {
            _summary.Text = "请先选择一个表面。";
            return;
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        _summary.Text = "正在运行公差分析…";
        try
        {
            var result = await _tolerancing.RunAsync(new TolerancingRequestDto(
                surface.Number,
                DoubleValue(_radiusSigma, 0.1),
                DoubleValue(_thicknessSigma, 0.05),
                IntValue(_trials, 50),
                IntValue(_seed, 1234),
                IntValue(_compensationIterations, 20)), cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _sensitivityGrid.ItemsSource = result.SensitivityRows;
            _monteCarloGrid.ItemsSource = result.TrialRows;
            _summary.Text = $"{result.Summary}    {result.Details}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _summary.Text = $"公差分析失败：{exception.Message}";
            }
        }
    }

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment, double width) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Width = width,
        ShowButtonSpinner = false
    };

    private static DataGrid CreateGrid() => new()
    {
        AutoGenerateColumns = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowBackground = Brushes.White,
        MinHeight = 260
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Avalonia.Thickness(10, 0, 4, 0)
    };

    private static double DoubleValue(NumericUpDown input, double fallback) => input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;

    private static int IntValue(NumericUpDown input, int fallback) => input.Value.HasValue ? Decimal.ToInt32(input.Value.Value) : fallback;
}
