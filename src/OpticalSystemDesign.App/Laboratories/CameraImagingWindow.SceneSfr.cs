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
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly Dictionary<string, CameraNumberInput> _sceneEdgeNumbers = [];
    private readonly Button _sceneEdgeRun = new() { Name = "camera-scene-edge-run", Content = "生成物方棋盘像" };
    private readonly Button _sceneEdgeReplay = new() { Content = "按冻结镜头复算" };
    private readonly Button _sceneEdgeCancel = new() { Content = "取消" };
    private readonly Button _sceneEdgeSave = new() { Content = "保存线性实验" };
    private readonly Button _sceneEdgeOpen = new() { Content = "打开线性实验" };
    private readonly TextBlock _sceneEdgeInfo = new() { Name = "camera-scene-edge-info", TextWrapping = TextWrapping.Wrap };
    private readonly CameraFrameView _sceneEdgeTarget = new() { Title = "物方棋盘 · 靶面局部坐标预览" };
    private readonly CameraFrameView _sceneEdgeImage = new() { Title = "几何场景像 · 线性预览" };
    private readonly ContentControl _sceneEdgeGraph = new() { Name = "camera-scene-edge-graph" };
    private DropDownButton? _sceneEdgeLineWidth;
    private readonly CheckBox _sceneEdgeMeasure = new() { Content = "测量 ROI（近竖直边）", IsChecked = true };
    private readonly CheckBox _sceneEdgePrimary = new() { Content = "使用镜头主波长", IsChecked = true };
    private readonly CheckBox _sceneEdgeFresnel = new() { Content = "计算裸界面 Fresnel 损失", IsChecked = true };
    private CancellationTokenSource? _sceneEdgeOperation;
    private bool _sceneSfrBusy, _sceneEdgeApplying, _sceneEdgeFrozen;
    private int _sceneEdgeGeneration;
    private CameraSceneSfrResult? _sceneEdgeResult;
    internal CameraSceneSfrResult? SceneEdgeResult => _sceneEdgeResult;

    private Control BuildSceneEdgePage()
    {
        var settings = new WrapPanel { Orientation = Orientation.Horizontal };
        void Number(string key, string label, decimal value, decimal min, decimal max, bool integer = false)
        {
            var input = new CameraNumberInput { Name = "camera-scene-edge-" + key, Value = value, Minimum = min,
                Maximum = max, Increment = integer ? 1 : .1m, IntegerOnly = integer, FormatString = "0.########", Width = 100 };
            AutomationProperties.SetName(input, label); _sceneEdgeNumbers[key] = input;
            settings.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 10),
                Children = { new TextBlock { Text = label }, input } });
            input.ValueChanged += (_, _) => { if (!_sceneEdgeApplying) ClearSceneEdge("参数已改变，请重新生成场景像。"); };
            input.PropertyChanged += (_, e) => { if (e.Property == NumericUpDown.TextProperty && !_sceneEdgeApplying) ClearSceneEdge("请输入有效参数后生成场景像。"); };
        }
        Number("wavelength", "单波段波长 (nm)", 550, 200, 20000);
        Number("distance", "靶标到第一镜面 (mm)", 100000, 1.001m, 1000000);
        Number("columns", "棋盘列数", 14, 1, 4096, true); Number("rows", "棋盘行数", 14, 1, 4096, true);
        Number("cell", "格边长 (mm)", 2000, .001m, 100000);
        // A declared distant-target control: place the middle of one vertical cell edge on the lens axis.
        // These are chart pose coordinates, independent of lens focal length/aberrations; no focus change.
        Number("x", "靶标中心 X (mm)", (decimal)(-Math.Sin(5 * Math.PI / 180) * 1000), -100000, 100000);
        Number("y", "靶标中心 Y (mm)", (decimal)(Math.Cos(5 * Math.PI / 180) * 1000), -100000, 100000);
        Number("tilt-x", "靶标 X 旋转 (°)", 0, -180, 180); Number("tilt-y", "靶标 Y 旋转 (°)", 0, -180, 180);
        Number("angle", "靶标 Z 旋转 (°)", 5, -180, 180);
        Number("bright", "亮格辐亮度 (W/m²/sr)", 1, 0, 1000000); Number("dark", "暗格辐亮度 (W/m²/sr)", .1m, 0, 1000000);
        Number("width", "完整图像宽 (px)", 96, 4, 1024, true); Number("height", "完整图像高 (px)", 96, 4, 1024, true);
        Number("pitch", "像元边长 (µm)", 3, .1m, 100);
        Number("subpixels", "像元每边采样", 2, 1, 4, true); Number("rings", "瞳面径向环数", 8, 1, 8, true); Number("arms", "每环方位点数", 24, 4, 32, true);
        Number("roi-x", "ROI X", 0, 0, 1023, true); Number("roi-y", "ROI Y", 0, 0, 1023, true);
        Number("roi-width", "ROI 宽", 96, 32, 256, true); Number("roi-height", "ROI 高", 96, 32, 256, true);
        Number("window", "ESF 半窗 (px)", 24, 8, 48, true);
        foreach (var check in new[] { _sceneEdgePrimary, _sceneEdgeMeasure, _sceneEdgeFresnel })
            check.IsCheckedChanged += (_, _) => { if (!_sceneEdgeApplying) ClearSceneEdge("选项已改变，请重新生成场景像。"); };
        _sceneEdgeRun.Classes.Add("accent");
        _sceneEdgeRun.Click += async (_, _) => await RunSceneEdgeAsync();
        _sceneEdgeReplay.Click += async (_, _) => await RunSceneEdgeAsync(true);
        _sceneEdgeCancel.Click += (_, _) => ClearSceneEdge("场景计算已取消。");
        _sceneEdgeSave.Click += async (_, _) =>
        {
            var result = _sceneEdgeResult; if (result is null) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存物方棋盘线性实验",
                SuggestedFileName = "chart.scene-sfr.zip", FileTypeChoices = [new("场景斜边实验") { Patterns = ["*.scene-sfr.zip"] }] });
            if (file is null || _closed) return;
            try { await _application.CameraImaging.ExportSceneSlantedEdgeAsync(result, file.Path.LocalPath); _sceneEdgeInfo.Text += "\n线性像元与冻结输入已保存。"; }
            catch (Exception ex) { _sceneEdgeInfo.Text += "\n保存失败：" + ex.Message; }
        };
        _sceneEdgeOpen.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开物方棋盘线性实验",
                FileTypeFilter = [new("场景斜边实验") { Patterns = ["*.scene-sfr.zip"] }] });
            if (files.Count > 0) await OpenSceneEdgeFromPathAsync(files[0].Path.LocalPath);
        };
        var png = new Button { Content = "PNG", Name = "camera-scene-edge-png" };
        png.Click += async (_, _) =>
        {
            if (_sceneEdgeGraph.Content is not ResponsiveAnalysisPlotHost) return;
            try
            {
                using var bitmap = CaptureSceneEdgeImage();
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出场景 SFR 曲线", SuggestedFileName = "scene-sfr.png",
                    DefaultExtension = "png", FileTypeChoices = [new("PNG") { Patterns = ["*.png"] }] });
                if (file is not null) AnalysisPanel.SavePlotImage(bitmap, file.Path.LocalPath);
            }
            catch (Exception ex) { if (!_closed) _sceneEdgeInfo.Text += "\n导出失败：" + ex.Message; }
        };
        _sceneEdgeLineWidth = AnalysisPanel.CreateCurveWidthButton(_sceneEdgeGraph);
        _sceneEdgeInfo.Text = "物方发光棋盘 → 当前实际镜头 → 线性像元。默认使用镜头主波长；请根据镜头取景设置靶标和 ROI。";
        var roiPage = Scroll(new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "场景斜边 · 几何成像", FontSize = 18, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "此页沿用镜片与保存像面，不自动调焦。初始参数是 100 m 远靶控制；有限共轭请填写实际物距。浮点功率和辐照度用于测量；两张预览仅用于取景。当前阶段尚未提供正式 MTF 对照、ROM 或收敛判定。", TextWrapping = TextWrapping.Wrap },
            settings, LaboratoryPresentation.Commands(_sceneEdgePrimary, _sceneEdgeMeasure, _sceneEdgeFresnel),
            LaboratoryPresentation.Commands(_sceneEdgeRun, _sceneEdgeCancel, _sceneEdgeSave, _sceneEdgeOpen, _sceneEdgeReplay, png, _sceneEdgeLineWidth),
            _sceneEdgeInfo, new UniformGrid { Columns = 2, Children = { _sceneEdgeTarget, _sceneEdgeImage } }, _sceneEdgeGraph
        } });
        return new TabControl { Name = "SceneImagingModes", ItemsSource = new[]
        {
            new TabItem { Header = "单 ROI 诊断", Content = roiPage },
            new TabItem { Header = "全幅逐波段", Name = "scene-linear-batch-tab", Content = BuildSceneBatchPage() }
        } };
    }

    private static bool SceneEdgeInteger(string key) => key is "columns" or "rows" or "width" or "height" or "subpixels" or "rings" or "arms"
        or "roi-x" or "roi-y" or "roi-width" or "roi-height" or "window";
    private void UpdateSceneEdgeControls()
    {
        var valid = _sceneEdgeNumbers.Count > 0 && _sceneEdgeNumbers.All(p => ValidNumber(p.Value, SceneEdgeInteger(p.Key)));
        var ready = !_closed && !_busy && !_sfrBusy && !SceneImagingBusy;
        ControlAvailability.Set(_sceneEdgeRun, ready && valid && _application.Modes.CurrentMode == OpticalWorkbenchMode.Sequential,
            "需要顺序镜头和有效参数，且当前没有其他计算。");
        ControlAvailability.Set(_sceneEdgeCancel, _sceneSfrBusy, "当前没有场景计算。");
        ControlAvailability.Set(_sceneEdgeReplay, ready && _sceneEdgeResult is not null, "先生成或打开线性实验。");
        ControlAvailability.Set(_sceneEdgeSave, !_sceneSfrBusy && _sceneEdgeResult is not null, "先生成或打开线性实验。");
        ControlAvailability.Set(_sceneEdgeOpen, ready, "请等待当前计算完成。");
        if (_sceneEdgeLineWidth is not null) _sceneEdgeLineWidth.IsVisible = _sceneEdgeGraph.Content is not null;
        if (_sceneEdgeNumbers.TryGetValue("wavelength", out var wave))
            ControlAvailability.Set(wave, _sceneEdgePrimary.IsChecked != true, "此选项使用当前镜头的实际主波长。");
    }

    private void ClearSceneEdge(string message, bool lensChange = false)
    {
        if (lensChange && _sceneEdgeFrozen && !_sceneSfrBusy) return;
        _sceneEdgeGeneration++; _sceneEdgeOperation?.Cancel(); _sceneEdgeOperation = null; _sceneSfrBusy = false; _sceneEdgeFrozen = false;
        _sceneEdgeResult = null; _sceneEdgeGraph.Content = null; _sceneEdgeImage.Set(null); _sceneEdgeTarget.Set(null);
        _sceneEdgeInfo.Text = message; UpdateSceneEdgeControls();
    }

    internal async Task RunSceneEdgeAsync(bool replay = false)
    {
        if (_closed || _busy || _sfrBusy || SceneImagingBusy) return;
        var previous = replay ? _sceneEdgeResult : null; if (replay && previous is null) return;
        try
        {
            double V(string key) => ValidNumber(_sceneEdgeNumbers[key], SceneEdgeInteger(key)) ? (double)_sceneEdgeNumbers[key].Value!.Value
                : throw new InvalidOperationException("请输入有效场景参数。");
            var request = previous?.Request ?? new CameraSceneSfrRequest(
                new((int)V("columns"), (int)V("rows"), V("cell"), V("distance"), V("x"), V("y"), V("tilt-x"), V("tilt-y"), V("angle")),
                [new(V("wavelength"), V("bright"), V("dark"))],
                (int)V("width"), (int)V("height"), V("pitch"), (int)V("subpixels"), 1, (int)V("rings"), (int)V("arms"),
                _sceneEdgeFresnel.IsChecked == true,
                _sceneEdgeMeasure.IsChecked == true ? new(new((int)V("roi-x"), (int)V("roi-y"), (int)V("roi-width"), (int)V("roi-height")), HalfWindowPixels: (int)V("window")) : null,
                CaptureLensPrimaryWavelength: _sceneEdgePrimary.IsChecked == true);
            ClearSceneEdge("正在从物方发光面追迹完整线性像元…");
            var generation = _sceneEdgeGeneration; var operation = new CancellationTokenSource(); _sceneEdgeOperation = operation; _sceneSfrBusy = true; UpdateAvailability();
            try
            {
                var result = previous is null ? await _application.CameraImaging.SimulateSceneSlantedEdgeAsync(request, operation.Token)
                    : await _application.CameraImaging.RecomputeSceneSlantedEdgeAsync(previous, operation.Token);
                if (!_closed && generation == _sceneEdgeGeneration) { _sceneEdgeFrozen = replay; PresentSceneEdge(result); }
            }
            catch (OperationCanceledException) { if (generation == _sceneEdgeGeneration) _sceneEdgeInfo.Text = "场景计算已取消。"; }
            catch (Exception ex) { if (generation == _sceneEdgeGeneration) _sceneEdgeInfo.Text = "场景计算失败：" + ex.Message; }
            finally { if (generation == _sceneEdgeGeneration) { _sceneSfrBusy = false; _sceneEdgeOperation = null; } operation.Dispose(); UpdateAvailability(); }
        }
        catch (Exception ex) { _sceneEdgeInfo.Text = ex.Message; }
    }

    internal void PresentSceneEdge(CameraSceneSfrResult result)
    {
        _sceneEdgeResult = result; var frame = result.Frame; var request = result.Request; var image = frame.ToLinearImage();
        var values = image.Bands[0].WattsPerSquareMeter; var ceiling = values.Max();
        _sceneEdgeImage.Set(new(frame.Width, frame.Height, values.SelectMany(v => Enumerable.Repeat((byte)Math.Round(Math.Clamp(ceiling > 0 ? v / ceiling : 0, 0, 1) * 255), 3)).ToArray()));
        var chart = request.Chart!; const int preview = 196;
        var pw = Math.Max(1, preview * chart.Columns / Math.Max(chart.Columns, chart.Rows));
        var ph = Math.Max(1, preview * chart.Rows / Math.Max(chart.Columns, chart.Rows));
        var bytes = new byte[pw * ph * 3];
        var band = request.Bands![0]; var sourceMax = Math.Max(band.BrightRadiance, band.DarkRadiance);
        for (var y = 0; y < ph; y++) for (var x = 0; x < pw; x++)
        {
            var dark = ((long)x * chart.Columns / pw + (long)(ph - 1 - y) * chart.Rows / ph) % 2 != 0;
            var level = dark ? band.DarkRadiance : band.BrightRadiance;
            for (var c = 0; c < 3; c++) bytes[(y * pw + x) * 3 + c] = (byte)Math.Round((sourceMax > 0 ? level / sourceMax : 0) * 255);
        }
        _sceneEdgeTarget.Set(new(pw, ph, bytes));
        _sceneEdgeInfo.Text = (_sceneEdgeFrozen ? "冻结实验 · 与当前工程独立\n" : "当前镜头生成 · 修改工程后清除\n")
            + $"{result.SourceDocument.Name} · 修订 {result.SourceDocument.Revision} · {band.WavelengthNanometers:G8} nm\n"
            + $"物方靶标 {chart.Columns} × {chart.Rows} 格，格边 {chart.CellSizeMillimeters:G8} mm，距离 {chart.DistanceMillimeters:G8} mm；Z 旋转 {chart.RotationZDegrees:G6}°\n"
            + $"完整探测器 {frame.Width} × {frame.Height}，像元 {frame.PixelPitchMicrometers:G6} µm；总功率 {image.Power.TotalPowerWatts:G8} W；像面峰值 {ceiling:G8} W/m²\n"
            + $"光线 {frame.LaunchedRays:N0}，通过镜头 {frame.PassedLensRays:N0}，击中靶面 {frame.SceneHitRays:N0}；未作采样收敛判定。\n"
            + $"原记录 {frame.Bands.Count} 波段，预览第 1 波段；预览分别按自身峰值显示，测量和导出保留原始线性量。\n"
            + (result.Edge is { } edge ? $"ROI SFR：拟合残差 {edge.Measurement.EdgeFitRmsPixels:G5} px；MTF50 {(edge.Measurement.Mtf50CyclesPerPixel?.ToString("G6", CultureInfo.InvariantCulture) ?? "Nyquist 内未交叉")} cycles/pixel。正式 MTF 对照待接入。"
                : result.EdgeFailure is { } reason ? "ROI 测量失败：" + reason : "未请求 ROI 测量。");
        _sceneEdgeGraph.Content = null;
        if (result.Edge is { } measured)
        {
            var measuredBand = request.Bands![request.Edge!.BandIndex];
            var curve = new AnalysisSeriesDto("Spatial frequency", "SFR", measured.Measurement.Curve.Select(p => new AnalysisPointDto(p.CyclesPerPixel, p.Response)).ToArray(),
                Name: "SFR · 场景线性像元", ColorIndex: 0, LegendKey: "scene-sfr", LegendLabel: "SFR · 场景线性像元",
                XQuantity: AnalysisAxisQuantity.SpatialFrequency, XUnit: AnalysisAxisUnit.CyclesPerPixel,
                YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless);
            var view = new AnalysisViewDto("物方棋盘 SFR", [new("传播", "几何逆向追迹"), new("测量波段", $"Band {request.Edge.BandIndex + 1}; {measuredBand.WavelengthNanometers:G8} nm"), new("正式 MTF / 收敛", "待接入")], "", [curve],
                new(Title: "Physical scene slanted-edge SFR", XMinimum: 0, XMaximum: .5, YMinimum: 0, YMaximum: Math.Max(1.05, measured.Measurement.Curve.Max(p => p.Response) * 1.05), ShowLegend: true, DottedGrid: true), [], 1);
            _sceneEdgeGraph.Content = AnalysisPanel.CreateResultPlot(view, result.SourceDocument, DateTimeOffset.Now);
        }
        UpdateSceneEdgeControls();
    }

    internal RenderTargetBitmap CaptureSceneEdgeImage()
    {
        var page = _sceneEdgeGraph.Content as ResponsiveAnalysisPlotHost ?? throw new InvalidOperationException("先显示场景 SFR 曲线。");
        page.UpdateLayout(); var content = page.Child ?? throw new InvalidOperationException("曲线尚未显示。");
        if (content is UniformDiagramPage source) source.ValidateForExport();
        return ExportImageFrame.Capture(content, UniformDiagramPage.ExportScale, this);
    }

    internal async Task<bool> OpenSceneEdgeFromPathAsync(string path)
    {
        if (_busy || _sfrBusy || SceneImagingBusy || _closed) return false;
        var generation = _sceneEdgeGeneration;
        try
        {
            var result = await _application.CameraImaging.OpenSceneSlantedEdgeAsync(path);
            if (_closed || generation != _sceneEdgeGeneration) return false;
            ClearSceneEdge(""); _sceneEdgeApplying = true;
            try
            {
                var r = result.Request; var c = r.Chart!; var e = r.Edge;
                foreach (var (key, value) in new (string, double)[] { ("wavelength", r.Bands![0].WavelengthNanometers), ("distance", c.DistanceMillimeters),
                    ("columns", c.Columns), ("rows", c.Rows), ("cell", c.CellSizeMillimeters), ("x", c.CenterXMillimeters), ("y", c.CenterYMillimeters),
                    ("tilt-x", c.RotationXDegrees), ("tilt-y", c.RotationYDegrees), ("angle", c.RotationZDegrees), ("bright", r.Bands[0].BrightRadiance), ("dark", r.Bands[0].DarkRadiance),
                    ("width", r.Width), ("height", r.Height), ("pitch", r.PixelPitchMicrometers), ("subpixels", r.PixelSamplesPerAxis), ("rings", r.PupilRings), ("arms", r.PupilArms),
                    ("roi-x", e?.Region.X ?? 0), ("roi-y", e?.Region.Y ?? 0), ("roi-width", e?.Region.Width ?? 96), ("roi-height", e?.Region.Height ?? 96), ("window", e?.HalfWindowPixels ?? 24) })
                    _sceneEdgeNumbers[key].Value = (decimal)value;
                _sceneEdgePrimary.IsChecked = false; _sceneEdgeMeasure.IsChecked = e is not null; _sceneEdgeFresnel.IsChecked = r.BareInterfaceFresnel;
            }
            finally { _sceneEdgeApplying = false; }
            _sceneEdgeFrozen = true; PresentSceneEdge(result); return true;
        }
        catch (Exception ex) { if (!_closed) _sceneEdgeInfo.Text = "线性实验打开失败：" + ex.Message; return false; }
    }
}
