using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private DataGrid? _resultTable;
    private Expander? _resultExpander;
    private TextBlock? _resultHeading;
    private DropDownButton? _resultExport;

    private Control BuildDesignResultsContent()
    {
        var candidates = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        _candidates.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_candidates, "候选");
        candidates.Children.Add(_candidates);
        var apply = ActionButton("采用候选", SelectCandidateAsync);
        Grid.SetColumn(apply, 1); candidates.Children.Add(apply);
        var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 10, Margin = new Thickness(16) };
        content.Children.Add(LaboratoryPresentation.Heading("候选", "design-candidates"));
        Put(content, candidates, 1);
        Put(content, LaboratoryPresentation.Heading("指标与公差", "coating-tolerance-metrics"), 2);
        Put(content, new ScrollViewer
        {
            Name = "CoatingResultDetails",
            Content = _summary,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, 3);
        return content;
    }

    private Task ShowDesignResultsAsync()
    {
        _workspace.Open(CoatingDockWorkspace.ResultsId, "镀膜设计结果", _designResultsContent);
        return Task.CompletedTask;
    }

    private Control BuildResultsPanel()
    {
        _resultTable = new DataGrid
        {
            Name = "CoatingSpectrumResults",
            AutoGenerateColumns = false,
            IsReadOnly = true,
            MaxHeight = 220,
            MinHeight = 100,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        _resultHeading = new TextBlock { Text = "结果 · 0 行", VerticalAlignment = VerticalAlignment.Center };
        var disclosure = new OpticalSystemDesign.App.Controls.LocalIcon { IconName = "disclosure-closed", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        disclosure.BindThemeResource(OpticalSystemDesign.App.Controls.LocalIcon.StrokeProperty, ThemeResourceBindings.TextSecondary);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { disclosure, _resultHeading } };
        _resultExpander = new Expander
        {
            Header = heading,
            Content = _resultTable,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _resultExpander.Expanding += (_, _) => RefreshResultTable();
        _resultExpander.Expanded += (_, _) => disclosure.IconName = "disclosure-expand";
        _resultExpander.Collapsed += (_, _) => disclosure.IconName = "disclosure-closed";
        var menu = new MenuFlyout();
        foreach (var (label, delimiter) in new[] { ("CSV", ','), ("TSV", '\t') })
        {
            var item = new MenuItem { Header = label }; AutomationProperties.SetName(item, "导出光谱 " + label);
            item.Click += (_, _) => TrackOperation(() => ExportResultDataAsync(delimiter)); menu.Items.Add(item);
        }
        var copy = new MenuItem { Header = UiLocalization.Text("复制全部") };
        copy.Click += (_, _) => TrackOperation(async () =>
        {
            var text = _plot.ResultsSnapshot().Delimited('\t');
            if (Clipboard is not { } clipboard) throw new InvalidOperationException("剪贴板不可用。");
            await clipboard.SetTextAsync(text);
        }); menu.Items.Add(copy);
        _resultExport = new DropDownButton
        {
            Content = ToolContent("file-export", "导出"),
            Flyout = menu,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 6, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(_resultExport, "导出评估结果");
        var host = new Grid(); host.Children.Add(_resultExpander); host.Children.Add(_resultExport);
        _plot.StateChanged += (_, _) =>
        {
            _resultHeading.Text = UiLocalization.Text("结果") + $" · {_plot.SampleCount:N0} " + UiLocalization.Text("行");
            _resultExport.IsEnabled = _plot.HasData;
            ToolTip.SetTip(_resultExport, _plot.HasData ? "导出当前选择列的全部实算采样，保留完整精度" : "请先计算评估光谱");
            if (_resultExpander.IsExpanded) RefreshResultTable();
        };
        _resultExport.IsEnabled = false;
        return host;
    }
    private void RefreshResultTable()
    {
        if (_resultTable is null) return;
        _resultTable.Columns.Clear(); _resultTable.ItemsSource = null;
        if (!_plot.HasData) return;
        var data = _plot.ResultsSnapshot();
        for (var i = 0; i < data.Headers.Length; i++)
        {
            var header = new TextBlock { Text = data.Headers[i], FontWeight = FontWeight.SemiBold };
            if (data.Quantities[i] is { } quantity) header.Foreground = SpectrumPlot.CurveBrush(quantity);
            _resultTable.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"Values[{i}]") { StringFormat = i == 0 ? "0.0#####" : "0.0000" },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = i == 0 ? 110 : 130
            });
        }
        _resultTable.ItemsSource = data.Rows;
    }
    private async Task ExportResultDataAsync(char delimiter)
    {
        var text = _plot.ResultsSnapshot().Delimited(delimiter); // Freeze values/columns before the picker.
        var extension = delimiter == ',' ? "csv" : "tsv"; string? path;
        if (_dialogs is not null)
        { var directory = await _dialogs.ExportDirectory(); path = directory is null ? null : System.IO.Path.Combine(directory, "coating-spectrum." + extension); }
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出评估结果",
                SuggestedFileName = "coating-spectrum." + extension,
                DefaultExtension = extension
            }); path = file?.TryGetLocalPath();
        }
        if (path is null) return;
        await BoundedFile.WriteAllTextAtomicAsync(path, text, BoundedFile.MaximumExportBytes, "镀膜评估数据");
        _status.Text = "评估数据已导出：" + path;
    }
}
