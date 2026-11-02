using System.Globalization;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private sealed record EditorState(Experiment Base, Dictionary<string, string?> Numbers, string?[] Texts,
        int[] Indices, bool?[] Toggles, LayerEditState[] Rows, LayerEditState[] BackRows, bool EditingBack, int[] Selected);
    private readonly List<EditorState> _undo = [], _redo = [];
    private EditorState? _historyCurrent;
    private string? _historyKey;
    private bool _historyRestoring;
    private int _editorTransaction;
    private Button? _undoButton, _redoButton;
    private readonly TextBox _batchValue = new() { Text = "1", Width = 80 };
    private readonly ComboBox _batchUnit = new() { ItemsSource = new[] { "nm", "OT", "QW", "FW" }, SelectedIndex = 0, Width = 80 };

    private void AddEditorTools(Panel tools)
    {
        _undoButton = LayerTool("撤销", "undo-2", () => { RestoreEditorHistory(false); return Task.CompletedTask; });
        _redoButton = LayerTool("重做", "redo-2", () => { RestoreEditorHistory(true); return Task.CompletedTask; });
        tools.Children.Add(_undoButton); tools.Children.Add(_redoButton);
        foreach (var (text, action) in new[] { ("全选", "selectAll"), ("反转层序", "reverse"), ("固定所选", "fix"), ("释放所选", "vary") })
            tools.Children.Add(ActionButton(text, () => EditLayer(action)));
        AutomationProperties.SetName(_batchValue, "批量厚度值"); AutomationProperties.SetName(_batchUnit, "批量厚度单位");
        tools.Children.Add(_batchValue); tools.Children.Add(_batchUnit);
        tools.Children.Add(ActionButton("设定所选厚度", () => EditLayer("setThickness")));
        tools.Children.Add(ActionButton("所选厚度乘倍数", () => EditLayer("multiply")));
        tools.Children.Add(ActionButton("堆栈公式", ShowStackFormulaAsync));
        tools.Children.Add(ActionButton("复制到剪贴板", () => CopyLayersAsync()));
        tools.Children.Add(ActionButton("剪切膜层", () => CopyLayersAsync(true)));
        tools.Children.Add(ActionButton("粘贴膜层", PasteLayersAsync));
    }
    private EditorState CaptureEditorState()
    {
        var basis = _session.Current with { Result = null, Candidates = null, Tolerance = null };
        return new(basis, _numbers.ToDictionary(p => p.Key, p => p.Value.Text),
            [_name.Text, _stopBands.Text, _incident.Text, _substrate.Text, _low.Text, _high.Text, _exit.Text],
            [_kind.SelectedIndex, _polarization.SelectedIndex, _algorithm.SelectedIndex, _distribution.SelectedIndex, _correlation.SelectedIndex, _narrowProtocol.SelectedIndex, _optimizeSurface.SelectedIndex, _evaluationSurface.SelectedIndex, _duplexProtocol.SelectedIndex],
            [_varyStructure.IsChecked, _includeBackSurface.IsChecked, _duplexEnabled.IsChecked, _symmetricFilms.IsChecked],
            FrontEditorRows.Select(r => r.Capture()).ToArray(), BackEditorRows.Select(r => r.Capture()).ToArray(), _editingBack,
            _table.SelectedItems.OfType<LayerRow>().Select(r => _rows.IndexOf(r)).Where(i => i >= 0).ToArray());
    }
    private static string EditorIdentity(EditorState s) => JsonSerializer.Serialize(new
    { s.Base.Id, s.Base.Materials, s.Numbers, s.Texts, s.Indices, s.Toggles, s.Rows, s.BackRows });
    private void RecordEditorHistory(string? key)
    {
        if (_loading || _historyRestoring || _editorTransaction != 0) return;
        ScheduleRecovery();
        var next = CaptureEditorState();
        if (_historyCurrent is { } old && old.Base.Id == next.Base.Id)
        {
            if (EditorIdentity(old) == EditorIdentity(next)) { _historyCurrent = next; if (key is null) _historyKey = null; return; }
            if (key is null || key != _historyKey) _undo.Add(old);
            if (_undo.Count > 50) _undo.RemoveAt(0);
        }
        else _undo.Clear();
        _redo.Clear(); _historyCurrent = next; _historyKey = key; RefreshHistoryButtons();
    }
    private void RefreshHistoryButtons()
    {
        if (_undoButton is not null) ControlAvailability.Set(_undoButton, _undo.Count > 0, "当前没有可撤销的镀膜编辑。");
        if (_redoButton is not null) ControlAvailability.Set(_redoButton, _redo.Count > 0, "当前没有可重做的镀膜编辑。");
    }
    private void HandleEditorHistoryKey(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Key is not (Key.Z or Key.Y)) return;
        e.Handled = true;
        RestoreEditorHistory(e.Key == Key.Y || (e.KeyModifiers & KeyModifiers.Shift) != 0);
    }
    private void RestoreEditorHistory(bool redo)
    {
        _table.CommitEdit();
        var source = redo ? _redo : _undo; var target = redo ? _undo : _redo;
        if (source.Count == 0 || _historyCurrent is null) return;
        var state = source[^1]; source.RemoveAt(source.Count - 1); target.Add(_historyCurrent);
        ApplyEditorState(state);
        _historyCurrent = state; _historyKey = null;
        RefreshHistoryButtons();
    }
    private void ApplyEditorState(EditorState state)
    {
        ValidateEditorState(state);
        _historyRestoring = true;
        try
        {
            Load(state.Base);
            _loading = true;
            foreach (var p in state.Numbers) _numbers[p.Key].Text = p.Value;
            _name.Text = state.Texts[0]; _stopBands.Text = state.Texts[1]; _incident.Text = state.Texts[2];
            _substrate.Text = state.Texts[3]; _low.Text = state.Texts[4]; _high.Text = state.Texts[5]; _exit.Text = state.Texts[6];
            _kind.SelectedIndex = state.Indices[0]; _polarization.SelectedIndex = state.Indices[1]; _algorithm.SelectedIndex = state.Indices[2];
            _distribution.SelectedIndex = state.Indices[3]; _correlation.SelectedIndex = state.Indices[4]; _narrowProtocol.SelectedIndex = state.Indices[5];
            _varyStructure.IsChecked = state.Toggles[0]; _includeBackSurface.IsChecked = state.Toggles[1];
            _duplexEnabled.IsChecked = state.Toggles[2]; _symmetricFilms.IsChecked = state.Toggles[3];
            _optimizeSurface.SelectedIndex = state.Indices[6]; _evaluationSurface.SelectedIndex = state.Indices[7];
            _duplexProtocol.SelectedIndex = state.Indices[8];
            LayerRow RestoreRow(LayerEditState row) { var restored = CreateRow(row.Original); restored.Restore(row); return restored; }
            _frontEditorRows = state.Rows.Select(RestoreRow).ToArray(); _backEditorRows = state.BackRows.Select(RestoreRow).ToArray();
            _editingBack = state.EditingBack; _filmSideTabs.SelectedIndex = _editingBack ? 1 : 0;
            _rows.Clear(); foreach (var row in _editingBack ? _backEditorRows : _frontEditorRows) _rows.Add(row);
            Renumber(); foreach (var index in state.Selected.Where(i => i >= 0 && i < _rows.Count)) _table.SelectedItems.Add(_rows[index]);
            UpdateDuplexControls();
        }
        finally { _loading = false; _historyRestoring = false; }
        // Invalidate the asynchronous revision before any old result can be delivered.
        _editorTransaction++; try { Changed(); Visibility(); } finally { _editorTransaction--; }
        ScheduleRecovery();
    }
    private void EditSelectedLayers(string action)
    {
        _table.CommitEdit();
        if (action == "selectAll") { _table.SelectAll(); _historyKey = null; return; }
        var selected = _table.SelectedItems.OfType<LayerRow>().OrderBy(r => _rows.IndexOf(r)).ToArray();
        if (action != "add" && selected.Length == 0) return;
        var indices = selected.Select(r => _rows.IndexOf(r)).ToArray();
        var d = _session.Current;
        var material = action is "add" or "material" ? d.Materials.FirstOrDefault(m => m.Id == _layerMaterial.Text)
            ?? _materials.Select(_layerMaterial.Text ?? "") : null;
        var copies = action == "copy" ? selected.Select(r => r.ToLayer()).ToArray() : [];
        if (_rows.Count + (action == "add" ? 1 : copies.Length) > 200) throw new ArgumentException("每面最多200层。");
        double[]? thickness = null;
        if (action is "multiply" or "setThickness")
        {
            if (!double.TryParse(_batchValue.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value <= 0)
                throw new ArgumentException("批量厚度值必须为正有限数值。");
            RefreshLayerOpticalThickness();
            thickness = selected.Select(r => action == "multiply" ? r.ToLayer().ThicknessNm * value
                : r.PhysicalFor(value, _batchUnit.SelectedIndex switch { 1 => "OT", 2 => "QW", 3 => "FW", _ => "nm" })).ToArray();
            if (thickness.Any(v => !double.IsFinite(v) || v <= 0 || v > 100000)) throw new ArgumentException("批量操作会使膜厚超出0–100000 nm，未修改任何膜层。");
        }
        _editorTransaction++;
        try
        {
            if (material is not null) _session.Replace(d with { Materials = d.Materials.Append(material).DistinctBy(m => m.Id).ToArray(), Result = null });
            if (action == "add") AddRow(new(material!.Id, 100));
            if (action == "material") foreach (var row in selected) row.MaterialId = material!.Id;
            if (action == "copy") foreach (var layer in copies) AddRow(layer);
            if (action == "remove") foreach (var row in selected) _rows.Remove(row);
            if (action == "up") foreach (var i in indices) if (i > 0 && !selected.Contains(_rows[i - 1])) _rows.Move(i, i - 1);
            if (action == "down") foreach (var i in indices.Reverse()) if (i < _rows.Count - 1 && !selected.Contains(_rows[i + 1])) _rows.Move(i, i + 1);
            if (action == "reverse") for (var j = 0; j < indices.Length / 2; j++) { var a = indices[j]; var b = indices[^(j + 1)]; (_rows[a], _rows[b]) = (_rows[b], _rows[a]); }
            if (action is "fix" or "vary") foreach (var row in selected) row.Variable = action == "vary";
            if (thickness is not null) for (var j = 0; j < selected.Length; j++) selected[j].SetPhysical(thickness[j]);
            Renumber(); _table.SelectedItems.Clear(); foreach (var row in selected.Where(_rows.Contains)) _table.SelectedItems.Add(row);
            Changed();
        }
        finally { _editorTransaction--; }
        RecordEditorHistory(null);
    }
}
