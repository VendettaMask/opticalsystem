using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class AnalysisPanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private readonly IAnalysisService _analyses;
    private readonly IVisualizationService _visualization;
    private readonly IOpticalDocumentService _documents;
    private readonly IWorkspaceEventStream _events;
    private readonly AppSettings _appSettings;
    private readonly Dictionary<string, Control> _parameterControls = new();
    private readonly WrapPanel _parameterPanel = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ContentControl _resultHost = new();
    private readonly TextBlock _stateText = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _syncButton;
    private readonly Border _settingsHost;
    private readonly CheckBox _parameterAutoApply = new()
    {
        Content = "自动应用",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DispatcherTimer _automaticRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private CancellationTokenSource? _runCancellation;
    private AnalysisViewDto? _view;
    private Dictionary<string, string> _settings;
    private int _generation;
    private bool _locked;
    private bool _isRunning;
    private bool _initialRunRequested;
    private bool _disposed;

    public AnalysisPanel(
        IAnalysisService analyses,
        IVisualizationService visualization,
        IOpticalDocumentService documents,
        IWorkspaceEventStream events,
        AppSettings appSettings,
        string analysisName,
        Guid? instanceId = null,
        IReadOnlyDictionary<string, string>? initialSettings = null)
    {
        _analyses = analyses;
        _visualization = visualization;
        _documents = documents;
        _events = events;
        _appSettings = appSettings;
        AnalysisName = analysisName;
        AnalysisKey = analyses.CanonicalKey(analysisName);
        InstanceId = instanceId ?? Guid.NewGuid();
        _settings = analyses.MergeSettings(
            analysisName,
            initialSettings ?? SavedAnalysisSettings());
        _automaticRefreshTimer.Tick += OnAutomaticRefreshTimerTick;

        _syncButton = IconButton("refresh-cw", "同步当前设置并重新运行");
        _syncButton.Click += async (_, _) => await RunAsync();
        var copyButton = CommandButton("clipboard-copy", "复制文本", 96);
        copyButton.Click += async (_, _) => await CopyReportAsync();
        var exportButton = CommandButton("upload", "导出文本", 96);
        exportButton.Click += async (_, _) => await ExportReportAsync();

        var settingsIcon = new LocalIcon
        {
            IconName = "circle-chevron-down",
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        var settingsButton = new Button
        {
            MinWidth = 72,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    settingsIcon,
                    new TextBlock { Text = "设置", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        _settingsHost = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #16000000"),
            Child = _parameterPanel
        };
        _stateText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _settingsHost.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        _settingsHost.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        StyleToolbarButton(settingsButton, iconOnly: false);
        settingsButton.Click += (_, _) =>
        {
            _settingsHost.IsVisible = !_settingsHost.IsVisible;
            settingsIcon.IconName = _settingsHost.IsVisible
                ? "circle-chevron-up"
                : "circle-chevron-down";
        };

        var commands = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                settingsButton,
                _syncButton,
                copyButton,
                exportButton,
                _stateText
            }
        };
        _syncButton.Margin = new Thickness(2, 0, 0, 0);
        copyButton.Margin = new Thickness(2, 0, 0, 0);
        exportButton.Margin = new Thickness(2, 0, 8, 0);

        var toolbar = new StackPanel
        {
            Margin = new Thickness(12, 10, 12, 8),
            Children = { commands, _settingsHost }
        };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_resultHost);
        Content = root;

        RebuildParameterPanel();
        _events.Changed += OnWorkspaceChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public Guid InstanceId { get; }

    public string AnalysisName { get; }

    public string AnalysisKey { get; }

    public IReadOnlyDictionary<string, string> Settings => _settings;

    public bool IsLocked
    {
        get => _locked;
        set
        {
            _locked = value;
            if (_isRunning)
            {
                return;
            }

            _stateText.Text = value
                ? "已锁定：保留当前结果"
                : _view is null ? "尚未运行" : "";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _automaticRefreshTimer.Stop();
        _automaticRefreshTimer.Tick -= OnAutomaticRefreshTimerTick;
        _events.Changed -= OnWorkspaceChanged;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
    }

    public void RefreshDisplaySettings()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        if (_disposed)
        {
            return;
        }

        _settings = CaptureParameterSettings();
        SaveAnalysisSettings();
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        _isRunning = true;
        _syncButton.IsEnabled = true;
        _stateText.Text = "正在计算…";

        try
        {
            var result = await _analyses.RunAsync(
                new AnalysisRequestDto(InstanceId, generation, AnalysisKey, _settings),
                cancellationToken);
            var cardinalScene = IsCardinalPointsView(result.View)
                ? await _visualization.BuildSceneAsync(
                    new VisualizationRequestDto(
                        SceneDimension.TwoDimensional,
                        IncludeAllWavelengths: false,
                        RayCount: 3,
                        LowerPupil: -1,
                        UpperPupil: 1,
                        MarginalAndChiefOnly: true),
                    cancellationToken)
                : null;
            if (_disposed
                || cancellationToken.IsCancellationRequested
                || result.InstanceId != InstanceId
                || result.Generation != _generation)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.SourceRevision != _events.Revision)
                {
                    _stateText.Text = "结果已过期，请同步";
                    return;
                }

                _view = result.View;
                _resultHost.Content = BuildResultContent(
                    result.View,
                    _documents.GetSnapshot(),
                    DateTimeOffset.Now,
                    cardinalScene);
                _stateText.Text = _locked ? "已锁定：保留当前结果" : "已同步";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _view = null;
                _resultHost.Content = BuildAnalysisErrorContent(exception.Message);
                _stateText.Text = $"分析失败：{exception.Message}";
            });
        }
        finally
        {
            if (!_disposed && generation == _generation)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _isRunning = false;
                    _syncButton.IsEnabled = true;
                });
            }
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_initialRunRequested || _disposed)
        {
            return;
        }

        _initialRunRequested = true;
        _ = RunAsync();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (args.FileSwitched)
        {
            _runCancellation?.Cancel();
        }

        if (_locked)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (args.Category is WorkspaceChangeCategory.SystemSettings
                or WorkspaceChangeCategory.Field
                or WorkspaceChangeCategory.Wavelength)
            {
                _stateText.Text = "系统设置已变化，正在自动刷新…";
                _automaticRefreshTimer.Stop();
                _automaticRefreshTimer.Start();
            }
            else if (_view is not null)
            {
                _stateText.Text = "结果已过期，请同步";
            }
        });
    }

    private async void OnAutomaticRefreshTimerTick(object? sender, EventArgs args)
    {
        _automaticRefreshTimer.Stop();
        if (!_disposed && !_locked)
        {
            await RunAsync();
        }
    }

}
