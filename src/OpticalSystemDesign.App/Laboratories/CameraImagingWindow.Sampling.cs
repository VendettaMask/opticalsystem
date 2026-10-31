using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly ComboBox _samplingAxis = Choice("camera-sampling-axis", "瞳面加密", "场景加密", "像元加密", "窗口扩大", "保护带扩大");
    private readonly Button _checkSampling = new() { Name = "camera-check-sampling", Content = "检查本项", Classes = { "accent" } };
    private readonly Button _exportSampling = new() { Name = "camera-export-sampling", Content = "导出采样报告" };
    private readonly TextBlock _samplingInfo = new() { Name = "camera-sampling-info", TextWrapping = TextWrapping.Wrap, Margin = new(12) };
    private CameraSamplingReport? _samplingReport;
    private bool _samplingBusy;
    internal CameraSamplingReport? SamplingReport => _samplingReport;

    private Control BuildSamplingCard()
    {
        _samplingAxis.SelectedIndex = (int)CameraSamplingAxis.Pixel;
        _samplingAxis.SelectionChanged += (_, _) => UpdateSamplingControls();
        _checkSampling.Click += async (_, _) => await RunSamplingAsync();
        _exportSampling.Click += async (_, _) =>
        {
            var run = _result; var report = _samplingReport;
            if (run is null || report is null || _busy) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出采样报告", SuggestedFileName = "camera-sampling.json", DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });
            if (file is null || _closed) return;
            try
            {
                await _application.CameraImaging.ExportSamplingAsync(run, report, file.Path.LocalPath);
                if (!_closed) _status.Text = "采样报告已导出；保存完整实验可同时保留 B、A 和检查结果。";
            }
            catch (Exception ex) { if (!_closed) _status.Text = "采样报告导出失败：" + ex.Message; }
        };
        _samplingInfo.Text = "完成三维衍射 B 后，选择原传感器中的区域，逐项检查采样差异。";
        return Group("局部采样检查", new Control[]
        {
            Field("检查项目", _samplingAxis),
            Number("sampling-x", "起点 X (从 0 开始)", 0, 0, 2048, 1),
            Number("sampling-y", "起点 Y (从 0 开始)", 0, 0, 2048, 1),
            Number("sampling-width", "区域宽度 (像元)", 4, 4, 16, 1),
            Number("sampling-height", "区域高度 (像元)", 4, 4, 16, 1),
            LaboratoryPresentation.Commands(_checkSampling, _exportSampling),
            LaboratoryPresentation.Hint("保留原像元坐标、尺寸与镜头。瞳面/场景逐级加密至实现上限，保存相邻层级差异和实际工作量；其余项目比较一级。结果见“采样检查”页。")
        });
    }

    internal async Task RunSamplingAsync()
    {
        var run = _result;
        if (_closed || _busy || !CanCheckSampling(run) || !SamplingRegionIsValid()) return;
        var region = new CameraSamplingRegion((int)V("sampling-x"), (int)V("sampling-y"), (int)V("sampling-width"), (int)V("sampling-height"));
        var axis = (CameraSamplingAxis)_samplingAxis.SelectedIndex; var previous = _samplingReport;
        Cancel(); var generation = _generation;
        var operation = new CancellationTokenSource(); _operation = operation; _busy = _samplingBusy = true; UpdateAvailability();
        _status.Text = "正在按冻结 B 检查局部采样，A/B 保留…";
        try
        {
            var report = await _application.CameraImaging.CheckSamplingAsync(run!, region, axis, previous, operation.Token,
                completeLadder: axis is CameraSamplingAxis.Pupil or CameraSamplingAxis.Scene);
            if (_closed || generation != _generation || !ReferenceEquals(_result, run)) return;
            RestoreSampling(report);
            var selected = report.Comparisons.Single(c => c.Axis == axis);
            _status.Text = selected.Status == CameraSamplingComparisonStatus.Compared
                && (selected.FurtherRefinements?.All(c => c.Status == CameraSamplingComparisonStatus.Compared) ?? true)
                ? "本项比较完成，详见采样检查页。差异大小不代表已通过精度认证。" : "本项有未完成层级，已完成数值保留；原因见采样检查页。";
        }
        catch (OperationCanceledException) { if (!_closed && generation == _generation) _status.Text = "采样检查已取消，A/B 和原检查记录保留。"; }
        catch (Exception ex) { if (!_closed && generation == _generation) _status.Text = "采样检查未完成，A/B 和原记录保留：" + ex.Message; }
        finally
        {
            if (generation == _generation) { _operation = null; _busy = _samplingBusy = false; UpdateAvailability(); }
            operation.Dispose();
        }
    }

    private static bool CanCheckSampling(CameraImagingResult? run) => run is
    { Request.Source: CameraSource.Scene3D, Request.Aberration: CameraAberration.Diffraction, Inputs.EngineVersion: CameraExperiment.CurrentEngineVersion };

    private void CancelSampling()
    { Cancel(); _status.Text = "采样检查已取消，A/B 和原检查记录保留。"; UpdateAvailability(); }

    private void ResetSampling(CameraImagingResult? run)
    {
        _samplingReport = null;
        if (run is not null) SetSamplingRegion(new(Math.Max(0, (run.Request.Width - 4) / 2), Math.Max(0, (run.Request.Height - 4) / 2)));
        _samplingInfo.Text = "完成三维衍射 B 后，选择原传感器中的区域，逐项检查采样差异。";
    }

    private void SetSamplingRegion(CameraSamplingRegion region)
    {
        var applying = _applyingSensor; _applyingSensor = true;
        try
        {
            _numbers["sampling-x"].Value = region.X; _numbers["sampling-y"].Value = region.Y;
            _numbers["sampling-width"].Value = region.Width; _numbers["sampling-height"].Value = region.Height;
        }
        finally { _applyingSensor = applying; }
    }

    private void RestoreSampling(CameraSamplingReport? report)
    {
        _samplingReport = report;
        if (report is null) { UpdateSamplingControls(); return; }
        SetSamplingRegion(report.Region);
        var r = report.Region;
        var text = new StringBuilder($"原传感器区域 ({r.X}, {r.Y}) · {r.Width} × {r.Height} 像元\nB 身份 {report.SourceRunSha256[..16]} · {report.SourceEngineVersion}\n");
        text.AppendLine("一次只改变所选采样。局部差异不代表整幅或精度认证；原物理容差不变，有限窗口不归一化。噪声与 ISP 不作收敛判据。");
        text.AppendLine($"基准复算对保存 B：最大光电子差 {N(report.SavedElectronDifference.MaximumAbsolute)} e⁻；波段功率最大差 {N(report.SavedBandDifferences.Max(d => d.MaximumAbsolute))} W/像元。");
        foreach (var c in report.Comparisons)
        {
            var label = c.Axis switch { CameraSamplingAxis.Pupil => "瞳面", CameraSamplingAxis.Scene => "场景", CameraSamplingAxis.Pixel => "像元", CameraSamplingAxis.Window => "窗口", _ => "保护带" };
            text.AppendLine($"\n{label} · {(c.Status == CameraSamplingComparisonStatus.Compared ? "已比较" : c.Status == CameraSamplingComparisonStatus.AtLimit ? "已到实现上限" : "未完成")}");
            var basis = report.Baseline;
            foreach (var level in new[] { c }.Concat(c.FurtherRefinements ?? []))
            {
                text.AppendLine($"{Settings(basis.Settings)} → {Settings(level.RequestedSettings)}");
                text.AppendLine(level.Message);
                if (level.Status != CameraSamplingComparisonStatus.Compared) break;
                text.AppendLine(Metric("光电子", level.ElectronDifference!, "e⁻"));
                text.AppendLine(Metric("无噪声 RAW", level.RawDifference!, "DN"));
                for (var b = 0; b < level.BandDifferences!.Count; b++)
                    text.AppendLine(Metric($"{basis.Bands[b].WavelengthNanometers:G4} nm", level.BandDifferences[b], "W/像元"));
                if (level.Refined?.Execution is { } e)
                    text.AppendLine($"本级追迹 {e.LaunchedRays:N0} / {e.MaximumRays:N0}，表面交互 {e.SurfaceInteractions:N0} / {e.MaximumSurfaceInteractions:N0}；省去空窗口辅助光线 {e.AvoidedAuxiliaryRays:N0}，采样点均保留。\n");
                basis = level.Refined!;
            }
        }
        _samplingInfo.Text = text.ToString(); UpdateSamplingControls();
    }

    private static string N(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
    private static string Metric(string name, CameraSamplingDifference d, string unit) =>
        $"{name}：最大差 {N(d.MaximumAbsolute)} {unit}，RMS {N(d.Rms)}；相对 L1 {(d.RelativeL1 is { } l ? N(l * 100) + "%" : "基准无信号")}，相对 RMS {(d.RelativeRms is { } s ? N(s * 100) + "%" : "基准无信号")}；最大差位置 ({d.WorstX}, {d.WorstY})";
    private static string Settings(CameraSamplingSettings s) => $"瞳 {s.Diffraction.PupilSamplesAcross}，场景 {s.Diffraction.SceneSubdivisions}，像元 {s.PixelSampling?.SamplesPerAxis ?? 1}，窗口 {s.Diffraction.PsfRadiusPixels}，保护带 {s.Diffraction.SourceGuardPixels}";

    private void UpdateSamplingControls()
    {
        if (!_numbers.ContainsKey("sampling-x")) return;
        var eligible = CanCheckSampling(_result);
        var valid = SamplingRegionIsValid();
        var reason = _busy ? "正在计算，可取消后再检查。" : !eligible ? "请先运行或打开当前版本的三维衍射结果 B。" : "区域须位于原传感器范围内。";
        ControlAvailability.Set(_checkSampling, !_busy && eligible && valid, reason);
        ControlAvailability.Set(_samplingAxis, !_busy, reason);
        foreach (var key in new[] { "sampling-x", "sampling-y", "sampling-width", "sampling-height" }) ControlAvailability.Set(_numbers[key], !_busy, reason);
        ControlAvailability.Set(_exportSampling, !_busy && _samplingReport is not null && _result is not null, "请先完成至少一项采样检查。");
    }

    private bool SamplingRegionIsValid() => _numbers.Where(p => p.Key.StartsWith("sampling-"))
        .All(p => ValidNumber(p.Value, integer: true))
        && _result is { } run && V("sampling-x") + V("sampling-width") <= run.Request.Width
        && V("sampling-y") + V("sampling-height") <= run.Request.Height;
}
