using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly Dictionary<string, NumericUpDown> _sfrNumbers = new();
    private readonly ComboBox _sfrSpectrumMode = new() { Name = "camera-sfr-spectrum-mode", Width = 190,
        ItemsSource = new[] { "单色", "当前镜头波长与权重" }, SelectedIndex = 1 };
    private readonly Button _sfrRun = new() { Name = "camera-sfr-run", Content = "生成斜边并计算", Classes = { "accent" } };
    private readonly Button _sfrCancel = new() { Name = "camera-sfr-cancel", Content = "取消" };
    private readonly Button _sfrSave = new() { Name = "camera-sfr-save", Content = "保存实验" };
    private readonly Button _sfrOpen = new() { Name = "camera-sfr-open", Content = "打开实验" };
    private readonly Button _sfrReplay = new() { Name = "camera-sfr-replay", Content = "按快照复算" };
    private readonly Button _sfrPng = new() { Name = "camera-sfr-png", Content = "PNG" };
    private readonly TextBlock _sfrInfo = new() { Name = "camera-sfr-info", TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl _sfrGraph = new() { Name = "camera-sfr-graph", MinHeight = 500 };
    private readonly CameraFrameView _sfrSource = new() { Title = "理想斜边 · 线性预览" };
    private readonly CameraFrameView _sfrImage = new() { Title = "镜头生成像 · 线性预览" };
    private DropDownButton? _sfrLineWidth;
    private CancellationTokenSource? _sfrOperation;
    private CameraSfrResult? _sfrResult;
    private bool _sfrBusy, _sfrApplying, _sfrFrozen;
    private int _sfrGeneration;
    internal CameraSfrResult? SfrResult => _sfrResult;

    private Control BuildSfrPage()
    {
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        settings.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 10),
            Children = { new TextBlock { Text = "波长模式" }, _sfrSpectrumMode } });
        AutomationProperties.SetName(_sfrSpectrumMode, "斜边波长模式");
        _sfrSpectrumMode.SelectionChanged += (_, _) => { if (!_sfrApplying) ClearSfr("波长模式已改变，请重新生成斜边。"); };
        void Number(string key, string label, decimal value, decimal min, decimal max, decimal step = 1, bool integer = false)
        {
            var input = new CameraNumberInput { Name = "camera-sfr-" + key, Value = value, Minimum = min,
                Maximum = max, Increment = step, IntegerOnly = integer, FormatString = "0.########", Width = 100 };
            AutomationProperties.SetName(input, label); _sfrNumbers[key] = input;
            var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 10),
                Children = { new TextBlock { Text = label }, input } };
            settings.Children.Add(field);
            input.ValueChanged += (_, _) => { if (!_sfrApplying) ClearSfr("参数已改变，请重新生成斜边。"); };
            input.PropertyChanged += (_, e) =>
            {
                if (e.Property != NumericUpDown.TextProperty || _sfrApplying) return;
                if (!ValidNumber(input, integer)) ClearSfr("请输入有效数值。");
                else UpdateSfrControls();
            };
        }
        Number("wavelength", "波长 (nm)", 550, 200, 20000, 10);
        Number("field", "视场编号", 1, 1, 100, integer: true);
        Number("pitch", "像元边长 (µm)", 3, .1m, 100, .1m);
        Number("angle", "斜边倾角 (°)", 5, 2, 10, .5m);
        Number("pupil", "瞳面采样", 256, 4, 512, 32, true);
        Number("subpixels", "像元每边采样", 8, 1, 16, 1, true);
        Number("radius", "PSF 半径 (像元)", 24, 2, 64, 2, true);
        Number("half-window", "ESF 半窗 (像元)", 24, 8, 48, integer: true);
        Number("roi-x", "ROI X", 0, 0, 64, integer: true);
        Number("roi-y", "ROI Y", 0, 0, 64, integer: true);
        Number("roi-w", "ROI 宽", 96, 32, 96, integer: true);
        Number("roi-h", "ROI 高", 96, 32, 96, integer: true);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        _sfrLineWidth = AnalysisPanel.CreateCurveWidthButton(_sfrGraph);
        foreach (var button in new Control[] { _sfrRun, _sfrCancel, _sfrSave, _sfrOpen, _sfrReplay, _sfrPng, _sfrLineWidth })
        { button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button); }
        var previews = new UniformGrid { Columns = 2, Children = { _sfrSource, _sfrImage } };
        var body = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "当前镜头 · 无噪声 · 单个 ROI。多波长使用镜头波长表及其权重，单色可手动设置。96 × 96 满填充方形像元，暗/亮 0.2/0.8。瞳面、像面和 PSF 窗口分别翻倍检查，差值门限 0.01。", TextWrapping = TextWrapping.Wrap },
            settings, actions, _sfrInfo, previews, _sfrGraph,
            new TextBlock { Text = "SFR 直接使用浮点线性像元数据，预览不参与计算。采用选定视场的局部恒定标量 PSF；未包含全视场畸变、偏振、CFA、ADC 或 ISP。有限 PSF 显式按截取能量归一化，并显示能量及窗口检查。结果不代表 ISO 12233 合规认证。", TextWrapping = TextWrapping.Wrap }
        }};
        _sfrRun.Click += async (_, _) => await RunSfrAsync();
        _sfrReplay.Click += async (_, _) => await RunSfrAsync(true);
        _sfrCancel.Click += (_, _) => ClearSfr("斜边计算已取消。");
        _sfrSave.Click += async (_, _) => await SaveSfrAsync();
        _sfrOpen.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开斜边实验", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("斜边实验") { Patterns = ["*.sfr-experiment.zip"] }] });
            if (files.Count > 0) await OpenSfrFromPathAsync(files[0].Path.LocalPath);
        };
        _sfrPng.Click += async (_, _) => await SaveSfrPngAsync();
        _sfrInfo.Text = "生成斜边测试图后，将显示 SFR、同条件 MTF 和独立采样检查。";
        UpdateSfrControls(); return Scroll(body);
    }

    private void ClearSfr(string message, bool lensChange = false)
    {
        if (lensChange && _sfrFrozen && !_sfrBusy) { UpdateSfrControls(); return; }
        _sfrGeneration++; _sfrOperation?.Cancel(); _sfrOperation = null; _sfrBusy = false; _sfrFrozen = false;
        _sfrResult = null; _sfrGraph.Content = null; _sfrSource.Set(null); _sfrImage.Set(null);
        _sfrInfo.Text = message; UpdateSfrControls();
        if (_sfrNumbers.Count > 0 && _numbers.ContainsKey("fill-factor")) UpdateAvailability();
    }

    private void UpdateSfrControls()
    {
        var validLens = _application.Modes.CurrentMode == OpticalWorkbenchMode.Sequential && _application.Documents.GetSnapshot().SurfaceCount >= 4;
        var useLensSpectrum = _sfrSpectrumMode.SelectedIndex == 1;
        var valid = _sfrNumbers.Count > 0 && _sfrNumbers.All(p => useLensSpectrum && p.Key == "wavelength"
            || ValidNumber(p.Value, p.Key is "field" or "pupil" or "subpixels" or "radius" or "half-window" or "roi-x" or "roi-y" or "roi-w" or "roi-h"));
        if (_sfrNumbers.TryGetValue("wavelength", out var wavelength))
            ControlAvailability.Set(wavelength, !useLensSpectrum, "多波长模式使用当前镜头波长表及其权重。");
        ControlAvailability.Set(_sfrRun, !_closed && !_busy && !_sfrBusy && !SceneImagingBusy && validLens && valid, "需要有效顺序镜头和有效数值；像元每边采样可选 1、2、4、8、16。");
        ControlAvailability.Set(_sfrCancel, _sfrBusy, "当前没有斜边计算。");
        ControlAvailability.Set(_sfrSave, !_sfrBusy && _sfrResult is not null, "先完成斜边计算或打开实验。");
        var currentModel = _sfrResult is null || CameraSfrResult.CanRecompute(_sfrResult.Algorithm);
        ControlAvailability.Set(_sfrReplay, !_sfrBusy && !_busy && !SceneImagingBusy && _sfrResult is not null && currentModel,
            !currentModel ? "旧版实验仅供查看，未使用当前完整瞳孔对照；请用当前镜头重新生成。" : "先完成计算或打开实验；按内嵌镜头复算。");
        ControlAvailability.Set(_sfrOpen, !_sfrBusy && !_busy && !SceneImagingBusy, "请先等待当前计算结束。");
        ControlAvailability.Set(_sfrPng, !_sfrBusy && _sfrResult is not null, "先显示斜边曲线。");
        if (_sfrLineWidth is not null) _sfrLineWidth.IsVisible = _sfrResult is not null;
    }

    internal async Task RunSfrAsync(bool replay = false)
    {
        if (_closed || _sfrBusy || _busy || SceneImagingBusy) return;
        var previous = replay ? _sfrResult : null;
        if (replay && previous is null) return;
        try
        {
            double V(string key) => ValidNumber(_sfrNumbers[key], key is "field" or "pupil" or "subpixels" or "radius" or "half-window" or "roi-x" or "roi-y" or "roi-w" or "roi-h")
                ? (double)_sfrNumbers[key].Value!.Value : throw new InvalidOperationException("请输入有效数值。");
            var request = previous?.Request ?? new CameraSfrRequest(WavelengthNanometers: _sfrSpectrumMode.SelectedIndex == 1 ? 550 : V("wavelength"), FieldNumber: (int)V("field"),
                PixelPitchMicrometers: V("pitch"), EdgeAngleDegrees: V("angle"), PupilSampling: (int)V("pupil"),
                ImageSamplesPerPixel: (int)V("subpixels"), PsfRadiusPixels: (int)V("radius"),
                RoiX: (int)V("roi-x"), RoiY: (int)V("roi-y"), RoiWidth: (int)V("roi-w"), RoiHeight: (int)V("roi-h"), EdgeHalfWindowPixels: (int)V("half-window"),
                UseLensWavelengths: _sfrSpectrumMode.SelectedIndex == 1);
            ClearSfr("正在生成线性斜边、测量 SFR 并检查三项采样…");
            var generation = _sfrGeneration; var operation = new CancellationTokenSource();
            _sfrOperation = operation; _sfrBusy = true; UpdateAvailability();
            try
            {
                var result = previous is null ? await _application.CameraImaging.SimulateSlantedEdgeAsync(request, operation.Token)
                    : await _application.CameraImaging.RecomputeSlantedEdgeAsync(previous, operation.Token);
                if (_closed || generation != _sfrGeneration) return;
                _sfrFrozen = previous is not null; PresentSfr(result);
            }
            catch (OperationCanceledException) { if (!_closed && generation == _sfrGeneration) _sfrInfo.Text = "斜边计算已取消。"; }
            catch (Exception ex) { if (!_closed && generation == _sfrGeneration) _sfrInfo.Text = "斜边计算失败：" + ex.Message; }
            finally
            {
                if (generation == _sfrGeneration) { _sfrOperation = null; _sfrBusy = false; UpdateAvailability(); }
                operation.Dispose();
            }
        }
        catch (Exception ex) { _sfrInfo.Text = ex.Message; }
    }

    internal void PresentSfr(CameraSfrResult result)
    {
        _sfrResult = result; var r = result.Request; var f = result.Baseline;
        CameraImage Image(IReadOnlyList<double> values) => new(r.Width, r.Height,
            values.SelectMany(v => Enumerable.Repeat((byte)Math.Round(Math.Clamp(v, 0, 1) * 255), 3)).ToArray());
        _sfrSource.Set(Image(f.Source)); _sfrImage.Set(Image(f.LinearPixels));
        var roi = $"ROI ({r.RoiX}, {r.RoiY}), {r.RoiWidth} × {r.RoiHeight}";
        string Number(double value) => NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture);
        var spectralDescription = r.UseLensWavelengths && f.Spectrum is { } bands
            ? "镜头多波长 · " + string.Join("; ", bands.Select(b => $"W{b.WavelengthNumber}: {Number(b.WavelengthNanometers)} nm × {Number(b.Weight)}"))
            : $"{Number(r.WavelengthNanometers)} nm";
        var spectralNote = r.UseLensWavelengths
            ? $"公共参考：主波长 {Number(f.ReferenceWavelengthNanometers!.Value)} nm；权重按相对像面能量使用，零权重不参与；未附加 QE/光子权重。\n" : "";
        string Crossing(double? value) => value.HasValue ? $"{value:F4} cycles/pixel ({value / (r.PixelPitchMicrometers / 1000):F2} cycles/mm)" : "Nyquist 内未交叉";
        string Axis(string axis) => axis switch { "Pupil" => "瞳面采样", "ImagePlane" => "像面采样", _ => "PSF 窗口" };
        var checks = result.Sampling.Select(c => c.Refined is null ? $"{Axis(c.Axis)}：未完成 · {c.Message}"
            : $"{Axis(c.Axis)}：{(c.WithinCriteria ? "通过" : "未收敛")}；图像 {c.MaximumImageDifference:G4}，SFR {c.MaximumSfrDifference:G4}，窗口 MTF {c.MaximumMtfDifference:G4}，完整瞳孔 MTF {c.MaximumFullPupilMtfDifference:G4}，能量 {c.RelativeReadoutIntegralDifference:G4}");
        var historical = !CameraSfrResult.CanRecompute(result.Algorithm);
        var fullReference = f.Curve.All(p => p.FullPupilMtf.HasValue && p.FullPupilPixelIntegratedMtf.HasValue);
        var referenceAgrees = f.MaximumSfrFullPupilDifference <= r.DifferenceThreshold && f.MaximumWindowFullPupilDifference <= r.DifferenceThreshold;
        _sfrInfo.Text = (historical ? "旧版实验记录：未使用当前完整瞳孔对照与瞳孔单元积分，仅供查看。\n" : "")
            + $"{result.DocumentName} · 修订 {result.SourceRevision} · Field {r.FieldNumber} · {spectralDescription} · {roi}\n" + spectralNote
            + $"MTF50：{Crossing(f.Mtf50CyclesPerPixel)}；MTF10：{Crossing(f.Mtf10CyclesPerPixel)}\n"
            + $"拟合边缘 {Math.Atan(f.EdgeSlope) * 180 / Math.PI:F4}°，残差 {f.EdgeFitRmsPixels:G4} px；PSF 截取能量 {f.RelativePsfReadoutIntegral:P3}；SFR 与窗口含像元 MTF 最大差 {f.MaximumSfrReferenceDifference:G4}\n"
            + (fullReference ? $"完整瞳孔对照：{(referenceAgrees ? "通过" : "未通过")}；SFR 与完整瞳孔含像元 MTF 最大差 {f.MaximumSfrFullPupilDifference:G4}，窗口与完整瞳孔光学 MTF 差 {f.MaximumWindowFullPupilDifference:G4}\n" : "完整瞳孔对照：旧版未提供\n")
            + $"采样检查：{(result.SamplingConverged == true ? "通过" : result.SamplingConverged == false ? "未收敛 / 未完成" : "未执行")}（门限 {r.DifferenceThreshold:G}）\n" + string.Join("\n", checks);
        AnalysisSeriesDto Curve(string name, int color, Func<CameraSfrPoint, double> value, AnalysisLineStyle style = AnalysisLineStyle.Solid) =>
            new("Spatial frequency", "Response", f.Curve.Select(p => new AnalysisPointDto(p.CyclesPerPixel, value(p))).ToArray(),
                Name: name, ColorIndex: color, LineStyle: style, LegendKey: name, LegendLabel: name, XQuantity: AnalysisAxisQuantity.SpatialFrequency, XUnit: AnalysisAxisUnit.CyclesPerPixel,
                YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless);
        var curves = new List<AnalysisSeriesDto> { Curve("SFR · 线性像元", 0, p => p.Sfr),
            Curve("MTF · 有限 PSF", 1, p => p.OpticalMtf, AnalysisLineStyle.Dashed), Curve("MTF · 有限 PSF × 像元", 2, p => p.PixelIntegratedMtf) };
        if (fullReference)
        {
            curves.Add(Curve("MTF · 完整瞳孔", 3, p => p.FullPupilMtf!.Value, AnalysisLineStyle.Dashed));
            curves.Add(Curve("MTF · 完整瞳孔 × 像元", 4, p => p.FullPupilPixelIntegratedMtf!.Value));
        }
        var view = new AnalysisViewDto(r.UseLensWavelengths ? "多波长斜边 SFR" : "单色斜边 SFR",
            [new("条件", $"Field {r.FieldNumber}; {spectralDescription}; {r.PixelPitchMicrometers:G} µm"), new("区域", roi),
                new("完整瞳孔对照", fullReference ? referenceAgrees ? "通过" : "未通过" : "未提供"),
                new("采样检查", historical ? "旧版记录 / 未按当前规则检查" : result.SamplingConverged == true ? "通过" : "未通过 / 未执行"), new("PSF 截取能量", f.RelativePsfReadoutIntegral.ToString("P3", CultureInfo.InvariantCulture))], "",
            curves,
            new(Title: r.UseLensWavelengths ? "Polychromatic slanted-edge SFR" : "Monochrome slanted-edge SFR", XMinimum: 0, XMaximum: .5, YMinimum: 0,
                YMaximum: Math.Max(1.05, f.Curve.Max(p => p.Sfr) * 1.05), ShowLegend: true, DottedGrid: true), [], 1);
        var document = result.SourceDocument ?? throw new InvalidDataException("缺少冻结文档来源。");
        _sfrGraph.Content = AnalysisPanel.CreateResultPlot(view, document, DateTimeOffset.Now); UpdateSfrControls();
    }

    internal async Task<bool> OpenSfrFromPathAsync(string path)
    {
        if (_sfrBusy || _closed) return false;
        try
        {
            var result = await _application.CameraImaging.OpenSlantedEdgeAsync(path);
            if (_closed) return false;
            ClearSfr(""); _sfrApplying = true;
            try
            {
                var r = result.Request;
                _sfrSpectrumMode.SelectedIndex = r.UseLensWavelengths ? 1 : 0;
                foreach (var (key, value) in new (string, double)[] { ("wavelength", r.WavelengthNanometers), ("field", r.FieldNumber),
                    ("pitch", r.PixelPitchMicrometers), ("angle", r.EdgeAngleDegrees), ("pupil", r.PupilSampling), ("subpixels", r.ImageSamplesPerPixel),
                    ("radius", r.PsfRadiusPixels), ("half-window", r.EdgeHalfWindowPixels), ("roi-x", r.RoiX), ("roi-y", r.RoiY), ("roi-w", r.RoiWidth), ("roi-h", r.RoiHeight) }) _sfrNumbers[key].Value = (decimal)value;
            }
            finally { _sfrApplying = false; }
            _sfrFrozen = true; PresentSfr(result); return true;
        }
        catch (Exception ex) { if (!_closed) _sfrInfo.Text = "实验打开失败：" + ex.Message; return false; }
    }

    private async Task SaveSfrAsync()
    {
        var result = _sfrResult; if (_sfrBusy || result is null) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存斜边实验", SuggestedFileName = "slanted-edge.sfr-experiment.zip",
                ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("斜边实验") { Patterns = ["*.sfr-experiment.zip"] }] });
            if (file is not null) await _application.CameraImaging.ExportSlantedEdgeAsync(result, file.Path.LocalPath);
        }
        catch (Exception ex) { if (!_closed) _sfrInfo.Text = "保存失败：" + ex.Message; }
    }

    internal RenderTargetBitmap CaptureSfrImage()
    {
        var page = _sfrGraph.Content as ResponsiveAnalysisPlotHost ?? throw new InvalidOperationException("先显示斜边曲线。");
        page.UpdateLayout(); var content = page.Child ?? throw new InvalidOperationException("曲线尚未显示。");
        if (content is UniformDiagramPage source) source.ValidateForExport();
        return ExportImageFrame.Capture(content, UniformDiagramPage.ExportScale, this);
    }
    private async Task SaveSfrPngAsync()
    {
        if (_sfrBusy || _sfrResult is null) return;
        try
        {
            using var bitmap = CaptureSfrImage();
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出斜边曲线", SuggestedFileName = "slanted-edge.png",
                DefaultExtension = "png", ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }] });
            if (file is not null) AnalysisPanel.SavePlotImage(bitmap, file.Path.LocalPath);
        }
        catch (Exception ex) { if (!_closed) _sfrInfo.Text = "导出失败：" + ex.Message; }
    }
}

