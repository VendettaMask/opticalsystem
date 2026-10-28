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
            var width = 128 << _size.SelectedIndex; var height = 96 << _size.SelectedIndex;
            var request = new CameraImagingRequest((CameraSource)_source.SelectedIndex, _sourceFile, width, height,
                (CameraOptics)_optics.SelectedIndex, (CameraAberration)_aberration.SelectedIndex, sensor, isp,
                _spectralResponse, _sensorIdentity, (CameraSpectralModel)_spectralModel.SelectedIndex, _illuminant, _reflectance,
                new(V("spectrum-min"), V("spectrum-max"), V("spectrum-step")), (CameraSceneMode)_sceneMode.SelectedIndex, _materialScene);
            var result = await _application.CameraImaging.SimulateAsync(request, operation.Token);
            if (_closed || generation != _generation || result.SourceRevision != _application.Events.Revision) return;
            _result = result;
            foreach (var (view, frame) in _stageViews.Zip(new[] { result.Source, result.Optical, result.Electrons, result.Raw, result.Noiseless, result.Developed })) view.Set(frame);
            _metrics.Text = $"{width} × {height} · 平均光电子 {result.MeanPhotoElectrons:G5} e⁻/像元 · "
                + $"RAW 对无噪声 RMSE {result.RawNoiseRmseDn:G4} DN · 饱和 {result.SaturatedPixels}/{width * height}\n"
                + $"光学模型：{result.OpticalModel} · 几何回退 {result.OpticalFallbackCount} · 曝光 {sensor.ExposureMilliseconds:G4} ms · "
                + $"像元 {sensor.PixelPitchMicrometers:G4} µm · {sensor.BitDepth} bit · seed {sensor.Seed}\n"
                + $"传感器：{result.Request.SensorIdentity?.Name ?? "通用参数"} · QE：{result.Request.SpectralResponse?.Name ?? "固定三波段"}";
            _metrics.Text += "\n" + SpectrumDescription(result);
            UpdateComparison(); _status.Text = "已完成。可固定 A、修改参数后运行 B，或导出各阶段图像、RAW 数值与参数。";
        }
        catch (OperationCanceledException) { if (generation == _generation && !_closed) _status.Text = "计算已取消"; }
        catch (Exception ex) { if (generation == _generation && !_closed) _status.Text = "仿真未完成：" + ex.Message; }
        finally
        {
            if (generation == _generation) { _operation = null; _busy = false; UpdateAvailability(); }
            operation.Dispose();
        }
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
        return $"{label}：{result.DocumentName} · revision {result.SourceRevision} · {source} · {s.ExposureMilliseconds:G4} ms · "
            + $"{s.GainDnPerElectron:G3} DN/e⁻ · {s.BitDepth} bit · read {s.ReadNoiseElectrons:G3} e⁻ · "
            + $"noise {s.NoiseEnabled} · ISP {isp.Enabled} · Gamma {isp.Gamma:G3} · seed {s.Seed}\n"
            + $"传感器：{result.Request.SensorIdentity?.Name ?? "通用参数"} · QE：{result.Request.SpectralResponse?.Name ?? "固定三波段"}"
            + (result.Request.SpectralResponse?.Origin is { } origin ? $" · SHA {origin.Sha256[..8]}" : "")
            + (result.Request.SensorIdentity?.Modified == true ? "（参数已修改）" : "")
            + "\n" + SpectrumDescription(result);
    }

    private void Invalidate(string message, bool clearComparison = false)
    {
        Cancel(); _result = null; _metrics.Text = "";
        _fileInfo.Text = _source.SelectedIndex == (int)CameraSource.ExternalImage
            ? (string.IsNullOrWhiteSpace(_sourceFile) ? "请导入图片" : System.IO.Path.GetFileName(_sourceFile)) : "内置测试图";
        foreach (var view in _stageViews) view.Set(null);
        if (clearComparison) { _comparison = null; _comparisonInfo.Text = "镜头或工作模式已改变，A/B 旧结果已清除。"; }
        UpdateComparison(); _status.Text = message; UpdateAvailability();
    }
    private void Cancel() { _generation++; _operation?.Cancel(); _operation = null; _busy = false; }
    private void UpdateSource() => _document.Text = "相机成像仿真 · " + _application.Documents.GetSnapshot().Name;
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) => OnUi(() =>
    { UpdateSource(); Invalidate("镜头已改变，旧结果已清除，请重新运行。", true); });
    private void OnModeChanged(object? sender, WorkbenchModeChangedEventArgs args) => OnUi(() => Invalidate("模式已改变，旧结果已清除。", true));
    private void OnUi(Action action)
    { if (Dispatcher.UIThread.CheckAccess()) { if (!_closed) action(); } else Dispatcher.UIThread.Post(() => { if (!_closed) action(); }); }
    private void UpdateAvailability()
    {
        var validLens = _application.Modes.CurrentMode == OpticalWorkbenchMode.Sequential && _application.Documents.GetSnapshot().SurfaceCount >= 4;
        var sourceReady = _source.SelectedIndex != (int)CameraSource.ExternalImage || !string.IsNullOrWhiteSpace(_sourceFile);
        var sizeReady = _optics.SelectedIndex == 1 || _size.SelectedIndex < 2;
        var spectrumReason = SpectrumReadiness();
        var canRun = !_busy && sourceReady && sizeReady && spectrumReason is null && (_optics.SelectedIndex == 1 || validLens);
        ControlAvailability.Set(_run, canRun, _busy ? "正在计算，可取消后修改。" : !sourceReady ? "请先导入图片。"
            : !sizeReady ? "当前镜头首批支持到 256 × 192；512 × 384 用于理想光学对照。"
            : spectrumReason ?? "请选择有效顺序镜头，或切换为理想光学对照。");
        ControlAvailability.Set(_cancel, _busy, "当前没有计算任务。");
        ControlAvailability.Set(_pin, !_busy && _result is not null, "请先完成当前仿真。");
        ControlAvailability.Set(_export, !_busy && _result is not null, "请先完成当前仿真，再导出对照包。");
        UpdateSensorFiles();
        UpdateSceneSpectra();
        UpdateMaterialControls();
    }
    private double V(string key) => (double)(_numbers[key].Value ?? throw new InvalidOperationException("请输入有效数值。"));
}
