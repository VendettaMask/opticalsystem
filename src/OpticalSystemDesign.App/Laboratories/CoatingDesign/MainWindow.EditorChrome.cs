using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private readonly List<Control> _editorPopupContents = [];
    internal IEnumerable<Control> EditorPopupContents => _editorPopupContents;
    private readonly ObservableCollection<string> _rowMaterialIds = [];
    private readonly ComboBox _surfaceMode = new() { ItemsSource = new[] { "正面", "背面", "双面独立", "对称双面" }, MinWidth = 126, SelectedIndex = 0 };
    private readonly CheckBox _ignoreOtherSide = new() { Content = "忽略另一面", IsChecked = true };
    private readonly TextBlock _substrateBoundary = new() { FontStyle = FontStyle.Italic, Margin = new(10, 2) };
    private readonly TextBlock _outsideBoundary = new() { FontStyle = FontStyle.Italic, Margin = new(10, 2) };
    private readonly Border _substrateBand = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    private Button? _lockAllLayers, _copyToOtherSide;
    private DropDownButton? _layerToolsMenu;
    private Flyout? _materialFlyout, _thicknessFlyout;
    private bool _syncingSurfaceMode;

    private Control BuildCompactEditorHeader()
    {
        AutomationProperties.SetName(_surfaceMode, "表面模式"); AutomationProperties.SetName(_ignoreOtherSide, "忽略另一面");
        _surfaceMode.SelectionChanged += (_, _) => ApplySurfaceBar();
        _ignoreOtherSide.IsCheckedChanged += (_, _) => ApplySurfaceBar();
        var modes = new StackPanel { Name = "CoatingSurfaceModeBar", Orientation = Orientation.Horizontal, Spacing = 12, Margin = new(10, 5) };
        modes.Children.Add(new TextBlock { Text = "表面：", VerticalAlignment = VerticalAlignment.Center });
        modes.Children.Add(_surfaceMode); modes.Children.Add(_ignoreOtherSide);
        var tools = new StackPanel { Name = "CoatingLayerToolbar", Orientation = Orientation.Horizontal, Spacing = 5, Margin = new(10, 5) };
        var add = ActionButton("添加层", () => EditLayer("add")); add.Content = ToolContent("coating-layer-add", "膜层"); tools.Children.Add(add);
        tools.Children.Add(CommandDivider());
        var reverse = ActionButton("反转层序", () => EditLayer("reverseAll")); reverse.Content = ToolContent("coating-layer-reverse", "反转"); tools.Children.Add(reverse);
        tools.Children.Add(CommandDivider());
        _lockAllLayers = ActionButton("全部锁定", LockAllLayersAsync); tools.Children.Add(_lockAllLayers);
        _copyToOtherSide = ActionButton("复制到另一面", CopyToOtherSideAsync); tools.Children.Add(_copyToOtherSide);
        tools.Children.Add(CommandDivider());
        _materialFlyout = BuildMaterialFlyout(); _thicknessFlyout = BuildThicknessFlyout();
        var menu = new MenuFlyout(); foreach (var item in EditorMenuItems()) menu.Items.Add(item);
        _layerToolsMenu = new DropDownButton { Content = "工具…", Flyout = menu, Margin = new(0, 0, 0, 4) };
        AutomationProperties.SetName(_layerToolsMenu, "膜层工具"); tools.Children.Add(_layerToolsMenu);
        var context = new ContextMenu(); foreach (var item in EditorMenuItems()) context.Items.Add(item);
        _table.ContextMenu = context;
        _table.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_table).Properties.IsRightButtonPressed || e.Source is not Control source) return;
            var row = source.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault()?.DataContext as LayerRow;
            if (row is not null && !_table.SelectedItems.Contains(row)) _table.SelectedItem = row;
        };
        return new StackPanel { Name = "CoatingCompactEditorHeader", Children = { _filmSideTabs, HorizontalStrip(modes), HorizontalStrip(tools) } };
    }
    private static ScrollViewer HorizontalStrip(Control content) => new()
    { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private void ApplySurfaceBar()
    {
        if (_loading || _syncingSurfaceMode || _surfaceMode.SelectedIndex < 0) return;
        var mode = _surfaceMode.SelectedIndex; var ignore = _ignoreOtherSide.IsChecked == true && mode < 2;
        _loading = true;
        try
        {
            _symmetricFilms.IsChecked = mode == 3;
            if (mode > 0 || !ignore) _duplexEnabled.IsChecked = true;
            _optimizeSurface.SelectedIndex = mode switch { 1 => 1, 2 or 3 => 2, _ => 0 };
            _evaluationSurface.SelectedIndex = ignore ? mode : 2;
            if (ignore && _editingBack != (mode == 1)) { SwitchFilmEditor(mode == 1); _filmSideTabs.SelectedIndex = mode == 1 ? 1 : 0; }
        }
        finally { _loading = false; }
        ChangeDuplexMode();
    }
    private void RefreshCompactSurfaceMode(bool enabled)
    {
        var mode = !enabled ? 0 : _symmetricFilms.IsChecked == true ? 3 : _optimizeSurface.SelectedIndex;
        var ignore = !enabled || _evaluationSurface.SelectedIndex != 2;
        _syncingSurfaceMode = true;
        try { _surfaceMode.SelectedIndex = mode; _ignoreOtherSide.IsChecked = ignore; }
        finally { _syncingSurfaceMode = false; }
        _ignoreOtherSide.IsEnabled = mode < 2;
        ((TabItem)_filmSideTabs.Items[0]!).IsEnabled = !ignore || mode == 0;
        ((TabItem)_filmSideTabs.Items[1]!).IsEnabled = _symmetricFilms.IsChecked != true && (!ignore || mode == 1);
        ToolTip.SetTip(_ignoreOtherSide, "勾选时仅评价所选表面；取消后评价整片。被忽略面的膜层保留，不删除。");
        RefreshLayerEditorPresentation();
    }
    private Control BuildLayerViewport()
    {
        _substrateBand.Child = _substrateBoundary;
        _substrateBand.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        var body = new Grid(); body.Children.Add(_table); body.Children.Add(_substrateBand);
        var outside = new Border { Child = _outsideBoundary, BorderThickness = new(0, 1, 0, 0) };
        outside.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        outside.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var viewport = new Grid { RowDefinitions = new("*,Auto") }; viewport.Children.Add(body); Grid.SetRow(outside, 1); viewport.Children.Add(outside);
        return viewport;
    }
    private void RefreshLayerEditorPresentation()
    {
        if (_table is null || _substrate is null) return;
        string NameOf(string? id) => _session.Current.Materials.FirstOrDefault(m => m.Id == id)?.Name ?? id ?? "";
        _substrateBoundary.Text = "▲ 基底：" + NameOf(_substrate.Text);
        _outsideBoundary.Text = (_editingBack ? "▼ 出射介质：" : "▼ 入射介质：") + NameOf(_editingBack ? _exit?.Text : _incident.Text);
        _substrateBoundary.FontSize = _outsideBoundary.FontSize = _table.FontSize;
        var ids = _session.Current.Materials.Concat(_materials.Films).DistinctBy(m => m.Id).OrderBy(m => m.Name).Select(m => m.Id).ToArray();
        if (!_rowMaterialIds.SequenceEqual(ids))
        { _rowMaterialIds.Clear(); foreach (var id in ids) _rowMaterialIds.Add(id); foreach (var row in _rows) row.RefreshMaterialIdentity(); }
        if (_lockAllLayers is not null)
        {
            var locked = _rows.Count > 0 && _rows.All(r => !r.Variable);
            _lockAllLayers.Content = ToolContent(locked ? "coating-layer-unlock" : "coating-layer-lock", locked ? "全部解锁" : "全部锁定");
            _lockAllLayers.IsEnabled = _rows.Count > 0;
        }
        if (_copyToOtherSide is not null)
        {
            _copyToOtherSide.Content = _editingBack ? "复制到正面" : "复制到背面";
            _copyToOtherSide.IsEnabled = _symmetricFilms.IsChecked != true && _rows.Count > 0;
        }
    }
    private Task LockAllLayersAsync()
    {
        _table.CommitEdit(); var makeVariable = _rows.Count > 0 && _rows.All(r => !r.Variable);
        _editorTransaction++;
        try { foreach (var row in _rows) row.Variable = makeVariable; Changed(); }
        finally { _editorTransaction--; }
        RecordEditorHistory(null); return Task.CompletedTask;
    }
    private Task CopyToOtherSideAsync()
    {
        _table.CommitEdit();
        if (_symmetricFilms.IsChecked == true) throw new ArgumentException("对称双面已链接，请先解除对称再复制。");
        var layers = _rows.Select(r => r.ToLayer()).ToArray(); var copies = layers.Select(CreateRow).ToArray();
        _editorTransaction++; _loading = true;
        try
        {
            if (_editingBack) _frontEditorRows = copies; else _backEditorRows = copies;
            if (_duplexEnabled.IsChecked != true)
            { _duplexEnabled.IsChecked = true; _optimizeSurface.SelectedIndex = _editingBack ? 1 : 0; _evaluationSurface.SelectedIndex = _editingBack ? 1 : 0; }
            UpdateDuplexControls();
        }
        finally { _loading = false; _editorTransaction--; }
        _editorTransaction++; try { Changed(); } finally { _editorTransaction--; }
        RecordEditorHistory(null); return Task.CompletedTask;
    }
    private Task ChangeInlineMaterialAsync(LayerRow row, string id)
    {
        if (!_rows.Contains(row) || row.MaterialId == id) return Task.CompletedTask;
        var current = _session.Current;
        var material = current.Materials.Concat(_materials.Films).FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException("找不到所选材料快照。");
        _editorTransaction++;
        try
        {
            if (!current.Materials.Any(m => m.Id == id)) _session.Replace(current with { Materials = current.Materials.Append(material).ToArray(), Result = null, Candidates = null, Tolerance = null });
            row.MaterialId = id;
        }
        finally { _editorTransaction--; }
        RecordEditorHistory(null); return Task.CompletedTask;
    }
    private Task RowCommandAsync(LayerRow row, string action)
    {
        _table.CommitEdit(); _table.SelectedItems.Clear(); _table.SelectedItem = row;
        return EditLayer(action == "up" ? "down" : action == "down" ? "up" : action);
    }
    private Flyout BuildMaterialFlyout()
    {
        _layerMaterial.Width = 340; AutomationProperties.SetName(_layerMaterial, "膜层材料");
        return EditorFlyout(new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "批量替换材料" }, _layerMaterial, ActionButton("替换材料", () => EditLayer("material")) } });
    }
    private Flyout BuildThicknessFlyout()
    {
        AutomationProperties.SetName(_batchValue, "批量厚度值"); AutomationProperties.SetName(_batchUnit, "批量厚度单位");
        return EditorFlyout(new StackPanel
        {
            Spacing = 10,
            Children = { new TextBlock { Text = "批量厚度" },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _batchValue, _batchUnit } },
            ActionButton("设定所选厚度", () => EditLayer("setThickness")), ActionButton("所选厚度乘倍数", () => EditLayer("multiply")) }
        });
    }
    private Flyout EditorFlyout(Control content)
    {
        _editorPopupContents.Add(content); var flyout = new Flyout { Content = content };
        flyout.Opening += (_, _) => { TextElement.SetFontFamily(content, FontFamily); TextElement.SetFontSize(content, FontSize); };
        return flyout;
    }
    private IEnumerable<Control> EditorMenuItems()
    {
        MenuItem Item(string name, Func<Task> action)
        { var item = new MenuItem { Header = UiLocalization.Text(name) }; AutomationProperties.SetName(item, name); item.Click += (_, _) => TrackOperation(action); return item; }
        var undo = Item("撤销", () => { RestoreEditorHistory(false); return Task.CompletedTask; }); _undoCommands.Add(undo); yield return undo;
        var redo = Item("重做", () => { RestoreEditorHistory(true); return Task.CompletedTask; }); _redoCommands.Add(redo); yield return redo;
        yield return new Separator();
        yield return Item("复制", () => EditLayer("copy")); yield return Item("删除", () => EditLayer("remove"));
        // Menu commands keep the existing multi-selection move operation available.
        // The projection reverses physical order, so map directions to the screen.
        yield return Item("所选上移", () => EditLayer("down")); yield return Item("所选下移", () => EditLayer("up"));
        yield return Item("复制到剪贴板", () => CopyLayersAsync()); yield return Item("剪切膜层", () => CopyLayersAsync(true)); yield return Item("粘贴膜层", PasteLayersAsync);
        yield return new Separator();
        foreach (var (name, action) in new[] { ("全选", "selectAll"), ("反转层序", "reverse"), ("固定所选", "fix"), ("释放所选", "vary") }) yield return Item(name, () => EditLayer(action));
        yield return Item("批量厚度", () => { Dispatcher.UIThread.Post(() => _thicknessFlyout!.ShowAt(_layerToolsMenu!)); return Task.CompletedTask; });
        yield return Item("批量替换材料", () => { Dispatcher.UIThread.Post(() => _materialFlyout!.ShowAt(_layerToolsMenu!)); return Task.CompletedTask; });
        yield return Item("堆栈公式", ShowStackFormulaAsync);
        yield return new Separator();
        yield return Item("表面与评价设置", () => ShowSettingsAsync(4));
    }
}
