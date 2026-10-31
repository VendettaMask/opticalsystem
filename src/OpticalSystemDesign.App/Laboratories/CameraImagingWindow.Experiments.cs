using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly Button _saveExperiment = new() { Name = "camera-save-experiment", Content = "保存完整实验" };
    private readonly Button _recomputeExperiment = new() { Name = "camera-recompute-experiment", Content = "复算 A/B" };
    private readonly CheckBox _useExperiment = new() { Name = "camera-use-experiment", Content = "使用实验镜头与输入", IsVisible = false };
    private readonly TextBlock _experimentInfo = new() { Name = "camera-experiment-info", TextWrapping = TextWrapping.Wrap };
    private CameraImagingResult? _experimentRun;
    private (int Width, int Height)? _savedSize;
    private int _experimentLoadSerial;
    private bool UsingExperimentInputs => _useExperiment.IsChecked == true && _experimentRun is not null;

    private Control BuildExperimentCard()
    {
        var open = new Button { Name = "camera-open-experiment", Content = "打开实验" };
        open.Click += async (_, _) =>
        {
            var path = await PickSensorFileAsync("打开相机实验", "*.camera-experiment.zip");
            if (path is not null) await OpenExperimentFromPathAsync(path);
        };
        _saveExperiment.Click += async (_, _) =>
        {
            var result = _result; var comparison = _comparison; var sampling = _samplingReport;
            if (result is null || _busy) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiLocalization.Text("保存完整相机实验"), SuggestedFileName = "experiment.camera-experiment.zip", DefaultExtension = "zip",
                FileTypeChoices = new[] { new FilePickerFileType(UiLocalization.Text("相机实验")) { Patterns = new[] { "*.camera-experiment.zip" } } }
            });
            if (file is null || _closed) return;
            try
            {
                await _application.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, result, comparison, sampling), file.Path.LocalPath);
                if (!_closed) _status.Text = "完整实验已保存：A/B 各自的镜头、输入、光谱、传感器、ISP 和数值结果均已归档。";
            }
            catch (Exception ex) { if (!_closed) _status.Text = "实验保存失败：" + ex.Message; }
        };
        _recomputeExperiment.Click += async (_, _) => await RecomputeExperimentAsync();
        _useExperiment.IsCheckedChanged += (_, _) =>
        {
            if (_applyingSensor) return;
            UpdateSource(); Invalidate("计算输入已切换；A 保留，请运行 B。");
        };
        return Group("实验文件", new Control[]
        {
            LaboratoryPresentation.Commands(open, _saveExperiment, _recomputeExperiment), _useExperiment, _experimentInfo,
            LaboratoryPresentation.Hint("完整实验保存 A/B 各自的镜头快照、输入原文件、光源/材质/传感器/ISP 与结果。打开后可直接复算，也可修改设置再运行 B。勾选时使用实验镜头；取消后使用当前工程和图片路径。")
        });
    }

    internal async Task<bool> OpenExperimentFromPathAsync(string path)
    {
        var generation = _generation; var serial = ++_experimentLoadSerial;
        try
        {
            var experiment = await _application.CameraImaging.OpenExperimentAsync(path);
            if (_closed || generation != _generation || serial != _experimentLoadSerial) return false;
            Cancel(); ApplyExperimentRequest(experiment.B.Request);
            _experimentRun = experiment.B; _comparison = experiment.A;
            _applyingSensor = true;
            try { _useExperiment.IsVisible = true; _useExperiment.IsChecked = true; }
            finally { _applyingSensor = false; }
            PresentResult(experiment.B); UpdateSource();
            RestoreSampling(experiment.SamplingReport);
            _status.Text = "实验已打开。使用保存的镜头与输入，不依赖原始文件路径；可复算 A/B 或修改参数运行 B。";
            return true;
        }
        catch (Exception ex)
        { if (!_closed) _status.Text = "实验打开失败，现有配置和 A/B 保留：" + ex.Message; return false; }
    }

    private void ApplyExperimentRequest(CameraImagingRequest request)
    {
        _applyingSensor = true; _applyingMaterialScene = true;
        try
        {
            ApplySensorProfile(new(CameraSensorProfile.FormatIdentifier, 1, request.SensorIdentity?.Name ?? "Generic laboratory sensor",
                request.SensorIdentity?.Source ?? "User configured", request.Sensor!, request.SpectralResponse,
                request.SensorIdentity?.Origin, request.SensorIdentity?.Calibration));
            _sensorIdentity = request.SensorIdentity;
            _source.SelectedIndex = (int)request.Source; _sourceFile = request.SourceFile;
            if (request.Scene3D is { } scene3d) _scene3D = scene3d;
            _scene3DWasActive = request.Source == CameraSource.Scene3D;
            _nscWasActive = request.Source == CameraSource.NonSequentialDetector; _nscInput = request.NscDetector;
            if (_nscInput is { } nsc)
            { _nscChoices = []; _nscDetector.ItemsSource = new[] { nsc.Detector.Name }; _nscDetector.SelectedIndex = 0; _nscFilter.Text = nsc.PathFilter; }
            _numbers["scene3d-depth"].Value = 0; _numbers["scene3d-power"].Value = 1;
            _numbers["scene3d-rings"].Value = request.Render3D?.PupilRings ?? 2;
            _numbers["scene3d-arms"].Value = request.Render3D?.PupilArms ?? 8;
            _scene3DFresnel.IsChecked = request.Render3D?.BareInterfaceFresnel ?? true;
            var diffraction = request.Render3D?.Diffraction ?? new();
            _numbers["scene3d-pupil-grid"].Value = diffraction.PupilSamplesAcross;
            _numbers["scene3d-subdivide"].Value = diffraction.SceneSubdivisions;
            _numbers["scene3d-psf-radius"].Value = diffraction.PsfRadiusPixels;
            _numbers["scene3d-guard"].Value = diffraction.SourceGuardPixels;
            _optics.SelectedIndex = (int)request.Optics; _aberration.SelectedIndex = (int)request.Aberration;
            _savedSize = (request.Width, request.Height);
            var index = Array.IndexOf(StandardSizes, (request.Width, request.Height));
            _size.ItemsSource = index < 0 ? SizeLabels.Append($"{request.Width} × {request.Height}（实验）").ToArray() : SizeLabels;
            _size.SelectedIndex = index < 0 ? StandardSizes.Length : index;
            var pixel = request.PixelSampling;
            _pixelSampling.SelectedIndex = pixel?.SamplesPerAxis == 4 ? 2 : pixel?.SamplesPerAxis == 2 ? 1 : 0;
            _numbers["fill-factor"].Value = (decimal)(pixel?.FillFactor ?? 1);
            _qeArea.SelectedIndex = pixel?.QeIncludesFillFactor == false ? 1 : 0;
            _spectralModel.SelectedIndex = (int)request.SpectralModel; _sceneMode.SelectedIndex = (int)request.SceneMode;
            _illuminant = request.Illuminant; _reflectance = request.Reflectance; _materialScene = request.MaterialScene;
            _materialChoice.ItemsSource = _materialScene?.Materials.Select(m => $"{m.Id} · {m.Name}").ToArray() ?? Array.Empty<string>();
            _materialChoice.SelectedIndex = _materialScene is null ? -1 : 0;
            var integration = request.Integration ?? new(); var isp = request.Isp!;
            foreach (var (key, value) in new (string, double)[]
            {
                ("spectrum-min", integration.MinimumNanometers), ("spectrum-max", integration.MaximumNanometers),
                ("spectrum-step", integration.MaximumIntervalNanometers), ("wb-r", isp.RedGain), ("wb-g", isp.GreenGain),
                ("wb-b", isp.BlueGain), ("gamma", isp.Gamma), ("denoise", isp.Denoise), ("sharpen", isp.Sharpen)
            }) _numbers[key].Value = (decimal)value;
            _isp.IsChecked = isp.Enabled;
            _fileInfo.Text = request.Source == CameraSource.ExternalImage ? Path.GetFileName(request.SourceFile) + "（实验内嵌）"
                : request.Source == CameraSource.Scene3D ? "三维场景：" + request.Scene3D?.Name : "内置测试图";
        }
        finally { _applyingSensor = false; _applyingMaterialScene = false; }
    }

    internal async Task RecomputeExperimentAsync()
    {
        var b = _result; var a = _comparison;
        if (_closed || _busy || b?.Inputs is null) return;
        Cancel(); var generation = _generation;
        var operation = new CancellationTokenSource(); _operation = operation; _busy = true; UpdateAvailability();
        _status.Text = "正在按各自冻结输入复算 A/B…";
        try
        {
            var newB = await _application.CameraImaging.RecomputeAsync(b, cancellationToken: operation.Token);
            var newA = a is null ? null : await _application.CameraImaging.RecomputeAsync(a, cancellationToken: operation.Token);
            if (_closed || generation != _generation) return;
            _comparison = newA; if (UsingExperimentInputs) _experimentRun = newB;
            PresentResult(newB); _status.Text = "A/B 已按各自镜头、输入和参数复算完成。";
        }
        catch (OperationCanceledException) { if (!_closed && generation == _generation) _status.Text = "复算已取消，原 A/B 保留。"; }
        catch (Exception ex) { if (!_closed && generation == _generation) _status.Text = "复算失败，原 A/B 保留：" + ex.Message; }
        finally
        {
            if (generation == _generation) { _operation = null; _busy = false; UpdateAvailability(); }
            operation.Dispose();
        }
    }

    private void UpdateExperimentControls()
    {
        _useExperiment.Content = IsNsc ? "使用实验探测器与输入" : "使用实验镜头与输入";
        ControlAvailability.Set(_saveExperiment, !_busy && _result?.Inputs is not null, "请先完成仿真，再保存完整实验。");
        var sameVersion = CameraExperiment.CanRecompute(_result?.Inputs?.EngineVersion)
            && (_comparison is null || CameraExperiment.CanRecompute(_comparison.Inputs?.EngineVersion));
        ControlAvailability.Set(_recomputeExperiment, !_busy && _result?.Inputs is not null && sameVersion,
            !sameVersion && _result is not null ? "计算模型版本不同，可查看结果，不能按当前模型复算。" : "请先完成仿真或打开完整实验。");
        _experimentInfo.Text = UsingExperimentInputs
            ? $"{(IsNsc ? "使用实验内嵌 NSC 功率" : "使用实验镜头与内嵌输入")} · {_experimentRun!.Inputs!.EngineVersion}"
            : "使用当前工程镜头与输入";
    }
}
