using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IOptimizationService _optimization;
    private readonly IWorkspaceEventStream _events;
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly ComboBox _optimizerPicker = new() { MinWidth = 180, SelectedIndex = 0 };
    private readonly NumericUpDown _iterationsInput = new()
    {
        Minimum = 1,
        Maximum = 1000,
        Increment = 10,
        Value = 80,
        Width = 100,
        ShowButtonSpinner = false
    };
    private readonly TextBlock _result = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 12, 0, 0)
    };
    private CancellationTokenSource? _runCancellation;
    private int _generation;
    private bool _disposed;

    public OptimizationPanel(
        IPrescriptionService prescription,
        IOptimizationService optimization,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _optimization = optimization;
        _events = events;
        _optimizerPicker.ItemsSource = optimization.OptimizerNames;
        var runButton = new Button { Content = new LocalIconLabel("play", "运行"), MinWidth = 86 };
        runButton.Click += async (_, _) => await RunAsync();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _surfacePicker, _optimizerPicker, _iterationsInput, runButton }
        };
        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "半径变量优化", FontWeight = FontWeight.SemiBold },
                row,
                _result
            }
        };
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

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });
    }

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
        if (_optimizerPicker.SelectedItem is null && _optimization.OptimizerNames.Count > 0)
        {
            _optimizerPicker.SelectedIndex = 0;
        }
    }

    private async Task RunAsync()
    {
        if (_surfacePicker.SelectedItem is not SurfaceEditorRow surface)
        {
            _result.Text = "请先选择一个表面。";
            return;
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        var optimizer = _optimizerPicker.SelectedItem as string
            ?? _optimization.OptimizerNames.FirstOrDefault()
            ?? "Orthogonal Descent";
        var iterations = _iterationsInput.Value.HasValue
            ? Decimal.ToInt32(_iterationsInput.Value.Value)
            : 80;
        _result.Text = "正在优化…";
        try
        {
            var result = await _optimization.OptimizeSurfaceRadiusAsync(
                surface.Number,
                optimizer,
                iterations,
                cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _result.Text =
                $"{result.Message}{Environment.NewLine}" +
                $"评价函数: {result.Merit:0.######}{Environment.NewLine}" +
                $"半径: {result.InitialRadius:0.###} -> {result.FinalRadius:0.###}{Environment.NewLine}" +
                $"迭代次数: {result.Iterations}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _result.Text = $"优化失败：{exception.Message}";
            }
        }
    }
}
