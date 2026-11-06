using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.App.Panels;

internal sealed class ToleranceScriptImportWindow : Window
{
    private readonly Dictionary<string, ToleranceMeritTableDto> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly NumericUpDown _travel = new() { Minimum = 0, Maximum = 1_000_000, Increment = .01m, Width = 130 };
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _apply = new() { Content = "应用脚本映射" };
    private readonly Button _pick = new() { Content = "选择评价表 ZMX" };
    private readonly string _script;
    private readonly int _surfaceCount;
    private readonly IReadOnlyList<MultiConfigurationOperandRowDto> _configurationRows;
    private readonly ToleranceConfigurationSettingsDto? _configurationSettings;
    private readonly Func<ToleranceScriptPreview, string?> _validate;
    private ToleranceScriptPreview? _current;
    private bool _loadingTables;

    internal ToleranceScriptImportWindow(string fileName, string script, int surfaceCount,
        IReadOnlyList<MultiConfigurationOperandRowDto> configurationRows, ToleranceConfigurationSettingsDto? configurationSettings,
        Func<ToleranceScriptPreview, string?> validate)
    {
        _script = script;
        _surfaceCount = surfaceCount;
        _configurationRows = configurationRows;
        _configurationSettings = configurationSettings;
        _validate = validate;
        Title = "导入 OpticStudio 公差脚本";
        Width = 740;
        Height = 590;
        MinWidth = 480;
        MinHeight = 410;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Workspace);
        ThemeChrome.ApplyDialogDecoration(this);
        _apply.Classes.Add("accent");
        _apply.Click += (_, _) =>
        {
            if (_current is { } candidate && _apply.IsEnabled) Close(candidate);
        };
        _pick.Click += async (_, _) => await PickTablesAsync();
        _travel.ValueChanged += (_, _) => RefreshPreview();
        Avalonia.Automation.AutomationProperties.SetName(_travel, "补偿行程 ± (mm)");
        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => Close(null);
        var header = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = fileName, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "为 LOADMERIT 选择同名 ZMX。输入本地补偿行程，应用后在评价标准中设置验收上限。", TextWrapping = TextWrapping.Wrap },
                new WrapPanel
                {
                    ItemSpacing = 8, LineSpacing = 6,
                    Children =
                    {
                        _pick,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children = { new TextBlock { Text = "补偿行程 ± (mm)", VerticalAlignment = VerticalAlignment.Center }, _travel }
                        }
                    }
                }
            }
        };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { _apply, cancel }
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 8, Margin = new Thickness(12) };
        layout.Children.Add(header);
        Grid.SetRow(_preview, 1);
        layout.Children.Add(_preview);
        Grid.SetRow(_status, 2);
        layout.Children.Add(_status);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        Content = layout;
        RefreshPreview();
    }

    private async Task PickTablesAsync()
    {
        _loadingTables = true;
        _pick.IsEnabled = false;
        _apply.IsEnabled = false;
        var errors = new List<string>();
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 LOADMERIT 评价表",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Zemax ZMX") { Patterns = ["*.zmx"] }]
            });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            foreach (var group in paths.GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                if (_current?.References.Contains(group.Key, StringComparer.OrdinalIgnoreCase) != true) continue;
                _tables.Remove(group.Key);
                if (group.Count() != 1)
                {
                    errors.Add($"{group.Key}：请选择唯一的同名 ZMX。");
                    continue;
                }
                var path = group.Single();
                try
                {
                    var rows = await new ZemaxZmxImporter().ReadMeritTableAsync(path, _surfaceCount);
                    BindMeritTable(new(group.Key, rows));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or InvalidDataException)
                {
                    errors.Add($"{group.Key}：{error.Message}");
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            errors.Add(error.Message);
        }
        finally
        {
            _loadingTables = false;
            _pick.IsEnabled = true;
            RefreshPreview(string.Join(Environment.NewLine, errors));
        }
    }

    private void RefreshPreview(string fileError = "")
    {
        _current = ToleranceScriptImporter.Preview(_script, _tables, _surfaceCount, _configurationRows, _configurationSettings,
            (double)(_travel.Value ?? 0));
        var content = new StringBuilder();
        foreach (var row in _current.Lines)
        {
            if (row.Number > 0) content.Append(row.Number.ToString().PadLeft(4)).Append("  ").AppendLine(row.Text);
            if (row.Detail.Length > 0) content.Append("      ").Append(row.Status).Append(" · ").AppendLine(row.Detail);
        }
        _preview.Text = content.ToString();
        var validation = _current.CanImport ? _validate(_current) : null;
        _apply.IsEnabled = !_loadingTables && _current.CanImport && validation is null && fileError.Length == 0;
        _status.Text = fileError.Length > 0 ? fileError : validation
            ?? (_apply.IsEnabled ? "应用后替换补偿设置，并保留现有制造公差。" : "请处理预览中的阻塞行。");
    }

    internal void BindMeritTable(ToleranceMeritTableDto table)
    {
        _tables[table.Name] = table;
        RefreshPreview();
    }
}
