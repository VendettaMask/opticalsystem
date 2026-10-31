using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private CameraNscDetectorInput? _nscInput;
    private bool _nscWasActive;
    private Control? _nscCard;
    private readonly ComboBox _nscDetector = Choice("camera-nsc-detector");
    private readonly TextBox _nscFilter = new() { Name = "camera-nsc-filter", PlaceholderText = "留空读取全部已保存路径" };
    private readonly Button _nscRead = new() { Name = "camera-nsc-read", Content = "读取 NSC 结果" };
    private readonly TextBlock _nscInfo = new() { Name = "camera-nsc-info", TextWrapping = TextWrapping.Wrap };
    private NonSequentialObjectRowDto[] _nscChoices = [];
    private bool IsNsc => _source.SelectedIndex == (int)CameraSource.NonSequentialDetector;

    private Control BuildNonSequentialCard()
    {
        _nscRead.Click += async (_, _) => await CaptureNscAsync();
        _nscDetector.SelectionChanged += (_, _) => { if (!_applyingSensor) { _nscInput = null; Invalidate("探测器选择已改变，请读取 NSC 结果。"); } };
        _nscFilter.PropertyChanged += (_, args) =>
        { if (args.Property == TextBox.TextProperty && !_applyingSensor) { _nscInput = null; Invalidate("路径筛选已改变，请读取 NSC 结果。"); } };
        _nscCard = Group("非序列分析到 RAW / ISP", new Control[]
        {
            Field("矩形探测器", _nscDetector), Field("路径筛选", _nscFilter), LaboratoryPresentation.Commands(_nscRead), _nscInfo,
            LaboratoryPresentation.Hint("先在非序列追迹控制中完成分析，再读取探测器。功率包含所选已保存路径，不另加顺序主成像。导入覆盖实际波长的绝对 QE，运行生成 RAW/ISP。像元和尺寸必须与探测器匹配；完整实验保留功率与来源，复算不重新追迹。")
        });
        _nscCard.Name = "camera-nsc-card";
        return _nscCard;
    }

    internal async Task<bool> CaptureNscAsync()
    {
        if (_closed || _busy || UsingExperimentInputs || _nscDetector.SelectedIndex < 0
            || _nscDetector.SelectedIndex >= _nscChoices.Length) return false;
        var id = _nscChoices[_nscDetector.SelectedIndex].Id; var filter = _nscFilter.Text ?? "";
        Cancel(); var generation = _generation; var operation = new CancellationTokenSource();
        _operation = operation; _busy = true; UpdateAvailability();
        try
        {
            var input = await _application.CameraImaging.CaptureNonSequentialDetectorAsync(id, filter, operation.Token);
            if (_closed || generation != _generation) return false;
            _applyingSensor = true;
            try
            {
                _numbers["pitch"].Value = (decimal)(input.Detector.WidthMillimeters / input.Detector.PixelsX * 1000);
                _savedSize = (input.Detector.PixelsX, input.Detector.PixelsY);
                var sizeIndex = Array.IndexOf(StandardSizes, _savedSize.Value);
                _size.ItemsSource = sizeIndex < 0 ? SizeLabels.Append($"{input.Detector.PixelsX} × {input.Detector.PixelsY}（NSC）").ToArray() : SizeLabels;
                _size.SelectedIndex = sizeIndex < 0 ? StandardSizes.Length : sizeIndex;
            }
            finally { _applyingSensor = false; }
            _nscInput = input;
            Invalidate("NSC 功率已读取，请导入匹配的 QE 后运行 B；A 保留。");
            return true;
        }
        catch (OperationCanceledException) { if (!_closed && generation == _generation) _status.Text = "读取已取消，原 A/B 保留。"; return false; }
        catch (Exception ex) { if (!_closed && generation == _generation) _status.Text = "NSC 读取失败，原 A/B 保留：" + ex.Message; return false; }
        finally
        {
            if (generation == _generation) { _operation = null; _busy = false; UpdateAvailability(); }
            operation.Dispose();
        }
    }

    private string? NscReadiness()
    {
        if (_nscInput is not { } input) return "请先读取非序列探测器结果。";
        if (!UsingExperimentInputs && _application.Modes.CurrentMode != OpticalWorkbenchMode.NonSequential)
            return "请切换到非序列模式后读取探测器。";
        if (!UsingExperimentInputs && (_application.NonSequentialAnalysis.GetCurrentSession() is not { IsStale: false } session || session.Id != input.SessionId))
            return "非序列分析结果已过期或未完成，请重新追迹。";
        if (_spectralResponse is not { } response) return "非序列输入需要覆盖探测波长的绝对 QE 曲线。";
        if (input.Wavelengths.Any(w => w.Nanometers < response.Samples[0].WavelengthNanometers || w.Nanometers > response.Samples[^1].WavelengthNanometers))
            return "绝对 QE 曲线必须覆盖全部探测波长。";
        var pitch = input.Detector.WidthMillimeters / input.Detector.PixelsX * 1000;
        var heightPitch = input.Detector.HeightMillimeters / input.Detector.PixelsY * 1000;
        if (_numbers["pitch"].Value is not { } selected || Math.Abs((double)selected - pitch) > 8e-15 * pitch
            || Math.Abs(heightPitch - pitch) > 8e-15 * pitch) return "传感器须为与 NSC 探测器完全匹配的方形像元。";
        return null;
    }

    private void UpdateNscControls()
    {
        if (_nscCard is null) return;
        _nscCard.IsVisible = IsNsc;
        if (IsNsc && !UsingExperimentInputs)
        {
            var choices = _application.NonSequential.GetDocument().Objects.Where(o => o.Enabled && o.Kind == NonSequentialObjectKind.DetectorRectangle).ToArray();
            if (!_nscChoices.Select(o => (o.Id, o.Name)).SequenceEqual(choices.Select(o => (o.Id, o.Name))))
            {
                var previous = _nscDetector.SelectedIndex >= 0 && _nscDetector.SelectedIndex < _nscChoices.Length ? _nscChoices[_nscDetector.SelectedIndex].Id : Guid.Empty;
                var applying = _applyingSensor; _applyingSensor = true;
                try
                {
                    _nscChoices = choices; _nscDetector.ItemsSource = choices.Select(o => o.Name).ToArray();
                    _nscDetector.SelectedIndex = choices.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(choices, o => o.Id == previous));
                }
                finally { _applyingSensor = applying; }
            }
        }
        var ready = _application.Modes.CurrentMode == OpticalWorkbenchMode.NonSequential
            && _application.NonSequentialAnalysis.GetCurrentSession() is { IsStale: false, State: NonSequentialTraceSessionState.Completed or NonSequentialTraceSessionState.Warning };
        ControlAvailability.Set(_nscRead, !_busy && !UsingExperimentInputs && ready && _nscChoices.Length > 0,
            UsingExperimentInputs ? "取消使用实验输入后，可读取当前 NSC 结果。" : "请先完成当前场景的非序列分析追迹。");
        ControlAvailability.Set(_nscDetector, !_busy && !UsingExperimentInputs, "取消使用实验输入后，可选择当前探测器。");
        ControlAvailability.Set(_nscFilter, !_busy && !UsingExperimentInputs, "取消使用实验输入后，可选择当前路径。");
        ControlAvailability.Set(_size, !IsNsc, "尺寸由冻结的 NSC 探测器决定。");
        ControlAvailability.Set(_pixelSampling, !IsNsc, "NSC 功率已包含整个像元的积分。");
        if (IsNsc)
        {
            ControlAvailability.Set(_numbers["fill-factor"], false, "NSC 功率已包含整个像元的积分。");
            ControlAvailability.Set(_qeArea, false, "NSC 使用整像元绝对 QE。");
            _nscInfo.Text = _nscInput is { } n ? $"{n.Detector.Name} · {n.Detector.PixelsX} × {n.Detector.PixelsY} · {n.Bands.Count} 波段\n"
                + $"{n.Bands.Sum(b => b.PixelPowerWatts.Sum()):G6} W · scene {n.SceneHash[..12]} · revision {n.SourceRevision}\n"
                + $"筛选：{(n.PathFilter.Length == 0 ? "全部已保存路径" : n.PathFilter)}\n"
                + (n.BsdfStatistics is { } stats ? $"{(n.SurfaceBsdfProtocol is "MeasuredBsdfGrid/1" or "MeasuredBsdfGrid/2" or "MeasuredBsdfGrid/3" or "MeasuredBsdfGrid/4" ? "Measured BSDF" : "Lambertian")} · N={stats.SampleCount:N0} · ⟨|cosθ|⟩={stats.MeanCosine:G5}\n"
                    + $"R={stats.ReflectionSampleCount:N0} · T={stats.TransmissionSampleCount:N0}\n"
                    + (stats.MaterialAxisSampleCount > 0 ? $"⟨material X,Y⟩={stats.MaterialDirectionXSum / stats.MaterialAxisSampleCount:G5}, {stats.MaterialDirectionYSum / stats.MaterialAxisSampleCount:G5}\n" : string.Empty)
                    + UiLocalization.Text("散射统计为筛选前全部交互，含重复反弹。") + "\n" : string.Empty)
                + string.Join("\n", n.SurfaceBsdfs?.Where(s => s.Model.MeasuredData is not null).Select(s => $"{s.Model.MeasuredData!.SourceFileName} · SHA-256 {s.Model.MeasuredData.SourceSha256[..12]}") ?? []) + "\n"
                + string.Join("\n", n.Warnings)
                : "请先完成当前场景的非序列分析追迹。";
        }
    }

    private void OnNscSessionChanged(object? sender, NonSequentialTraceSessionDto? session) => OnUi(() =>
    {
        if (IsNsc && !UsingExperimentInputs)
        { _nscInput = null; Invalidate("NSC 分析会话已改变，请重新读取探测器。"); }
    });
}
