using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

/// <summary>Scene-wide editing, shared by the legacy dock tool and the compact workspace entry.</summary>
public sealed class NonSequentialModePanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private readonly INonSequentialDocumentService _service;
    private readonly IWorkspaceEventStream _events;
    private readonly TextBlock _counts = new() { Name = "nsc-scene-counts", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _waveSummary = new() { Name = "nsc-scene-wave-summary", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "nsc-scene-status", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = UiLocalization.Preserve(new TextBox { Name = "nsc-scene-name", MinWidth = 0 });
    private readonly ComboBox _ambient = UiLocalization.Preserve(new ComboBox { Name = "nsc-scene-ambient", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch });
    private readonly CheckBox _polarized = new() { Name = "nsc-scene-polarized", Content = "偏振追迹（Jones / Stokes）" };
    private readonly CheckBox _coherent = new() { Name = "nsc-scene-coherent", Content = "相干探测" };
    private readonly CheckBox _split = new() { Name = "nsc-scene-split", Content = "Fresnel 分裂" };
    private readonly Dictionary<string, TextBox> _numbers = [];
    private readonly ObservableCollection<WaveRow> _waves = [];
    private readonly DataGrid _waveGrid;
    private readonly Button _apply = new() { Name = "nsc-scene-apply", Content = "应用设置", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _reload = new() { Name = "nsc-scene-reload", Content = "重载" };
    private readonly Button _remove = new() { Name = "nsc-scene-wave-remove", Content = "删除末行" };
    private NonSequentialDocumentDto? _document;
    private string _identity = "";
    private string _baselineDraft = "";
    private long _baselineRevision;
    private bool _loading, _dirty, _disposed;

    public NonSequentialModePanel(INonSequentialDocumentService service, IWorkspaceEventStream events, AppSettings? settings = null)
    {
        _service = service; _events = events;
        Name = "nsc-scene-settings";
        _waveGrid = CreateWaveGrid();
        var general = new StackPanel { Spacing = 6, Children = { Field("名称", _name), Field("环境介质", _ambient) } };
        var add = new Button { Name = "nsc-scene-wave-add", Content = "添加波长" };
        add.Click += (_, _) =>
        {
            if (_waves.Count >= 1024) return;
            _waves.Add(new WaveRow(_waves.Count, "W" + (_waves.Count + 1), "550", "1", false, WaveChanged));
            _waveGrid.ScrollIntoView(_waves[^1], null); MarkDirty(); UpdateWaveSummary();
        };
        _remove.Click += (_, _) =>
        {
            if (_waves.Count <= 1) return;
            var wasPrimary = _waves[^1].Primary; _waves.RemoveAt(_waves.Count - 1);
            if (wasPrimary) _waves[0].Primary = true;
            MarkDirty(); UpdateWaveSummary();
        };
        var wavelength = new StackPanel
        {
            Spacing = 6,
            Children =
        { _waveSummary, _waveGrid, new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, Children = { add, _remove } } }
        };
        var limits = new StackPanel { Spacing = 6, Children = { _split } };
        foreach (var (key, title) in new[] { ("segments", "每条最大段数"), ("branches", "最大活动分支"),
            ("minimum", "最小相对能量"), ("total", "最大源光线总数"), ("seed", "随机种子") })
        {
            var input = new TextBox { Name = "nsc-scene-" + key, MinWidth = 0, Classes = { "numeric-text" } };
            _numbers[key] = input; input.TextChanged += (_, _) => MarkDirty(); limits.Children.Add(Field(title, input));
        }
        var physics = new StackPanel { Spacing = 6, Children = { _polarized, _coherent } };
        var body = new StackPanel
        {
            Margin = new Thickness(10),
            Spacing = 9,
            Children =
        {
            _counts,
            SystemPropertiesPanel.Section("场景", "nonsequential-mode", general, true),
            SystemPropertiesPanel.Section("波长", "wavelength-settings", wavelength, true),
            SystemPropertiesPanel.Section("偏振与相干", "polarization-settings", physics, true),
            SystemPropertiesPanel.Section("追迹限值", "settings-general", limits, true)
        }
        };
        if (settings is not null)
            body.Children.Add(SystemPropertiesPanel.Section("高级", "settings-general", SystemPropertiesPanel.BuildAdvancedSection(settings)));
        foreach (var text in new[] { _counts, _waveSummary })
        { text.FontSize = DisplayTypography.BodySmall; text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary); }
        var footer = new StackPanel { Margin = new Thickness(10, 5), Spacing = 5, Children = { _status } };
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { _apply, _reload } };
        Grid.SetColumn(_reload, 1); footer.Children.Add(buttons);
        _apply.Click += (_, _) => ApplySettings(); _reload.Click += (_, _) => Reload();
        var root = new DockPanel(); root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.SidebarSectionContent);
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        _name.TextChanged += (_, _) => MarkDirty(); _ambient.SelectionChanged += (_, _) => MarkDirty();
        foreach (var check in new[] { _polarized, _coherent, _split }) check.IsCheckedChanged += (_, _) => MarkDirty();
        _events.Changed += Changed;
        UiLocalization.BindLanguage(this, UpdateWaveSummary);
        Reload();
    }

    private static Control Field(string text, Control editor)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*"), ColumnSpacing = 6 };
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1); row.Children.Add(editor); return row;
    }
    private DataGrid CreateWaveGrid()
    {
        var group = "nsc-primary-" + Guid.NewGuid();
        var grid = new DataGrid
        {
            Name = "nsc-scene-wavelengths",
            Height = 224,
            ItemsSource = _waves,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = UiDensity.CompactTableRowHeight,
            ColumnHeaderHeight = UiDensity.TableHeaderHeight,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "λ₀",
            Width = new DataGridLength(40),
            CellTemplate = new FuncDataTemplate<WaveRow>((row, _) =>
            {
                var radio = new RadioButton { GroupName = group, HorizontalAlignment = HorizontalAlignment.Center };
                radio.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(WaveRow.Primary)) { Mode = BindingMode.OneWay });
                radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true && row is not null) row.Primary = true; };
                return radio;
            })
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "#",
            Width = new DataGridLength(40),
            IsReadOnly = true,
            CellTemplate = new FuncDataTemplate<WaveRow>((row, _) => new TextBlock
            {
                Text = row?.Number.ToString(CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            })
        });
        grid.Columns.Add(NumericColumn("nm", nameof(WaveRow.Nanometers), nameof(WaveRow.NanometersDisplay), 1.2));
        grid.Columns.Add(NumericColumn("权重", nameof(WaveRow.Weight), nameof(WaveRow.WeightDisplay), 1));
        return grid;
    }
    private static DataGridTemplateColumn NumericColumn(string header, string rawProperty, string displayProperty, double width) => new()
    {
        Header = header,
        Width = new DataGridLength(width, DataGridLengthUnitType.Star),
        MinWidth = 84,
        CellTemplate = new FuncDataTemplate<WaveRow>((_, _) =>
        {
            var text = new TextBlock { Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Bind(TextBlock.TextProperty, new Binding(displayProperty));
            text.Bind(ToolTip.TipProperty, new Binding(rawProperty));
            return text;
        }),
        CellEditingTemplate = new FuncDataTemplate<WaveRow>((_, _) =>
        {
            var input = new TextBox { MinWidth = 0, Classes = { "numeric-text" } };
            input.Bind(TextBox.TextProperty, new Binding(rawProperty) { Mode = BindingMode.TwoWay });
            return input;
        })
    };
    private void WaveChanged(WaveRow row, bool primary)
    {
        if (_loading) return;
        if (primary && row.Primary)
        {
            _loading = true;
            try { foreach (var other in _waves.Where(w => w != row)) other.Primary = false; }
            finally { _loading = false; }
        }
        MarkDirty(); UpdateWaveSummary();
    }
    private void UpdateWaveSummary()
    {
        _waveSummary.Text = UiLocalization.Format("{0} 个波长 · 主波长 {1} nm", _waves.Count, _waves.FirstOrDefault(w => w.Primary)?.Nanometers ?? "—");
        _waveGrid.Height = UiDensity.TableHeaderHeight + Math.Clamp(_waves.Count, 1, 5) * UiDensity.CompactTableRowHeight + 10;
        _remove.IsEnabled = _waves.Count > 1;
    }
    private void MarkDirty()
    {
        if (_loading || _disposed) return;
        _dirty = CaptureDraft() != _baselineDraft; _apply.IsEnabled = _dirty;
        _status.Text = _dirty ? UiLocalization.Text("有未应用的更改") : "";
    }
    private string CaptureDraft() => System.Text.Json.JsonSerializer.Serialize(new
    {
        Name = _name.Text,
        Ambient = _ambient.SelectedItem as string,
        Numbers = _numbers.ToDictionary(p => p.Key, p => p.Value.Text),
        Polarized = _polarized.IsChecked,
        Coherent = _coherent.IsChecked,
        Split = _split.IsChecked,
        Waves = _waves.Select(w => new { w.Label, w.Nanometers, w.Weight, w.Primary }).ToArray()
    });
    private void Changed(object? sender, WorkspaceChangedEventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || args.Revision <= _baselineRevision) return;
        if (args.FileSwitched || !_dirty) Reload();
        else _status.Text = UiLocalization.Text("场景已更新，请重载设置。");
    });
    public void RefreshDisplaySettings() { if (!_dirty) Reload(); }
    internal void Reload()
    {
        if (_disposed) return;
        _loading = true;
        try
        {
            _baselineRevision = _events.Revision;
            _document = _service.GetDocument(); _identity = _service.GetDesignSceneIdentity();
            if (_baselineRevision != _events.Revision) { Dispatcher.UIThread.Post(Reload); return; }
            _name.Text = _document.Name;
            _ambient.ItemsSource = _service.GetMaterialNames().Append(_document.AmbientMaterial).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _ambient.SelectedItem = ((IEnumerable<string>)_ambient.ItemsSource).First(n => n.Equals(_document.AmbientMaterial, StringComparison.OrdinalIgnoreCase));
            _waves.Clear();
            foreach (var wave in _document.Wavelengths)
                _waves.Add(new WaveRow(_waves.Count, wave.Label, wave.Nanometers.ToString("R", CultureInfo.InvariantCulture), wave.Weight.ToString("R", CultureInfo.InvariantCulture), wave.IsPrimary, WaveChanged));
            var t = _document.TraceSettings;
            _numbers["segments"].Text = t.MaximumSegmentsPerRay.ToString(CultureInfo.InvariantCulture);
            _numbers["branches"].Text = t.MaximumActiveBranches.ToString(CultureInfo.InvariantCulture);
            _numbers["minimum"].Text = t.MinimumRelativeIntensity.ToString("R", CultureInfo.InvariantCulture);
            _numbers["total"].Text = t.MaximumTotalSourceRays.ToString(CultureInfo.InvariantCulture);
            _numbers["seed"].Text = t.RandomSeed.ToString(CultureInfo.InvariantCulture);
            _polarized.IsChecked = t.UsePolarization; _coherent.IsChecked = t.CoherentDetection; _split.IsChecked = t.SplitFresnelRays;
            _counts.Text = UiLocalization.Format("{0} 个对象 · {1} 个光源 · {2} 个探测器", _document.Objects.Count,
                _document.Objects.Count(o => o.Parameters is SourceParameters), _document.Objects.Count(o => o.Kind == NonSequentialObjectKind.DetectorRectangle));
            UpdateWaveSummary(); _baselineDraft = CaptureDraft(); _dirty = false; _apply.IsEnabled = false; _status.Text = "";
        }
        finally { _loading = false; }
    }
    private int Integer(string key) => int.TryParse(_numbers[key].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException(UiLocalization.Text("请输入有效整数。"));
    private static double Number(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new FormatException(UiLocalization.Text("请输入有效数值。"));
    internal bool ApplySettings()
    {
        if (_document is null || _disposed) return false;
        try
        {
            if (!_waveGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !_waveGrid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
            var settings = _document.TraceSettings with
            {
                MaximumSegmentsPerRay = Integer("segments"),
                MaximumActiveBranches = Integer("branches"),
                MinimumRelativeIntensity = Number(_numbers["minimum"].Text),
                MaximumTotalSourceRays = Integer("total"),
                RandomSeed = Integer("seed"),
                SplitFresnelRays = _split.IsChecked == true,
                UsePolarization = _polarized.IsChecked == true,
                CoherentDetection = _coherent.IsChecked == true
            };
            var waves = _waves.Select(w => new NonSequentialWavelengthDto(w.Number - 1, w.Label, Number(w.Nanometers), Number(w.Weight), w.Primary)).ToArray();
            _service.UpdateSceneSettings(new(_name.Text ?? "", _ambient.SelectedItem as string ?? "", waves, settings), _identity, _baselineRevision);
            Reload(); _status.Text = UiLocalization.Text("设置已应用，可撤销。"); return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException)
        { _status.Text = UiLocalization.Text(e.Message); return false; }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _events.Changed -= Changed; }

    private sealed class WaveRow(int index, string label, string nanometers, string weight, bool primary, Action<WaveRow, bool> changed) : INotifyPropertyChanged, IEditableObject
    {
        private string _nanometers = nanometers, _weight = weight;
        private bool _primary = primary;
        private (string Nanometers, string Weight)? _editSnapshot;
        public int Number { get; } = index + 1;
        public string Label { get; } = label;
        public string Nanometers { get => _nanometers; set { if (_nanometers == value) return; _nanometers = value; Notify(nameof(Nanometers)); Notify(nameof(NanometersDisplay)); changed(this, false); } }
        public string Weight { get => _weight; set { if (_weight == value) return; _weight = value; Notify(nameof(Weight)); Notify(nameof(WeightDisplay)); changed(this, false); } }
        public string NanometersDisplay => Compact(Nanometers);
        public string WeightDisplay => Compact(Weight);
        private static string Compact(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number) ? number.ToString("G8", CultureInfo.InvariantCulture) : value;
        public bool Primary { get => _primary; set { if (_primary == value) return; _primary = value; Notify(nameof(Primary)); changed(this, true); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        public void BeginEdit() => _editSnapshot ??= (Nanometers, Weight);
        public void EndEdit() => _editSnapshot = null;
        public void CancelEdit()
        {
            if (_editSnapshot is not { } saved) return;
            _editSnapshot = null; Nanometers = saved.Nanometers; Weight = saved.Weight;
        }
    }
}
