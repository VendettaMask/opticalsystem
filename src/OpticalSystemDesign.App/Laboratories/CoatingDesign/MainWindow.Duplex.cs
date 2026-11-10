using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private readonly CheckBox _duplexEnabled = new() { Content = "启用双面设计" };
    private readonly CheckBox _symmetricFilms = new() { Content = "对称双面（背面 = 正面）" };
    private readonly ComboBox _optimizeSurface = new() { ItemsSource = new[] { "正面", "背面", "两面" }, SelectedIndex = 0, MinWidth = 140 };
    private readonly ComboBox _evaluationSurface = new() { ItemsSource = new[] { "正面", "背面", "整片" }, SelectedIndex = 2, MinWidth = 140 };
    private readonly ComboBox _duplexProtocol = new() { ItemsSource = new[] { "原双面流程 v1", "解析导数 · 恢复/质量 v2" }, SelectedIndex = 1, MinWidth = 210 };
    private readonly TabControl _filmSideTabs = new()
    {
        Name = "CoatingFilmSideTabs",
        Items = { new TabItem { Header = "正面镀膜", FontSize = DisplayTypography.Body, Padding = new(14, 6) },
            new TabItem { Header = "背面镀膜", FontSize = DisplayTypography.Body, Padding = new(14, 6) } },
        SelectedIndex = 0
    };
    private readonly TextBlock _evaluationBadge = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private LayerRow[] _frontEditorRows = [], _backEditorRows = [];
    private bool _editingBack;
    private bool _linkedSymmetric;
    private LayerRow[] FrontEditorRows => _editingBack ? _frontEditorRows : _rows.ToArray();
    private LayerRow[] BackEditorRows => _editingBack ? _rows.ToArray() : _backEditorRows;

    private Control BuildDuplexControls()
    {
        AutomationProperties.SetName(_duplexEnabled, "启用双面设计"); AutomationProperties.SetName(_symmetricFilms, "对称双面");
        AutomationProperties.SetName(_optimizeSurface, "优化表面"); AutomationProperties.SetName(_evaluationSurface, "评价对象");
        UiLocalization.BindChoices(_optimizeSurface);
        UiLocalization.BindChoices(_evaluationSurface);
        UiLocalization.BindChoices(_duplexProtocol);
        AutomationProperties.SetName(_duplexProtocol, "双面优化流程");
        _duplexProtocol.SelectionChanged += (_, _) => { if (!_loading) Changed(); };
        _duplexEnabled.IsCheckedChanged += (_, _) => ChangeDuplexMode();
        _symmetricFilms.IsCheckedChanged += (_, _) => ChangeDuplexMode();
        _optimizeSurface.SelectionChanged += (_, _) => ChangeDuplexMode();
        _evaluationSurface.SelectionChanged += (_, _) => ChangeDuplexMode();
        _filmSideTabs.SelectionChanged += (_, _) =>
        {
            if (_loading || _filmSideTabs.SelectedIndex < 0 || _editingBack == (_filmSideTabs.SelectedIndex == 1)) return;
            _table.CommitEdit(); _editorTransaction++;
            try
            {
                if (_filmSideTabs.SelectedIndex == 1 && _duplexEnabled.IsChecked != true) _duplexEnabled.IsChecked = true;
                SwitchFilmEditor(_filmSideTabs.SelectedIndex == 1); RefreshLayerOpticalThickness(); RefreshStackGeometry();
            }
            finally { _editorTransaction--; }
            RecordEditorHistory(null);
        };
        return new StackPanel { Spacing = 12, Children = { _duplexEnabled, Field("优化表面", _optimizeSurface),
            _symmetricFilms, Field("双面优化流程", _duplexProtocol), _evaluationBadge } };
    }
    private void ChangeDuplexMode()
    {
        if (_loading) return;
        _editorTransaction++; _loading = true;
        try
        {
            if (_duplexEnabled.IsChecked != true && (BackEditorRows.Length > 0 || _symmetricFilms.IsChecked == true))
            { _duplexEnabled.IsChecked = true; ShowFailure("请先清空背面膜系并解除对称，再切回旧单面模式；背面数据保持。"); }
            if (_symmetricFilms.IsChecked == true) _optimizeSurface.SelectedIndex = 2;
            if (_optimizeSurface.SelectedIndex == 2) _evaluationSurface.SelectedIndex = 2;
            if (_optimizeSurface.SelectedIndex == 0 && _evaluationSurface.SelectedIndex == 1
                || _optimizeSurface.SelectedIndex == 1 && _evaluationSurface.SelectedIndex == 0) _evaluationSurface.SelectedIndex = 2;
            if (_symmetricFilms.IsChecked == true)
            {
                if (_editingBack) { SwitchFilmEditor(false); _filmSideTabs.SelectedIndex = 0; }
                _backEditorRows = FrontEditorRows.Select(r => { var copy = CreateRow(r.Capture().Original); copy.Restore(r.Capture()); return copy; }).ToArray();
            }
            else if (_linkedSymmetric)
                _backEditorRows = FrontEditorRows.Select(r => { var copy = CreateRow(r.Capture().Original); copy.Restore(r.Capture()); return copy; }).ToArray();
            UpdateDuplexControls();
        }
        finally { _loading = false; _editorTransaction--; }
        Changed();
    }
    private void UpdateDuplexControls()
    {
        var enabled = _duplexEnabled.IsChecked == true;
        _linkedSymmetric = _symmetricFilms.IsChecked == true;
        _optimizeSurface.IsEnabled = enabled;
        _duplexProtocol.IsEnabled = enabled;
        ControlAvailability.Set(_narrowProtocol, !enabled, "双面模式使用统一双面优化路径；原单面设计流程保留，仅在单面模式使用。");
        ControlAvailability.Set(_evaluationSurface, enabled && _optimizeSurface.SelectedIndex != 2, "两面优化统一评价整片；旧单面实验保持原正面验收。");
        _symmetricFilms.IsEnabled = enabled;
        ((TabItem)_filmSideTabs.Items[1]!).IsEnabled = _symmetricFilms.IsChecked != true;
        _includeBackSurface.IsVisible = !enabled;
        RefreshCompactSurfaceMode(enabled);
        var optimize = UiLocalization.Text(_optimizeSurface.SelectedIndex switch { 1 => "背面", 2 => "两面", _ => "正面" });
        var evaluate = UiLocalization.Text(_evaluationSurface.SelectedIndex switch { 1 => "背面", 2 => "整片", _ => "正面" });
        _evaluationBadge.Text = enabled ? $"{UiLocalization.Text("优化表面")}：{optimize}；{UiLocalization.Text("评价对象")}：{evaluate}"
            + (_symmetricFilms.IsChecked == true ? "；" + UiLocalization.Text("对称双面（背面 = 正面）") : "")
            : UiLocalization.Text("旧单面验收：正面；整片显示设置不改变原验收对象。");
    }
    private void OnEditorLanguageChanged(object? sender, EventArgs args) { UpdateDuplexControls(); RefreshLayerEditorPresentation(); }
    private void SwitchFilmEditor(bool back)
    {
        if (_editingBack) _backEditorRows = _rows.ToArray(); else _frontEditorRows = _rows.ToArray();
        _editingBack = back; _rows.Clear();
        foreach (var row in back ? _backEditorRows : _frontEditorRows) _rows.Add(row);
        Renumber(); _table.SelectedItems.Clear();
    }
    private void LoadFilmEditors(Experiment d)
    {
        _frontEditorRows = d.Layers.Select(CreateRow).ToArray(); _backEditorRows = d.EffectiveBackLayers.Select(CreateRow).ToArray();
        if (d.Duplex is null || d.Duplex.Symmetric) _editingBack = false;
        _rows.Clear(); foreach (var row in _editingBack ? _backEditorRows : _frontEditorRows) _rows.Add(row); Renumber();
        _duplexEnabled.IsChecked = d.Duplex is not null; _symmetricFilms.IsChecked = d.Duplex?.Symmetric ?? false;
        _optimizeSurface.SelectedIndex = (int)(d.Duplex?.Optimize ?? CoatingDesignSurface.Front);
        _evaluationSurface.SelectedIndex = (int)(d.Duplex?.Evaluate ?? CoatingEvaluationSurface.WholePlate);
        _duplexProtocol.SelectedIndex = d.Duplex is null || d.Duplex.OptimizationVersion == 2 ? 1 : 0;
        _filmSideTabs.SelectedIndex = _editingBack ? 1 : 0; UpdateDuplexControls();
    }
    private Experiment ReadFilmEditors(Experiment d)
    {
        var front = FrontEditorRows.Select(r => r.ToLayer()).ToArray();
        if (_duplexEnabled.IsChecked != true) return d with { SchemaVersion = Experiment.CurrentSchema, Layers = front, BackLayers = null, Duplex = null };
        var symmetric = _symmetricFilms.IsChecked == true;
        var options = new DuplexCoatingOptions((CoatingDesignSurface)_optimizeSurface.SelectedIndex, (CoatingEvaluationSurface)_evaluationSurface.SelectedIndex, symmetric)
        { OptimizationVersion = _duplexProtocol.SelectedIndex == 1 ? 2 : null };
        return d with
        {
            SchemaVersion = Experiment.DuplexSchema,
            Layers = front,
            BackLayers = symmetric ? null : BackEditorRows.Select(r => r.ToLayer()).ToArray(),
            Duplex = options
        };
    }
}
