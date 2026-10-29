using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Panels;

internal sealed partial class MarkdownReportWindow : Window
{
    private readonly IWorkbenchApplication _application;
    private readonly AppSettings _appSettings;
    private readonly MarkdownReportSettings _options;
    private readonly Dictionary<string, CheckBox> _analyses = new();
    private readonly Dictionary<string, Func<string>> _parameterValues = new();
    private readonly List<Control> _editable = new();
    private readonly StackPanel _parameters = new() { Spacing = 8, Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBox _directory = new() { Name = "ReportOutputDirectory" };
    private static readonly DesignReportFormat[] Formats = [DesignReportFormat.Word, DesignReportFormat.Pdf, DesignReportFormat.Markdown, DesignReportFormat.Html];
    private readonly ComboBox _format = new() { Name = "ReportExportFormat", ItemsSource = new[] { "Word (.docx)", "PDF (.pdf)", "Markdown (.md)", "HTML (.html)" }, Width = 220 };
    private readonly CheckBox _twoD = new() { Content = "二维布局" };
    private readonly CheckBox _threeD = new() { Content = "三维布局" };
    private readonly CheckBox _allConfigurations = new() { Content = "逐一导出所有配置" };
    private readonly OperationStatusBar _operationStatus = new("ReportExportProgress");
    private readonly TextBlock _status = new() { Text = "就绪", TextWrapping = TextWrapping.Wrap };
    private readonly Button _export = new() { Name = "ExportMarkdownReport", Content = "开始导出", MinWidth = 170 };
    private readonly Border _renderHost = new() { Width = 1200, Height = 900, Opacity = 0, IsHitTestVisible = false };
    private CancellationTokenSource? _cancellation;
    private string? _editingKey;
    private string _defaultDirectory;

    public MarkdownReportWindow(IWorkbenchApplication application, AppSettings settings)
    {
        _application = application;
        _appSettings = settings;
        _options = (settings.ReportExport ?? new()).Copy();
        Title = "光学设计报告导出";
        Width = 980; Height = 740; MinWidth = 720; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _defaultDirectory = MarkdownReportExporter.SuggestedDirectory(application.Documents.CurrentPath);
        _directory.Text = _defaultDirectory;
        var selectedFormat = Array.IndexOf(Formats, _options.Format);
        _format.SelectedIndex = selectedFormat >= 0 ? selectedFormat : 2;
        AutomationProperties.SetName(_format, "导出格式");
        _twoD.IsChecked = _options.Include2DLayout;
        _threeD.IsChecked = _options.Include3DLayout;
        _allConfigurations.IsChecked = _options.AllConfigurations;
        var selection = new StackPanel { Spacing = 7 };
        var presets = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, mode) in new[] { ("常用分析", 0), ("全选", 1), ("清空", 2) })
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) =>
            {
                foreach (var (key, check) in _analyses)
                    check.IsChecked = mode == 1 || mode == 0 && MarkdownReportSettings.CommonAnalyses.Contains(key);
            };
            presets.Children.Add(button); _editable.Add(button);
        }
        selection.Children.Add(presets);
        foreach (var key in application.Analyses.AnalysisNames.Select(application.Analyses.CanonicalKey)
                     .Where(MarkdownReportExporter.IsSupportedAnalysis).Distinct())
        {
            if (!_options.AnalysisParameters.ContainsKey(key) && settings.AnalysisSettings.TryGetValue(key, out var cached))
                _options.AnalysisParameters[key] = new(cached);
            var check = new CheckBox
            {
                Content = new TextBlock { Text = UiLocalization.Text(WorkbenchAnalysisCatalog.DisplayName(key)), TextWrapping = TextWrapping.Wrap },
                IsChecked = _options.AnalysisKeys.Contains(key),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetAutomationId(check, "report-analysis-" + key);
            var edit = new Button { Content = "分析设置", Width = 86, MinWidth = 0, Padding = new Thickness(8, 3) };
            AutomationProperties.SetAutomationId(edit, "report-settings-" + key);
            edit.Click += (_, _) => EditParameters(key);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 0, 32, 0) };
            row.Children.Add(check); Grid.SetColumn(edit, 1); row.Children.Add(edit);
            selection.Children.Add(row); _analyses[key] = check;
            _editable.Add(check); _editable.Add(edit);
        }
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        columns.Children.Add(new ScrollViewer { Content = selection, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var parameterScroll = new ScrollViewer { Content = _parameters, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(parameterScroll, 1); columns.Children.Add(parameterScroll);
        var browse = new Button { Content = "选择目录", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择报告目录", AllowMultiple = false });
            if (result.Count > 0) _directory.Text = result[0].Path.LocalPath;
        };
        var directoryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        directoryRow.Children.Add(_directory); Grid.SetColumn(browse, 1); directoryRow.Children.Add(browse);
        var layout = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _twoD, _threeD, _allConfigurations } };
        foreach (var control in layout.Children) control.Margin = new Thickness(0, 0, 18, 0);
        var saveSettings = new Button { Content = "保存导出设置" };
        saveSettings.Click += (_, _) =>
        {
            CaptureOptions();
            _appSettings.ReportExport = _options.Copy();
            _status.Text = _appSettings.TrySave(out var error) ? UiLocalization.Text("导出设置已保存") : error;
        };
        _editable.Add(saveSettings);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { saveSettings, _export } };
        var formatRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            { new TextBlock { Text = "导出格式", VerticalAlignment = VerticalAlignment.Center }, _format }
        };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 12, Margin = new Thickness(18), ClipToBounds = true };
        Add(new TextBlock { Text = "导出镜头结构、系统设置、布局和所选分析的结果及截图。文件名自动包含时间。", TextWrapping = TextWrapping.Wrap }, 0);
        Add(formatRow, 1); Add(directoryRow, 2); Add(layout, 3); Add(columns, 4); Add(_operationStatus, 5); Add(_status, 6); Add(actions, 7);
        var shell = new Grid { ClipToBounds = true };
        shell.Children.Add(_renderHost); shell.Children.Add(root); Content = shell;
        _editable.AddRange([_format, _directory, browse, _twoD, _threeD, _allConfigurations, _export]);
        _export.Click += async (_, _) => await ExportAsync();
        Closing += (_, args) =>
        {
            if (_cancellation is null) return;
            args.Cancel = true; _cancellation.Cancel(); _status.Text = UiLocalization.Text("正在取消导出");
        };
        UiLocalization.Attach(this); DisplayTypography.Apply(this);
        if (_analyses.Count > 0) EditParameters(_analyses.FirstOrDefault(item => item.Value.IsChecked == true).Key ?? _analyses.Keys.First());
        application.Events.Changed += OnSourceChanged;
        Closed += (_, _) => { application.Events.Changed -= OnSourceChanged; _operationStatus.Dispose(); };

        void Add(Control control, int row) { Grid.SetRow(control, row); root.Children.Add(control); }
    }

    private void OnSourceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (!args.FileSwitched) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_cancellation is not null) return;
            var next = MarkdownReportExporter.SuggestedDirectory(_application.Documents.CurrentPath);
            if (_directory.Text == _defaultDirectory) _directory.Text = next;
            _defaultDirectory = next;
            if (_editingKey is not null && _application.Analyses.AnalysisNames.Select(_application.Analyses.CanonicalKey).Contains(_editingKey))
                EditParameters(_editingKey);
        });
    }

    private void CaptureParameters()
    {
        if (_editingKey is not null)
            _options.AnalysisParameters[_editingKey] = _parameterValues.ToDictionary(item => item.Key, item => item.Value());
    }

    private void EditParameters(string key)
    {
        CaptureParameters(); _editingKey = key; _parameterValues.Clear(); _parameters.Children.Clear();
        _parameters.Children.Add(new TextBlock { Text = UiLocalization.Text(WorkbenchAnalysisCatalog.DisplayName(key)), FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        if (!_options.AnalysisParameters.TryGetValue(key, out var saved))
            _appSettings.AnalysisSettings.TryGetValue(key, out saved);
        var merged = _application.Analyses.MergeSettings(key, saved);
        foreach (var descriptor in _application.Analyses.GetParameters(key, merged))
        {
            var value = merged.GetValueOrDefault(descriptor.Key, descriptor.DefaultValue);
            Control input;
            Func<string> read;
            if (descriptor.Kind == AnalysisParameterKind.Choice)
            {
                var choices = descriptor.Choices?.ToArray() ?? [];
                var combo = new ComboBox { ItemsSource = choices.Select(choice => UiLocalization.Text(descriptor.ChoiceLabels?.GetValueOrDefault(choice) ?? choice)).ToArray(), SelectedIndex = Array.IndexOf(choices, value), HorizontalAlignment = HorizontalAlignment.Stretch };
                input = combo; read = () => combo.SelectedIndex >= 0 ? choices[combo.SelectedIndex] : descriptor.DefaultValue;
                if (descriptor.Key == AnalysisConfigurationSelection.Key)
                    combo.SelectionChanged += (_, _) => EditParameters(key);
            }
            else if (descriptor.Kind == AnalysisParameterKind.Boolean)
            {
                var check = new CheckBox { IsChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase) };
                input = check; read = () => check.IsChecked == true ? "true" : "false";
            }
            else if (descriptor.Kind == AnalysisParameterKind.File && descriptor.Key == "OutputFile")
            {
                input = new TextBlock { Text = UiLocalization.Text("自动写入报告附件"), TextWrapping = TextWrapping.Wrap };
                read = () => "";
            }
            else if (descriptor.Kind == AnalysisParameterKind.File)
            {
                var box = new TextBox { Text = value, PlaceholderText = "文件路径" };
                var picker = new Button { Content = "选择文件", Margin = new Thickness(4, 0, 0, 0) };
                picker.Click += async (_, _) =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = descriptor.DisplayName });
                    if (files.Count > 0) box.Text = files[0].Path.LocalPath;
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(box); Grid.SetColumn(picker, 1); row.Children.Add(picker);
                input = row; read = () => box.Text ?? "";
            }
            else
            {
                var number = new NumericUpDown { Minimum = (decimal)descriptor.Minimum, Maximum = (decimal)descriptor.Maximum, Increment = (decimal)descriptor.Increment, ShowButtonSpinner = false };
                number.Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? Math.Clamp(parsed, number.Minimum, number.Maximum) : number.Minimum;
                input = number; read = () => (number.Value ?? number.Minimum).ToString(CultureInfo.InvariantCulture);
            }
            AutomationProperties.SetName(input, descriptor.DisplayName);
            _parameterValues[descriptor.Key] = read;
            _parameters.Children.Add(new TextBlock { Text = UiLocalization.Text(descriptor.DisplayName), TextWrapping = TextWrapping.Wrap });
            _parameters.Children.Add(input);
        }
    }

    private void CaptureOptions()
    {
        CaptureParameters();
        _options.AnalysisKeys = _analyses.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToList();
        _options.Include2DLayout = _twoD.IsChecked == true; _options.Include3DLayout = _threeD.IsChecked == true;
        _options.AllConfigurations = _allConfigurations.IsChecked == true;
        _options.Format = Formats[Math.Clamp(_format.SelectedIndex, 0, Formats.Length - 1)];
    }

    private async Task ExportAsync()
    {
        if (_cancellation is not null) return;
        CaptureOptions();
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        foreach (var control in _editable) { control.IsEnabled = false; ControlAvailability.Explain(control, "正在导出，完成后可修改设置"); }
        foreach (var child in _parameters.Children) if (child is not TextBlock) child.IsEnabled = false;
        _operationStatus.Start(UiLocalization.Text("正在捕获当前工程"), () =>
        {
            _cancellation?.Cancel(); _status.Text = UiLocalization.Text("正在取消导出");
        });
        _operationStatus.ReportProgress(0, 1, UiLocalization.Text("正在捕获当前工程"));
        _status.Text = UiLocalization.Text("正在捕获当前工程");
        try
        {
            var output = _directory.Text ?? throw new InvalidOperationException("请选择报告目录。");
            var options = _options.Copy();
            var renderer = new ReportImageRenderer(_renderHost);
            var progress = new Progress<ReportExportProgress>(value =>
            {
                _operationStatus.ReportProgress(value.Completed, value.Total,
                    $"{value.Completed}/{value.Total} — {UiLocalization.Text(value.Item)}");
            });
            using var snapshot = _application.CaptureReportSnapshot();
            var result = await Task.Run(() => MarkdownReportExporter.ExportAsync(snapshot, options, output,
                renderer.WriteAsync, progress, cancellation.Token), cancellation.Token);
            _appSettings.ReportExport = options;
            var saved = _appSettings.TrySave(out var saveError);
            if (result.Failed == 0) _operationStatus.MarkSynced(UiLocalization.Text("报告已导出"));
            else _operationStatus.MarkStale(UiLocalization.Text("失败或不适用") + $"：{result.Failed}");
            _status.Text = $"{UiLocalization.Text("报告已导出")}：{result.ReportPath}\n{result.Succeeded} {UiLocalization.Text("成功")}；{result.Failed} {UiLocalization.Text("失败或不适用")}"
                + (saved ? "" : $"\n{saveError}");
        }
        catch (OperationCanceledException) { _status.Text = UiLocalization.Text("导出已取消"); _operationStatus.MarkIdle(_status.Text); }
        catch (Exception error) { _status.Text = $"{UiLocalization.Text("导出失败：")}{error.Message}"; _operationStatus.MarkFailed(_status.Text); }
        finally
        {
            _cancellation = null;
            foreach (var control in _editable) { control.IsEnabled = true; ControlAvailability.Explain(control, null); }
            foreach (var child in _parameters.Children) child.IsEnabled = true;
        }
    }
}
