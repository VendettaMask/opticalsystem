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
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly ComboBox _analysisPicker = new() { MinWidth = 220, SelectedIndex = 0 };
    private readonly ObservableCollection<TabItem> _pages = new();
    private readonly Dictionary<TabItem, AnalysisView> _views = new();
    private readonly TabControl _pageTabs;

    public AnalysisPanel(OptilandConnector connector)
    {
        _connector = connector;
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

        _pageTabs.SelectionChanged += (_, _) => SyncPickerToSelectedPage();
        _connector.OpticLoaded += (_, _) => RefreshPages();
        _connector.OpticChanged += (_, _) => RefreshPages();
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
        var view = _connector.BuildAnalysisView(name);
        if (createPage || _pageTabs.SelectedItem is not TabItem selected)
        {
            var page = new TabItem { Tag = name };
            _pages.Add(page);
            SetPageView(page, view);
            _pageTabs.SelectedItem = page;
        }
        else
        {
            selected.Tag = name;
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

        var clone = new TabItem { Tag = selected.Tag };
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
            var name = page.Tag as string ?? _connector.AnalysisDisplayNames.FirstOrDefault() ?? "处方报告";
            SetPageView(page, _connector.BuildAnalysisView(name));
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
        if (_pageTabs.SelectedItem is TabItem page && page.Tag is string analysisName)
        {
            _analysisPicker.SelectedItem = analysisName;
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
}
