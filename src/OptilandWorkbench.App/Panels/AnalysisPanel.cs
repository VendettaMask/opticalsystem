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
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private readonly IAnalysisService _analyses;
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
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 115))
    };
    private readonly Button _syncButton;
    private readonly DispatcherTimer _automaticRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private CancellationTokenSource? _runCancellation;
    private AnalysisViewDto? _view;
    private Dictionary<string, string> _settings;
    private int _generation;
    private bool _locked;
    private bool _disposed;

    public AnalysisPanel(
        IAnalysisService analyses,
        IOpticalDocumentService documents,
        IWorkspaceEventStream events,
        AppSettings appSettings,
        string analysisName,
        Guid? instanceId = null,
        IReadOnlyDictionary<string, string>? initialSettings = null)
    {
        _analyses = analyses;
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

        var settingsArrow = new LocalIcon
        {
            IconName = "chevron-right",
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        var settingsButton = new Button
        {
            MinWidth = 84,
            Height = 30,
            Padding = new Thickness(8, 2),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    settingsArrow,
                    new TextBlock { Text = "设置", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        var settingsHost = new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(12, 10),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #16000000"),
            Child = _parameterPanel
        };
        settingsButton.Click += (_, _) =>
        {
            settingsHost.IsVisible = !settingsHost.IsVisible;
            settingsArrow.IconName = settingsHost.IsVisible ? "chevron-down" : "chevron-right";
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
        _syncButton.Margin = new Thickness(6, 0, 0, 0);
        copyButton.Margin = new Thickness(6, 0, 0, 0);
        exportButton.Margin = new Thickness(6, 0, 8, 0);

        var toolbar = new StackPanel
        {
            Margin = new Thickness(12, 10, 12, 8),
            Children = { commands, settingsHost }
        };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_resultHost);
        Content = root;

        RebuildParameterPanel();
        _events.Changed += OnWorkspaceChanged;
        _ = RunAsync();
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
        _syncButton.IsEnabled = false;
        _stateText.Text = "正在计算…";

        try
        {
            var result = await _analyses.RunAsync(
                new AnalysisRequestDto(InstanceId, generation, AnalysisKey, _settings),
                cancellationToken);
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
                    DateTimeOffset.Now);
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
                _stateText.Text = $"分析失败：{exception.Message}");
        }
        finally
        {
            if (!_disposed && generation == _generation)
            {
                await Dispatcher.UIThread.InvokeAsync(() => _syncButton.IsEnabled = true);
            }
        }
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

    private void RebuildParameterPanel()
    {
        _parameterPanel.Children.Clear();
        _parameterControls.Clear();
        foreach (var descriptor in _analyses.GetParameters(AnalysisName))
        {
            _parameterPanel.Children.Add(Label(descriptor.DisplayName));
            var value = _settings.TryGetValue(descriptor.Key, out var saved)
                ? saved
                : descriptor.DefaultValue;
            var control = CreateParameterControl(descriptor, value);
            _parameterControls[descriptor.Key] = control;
            _parameterPanel.Children.Add(control);
        }
    }

    private Dictionary<string, string> CaptureParameterSettings()
    {
        var settings = _analyses.MergeSettings(AnalysisName, null);
        foreach (var descriptor in _analyses.GetParameters(AnalysisName))
        {
            if (!_parameterControls.TryGetValue(descriptor.Key, out var control))
            {
                continue;
            }

            settings[descriptor.Key] = control switch
            {
                NumericUpDown numeric when numeric.Value.HasValue =>
                    numeric.Value.Value.ToString(CultureInfo.InvariantCulture),
                ComboBox combo when combo.SelectedItem is string selected => selected,
                CheckBox check => (check.IsChecked == true).ToString(CultureInfo.InvariantCulture),
                _ => settings[descriptor.Key]
            };
        }

        return settings;
    }

    private IReadOnlyDictionary<string, string>? SavedAnalysisSettings()
    {
        return _appSettings.AnalysisSettings.TryGetValue(AnalysisKey, out var settings)
            ? settings
            : null;
    }

    private void SaveAnalysisSettings()
    {
        if (_settings.Count == 0)
        {
            _appSettings.AnalysisSettings.Remove(AnalysisKey);
        }
        else
        {
            _appSettings.AnalysisSettings[AnalysisKey] = new Dictionary<string, string>(_settings);
        }

        _appSettings.Save();
    }

    private static Control CreateParameterControl(AnalysisParameterDescriptor descriptor, string value)
    {
        return descriptor.Kind switch
        {
            AnalysisParameterKind.Choice => ChoiceInput(descriptor, value),
            AnalysisParameterKind.Boolean => BooleanInput(value),
            _ => NumericInput(descriptor, value)
        };
    }

    private static NumericUpDown NumericInput(AnalysisParameterDescriptor descriptor, string value)
    {
        var input = new NumericUpDown
        {
            Minimum = (decimal)descriptor.Minimum,
            Maximum = (decimal)descriptor.Maximum,
            Increment = (decimal)descriptor.Increment,
            Width = descriptor.Kind == AnalysisParameterKind.Double ? 108 : 92,
            ShowButtonSpinner = false
        };
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            input.Value = Math.Clamp(parsed, input.Minimum, input.Maximum);
        }
        else if (decimal.TryParse(descriptor.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var fallback))
        {
            input.Value = Math.Clamp(fallback, input.Minimum, input.Maximum);
        }

        return input;
    }

    private static ComboBox ChoiceInput(AnalysisParameterDescriptor descriptor, string value)
    {
        var choices = descriptor.Choices?.ToArray() ?? Array.Empty<string>();
        return new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.Contains(value) ? value : descriptor.DefaultValue,
            MinWidth = 104
        };
    }

    private static CheckBox BooleanInput(string value) => new()
    {
        IsChecked = bool.TryParse(value, out var flag) && flag,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Control BuildResultContent(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt)
    {
        var plotRoot = view.PlotPanes.Count > 0
            ? BuildPanePlot(view.PlotPanes, view.PlotPaneColumns)
            : BuildSinglePlot(view);
        var plotPage = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        plotPage.Children.Add(plotRoot);
        var titleBlock = BuildAnalysisTitleBlock(view, document, generatedAt);
        Grid.SetRow(titleBlock, 1);
        plotPage.Children.Add(titleBlock);
        var resultsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowBackground = Brushes.White,
            MinHeight = 220,
            ItemsSource = view.Rows
        };
        resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "指标",
            Binding = new Binding(nameof(AnalysisRowDto.Metric)),
            Width = new DataGridLength(180)
        });
        resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "值",
            Binding = new Binding(nameof(AnalysisRowDto.Value)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        var report = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 300,
            Text = view.ReportText
        };
        return new TabControl
        {
            TabStripPlacement = Avalonia.Controls.Dock.Bottom,
            ItemsSource = new object[]
            {
                new TabItem { Header = "绘图", Content = plotPage },
                new TabItem { Header = "数据", Content = resultsGrid },
                new TabItem { Header = "文本", Content = report }
            }
        };
    }

    private static Control BuildAnalysisTitleBlock(
        AnalysisViewDto view,
        OpticalDocumentSnapshot document,
        DateTimeOffset generatedAt)
    {
        var compactSummary = BuildCompactAnalysisSummary(view);
        var hasPaneMetrics = view.PlotPanes.Any(pane => pane.Metrics is { Count: > 0 });
        var visibleRows = hasPaneMetrics || compactSummary is not null
            ? Array.Empty<AnalysisRowDto>()
            : view.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Metric))
                .Take(7)
                .ToArray();
        var resultLines = compactSummary?.Lines ?? (visibleRows.Length == 0
            ? "暂无摘要数据"
            : string.Join(Environment.NewLine, visibleRows.Select(row => $"{row.Metric}: {row.Value}")));
        if (compactSummary is null && view.Rows.Count > visibleRows.Length)
        {
            resultLines += $"{Environment.NewLine}其余 {view.Rows.Count - visibleRows.Length} 项见“数据”页";
        }

        var documentName = string.IsNullOrWhiteSpace(document.Path)
            ? document.Name
            : Path.GetFileName(document.Path);
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "1.0.0";

        var left = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(16, 10, 16, 10)
        };
        left.Children.Add(new TextBlock
        {
            Text = compactSummary?.Title ?? view.Name,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        left.Children.Add(new TextBlock
        {
            Text = generatedAt.LocalDateTime.ToString(compactSummary is null ? "yyyy/MM/dd HH:mm:ss" : "yyyy/M/d"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87))
        });
        var resultSummary = new TextBlock
        {
            Text = resultLines,
            FontSize = 11,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap
        };
        var paneMetrics = BuildPaneMetricsSummary(view.PlotPanes);
        if (paneMetrics is null)
        {
            left.Children.Add(resultSummary);
        }
        else if (visibleRows.Length == 0)
        {
            left.Children.Add(paneMetrics);
        }
        else
        {
            var summaryBody = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };
            summaryBody.Children.Add(resultSummary);
            Grid.SetColumn(paneMetrics, 1);
            summaryBody.Children.Add(paneMetrics);
            left.Children.Add(summaryBody);
        }

        var product = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "S.T.A.R. Labs",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = $"Optical System Design  {version}",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        var documentInfo = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = documentName,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = document.Name,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        right.Children.Add(product);
        var documentBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = documentInfo
        };
        Grid.SetRow(documentBorder, 1);
        right.Children.Add(documentBorder);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,*"),
            MinHeight = 126
        };
        grid.Children.Add(left);
        var rightBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = right
        };
        Grid.SetColumn(rightBorder, 1);
        grid.Children.Add(rightBorder);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = grid
        };
    }

    private static CompactAnalysisSummary? BuildCompactAnalysisSummary(AnalysisViewDto view)
    {
        if (string.Equals(view.Name, "场曲", StringComparison.Ordinal)
            || string.Equals(view.Name, "Field Curvature", StringComparison.OrdinalIgnoreCase))
        {
            var curvatureMaximumField = FindMaximumFieldRow(view);
            var sagittalFieldCurvature = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大弧矢场曲", StringComparison.Ordinal));
            var tangentialFieldCurvature = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大子午场曲", StringComparison.Ordinal));
            var maximumImageDelta = view.Rows.FirstOrDefault(row =>
                row.Metric.Contains("最大像面偏移", StringComparison.Ordinal));
            var curvatureLines = new List<string>();
            AddMaximumFieldLine(curvatureLines, curvatureMaximumField);
            if (sagittalFieldCurvature is not null)
            {
                curvatureLines.Add($"弧矢场曲 = {sagittalFieldCurvature.Value} mm");
            }

            if (tangentialFieldCurvature is not null)
            {
                curvatureLines.Add($"子午场曲 = {tangentialFieldCurvature.Value} mm");
            }

            if (maximumImageDelta is not null)
            {
                curvatureLines.Add($"最大像面偏移 = {maximumImageDelta.Value} mm");
            }

            return new CompactAnalysisSummary(
                "场曲",
                curvatureLines.Count == 0 ? "暂无摘要数据" : string.Join(Environment.NewLine, curvatureLines));
        }

        if (!string.Equals(view.Name, "畸变", StringComparison.Ordinal)
            && !string.Equals(view.Name, "Distortion", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var model = view.Rows.FirstOrDefault(row => row.Metric.Contains("畸变模型", StringComparison.Ordinal))?.Value;
        var title = model?.ToLowerInvariant() switch
        {
            "f-tan" => "F-Tan(Theta) 畸变",
            "f-theta" => "F-Theta 畸变",
            _ => "畸变"
        };
        var maximumField = FindMaximumFieldRow(view);
        var maximumDistortion = view.Rows.FirstOrDefault(row =>
            row.Metric.Contains("最大绝对畸变", StringComparison.Ordinal));
        var distortionUnit = maximumDistortion?.Metric.Contains("mm", StringComparison.OrdinalIgnoreCase) == true
            ? " mm"
            : "%";
        var lines = new List<string>();
        AddMaximumFieldLine(lines, maximumField);

        if (maximumDistortion is not null)
        {
            lines.Add($"最大畸变 = {maximumDistortion.Value}{distortionUnit}");
        }

        return new CompactAnalysisSummary(title, lines.Count == 0 ? "暂无摘要数据" : string.Join(Environment.NewLine, lines));
    }

    private static AnalysisRowDto? FindMaximumFieldRow(AnalysisViewDto view)
    {
        return view.Rows.FirstOrDefault(row =>
            row.Metric.Contains("最大视场", StringComparison.Ordinal)
            || row.Metric.Contains("最大物高", StringComparison.Ordinal)
            || row.Metric.Contains("最大像高", StringComparison.Ordinal));
    }

    private static void AddMaximumFieldLine(ICollection<string> lines, AnalysisRowDto? maximumField)
    {
        if (maximumField is null)
        {
            return;
        }

        var fieldUnit = maximumField.Metric.Contains("deg", StringComparison.OrdinalIgnoreCase)
            ? " 度"
            : " mm";
        lines.Add($"最大视场 = {maximumField.Value}{fieldUnit}");
    }

    private sealed record CompactAnalysisSummary(string Title, string Lines);

    private static Control BuildSinglePlot(AnalysisViewDto view)
    {
        return new Grid
        {
            Children =
            {
                new AnalysisPlotControl
                {
                    Series = view.Series,
                    PlotOptions = view.PlotOptions,
                    MinHeight = 360
                },
                new TextBlock
                {
                    Text = "当前分析没有可绘制的数值序列",
                    IsVisible = view.Series.Count == 0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray
                }
            }
        };
    }

    private static Control BuildPanePlot(IReadOnlyList<AnalysisPlotPaneDto> panes, int requestedColumns)
    {
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var rows = (int)Math.Ceiling(panes.Count / (double)columns);
        var paneGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("*", rows))),
            MinWidth = columns * 300,
            MinHeight = rows * 300
        };
        for (var index = 0; index < panes.Count; index++)
        {
            var pane = panes[index];
            var plot = new AnalysisPlotControl
            {
                Series = pane.Series,
                PlotOptions = pane.PlotOptions,
                MinWidth = 300,
                MinHeight = 300
            };
            Grid.SetColumn(plot, index % columns);
            Grid.SetRow(plot, index / columns);
            paneGrid.Children.Add(plot);
        }

        var legend = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 4, 12, 12)
        };
        foreach (var series in panes[0].Series.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            legend.Children.Add(new TextBlock
            {
                Text = $"●  {series.Name}",
                Foreground = SeriesBrush(series.ColorIndex),
                Margin = new Thickness(10, 2)
            });
        }

        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(paneGrid);
        content.Children.Add(legend);
        Grid.SetRow(legend, 1);
        return new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static Control? BuildPaneMetricsSummary(IReadOnlyList<AnalysisPlotPaneDto> panes)
    {
        var populated = panes
            .Where(pane => pane.Metrics is { Count: > 0 })
            .ToArray();
        if (populated.Length == 0)
        {
            return null;
        }

        var labels = populated
            .SelectMany(pane => pane.Metrics!)
            .Select(metric => metric.Label)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var grid = new Grid
        {
            Margin = new Thickness(28, 0, 0, 0),
            ColumnDefinitions = new ColumnDefinitions(
                "Auto," + string.Join(',', Enumerable.Repeat("Auto", populated.Length))),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("Auto", labels.Length + 1)))
        };

        for (var paneIndex = 0; paneIndex < populated.Length; paneIndex++)
        {
            var header = new TextBlock
            {
                Text = $"视场 {paneIndex + 1}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(12, 0, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(header, paneIndex + 1);
            grid.Children.Add(header);
        }

        for (var rowIndex = 0; rowIndex < labels.Length; rowIndex++)
        {
            var label = new TextBlock
            {
                Text = labels[rowIndex] + "：",
                FontSize = 11,
                Margin = new Thickness(0, 1, 6, 1)
            };
            Grid.SetRow(label, rowIndex + 1);
            grid.Children.Add(label);
            for (var paneIndex = 0; paneIndex < populated.Length; paneIndex++)
            {
                var metric = populated[paneIndex].Metrics!
                    .FirstOrDefault(item => item.Label == labels[rowIndex]);
                if (metric is null)
                {
                    continue;
                }

                var value = new TextBlock
                {
                    Text = $"{NumericDisplayFormatter.Format(metric.Value)} {metric.Unit}".TrimEnd(),
                    FontSize = 11,
                    Margin = new Thickness(12, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(value, paneIndex + 1);
                Grid.SetRow(value, rowIndex + 1);
                grid.Children.Add(value);
            }
        }

        var reference = populated
            .Select(pane => pane.Footer)
            .FirstOrDefault(footer => !string.IsNullOrWhiteSpace(footer));
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = reference ?? string.Empty,
                    IsVisible = !string.IsNullOrWhiteSpace(reference),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 87))
                }
            }
        };
    }

    private static IBrush SeriesBrush(int colorIndex)
    {
        var colors = new[]
        {
            Color.FromRgb(31, 119, 180),
            Color.FromRgb(255, 127, 14),
            Color.FromRgb(44, 160, 44)
        };
        return new SolidColorBrush(colors[Math.Abs(colorIndex) % colors.Length]);
    }

    private async Task CopyReportAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrWhiteSpace(_view?.ReportText))
            {
                await clipboard.SetTextAsync(_view.ReportText);
                _stateText.Text = "报告文本已复制";
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _stateText.Text = $"复制失败：{exception.Message}";
            }
        }
    }

    private async Task ExportReportAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (_view is null || topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出分析报告",
                SuggestedFileName = $"{_view.Name}.txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("文本报告")
                    {
                        Patterns = new[] { "*.txt" },
                        MimeTypes = new[] { "text/plain" }
                    }
                }
            });
            if (file is not null)
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, _view.ReportText);
                _stateText.Text = "报告文本已导出";
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                _stateText.Text = $"导出失败：{exception.Message}";
            }
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 4, 0)
    };

    private static Button CommandButton(string iconName, string text, double minWidth) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = minWidth
    };

    private static Button IconButton(string iconName, string tooltip)
    {
        var button = new Button
        {
            Content = new LocalIcon { IconName = iconName, Width = 18, Height = 18 },
            Width = 34,
            Height = 30,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }
}
