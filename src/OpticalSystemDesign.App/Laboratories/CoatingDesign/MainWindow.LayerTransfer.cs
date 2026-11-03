using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private static readonly DataFormat<string> LayerClipboardFormat = DataFormat.CreateStringApplicationFormat("OpticalSystemDesign.CoatingLayers.v1");
    private CoatingFormulaWindow? _formulaWindow;
    private async Task ShowStackFormulaAsync()
    {
        var basis = ReadInputs(); var revision = _uiRevision;
        var dialog = new CoatingFormulaWindow(basis, _editingBack) { FontFamily = FontFamily, FontSize = FontSize };
        _formulaWindow = dialog;
        FilmLayer[]? layers;
        try { layers = await dialog.ShowDialog<FilmLayer[]?>(this); }
        finally { _formulaWindow = null; }
        if (layers is null) return;
        if (revision != _uiRevision) throw new InvalidOperationException("当前输入已更新，请重新预览公式后应用。");
        ApplyIncomingLayers(layers, basis.Materials, replaceAll: true);
    }
    private async Task CopyLayersAsync(bool cut = false)
    {
        _table.CommitEdit();
        var rows = _table.SelectedItems.OfType<LayerRow>().OrderBy(r => _rows.IndexOf(r)).ToArray();
        if (rows.Length == 0) rows = _rows.ToArray();
        if (rows.Length == 0) throw new ArgumentException("没有可复制的膜层。");
        var layers = rows.Select(r => r.ToLayer()).ToArray(); var revision = _uiRevision;
        var basis = _session.Current with { ReferenceWavelengthNm = Value("reference") };
        var payload = CoatingLayerClipboard.Encode(basis, layers);
        var clipboard = Clipboard ?? throw new InvalidOperationException("剪贴板不可用。");
        var item = new DataTransferItem(); item.SetText(CoatingLayerClipboard.PlainText(layers)); item.Set(LayerClipboardFormat, payload);
        var data = new DataTransfer(); data.Add(item);
        await clipboard.SetDataAsync(data);
        await clipboard.FlushAsync();
        if (cut)
        {
            if (revision != _uiRevision) throw new InvalidOperationException("膜层已修改，本次只复制，没有剪切。");
            _editorTransaction++;
            try { foreach (var row in rows) _rows.Remove(row); Renumber(); Changed(); }
            finally { _editorTransaction--; }
            RecordEditorHistory(null);
        }
    }
    private async Task PasteLayersAsync()
    {
        _table.CommitEdit(); var revision = _uiRevision;
        var clipboard = Clipboard ?? throw new InvalidOperationException("剪贴板不可用。");
        using var data = await clipboard.TryGetDataAsync();
        if (data is null) throw new ArgumentException("剪贴板没有膜层数据。");
        var rich = await data.TryGetValueAsync(LayerClipboardFormat);
        var text = rich ?? await data.TryGetTextAsync() ?? throw new ArgumentException("剪贴板没有膜层文本。");
        if (revision != _uiRevision) throw new InvalidOperationException("输入已修改，请再次粘贴。");
        var pasted = CoatingLayerClipboard.Decode(_session.Current, text, rich is not null);
        ApplyIncomingLayers(pasted.Layers, pasted.Materials);
    }
    private void ApplyIncomingLayers(FilmLayer[] incoming, MaterialSnapshot[] materials, bool replaceAll = false)
    {
        if (_editingBack && _symmetricFilms.IsChecked == true) throw new ArgumentException("对称背面不可直接编辑。");
        if ((replaceAll ? 0 : _rows.Count) + incoming.Length > 200) throw new ArgumentException("应用后每面不能超过200层，未修改任何膜层。");
        var basis = _session.Current with { Materials = materials }; basis.Validate();
        var ids = materials.Select(m => m.Id).ToHashSet();
        if (incoming.Any(l => !ids.Contains(l.MaterialId) || !double.IsFinite(l.ThicknessNm) || l.ThicknessNm <= 0 || l.ThicknessNm > 100000))
            throw new ArgumentException("导入膜层的材料或厚度无效。");
        var index = _table.SelectedItems.OfType<LayerRow>().Select(r => _rows.IndexOf(r)).DefaultIfEmpty(0).Min();
        var rows = incoming.Select(CreateRow).ToArray();
        _editorTransaction++;
        try
        {
            _session.Replace(basis with { Result = null, Candidates = null, Tolerance = null });
            if (replaceAll) { _rows.Clear(); index = 0; }
            foreach (var row in rows) _rows.Insert(index++, row);
            Renumber(); _table.SelectedItems.Clear(); foreach (var row in rows) _table.SelectedItems.Add(row);
            Changed();
        }
        finally { _editorTransaction--; }
        RecordEditorHistory(null);
    }
    private void HandleEditorClipboardKey(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Key is not (Key.C or Key.X or Key.V) || e.Source is not Control source
            || source is TextBox || !ReferenceEquals(source, _table) && !source.GetVisualAncestors().Any(c => ReferenceEquals(c, _table))) return;
        e.Handled = true;
        TrackOperation(e.Key == Key.V ? PasteLayersAsync : () => CopyLayersAsync(e.Key == Key.X));
    }
}
