using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly ComboBox _sceneMode = Choice("camera-scene-mode", "单材质曲线", "材质标签分区");
    private readonly ComboBox _materialChoice = new() { Name = "camera-material-choice", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBlock _materialInfo = new() { Name = "camera-material-info", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _saveMaterials = new() { Name = "camera-save-materials", Content = "保存材质场景" };
    private readonly Button _replaceMaterial = new() { Name = "camera-replace-material", Content = "指定反射率 CSV" };
    private readonly Button _clearMaterials = new() { Name = "camera-clear-materials", Content = "移除材质分区" };
    private readonly CameraFrameView _materialMapView = new(categorical: true) { Name = "camera-material-preview", IsVisible = false };
    private CameraMaterialScene? _materialScene, _plottedMaterialScene, _previewedMaterialScene;
    private bool _applyingMaterialScene;
    private bool IsMaterialMode => _sceneMode.SelectedIndex == (int)CameraSceneMode.MaterialMap;
    private int? SelectedMaterialId => _materialScene is not null && _materialChoice.SelectedIndex >= 0 && _materialChoice.SelectedIndex < _materialScene.Materials.Count
        ? _materialScene.Materials[_materialChoice.SelectedIndex].Id : null;

    private Control BuildMaterialSceneCard()
    {
        var import = new Button { Name = "camera-import-materials", Content = "导入材质分区" };
        var example = new Button { Name = "camera-material-example", Content = "三材质样例" };
        import.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "导入标签 PNG / 材质场景 JSON", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("材质标签与场景") { Patterns = new[] { "*.png", "*.json" } } } });
            if (!_closed && files.Count > 0) await ImportMaterialSceneFromPathAsync(files[0].Path.LocalPath);
        };
        example.Click += async (_, _) => await ImportMaterialSceneFromPathAsync(Path.Combine(AppContext.BaseDirectory, "Examples", "CameraImaging", "three-materials.camera-scene.json"));
        _replaceMaterial.Click += async (_, _) =>
        {
            var scene = _materialScene; var id = SelectedMaterialId;
            var path = await PickSensorFileAsync("为选中材质指定反射率 CSV", "*.csv");
            if (path is not null && id is not null && ReferenceEquals(scene, _materialScene)) await ReplaceMaterialReflectanceFromPathAsync(path, id.Value);
        };
        _saveMaterials.Click += async (_, _) =>
        {
            var scene = _materialScene; if (scene is null) return;
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "保存材质场景", SuggestedFileName = "scene.camera-scene.json", DefaultExtension = "json" });
                if (file is null || _closed) return;
                await _application.CameraImaging.SaveMaterialSceneAsync(scene, file.Path.LocalPath);
                if (!_closed) _status.Text = "已保存标签与各材质反射率；未指定的材质仍保留未指定状态。";
            }
            catch (Exception ex) { if (!_closed) _status.Text = "材质场景保存失败：" + ex.Message; }
        };
        _clearMaterials.Click += (_, _) =>
        {
            _applyingMaterialScene = true;
            try { _materialScene = null; _sceneMode.SelectedIndex = 0; _materialChoice.ItemsSource = Array.Empty<string>(); }
            finally { _applyingMaterialScene = false; }
            Invalidate("已恢复单材质输入，A 对照保留。");
        };
        _sceneMode.SelectionChanged += (_, _) => { if (!_applyingMaterialScene) Invalidate("场景反射率模式已改变；A 保留，请重新运行 B。"); };
        _materialChoice.SelectionChanged += (_, _) => UpdateMaterialControls();
        _materialMapView.Title = "材质标签 · 显示色仅表示 ID，不是物理反射率";
        Closed += (_, _) => _materialMapView.Dispose();
        return Group("材质反射率分区", new Control[]
        {
            Field("场景反射率", _sceneMode), LaboratoryPresentation.Commands(import, example, _saveMaterials, _clearMaterials),
            _materialInfo, Field("指定材质", _materialChoice), _replaceMaterial,
            LaboratoryPresentation.Hint("PNG 使用不透明 8 位灰度：R=G=B=ID（0–15），每个 ID 指定一条反射谱。JSON 可连同反射谱保存。标签按像素中心最近邻映射；不把普通照片自动解释成材质。三波段模式保留这些设置但暂不使用。")
        });
    }

    internal async Task<bool> ImportMaterialSceneFromPathAsync(string path)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial;
        try
        {
            var scene = await _application.CameraImaging.ImportMaterialSceneAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial) return false;
            _applyingMaterialScene = true;
            try { _materialScene = scene; RebuildMaterialChoices(); _sceneMode.SelectedIndex = 1; }
            finally { _applyingMaterialScene = false; }
            Invalidate("材质分区已导入；PNG 尚需为各 ID 指定反射率。A 保留，请运行新的 B。"); return true;
        }
        catch (Exception ex)
        { if (!_closed && generation == _generation && serial == _sensorFileLoadSerial) _status.Text = "材质分区导入失败，现有参数与 A/B 保留：" + ex.Message; return false; }
    }
    internal async Task<bool> ReplaceMaterialReflectanceFromPathAsync(string path, int id)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial; var scene = _materialScene;
        try
        {
            if (scene is null || !scene.Materials.Any(m => m.Id == id)) throw new InvalidOperationException("请先选择有效材质。");
            var curve = await _application.CameraImaging.ImportReflectanceAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial || !ReferenceEquals(scene, _materialScene)) return false;
            _materialScene = scene with { Modified = true, Materials = Array.AsReadOnly(scene.Materials.Select(m => m.Id == id ? m with { Reflectance = curve } : m).ToArray()) };
            RebuildMaterialChoices(id); Invalidate("材质反射率已更新，A 的标签与反射率保持不变。"); return true;
        }
        catch (Exception ex)
        { if (!_closed && generation == _generation && serial == _sensorFileLoadSerial) _status.Text = "材质反射率导入失败，现有数据与 A/B 保留：" + ex.Message; return false; }
    }
    private void RebuildMaterialChoices(int? selected = null)
    {
        _materialChoice.ItemsSource = _materialScene?.Materials.Select(m => $"{m.Id} · {m.Name}").ToArray() ?? Array.Empty<string>();
        _materialChoice.SelectedIndex = _materialScene is null ? -1 : Math.Max(0, _materialScene.Materials.ToList().FindIndex(m => m.Id == selected));
    }
    private string MaterialSceneSummary() => _materialScene is null ? "尚未导入材质分区"
        : $"{_materialScene.Name} · {_materialScene.Width} × {_materialScene.Height} · {_materialScene.Materials.Count} 材质"
            + (_materialScene.Modified ? "（反射率已修改）" : "") + "\n" + OriginText(_materialScene.Origin);
    private void UpdateMaterialControls()
    {
        _materialInfo.Text = MaterialSceneSummary();
        if (SelectedMaterialId is { } id && _materialScene is { } scene)
            _materialInfo.Text += "\n选中反射率：" + CurveInfo(scene.Materials.Single(m => m.Id == id).Reflectance);
        ControlAvailability.Set(_saveMaterials, _materialScene is not null, "请先导入材质分区。");
        ControlAvailability.Set(_clearMaterials, _materialScene is not null, "当前没有材质分区。");
        ControlAvailability.Set(_replaceMaterial, SelectedMaterialId is not null, "请先导入分区并选择材质 ID。");
        ControlAvailability.Set(_materialChoice, _materialScene is not null, "请先导入材质分区。");
        if (ReferenceEquals(_previewedMaterialScene, _materialScene)) return;
        _previewedMaterialScene = _materialScene; _materialMapView.IsVisible = _materialScene is not null;
        if (_materialScene is null) { _materialMapView.Set(null); return; }
        var width = Math.Min(256, _materialScene.Width); var height = Math.Min(256, _materialScene.Height); var bytes = new byte[width * height * 3];
        var colors = Enumerable.Range(0, 16).Select(AnalysisPlotControl.SeriesColor).ToArray();
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var sx = (int)((2L * x + 1) * _materialScene.Width / (2L * width)); var sy = (int)((2L * y + 1) * _materialScene.Height / (2L * height));
            var color = colors[_materialScene.MaterialIds[sy * _materialScene.Width + sx]]; var offset = (y * width + x) * 3;
            bytes[offset] = color.R; bytes[offset + 1] = color.G; bytes[offset + 2] = color.B;
        }
        _materialMapView.Set(new(width, height, bytes));
    }
    private void PlotMaterialReflectances()
    {
        var materials = _materialScene?.Materials.Where(m => m.Reflectance is not null).ToArray() ?? Array.Empty<CameraSceneMaterial>();
        _reflectanceLegend.Children.Clear(); _reflectancePlot.IsVisible = _reflectanceLegend.IsVisible = materials.Length > 0;
        if (materials.Length == 0) { _reflectancePlot.Series = Array.Empty<AnalysisSeriesDto>(); return; }
        _reflectancePlot.PlotOptions = new(Title: "各材质反射率 · 源平面", XMinimum: materials.Min(m => m.Reflectance!.Samples[0].WavelengthNanometers),
            XMaximum: materials.Max(m => m.Reflectance!.Samples[^1].WavelengthNanometers), YMinimum: 0, YMaximum: 1, ShowLegend: false);
        var series = materials.Select(m => new AnalysisSeriesDto("波长", "反射率", m.Reflectance!.Samples.Select(s => new AnalysisPointDto(s.WavelengthNanometers, s.Value)).ToArray(),
            Name: $"{m.Id} · {m.Name}", ColorIndex: m.Id, XQuantity: AnalysisAxisQuantity.Wavelength, XUnit: AnalysisAxisUnit.Nanometer,
            YQuantity: AnalysisAxisQuantity.Probability, YUnit: AnalysisAxisUnit.Dimensionless)).ToArray();
        _reflectancePlot.Series = series; var enabled = Enumerable.Repeat(true, series.Length).ToArray();
        for (var i = 0; i < series.Length; i++)
        {
            var index = i; var check = new CheckBox { Name = "camera-material-visible-" + materials[i].Id,
                Content = new TextBlock { Text = series[i].Name, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 220 }, IsChecked = true };
            check.IsCheckedChanged += (_, _) => { enabled[index] = check.IsChecked == true; _reflectancePlot.SetVisibleSeries(series.Where((_, j) => enabled[j]).ToArray()); };
            _reflectanceLegend.Children.Add(check);
        }
    }
}
