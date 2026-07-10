using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Connectors;

namespace OptilandWorkbench.App.Panels;

public sealed class AnalysisPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly ComboBox _analysisPicker = new()
    {
        MinWidth = 220,
        SelectedIndex = 0
    };
    private readonly DataGrid _resultsGrid = new()
    {
        AutoGenerateColumns = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowBackground = Brushes.White,
        MinHeight = 220
    };
    private readonly TextBox _report = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 300
    };
    private AnalysisView? _currentView;

    public AnalysisPanel(OptilandConnector connector)
    {
        _connector = connector;
        _analysisPicker.ItemsSource = _connector.AnalysisDisplayNames;
        _resultsGrid.Columns.Add(new DataGridTextColumn { Header = "指标", Binding = new Binding(nameof(AnalysisRow.Metric)), Width = new DataGridLength(180) });
        _resultsGrid.Columns.Add(new DataGridTextColumn { Header = "值", Binding = new Binding(nameof(AnalysisRow.Value)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var runButton = new Button
        {
            Content = "运行分析",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 120
        };
        runButton.Click += (_, _) => Refresh();

        var copyButton = new Button
        {
            Content = "复制报告",
            MinWidth = 100
        };
        copyButton.Click += async (_, _) => await CopyReportAsync();

        var exportButton = new Button
        {
            Content = "导出文本",
            MinWidth = 100
        };
        exportButton.Click += async (_, _) => await ExportReportAsync();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                _analysisPicker,
                runButton,
                copyButton,
                exportButton
            }
        };

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "结果表", Content = _resultsGrid },
                new TabItem { Header = "报告文本", Content = _report }
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(tabs);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        _analysisPicker.ItemsSource = _connector.AnalysisDisplayNames;
        if (_analysisPicker.SelectedItem is null && _connector.AnalysisDisplayNames.Count > 0)
        {
            _analysisPicker.SelectedIndex = 0;
        }

        var name = _analysisPicker.SelectedItem as string ?? _connector.AnalysisDisplayNames.FirstOrDefault() ?? "处方报告";
        _currentView = _connector.BuildAnalysisView(name);
        _resultsGrid.ItemsSource = _currentView.Rows;
        _report.Text = _currentView.ReportText;
    }

    private async Task CopyReportAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrWhiteSpace(_currentView?.ReportText))
        {
            return;
        }

        await clipboard.SetTextAsync(_currentView.ReportText);
    }

    private async Task ExportReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentView?.ReportText))
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出分析报告",
            SuggestedFileName = $"{_currentView.Name}.txt",
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
            await File.WriteAllTextAsync(file.Path.LocalPath, _currentView.ReportText);
        }
    }
}
