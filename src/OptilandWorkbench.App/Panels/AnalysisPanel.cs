using System.Globalization;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly AppSettings _settings;
    private readonly ComboBox _analysisPicker = new() { MinWidth = 220, SelectedIndex = 0 };
    private readonly WrapPanel _parameterPanel = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ObservableCollection<TabItem> _pages = new();
    private readonly Dictionary<TabItem, AnalysisView> _views = new();
    private readonly Dictionary<TabItem, Window> _floatingWindows = new();
    private readonly Dictionary<string, Control> _parameterControls = new();
    private readonly TabControl _pageTabs;
    private readonly TextBlock _emptyState = new()
    {
        Text = "请从顶部“分析”分类中选择需要运行的分析。",
        FontSize = 18,
        Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 115)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _rerunButton;
    private bool _syncingSelection;
    private bool _floatingMode;
    private bool _dockingFloatingWindows;

    public AnalysisPanel(OptilandConnector connector, AppSettings settings)
    {
        _connector = connector;
        _settings = settings;
        _analysisPicker.ItemsSource = _connector.AnalysisDisplayNames;
        _pageTabs = new TabControl { ItemsSource = _pages };

        _rerunButton = IconButton("⟳", "同步当前设置并重新运行", () => RunSelected(createPage: false));
        _rerunButton.IsEnabled = false;
        var copyButton = Button("⧉  复制报告", CopyReportAsync, 104);
        var exportButton = Button("⇧  导出文本", ExportReportAsync, 104);
        var settingsArrow = new TextBlock
        {
            Text = "›",
            Width = 16,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        var settingsButton = new Button
        {
            MinWidth = 86,
            Height = 30,
            Padding = new Avalonia.Thickness(8, 2),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    settingsArrow,
                    new TextBlock
                    {
                        Text = "设置",
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        var settingsHost = new Border
        {
            IsVisible = false,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            Padding = new Avalonia.Thickness(12, 10),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            BoxShadow = BoxShadows.Parse("0 3 10 0 #16000000"),
            Child = _parameterPanel
        };
        settingsButton.Click += (_, _) =>
        {
            settingsHost.IsVisible = !settingsHost.IsVisible;
            settingsArrow.Text = settingsHost.IsVisible ? "⌄" : "›";
        };
        ToolTip.SetTip(settingsButton, "展开或收起当前分析的绘图设置");

        var commandRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                settingsButton,
                _rerunButton,
                copyButton,
                exportButton
            }
        };
        _rerunButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
        copyButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
        exportButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
        var toolbar = new StackPanel
        {
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { commandRow, settingsHost }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(new Grid
        {
            Children = { _pageTabs, _emptyState }
        });
        Content = root;

        _analysisPicker.SelectionChanged += (_, _) =>
        {
            if (!_syncingSelection)
            {
                RebuildParameterPanel();
            }
        };
        _pageTabs.SelectionChanged += (_, _) => SyncPickerToSelectedPage();
        _connector.OpticLoaded += (_, _) => RefreshPages();
        _connector.OpticChanged += (_, _) => RefreshPages();
    }

    public void OpenAnalysis(string analysisName)
    {
        var canonicalName = _connector.CanonicalAnalysisKey(analysisName);
        var displayName = _connector.AnalysisDisplayNames.FirstOrDefault(name =>
                _connector.CanonicalAnalysisKey(name).Equals(canonicalName, StringComparison.Ordinal))
            ?? analysisName;
        var existingPage = _pages.FirstOrDefault(page =>
            _connector.CanonicalAnalysisKey(PageState(page).Name).Equals(canonicalName, StringComparison.Ordinal));

        _syncingSelection = true;
        _analysisPicker.SelectedItem = displayName;
        _syncingSelection = false;
        _rerunButton.IsEnabled = true;

        if (existingPage is not null)
        {
            _pageTabs.SelectedItem = existingPage;
            RebuildParameterPanel(PageState(existingPage).Settings);
            RunSelected(createPage: false);
        }
        else
        {
            RebuildParameterPanel(SavedAnalysisSettings(displayName));
            RunSelected(createPage: true);
        }

        _emptyState.IsVisible = false;
        if (_floatingMode && _pageTabs.SelectedItem is TabItem selectedPage)
        {
            FloatPage(selectedPage);
        }
    }

    public void DockAllWindows()
    {
        _floatingMode = false;
        _dockingFloatingWindows = true;
        var windows = _floatingWindows.Values.ToArray();
        _floatingWindows.Clear();
        foreach (var window in windows)
        {
            window.Close();
        }

        _dockingFloatingWindows = false;
        _pageTabs.IsVisible = true;
        _emptyState.Text = "请从顶部“分析”分类中选择需要运行的分析。";
        _emptyState.IsVisible = _pages.Count == 0;
    }

    public void FloatAllWindows()
    {
        _floatingMode = true;
        foreach (var page in _pages.ToArray())
        {
            FloatPage(page);
        }

        _pageTabs.IsVisible = false;
        _emptyState.Text = "分析结果已浮动，可拖动和调整每个独立窗口。";
        _emptyState.IsVisible = true;
    }

    public void TileAllWindows()
    {
        FloatAllWindows();
        var windows = FloatingWindowsInPageOrder();
        if (windows.Count == 0)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var columns = (int)Math.Ceiling(Math.Sqrt(windows.Count));
        var rows = (int)Math.Ceiling(windows.Count / (double)columns);
        var availableWidth = Math.Max(900, owner?.Width ?? 1440);
        var availableHeight = Math.Max(620, owner?.Height ?? 900);
        var cellWidth = Math.Max(360, (availableWidth - 32) / columns);
        var cellHeight = Math.Max(280, (availableHeight - 56) / rows);
        var origin = owner?.Position ?? new PixelPoint(80, 80);

        for (var index = 0; index < windows.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var window = windows[index];
            window.Width = cellWidth;
            window.Height = cellHeight;
            window.Position = new PixelPoint(
                origin.X + 16 + (int)(column * cellWidth),
                origin.Y + 36 + (int)(row * cellHeight));
        }
    }

    public void CascadeAllWindows()
    {
        FloatAllWindows();
        var windows = FloatingWindowsInPageOrder();
        if (windows.Count == 0)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var origin = owner?.Position ?? new PixelPoint(80, 80);
        var width = Math.Max(680, (owner?.Width ?? 1200) * 0.72);
        var height = Math.Max(480, (owner?.Height ?? 800) * 0.72);
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            window.Width = width;
            window.Height = height;
            window.Position = new PixelPoint(origin.X + 32 + (index * 30), origin.Y + 48 + (index * 28));
            window.Activate();
        }
    }

    private void RunSelected(bool createPage)
    {
        _analysisPicker.ItemsSource = _connector.AnalysisDisplayNames;
        if (_analysisPicker.SelectedItem is null && _connector.AnalysisDisplayNames.Count > 0)
        {
            _analysisPicker.SelectedIndex = 0;
        }

        var name = _analysisPicker.SelectedItem as string
            ?? _connector.AnalysisDisplayNames.FirstOrDefault()
            ?? "处方报告";
        var settings = CaptureParameterSettings(name);
        SaveAnalysisSettings(name, settings);
        var view = _connector.BuildAnalysisView(name, settings);
        var state = new AnalysisPageState(name, settings);
        if (createPage || _pageTabs.SelectedItem is not TabItem selected)
        {
            var page = new TabItem { Tag = state };
            _pages.Add(page);
            SetPageView(page, view);
            _pageTabs.SelectedItem = page;
        }
        else
        {
            selected.Tag = state;
            SetPageView(selected, view);
        }

        RenumberPages();
    }

    private void ClosePage(TabItem page, bool closeFloatingWindow = true)
    {
        if (closeFloatingWindow && _floatingWindows.Remove(page, out var floatingWindow))
        {
            floatingWindow.Close();
        }

        var index = _pages.IndexOf(page);
        if (index < 0)
        {
            return;
        }

        _views.Remove(page);
        _pages.Remove(page);
        _pageTabs.SelectedIndex = _pages.Count == 0 ? -1 : Math.Clamp(index, 0, _pages.Count - 1);
        _emptyState.Text = "请从顶部“分析”分类中选择需要运行的分析。";
        _emptyState.IsVisible = _pages.Count == 0 || _floatingMode;
        RenumberPages();
    }

    private void RefreshPages()
    {
        foreach (var page in _pages.ToArray())
        {
            var state = PageState(page);
            SetPageView(page, _connector.BuildAnalysisView(state.Name, state.Settings));
        }

        RenumberPages();
    }

    private void SetPageView(TabItem page, AnalysisView view)
    {
        _views[page] = view;
        page.Content = BuildResultContent(view);
        if (_floatingWindows.TryGetValue(page, out var floatingWindow))
        {
            floatingWindow.Title = view.Name;
            floatingWindow.Content = BuildResultContent(view);
        }
    }

    private static Control BuildResultContent(AnalysisView view)
    {
        var plotRoot = view.PlotPanes.Count > 0
            ? BuildPanePlot(view.PlotPanes, view.PlotPaneColumns)
            : BuildSinglePlot(view);

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
            Binding = new Binding(nameof(AnalysisRow.Metric)),
            Width = new DataGridLength(180)
        });
        resultsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "值",
            Binding = new Binding(nameof(AnalysisRow.Value)),
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
            TabStripPlacement = Dock.Bottom,
            ItemsSource = new object[]
            {
                new TabItem { Header = "绘图", Content = plotRoot },
                new TabItem { Header = "数据", Content = resultsGrid },
                new TabItem { Header = "文本", Content = report }
            }
        };
    }

    private static Control BuildSinglePlot(AnalysisView view)
    {
        return new Grid
        {
            Children =
            {
                new AnalysisPlotControl
                {
                    Series = view.SeriesList,
                    PlotOptions = view.PlotOptions,
                    MinHeight = 360
                },
                new TextBlock
                {
                    Text = "当前分析没有可绘制的数值序列",
                    IsVisible = view.SeriesList.Count == 0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray
                }
            }
        };
    }

    private static Control BuildPanePlot(IReadOnlyList<AnalysisPlotPane> panes, int requestedColumns)
    {
        var columns = Math.Clamp(requestedColumns, 1, Math.Max(1, panes.Count));
        var paneGrid = new UniformGrid
        {
            Columns = columns,
            Rows = (int)Math.Ceiling(panes.Count / (double)columns)
        };
        foreach (var pane in panes)
        {
            paneGrid.Children.Add(new AnalysisPlotControl
            {
                Series = pane.Series,
                PlotOptions = pane.PlotOptions,
                MinWidth = 300,
                MinHeight = 300
            });
        }

        var legend = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 4, 12, 12)
        };
        foreach (var series in panes[0].Series.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            legend.Children.Add(new TextBlock
            {
                Text = $"\u25CF  {series.Name}",
                Foreground = SeriesBrush(series.ColorIndex),
                Margin = new Avalonia.Thickness(10, 2)
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

    private void SyncPickerToSelectedPage()
    {
        if (_pageTabs.SelectedItem is TabItem page)
        {
            var state = PageState(page);
            _syncingSelection = true;
            _analysisPicker.SelectedItem = state.Name;
            _syncingSelection = false;
            RebuildParameterPanel(state.Settings);
        }
    }

    private void RenumberPages()
    {
        for (var index = 0; index < _pages.Count; index++)
        {
            var viewName = _views.TryGetValue(_pages[index], out var view) ? view.Name : "分析";
            _pages[index].Header = BuildPageHeader(_pages[index], index + 1, viewName);
        }
    }

    private Control BuildPageHeader(TabItem page, int number, string viewName)
    {
        var closeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(5, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            FontSize = 14,
            CornerRadius = new Avalonia.CornerRadius(11)
        };
        closeButton.Click += (_, _) => ClosePage(page);
        ToolTip.SetTip(closeButton, "关闭分析");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = $"{number}. {viewName}",
                    VerticalAlignment = VerticalAlignment.Center
                },
                closeButton
            }
        };
    }

    private void FloatPage(TabItem page)
    {
        if (_floatingWindows.TryGetValue(page, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        if (!_views.TryGetValue(page, out var view))
        {
            return;
        }

        var window = new Window
        {
            Title = view.Name,
            Width = 920,
            Height = 680,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = BuildResultContent(view)
        };
        window.Closed += (_, _) =>
        {
            _floatingWindows.Remove(page);
            if (!_dockingFloatingWindows)
            {
                ClosePage(page, closeFloatingWindow: false);
            }
        };
        _floatingWindows[page] = window;
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    private IReadOnlyList<Window> FloatingWindowsInPageOrder()
    {
        return _pages
            .Where(page => _floatingWindows.ContainsKey(page))
            .Select(page => _floatingWindows[page])
            .ToArray();
    }

    private void RebuildParameterPanel(IReadOnlyDictionary<string, string>? pageSettings = null)
    {
        _parameterPanel.Children.Clear();
        _parameterControls.Clear();
        var name = _analysisPicker.SelectedItem as string
            ?? _connector.AnalysisDisplayNames.FirstOrDefault()
            ?? "处方报告";
        var settings = _connector.MergeAnalysisSettings(name, pageSettings ?? SavedAnalysisSettings(name));
        foreach (var descriptor in _connector.GetAnalysisParameters(name))
        {
            _parameterPanel.Children.Add(Label(descriptor.DisplayName));
            var value = settings.TryGetValue(descriptor.Key, out var saved)
                ? saved
                : descriptor.DefaultValue;
            var control = CreateParameterControl(descriptor, value);
            _parameterControls[descriptor.Key] = control;
            _parameterPanel.Children.Add(control);
        }
    }

    private Control CreateParameterControl(AnalysisParameterDescriptor descriptor, string value)
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
            Width = descriptor.Kind == AnalysisParameterKind.Double ? 108 : 92
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

    private static CheckBox BooleanInput(string value)
    {
        return new CheckBox
        {
            IsChecked = bool.TryParse(value, out var flag) ? flag : false,
            VerticalAlignment = VerticalAlignment.Center
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

    private Dictionary<string, string> CaptureParameterSettings(string name)
    {
        var settings = _connector.MergeAnalysisSettings(name, null);
        foreach (var descriptor in _connector.GetAnalysisParameters(name))
        {
            if (!_parameterControls.TryGetValue(descriptor.Key, out var control))
            {
                continue;
            }

            settings[descriptor.Key] = control switch
            {
                NumericUpDown numeric when numeric.Value.HasValue => numeric.Value.Value.ToString(CultureInfo.InvariantCulture),
                ComboBox combo when combo.SelectedItem is string selected => selected,
                CheckBox check => (check.IsChecked == true).ToString(CultureInfo.InvariantCulture),
                _ => settings[descriptor.Key]
            };
        }

        return settings;
    }

    private IReadOnlyDictionary<string, string>? SavedAnalysisSettings(string name)
    {
        var key = _connector.CanonicalAnalysisKey(name);
        return _settings.AnalysisSettings.TryGetValue(key, out var settings)
            ? settings
            : null;
    }

    private void SaveAnalysisSettings(string name, Dictionary<string, string> settings)
    {
        var key = _connector.CanonicalAnalysisKey(name);
        if (settings.Count == 0)
        {
            _settings.AnalysisSettings.Remove(key);
        }
        else
        {
            _settings.AnalysisSettings[key] = settings;
        }

        _settings.Save();
    }

    private AnalysisPageState PageState(TabItem page)
    {
        if (page.Tag is AnalysisPageState state)
        {
            return state;
        }

        var name = page.Tag as string
            ?? _connector.AnalysisDisplayNames.FirstOrDefault()
            ?? "处方报告";
        return new AnalysisPageState(name, _connector.MergeAnalysisSettings(name, SavedAnalysisSettings(name)));
    }

    private async Task CopyReportAsync()
    {
        var view = SelectedView();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && !string.IsNullOrWhiteSpace(view?.ReportText))
        {
            await clipboard.SetTextAsync(view.ReportText);
        }
    }

    private async Task ExportReportAsync()
    {
        var view = SelectedView();
        var topLevel = TopLevel.GetTopLevel(this);
        if (view is null || topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出分析报告",
            SuggestedFileName = $"{view.Name}.txt",
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
            await File.WriteAllTextAsync(file.Path.LocalPath, view.ReportText);
        }
    }

    private AnalysisView? SelectedView()
    {
        return _pageTabs.SelectedItem is TabItem page && _views.TryGetValue(page, out var view)
            ? view
            : null;
    }

    private static Button Button(string text, Action action, double minWidth)
    {
        var button = new Button { Content = text, MinWidth = minWidth };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Button(string text, Func<Task> action, double minWidth)
    {
        var button = new Button { Content = text, MinWidth = minWidth };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button IconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 34,
            Height = 30,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Avalonia.Thickness(0),
            FontSize = 17
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private sealed record AnalysisPageState(string Name, Dictionary<string, string> Settings);
}
