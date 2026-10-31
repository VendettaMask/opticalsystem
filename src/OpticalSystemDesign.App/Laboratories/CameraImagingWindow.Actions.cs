using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    internal async Task RunAsync()
    {
        if (_closed) return;
        Invalidate("正在生成光学像、传感器 RAW 与 ISP 对照…");
        var generation = _generation;
        var operation = new CancellationTokenSource(); _operation = operation; _busy = true; UpdateAvailability();
        try
        {
            var sensor = ReadSensor();
            var isp = new CameraIspParameters(_isp.IsChecked == true, V("wb-r"), V("wb-g"), V("wb-b"), V("gamma"), V("denoise"), V("sharpen"));
            var (width, height) = SelectedSize;
            var request = new CameraImagingRequest((CameraSource)_source.SelectedIndex, _sourceFile, width, height,
                (CameraOptics)_optics.SelectedIndex, (CameraAberration)_aberration.SelectedIndex, sensor, isp,
                _spectralResponse, _sensorIdentity, (CameraSpectralModel)_spectralModel.SelectedIndex, _illuminant, _reflectance,
                new(V("spectrum-min"), V("spectrum-max"), V("spectrum-step")), IsScene3D ? CameraSceneMode.Uniform : (CameraSceneMode)_sceneMode.SelectedIndex, IsScene3D ? null : _materialScene,
                ReadPixelSampling(), IsScene3D ? ReadScene3D() : null, IsScene3D ? new((int)V("scene3d-rings"), (int)V("scene3d-arms"), _scene3DFresnel.IsChecked == true,
                    _aberration.SelectedIndex == (int)CameraAberration.Diffraction
                        ? new((int)V("scene3d-pupil-grid"), (int)V("scene3d-subdivide"), (int)V("scene3d-psf-radius"), (int)V("scene3d-guard")) : null) : null);
            if (IsNsc)
            {
                var input = _nscInput ?? throw new InvalidOperationException("请先读取非序列探测器结果。");
                request = request with { Width = input.Detector.PixelsX, Height = input.Detector.PixelsY,
                    NscDetector = input, SceneMode = CameraSceneMode.Uniform, MaterialScene = null, PixelSampling = null };
            }
            var result = UsingExperimentInputs
                ? await _application.CameraImaging.RecomputeAsync(_experimentRun!, request, operation.Token)
                : await _application.CameraImaging.SimulateAsync(request, operation.Token);
            if (_closed || generation != _generation || result.SourceRevision != _application.Events.Revision) return;
            if (UsingExperimentInputs) _experimentRun = result;
            PresentResult(result);
            _status.Text = "已完成。可固定 A、修改参数后运行 B，或保存完整实验。";
        }
        catch (OperationCanceledException) { if (generation == _generation && !_closed) _status.Text = "计算已取消"; }
        catch (Exception ex) { if (generation == _generation && !_closed) _status.Text = "仿真未完成：" + ex.Message; }
        finally
        {
            if (generation == _generation) { _operation = null; _busy = false; UpdateAvailability(); }
            operation.Dispose();
        }
    }

    private void PresentResult(CameraImagingResult result)
    {
        var sensor = result.Request.Sensor!; var width = result.Request.Width; var height = result.Request.Height;
        _result = result;
        ResetSampling(result);
        _stageViews[0].Title = result.Request.NscDetector is not null ? "NSC 总辐照度 · 固定标尺灰度预览" : result.Scene3D is null ? "输入图 · sRGB 解码后预览" : "可见表面反射率 · 几何预览";
        _stageViews[1].Title = result.Request.NscDetector is not null ? "已计算光学功率 · 不再次传播" : result.Scene3D is null ? "光学像 · 固定 Gamma 2.2" : "像面辐照度 · 满白每波段 0.02 W/m²";
        foreach (var (view, frame) in _stageViews.Zip(new[] { result.Source, result.Optical, result.Electrons, result.Raw, result.Noiseless, result.Developed })) view.Set(frame);
        _metrics.Text = $"{width} × {height} · 平均光电子 {result.MeanPhotoElectrons:G5} e⁻/像元 · "
            + $"RAW 对无噪声 RMSE {result.RawNoiseRmseDn:G4} DN · 饱和 {result.SaturatedPixels}/{width * height}\n"
            + $"光学模型：{result.OpticalModel} · 几何回退 {result.OpticalFallbackCount} · 曝光 {sensor.ExposureMilliseconds:G4} ms · "
            + $"像元 {sensor.PixelPitchMicrometers:G4} µm · {sensor.BitDepth} bit · seed {sensor.Seed}\n"
            + $"传感器：{result.Request.SensorIdentity?.Name ?? "通用参数"} · QE：{result.Request.SpectralResponse?.Name ?? "固定三波段"}";
        _metrics.Text += "\n" + SpectrumDescription(result);
        if (result.Request.PixelSampling is { } p) _metrics.Text += $"\n像元面积 {p.SamplesPerAxis} × {p.SamplesPerAxis} · 填充因子 {p.FillFactor:G4} · "
            + (p.QeIncludesFillFactor ? "整像元总 QE" : "感光区 QE");
        if (result.Scene3D?.Diffraction is { } wave) _metrics.Text += $"\n表面点 {wave.SurfaceSamples} · 复场单元求和 {wave.ScalarCellEvaluations} · 被遮瞳面单元 {wave.OccludedPupilCells} · 有限窗口未归一化";
        UpdateComparison(); UpdateAvailability();
    }

    internal void PinComparison()
    {
        if (_result is null || _busy) return;
        _comparison = _result; UpdateComparison(); _status.Text = "A 已固定；修改曝光、传感器或 ISP 后重新运行 B。";
    }

    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择相机仿真输入图像",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("图像") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff", "*.webp" } } }
        });
        if (files.Count == 0 || _closed) return;
        if (UsingExperimentInputs)
            _experimentRun = _experimentRun! with { Inputs = _experimentRun!.Inputs! with { ExternalImageBytes = null, ImageOrigin = null } };
        _sourceFile = files[0].Path.LocalPath; _source.SelectedIndex = (int)CameraSource.ExternalImage;
        _fileInfo.Text = System.IO.Path.GetFileName(_sourceFile); Invalidate("输入图片已改变，请重新运行。");
    }

    private async Task ExportAsync()
    {
        var result = _result; var comparison = _comparison;
        if (result is null || _busy) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出相机成像对照包",
            SuggestedFileName = "camera-imaging-comparison.zip",
            DefaultExtension = "zip",
            FileTypeChoices = new[] { new FilePickerFileType("ZIP 对照包") { Patterns = new[] { "*.zip" } } }
        });
        if (file is null || _closed) return;
        try
        {
            await _application.CameraImaging.ExportAsync(result, file.Path.LocalPath, comparison);
            if (!_closed) _status.Text = "已导出各阶段 PNG、传感器 CSV 和参数元数据。";
        }
        catch (Exception ex) { if (!_closed) _status.Text = "导出失败：" + ex.Message; }
    }

    private void UpdateComparison()
    {
        _comparisonViews[0].Set(_comparison?.Developed); _comparisonViews[1].Set(_result?.Developed); _comparisonViews[2].Set(null);
        if (_comparison is null) return;
        _comparisonInfo.Text = Describe("A", _comparison);
        if (_result is null) return;
        _comparisonInfo.Text += "\n" + Describe("B", _result)
            + "\n差异为显示 RGB 差，非物理误差认证；导出包保留 A/B 各自源图与全部参数。";
        if (_comparison.Developed.Width != _result.Developed.Width || _comparison.Developed.Height != _result.Developed.Height)
        { _comparisonInfo.Text += "\nA/B 尺寸不同，不计算逐像素差。"; return; }
        var bytes = new byte[_result.Developed.Rgb24.Count]; double sum = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var difference = Math.Abs(_comparison.Developed.Rgb24[i] - _result.Developed.Rgb24[i]);
            sum += difference; bytes[i] = (byte)Math.Min(255, difference * 4);
        }
        _comparisonViews[2].Set(new(_result.Developed.Width, _result.Developed.Height, bytes));
        _comparisonInfo.Text += $"\nA/B 显示 RGB 平均绝对差：{sum / bytes.Length:F3} / 255。";
    }

    private static string Describe(string label, CameraImagingResult result)
    {
        var s = result.Request.Sensor!; var isp = result.Request.Isp!;
        var source = result.Request.Source == CameraSource.ExternalImage ? System.IO.Path.GetFileName(result.Request.SourceFile) : result.Request.Source.ToString();
        return $"{label}：{result.Inputs?.LensDocumentName ?? result.DocumentName} · revision {result.Inputs?.LensSourceRevision ?? result.SourceRevision} · {source} · {s.ExposureMilliseconds:G4} ms · "
            + $"{s.GainDnPerElectron:G3} DN/e⁻ · {s.BitDepth} bit · read {s.ReadNoiseElectrons:G3} e⁻ · "
            + $"noise {s.NoiseEnabled} · ISP {isp.Enabled} · Gamma {isp.Gamma:G3} · seed {s.Seed}\n"
            + $"传感器：{result.Request.SensorIdentity?.Name ?? "通用参数"} · QE：{result.Request.SpectralResponse?.Name ?? "固定三波段"}"
            + (result.Request.SpectralResponse?.Origin is { } origin ? $" · SHA {origin.Sha256[..8]}" : "")
            + (result.Request.SensorIdentity?.Modified == true ? "（参数已修改）" : "")
            + "\n" + SpectrumDescription(result);
    }

    private void Invalidate(string message, bool clearComparison = false)
    {
        Cancel(); _result = null; _metrics.Text = ""; ResetSampling(null);
        _fileInfo.Text = _source.SelectedIndex == (int)CameraSource.ExternalImage
            ? (string.IsNullOrWhiteSpace(_sourceFile) ? "请导入图片" : System.IO.Path.GetFileName(_sourceFile))
            : IsScene3D ? "三维场景：" + _scene3D?.Name : "内置测试图";
        foreach (var view in _stageViews) view.Set(null);
        if (clearComparison) { _comparison = null; _comparisonInfo.Text = "镜头或工作模式已改变，A/B 旧结果已清除。"; }
        UpdateComparison(); _status.Text = message; UpdateAvailability();
    }
    private void Cancel() { _generation++; _operation?.Cancel(); _operation = null; _busy = false; _samplingBusy = false; }
    private void UpdateSource()
    {
        _document.Text = "相机成像仿真 · " + (UsingExperimentInputs ? "实验快照：" + _experimentRun!.DocumentName : _application.Documents.GetSnapshot().Name);
        _opticsInputInfo.Text = UsingExperimentInputs
            ? "当前镜头使用实验快照；像面视场按像元尺寸与分辨率采样。取消上方勾选后使用当前工程。"
            : "当前镜头沿用未保存处方；像面视场按像元尺寸与分辨率采样。";
        if (IsScene3D) _opticsInputInfo.Text = UsingExperimentInputs
            ? "三维场景使用实验镜头和实际像面；物体坐标相对第一镜面，不自动调焦。"
            : "三维场景使用当前未保存镜头和实际像面；物体坐标相对第一镜面，不自动调焦。";
        if (IsNsc) _opticsInputInfo.Text = "光学传播已经包含在 NSC 功率中；只应用一次曝光、QE、噪声、ADC 和 ISP。";
    }
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) => OnUi(() =>
    { UpdateSource(); if (!UsingExperimentInputs) { _nscInput = null; Invalidate("镜头已改变，旧结果已清除，请重新运行。", true); } });
    private void OnModeChanged(object? sender, WorkbenchModeChangedEventArgs args) => OnUi(() =>
    { if (!UsingExperimentInputs) Invalidate("模式已改变，旧结果已清除。", true); });
    private void OnUi(Action action)
    { if (Dispatcher.UIThread.CheckAccess()) { if (!_closed) action(); } else Dispatcher.UIThread.Post(() => { if (!_closed) action(); }); }
    private void UpdateAvailability()
    {
        var validLens = UsingExperimentInputs ? _experimentRun!.Inputs?.LensSnapshotJson is not null
            : _application.Modes.CurrentMode == OpticalWorkbenchMode.Sequential && _application.Documents.GetSnapshot().SurfaceCount >= 4;
        var sourceReady = _source.SelectedIndex != (int)CameraSource.ExternalImage || !string.IsNullOrWhiteSpace(_sourceFile);
        var sizeReady = IsNsc || _optics.SelectedIndex == 1 || SelectedSize is { Width: <= 256, Height: <= 256 };
        var pixelReady = IsNsc || IsScene3D || _optics.SelectedIndex == 1 || _pixelSampling.SelectedIndex != 2;
        var fillReady = IsScene3D || _pixelSampling.SelectedIndex == 0 || _numbers["fill-factor"].Value == 1;
        var validNumbers = _numbers.Where(pair => NumberUsedByRun(pair.Key)).All(pair => ValidNumber(pair.Value, IntegerNumber(pair.Key)));
        var spectrumReason = validNumbers ? Scene3DReadiness() ?? SpectrumReadiness() : "请输入有效数值。";
        var canRun = !_busy && validNumbers && sourceReady && sizeReady && pixelReady && fillReady && spectrumReason is null && (IsNsc || _optics.SelectedIndex == 1 || validLens);
        ControlAvailability.Set(_run, canRun, _busy ? "正在计算，可取消后修改。" : !sourceReady ? "请先导入图片。"
            : !validNumbers ? "请输入有效数值。"
            : !sizeReady ? "当前镜头首批支持到 256 × 192；512 × 384 用于理想光学对照。"
            : !pixelReady ? "保留原物理 PSF 范围和计算预算时，当前镜头使用 2×2；4×4 用于理想光学对照。"
            : !fillReady ? "二维旧网格不能正确表示非满填充；请选择三维场景的感光区积分，或恢复填充因子 1。"
            : spectrumReason ?? "请选择有效顺序镜头，或切换为理想光学对照。");
        ControlAvailability.Set(_cancel, _busy, "当前没有计算任务。");
        ControlAvailability.Set(_pin, !_busy && _result is not null, "请先完成当前仿真。");
        ControlAvailability.Set(_export, !_busy && _result is not null, "请先完成当前仿真，再导出对照包。");
        UpdateSensorFiles();
        UpdateSceneSpectra();
        UpdateMaterialControls();
        UpdateExperimentControls();
        ControlAvailability.Set(_numbers["fill-factor"], _pixelSampling.SelectedIndex > 0, "填充因子用于面积采样；中心采样保持原模型。");
        if (_spectralResponse is not null && _qeArea.SelectedIndex != 0)
        {
            var applying = _applyingSensor; _applyingSensor = true;
            try { _qeArea.SelectedIndex = 0; } finally { _applyingSensor = applying; }
        }
        ControlAvailability.Set(_qeArea, _pixelSampling.SelectedIndex > 0 && _spectralResponse is null,
            _spectralResponse is not null ? "导入曲线定义为整像元总 QE，填充损失不会重复计算。" : "QE 面积口径用于面积采样；中心采样保持原模型。");
        UpdateScene3DControls();
        UpdateNscControls();
        UpdateSamplingControls();
    }
    private double V(string key) => ValidNumber(_numbers[key], IntegerNumber(key))
        ? (double)_numbers[key].Value!.Value : throw new InvalidOperationException("请输入有效数值。");
}
