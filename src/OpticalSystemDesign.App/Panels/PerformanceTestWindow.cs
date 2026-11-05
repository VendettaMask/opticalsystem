using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

internal sealed class PerformanceTestWindow : Window
{
    private readonly IPerformanceTestService _service;
    private readonly Button _start = new() { Content = UiLocalization.Text("开始检验") };
    private readonly OperationStatusBar _status = new() { IsVisible = false };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? _run;
    private bool _closed;

    public PerformanceTestWindow(IPerformanceTestService service)
    {
        _service = service;
        Title = UiLocalization.Text("性能检验");
        Width = 640;
        Height = 500;
        MinWidth = 410;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _start.Classes.Add("accent");
        var close = new Button { Content = UiLocalization.Text("关闭") };
        _start.Click += async (_, _) => await RunAsync();
        close.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _closed = true;
            _run?.Cancel();
            _status.Dispose();
        };

        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock
                {
                    Text = UiLocalization.Text("性能检验"),
                    FontSize = DisplayTypography.PageTitle,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = UiLocalization.Text("使用当前顺序镜头测量 CPU 追迹吞吐和系统更新速度。"),
                    TextWrapping = TextWrapping.Wrap
                },
                _status
            }
        };
        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8,
            Children = { close, _start }
        };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(commands, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(commands);
        root.Children.Add(_result);
        Content = root;
        ThemeChrome.ApplyDialogDecoration(this);
    }

    private async Task RunAsync()
    {
        if (_run is not null) return;
        using var cancellation = new CancellationTokenSource();
        _run = cancellation;
        _start.IsEnabled = false;
        _result.Text = string.Empty;
        _status.IsVisible = true;
        _status.Start(UiLocalization.Text("正在检验性能…"), cancellation.Cancel);
        try
        {
            var result = await _service.RunAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed) return;
            _result.Text = FormatResult(result);
            _status.MarkSynced(UiLocalization.Text("检验完成。读数为三轮的中位数。"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_closed) _status.MarkIdle(UiLocalization.Text("性能检验已取消。"));
        }
        catch (Exception error)
        {
            if (!_closed) _status.MarkFailed(UiLocalization.Format("性能检验失败：{0}", error.Message));
        }
        finally
        {
            _run = null;
            if (!_closed) _start.IsEnabled = true;
        }
    }

    private static string FormatResult(PerformanceTestResult result)
    {
        var lines = new List<string>
        {
            result.LensName,
            UiLocalization.Format("配置 {0} · {1} 个追迹面 · 视场 {2} · {3:G6} µm",
                result.ConfigurationNumber, result.SurfaceCount, result.FieldNumber, result.WavelengthMicrometers),
            UiLocalization.Format("CPU · {0} 个逻辑处理器 · {1} · 软件版本 {2}", result.LogicalProcessors,
                RuntimeInformation.ProcessArchitecture, ProductVersionInfo.Current),
            string.Empty,
            UiLocalization.Format("追迹光线/秒    {0:N0}", Median(result.TraceTrials.Select(trial => trial.RaysPerSecond))),
            UiLocalization.Format("处理面次/秒    {0:N0}", Median(result.TraceTrials.Select(trial => trial.SurfaceVisitsPerSecond))),
            UiLocalization.Format("系统更新/秒    {0:N1}", Median(result.UpdateTrials.Select(trial => trial.UpdatesPerSecond))),
            string.Empty
        };
        for (var i = 0; i < result.TraceTrials.Count; i++)
        {
            var trace = result.TraceTrials[i];
            var update = result.UpdateTrials[i];
            lines.Add(UiLocalization.Format("第 {0} 轮 · 到达末面 {1:N0}/{2:N0} · 处理 {3:N0} 面次 · 追迹 {4:F3} s · 更新 {5} 次 / {6:F3} s",
                i + 1, trace.ImageArrivals, trace.Rays, trace.SurfaceVisits, trace.Seconds, update.Updates, update.Seconds));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }
}
