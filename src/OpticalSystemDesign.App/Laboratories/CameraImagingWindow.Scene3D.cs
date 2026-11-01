using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private Camera3DScene? _scene3D;
    private Control? _scene3DCard, _legacySpectraCard, _legacyMaterialsCard;
    private readonly TextBlock _scene3DInfo = new() { Name = "camera-scene3d-info", TextWrapping = TextWrapping.Wrap };
    private bool _scene3DWasActive;
    private readonly CheckBox _scene3DFresnel = new() { Name = "camera-scene3d-fresnel", Content = "计算裸界面 Fresnel 损失", IsChecked = true };
    private (int Optics, int Aberration, int Spectrum) _previous2D = (0, 1, 0);
    private bool IsScene3D => _source.SelectedIndex == (int)CameraSource.Scene3D;

    private Control BuildScene3DCard()
    {
        _scene3D = _application.CameraImaging.CreateExampleScene3D();
        var example = new Button { Name = "camera-scene3d-example", Content = "载入示例" };
        var import = new Button { Name = "camera-scene3d-import", Content = "打开三维场景" };
        var save = new Button { Name = "camera-scene3d-save", Content = "保存三维场景" };
        example.Click += (_, _) => { SetScene3D(_application.CameraImaging.CreateExampleScene3D()); Invalidate("示例三维场景已载入，请运行 B。"); };
        import.Click += async (_, _) => { var path = await PickSensorFileAsync("打开三维相机场景", "*.camera-scene3d.json"); if (path is not null) await ImportScene3DFromPathAsync(path); };
        save.Click += async (_, _) =>
        {
            Camera3DScene scene;
            try { scene = ReadScene3D(); } // Freeze edits before opening the picker.
            catch (Exception ex) { _status.Text = "请补全三维参数后保存：" + ex.Message; return; }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存三维相机场景", SuggestedFileName = "scene.camera-scene3d.json", DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("三维相机场景") { Patterns = ["*.camera-scene3d.json"] }]
            });
            if (file is null || _closed) return;
            try { await _application.CameraImaging.SaveScene3DAsync(scene, file.Path.LocalPath); if (!_closed) _status.Text = "三维场景已保存。"; }
            catch (Exception ex) { if (!_closed) _status.Text = "三维场景保存失败：" + ex.Message; }
        };
        _scene3DCard = Group("三维场景与照明", new Control[]
        {
            LaboratoryPresentation.Commands(example, import, save), _scene3DInfo,
            Number("scene3d-depth", "物体深度偏移 (mm)", 0, -100000, 100000, 10),
            Number("scene3d-power", "点光源功率倍率", 1, 0, 1000, .1m),
            Number("scene3d-rings", "瞳面径向环数", 2, 1, 8, 1), Number("scene3d-arms", "每环方位点数", 8, 4, 32, 4), _scene3DFresnel,
            Number("scene3d-pupil-grid", "衍射瞳面每边采样", 12, 4, 64, 4),
            Number("scene3d-subdivide", "每像元场景细分", 1, 1, 4, 1),
            Number("scene3d-psf-radius", "衍射窗口半径 (像元)", 4, 1, 32, 1),
            Number("scene3d-guard", "源保护带 (像元)", 24, 1, 32, 1),
            LaboratoryPresentation.Hint("坐标以第一实体镜面顶点为原点，单位 mm；波段依次为 650/550/450 nm。实际镜头和像面沿用处方，不自动调焦。衍射采用小角度标量 Huygens：同一表面点的瞳面先叠加复场，不同点/波段再叠加强度，最后积分感光区。源保护带不小于窗口半径；不作截窗归一化。有限采样须比较收敛，可先选 32×24。无多次反射、矢量偏振或散射。")
        });
        _scene3DFresnel.IsCheckedChanged += (_, _) => { if (!_applyingSensor) Invalidate("界面透射设置已改变，请运行 B。"); };
        return _scene3DCard;
    }

    private void SetScene3D(Camera3DScene scene)
    {
        var applying = _applyingSensor; _applyingSensor = true;
        try { _scene3D = scene; _numbers["scene3d-depth"].Value = 0; _numbers["scene3d-power"].Value = 1; }
        finally { _applyingSensor = applying; }
    }

    internal async Task<bool> ImportScene3DFromPathAsync(string path)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial;
        try
        {
            var scene = await _application.CameraImaging.ImportScene3DAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial) return false;
            SetScene3D(scene); _source.SelectedIndex = (int)CameraSource.Scene3D;
            Invalidate("三维场景已打开；A 保留，请运行 B。"); return true;
        }
        catch (Exception ex) { if (!_closed) _status.Text = "三维场景打开失败，现有场景与 A/B 保留：" + ex.Message; return false; }
    }

    private Camera3DScene ReadScene3D()
    {
        var scene = _scene3D ?? throw new InvalidOperationException("请载入三维场景。");
        var depth = V("scene3d-depth"); var power = V("scene3d-power");
        return scene with
        {
            Source = depth == 0 && power == 1 ? scene.Source : scene.Source + $"; object depth offset {depth:G17} mm; light power scale {power:G17}",
            Objects = scene.Objects.Select(o => o with { Z = o.Z + depth }).ToArray(),
            Lights = scene.Lights.Select(l => l with { BandPowerWatts = l.BandPowerWatts.Select(p => p * power).ToArray() }).ToArray()
        };
    }

    private void SynchronizeScene3DMode()
    {
        if (IsScene3D == _scene3DWasActive && IsNsc == _nscWasActive) return;
        var applying = _applyingSensor; _applyingSensor = true;
        try
        {
            if (IsScene3D || IsNsc)
            {
                if (!_scene3DWasActive && !_nscWasActive) _previous2D = (_optics.SelectedIndex, _aberration.SelectedIndex, _spectralModel.SelectedIndex);
                _optics.SelectedIndex = IsNsc ? (int)CameraOptics.DetectorInput : 0; _aberration.SelectedIndex = 0;
                _spectralModel.SelectedIndex = IsNsc ? (int)CameraSpectralModel.DetectorBands : (int)CameraSpectralModel.SceneBands;
                if (IsNsc) _pixelSampling.SelectedIndex = 0;
            }
            else { _optics.SelectedIndex = _previous2D.Optics; _aberration.SelectedIndex = _previous2D.Aberration; _spectralModel.SelectedIndex = _previous2D.Spectrum; }
            _scene3DWasActive = IsScene3D;
            _nscWasActive = IsNsc;
        }
        finally { _applyingSensor = applying; }
        UpdateSource();
    }

    private void UpdateScene3DControls()
    {
        if (_scene3DCard is null) return;
        _scene3DCard.IsVisible = IsScene3D;
        if (_legacySpectraCard is not null) _legacySpectraCard.IsVisible = !IsScene3D && !IsNsc;
        if (_legacyMaterialsCard is not null) _legacyMaterialsCard.IsVisible = !IsScene3D && !IsNsc;
        ControlAvailability.Set(_optics, !IsScene3D && !IsNsc, IsNsc ? "光学传播已经包含在 NSC 功率中。" : "三维场景通过当前实际镜头追迹。");
        ControlAvailability.Set(_aberration, !IsNsc, "光学传播已经包含在 NSC 功率中。");
        ControlAvailability.Set(_spectralModel, !IsScene3D && !IsNsc, IsNsc ? "使用冻结探测器的实际波长表。" : "三维场景使用明确的 650/550/450 nm 绝对波段值。");
        var wave = IsScene3D && _aberration.SelectedIndex == (int)CameraAberration.Diffraction;
        foreach (var key in new[] { "scene3d-rings", "scene3d-arms" })
            ControlAvailability.Set(_numbers[key], !wave, "几何环形瞳面用于几何追迹；衍射采用下方笛卡尔瞳面网格。");
        foreach (var key in new[] { "scene3d-pupil-grid", "scene3d-subdivide", "scene3d-psf-radius", "scene3d-guard" })
            ControlAvailability.Set(_numbers[key], wave, "请选择三维场景的衍射像差模式。");
        if (IsScene3D && _scene3D is { } scene)
        {
            if (Scene3DReadiness() is { } reason) { _scene3DInfo.Text = scene.Name + "\n" + reason; return; }
            _scene3DInfo.Text = scene.Name + $" · {scene.Objects.Count} 个物体 · {scene.Lights.Count} 个点光源\n"
                + string.Join("\n", scene.Objects.Select(o => $"{o.Name} · {o.Shape} · z {o.Z + V("scene3d-depth"):G5} mm"))
                + "\n" + string.Join("\n", scene.Lights.Select(l => $"{l.Name} · 三波段总功率 {l.BandPowerWatts.Sum() * V("scene3d-power"):G5} W"));
        }
    }

    private string? Scene3DReadiness() => !IsScene3D ? null : _scene3D is null ? "请载入三维场景。"
        : new[] { "scene3d-depth", "scene3d-power", "scene3d-rings", "scene3d-arms", "scene3d-pupil-grid", "scene3d-subdivide", "scene3d-psf-radius", "scene3d-guard" }.Any(key => !ValidNumber(_numbers[key], IntegerNumber(key)))
            ? "请输入有效数值。"
            : _aberration.SelectedIndex == (int)CameraAberration.Diffraction && V("scene3d-guard") < V("scene3d-psf-radius")
                    ? "源保护带不得小于衍射窗口半径。" : null;
}
