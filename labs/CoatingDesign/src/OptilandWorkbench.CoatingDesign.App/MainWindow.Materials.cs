using Avalonia.Controls;
using OptilandWorkbench.CoatingDesign.Engine;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed partial class MainWindow
{
    private Button? _cancel;
    private readonly ComboBox _distribution = new() { ItemsSource = new[] { "均匀 ±范围", "正态 1σ" }, SelectedIndex = 0 };
    private readonly ComboBox _correlation = new() { ItemsSource = new[] { "逐层独立", "同材料同步", "全部同步" }, SelectedIndex = 0 };
    private readonly CheckBox _varyStructure = new() { Content = "枚举 / 增删 / 换料", IsChecked = true };
    private void RefreshMaterials(Experiment d)
    {
        var names = _materials.Names.Concat(d.Materials.Select(m => m.Id)).Concat(d.Materials.Select(m => m.Name)).Distinct().Order().ToArray();
        foreach (var picker in new[] { _incident, _substrate, _low, _high, _layerMaterial }) picker.ItemsSource = names;
    }
    private async Task ManageMaterialsAsync()
    {
        var revision = _uiRevision;
        var d = ReadInputs();
        var material = d.Materials.FirstOrDefault(m => m.Id == _layerMaterial.Text || m.Name == _layerMaterial.Text);
        var editor = new MaterialEditorWindow(d.Materials, material)
        { FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight, FontStyle = FontStyle };
        var result = await editor.ShowDialog<MaterialSnapshot?>(this);
        if (result is null || revision != _uiRevision) return;
        var materials = d.Materials.Where(m => m.Id != result.Id).Append(result).ToArray();
        Load(d with { Materials = materials, Result = null, Candidates = null, Tolerance = null });
        _layerMaterial.Text = result.Id;
        _status.Text = "材料快照已更新，仅影响此实验；请选择用途或替换所选膜层，然后重新计算。";
    }
}
