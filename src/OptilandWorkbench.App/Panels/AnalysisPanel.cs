using System.Globalization;
using System.Collections.ObjectModel;
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
    private readonly Dictionary<string, Control> _parameterControls = new();
    private readonly TabControl _pageTabs;
    private bool _syncingSelection;

    public AnalysisPanel(OptilandConnector connector, AppSettings settings)
    {
        _connector = connector;
        _settings = settings;
        _analysisPicker.ItemsSource = _connector.AnalysisDisplayNames;
        _pageTabs = new TabControl { ItemsSource = _pages };

        var runButton = Button("运行", () => RunSelected(createPage: false), 82);
        var newPageButton = Button("新分析页", () => RunSelected(createPage: true), 96);
        var cloneButton = Button("复制页", CloneSelectedPage, 82);
        var closeButton = Button("关闭页", CloseSelectedPage, 82);
        var copyButton = Button("复制报告", CopyReportAsync, 96);
        var exportButton = Button("导出文本", ExportReportAsync, 96);
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                _analysisPicker,
                _parameterPanel,
                runButton,
                newPageButton,
                cloneButton,
                closeButton,
                copyButton,
                exportButton
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_pageTabs);
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
        RebuildParameterPanel();
        RunSelected(createPage: true);
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

    private void CloneSelectedPage()
    {
        if (_pageTabs.SelectedItem is not TabItem selected || !_views.TryGetValue(selected, out var view))
        {
            return;
        }

        var state = PageState(selected);
        var clone = new TabItem { Tag = state with { Settings = new Dictionary<string, string>(state.Settings) } };
        _pages.Add(clone);
        SetPageView(clone, view);
        _pageTabs.SelectedItem = clone;
        RenumberPages();
    }

    private void CloseSelectedPage()
    {
        if (_pages.Count <= 1 || _pageTabs.SelectedItem is not TabItem selected)
        {
            return;
        }

        var index = _pages.IndexOf(selected);
        _views.Remove(selected);
        _pages.Remove(selected);
        _pageTabs.SelectedIndex = Math.Clamp(index, 0, _pages.Count - 1);
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
            ItemsSource = new object[]
            {
                new TabItem { Header = "图形", Content = plotRoot },
                new TabItem { Header = "结果表", Content = resultsGrid },
                new TabItem { Header = "报告文本", Content = report }
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
            _pages[index].Header = $"{index + 1}. {viewName}";
        }
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

    private sealed record AnalysisPageState(string Name, Dictionary<string, string> Settings);
}
