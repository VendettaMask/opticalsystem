using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    private readonly List<LinearSfrRoi> _sceneRois = [];
    private readonly Dictionary<string, CameraNumberInput> _sceneRoiNumbers = [];
    private readonly TextBox _sceneRoiName = new() { Name = "camera-roi-name", Text = "ROI 1", Width = 140 };
    private readonly ComboBox _sceneRoiDirection = new() { Name = "camera-roi-direction", ItemsSource = new[] { "近竖直边", "近水平边" }, SelectedIndex = 0, Width = 150 };
    private readonly ComboBox _sceneRoiTask = new() { Name = "camera-roi-task", Width = 300 };
    private readonly ComboBox _sceneRoiProfile = new() { Name = "camera-roi-profile", ItemsSource = new[] { "SFR", "ESF", "LSF" }, SelectedIndex = 0, Width = 110 };
    private readonly CheckBox _sceneRoiAllTasks = new() { Content = "测量全部波段任务", IsChecked = true };
    private readonly ListBox _sceneRoiList = new() { Name = "camera-roi-list", MinHeight = 130, MaxHeight = 240 };
    private readonly LinearRoiImageView _sceneRoiImage = new() { Name = "camera-roi-image" };
    private readonly TextBlock _sceneRoiInfo = new() { Name = "camera-roi-info", TextWrapping = TextWrapping.Wrap, Text = "先在“全幅逐波段”生成或打开完整线性实验。" };
    private readonly TextBlock _sceneRoiDetail = new() { Name = "camera-roi-detail", TextWrapping = TextWrapping.Wrap };
    private readonly Button _sceneRoiAdd = new() { Name = "camera-roi-add", Content = "添加 ROI" };
    private readonly Button _sceneRoiUpdate = new() { Name = "camera-roi-update", Content = "更新选中" };
    private readonly Button _sceneRoiDelete = new() { Name = "camera-roi-delete", Content = "删除选中" };
    private readonly Button _sceneRoiRun = new() { Name = "camera-roi-run", Content = "计算 SFR" };
    private readonly Button _sceneRoiCancel = new() { Content = "取消测量" };
    private readonly Button _sceneRoiSave = new() { Name = "camera-roi-save", Content = "保存 ROI 与结果" };
    private readonly Button _sceneRoiOpen = new() { Name = "camera-roi-open", Content = "打开并验证 ROI 记录" };
    private readonly ContentControl _sceneRoiGraph = new() { Name = "camera-roi-graph" };
    private CameraSceneRoiSfrReport? _sceneRoiReport;
    private CancellationTokenSource? _sceneRoiOperation;
    private bool _sceneRoiBusy, _sceneRoiApplying;
    private int _sceneRoiGeneration;
    internal CameraSceneRoiSfrReport? SceneRoiReport => _sceneRoiReport;
    internal IReadOnlyList<LinearSfrRoi> SceneRois => _sceneRois.AsReadOnly();

    private Control BuildSceneRoiPage()
    {
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        void Number(string key, string label, int value, int min, int max)
        {
            var input = new CameraNumberInput { Name = "camera-roi-" + key, Value = value, Minimum = min, Maximum = max,
                Increment = 1, IntegerOnly = true, FormatString = "0", Width = 90 };
            AutomationProperties.SetName(input, label); _sceneRoiNumbers.Add(key, input);
            fields.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 12, 6), Spacing = 3, Children = { new TextBlock { Text = label }, input } });
        }
        Number("x", "左列（从 0 起）", 0, 0, 1023); Number("y", "顶行（从 0 起）", 0, 0, 1023);
        Number("width", "宽 (px)", 96, 1, 1024); Number("height", "高 (px)", 96, 1, 1024);
        Number("window", "法线半窗口 (px)", 24, 8, 48);
        _sceneRoiImage.RegionDrawn += r =>
        {
            if (_sceneRoiBusy) return;
            foreach (var (key, value) in new[] { ("x", r.X), ("y", r.Y), ("width", r.Width), ("height", r.Height) }) _sceneRoiNumbers[key].Value = value;
            _sceneRoiInfo.Text = $"已框选 ({r.X}, {r.Y})，{r.Width} × {r.Height} px；选择边缘方向后添加或更新 ROI。";
        };
        _sceneRoiAdd.Click += (_, _) => EditSceneRoi(false); _sceneRoiUpdate.Click += (_, _) => EditSceneRoi(true);
        _sceneRoiDelete.Click += (_, _) =>
        {
            if (_sceneRoiBusy || _sceneRoiList.SelectedIndex < 0 || _sceneRoiList.SelectedIndex >= _sceneRois.Count) return;
            var index = _sceneRoiList.SelectedIndex; _sceneRois.RemoveAt(index); InvalidateSceneRoiMeasurement(); RefreshSceneRoiList(Math.Min(index, _sceneRois.Count - 1));
        };
        _sceneRoiList.SelectionChanged += (_, _) => { if (!_sceneRoiApplying) SelectSceneRoi(); };
        _sceneRoiTask.SelectionChanged += (_, _) =>
        {
            if (_sceneRoiApplying || _sceneRoiTask.SelectedIndex < 0) return;
            if (_sceneBatchTask.SelectedIndex != _sceneRoiTask.SelectedIndex) _sceneBatchTask.SelectedIndex = _sceneRoiTask.SelectedIndex;
            PresentSceneRoiResults();
        };
        _sceneRoiProfile.SelectionChanged += (_, _) => { if (!_sceneRoiApplying) PresentSceneRoiResults(); };
        _sceneRoiAllTasks.IsCheckedChanged += (_, _) => UpdateSceneRoiControls();
        _sceneRoiRun.Classes.Add("accent"); _sceneRoiRun.Click += async (_, _) => await RunSceneRoisAsync();
        _sceneRoiCancel.Click += (_, _) => InvalidateSceneRoiMeasurement("测量已取消，完整线性实验保持可用。");
        _sceneRoiSave.Click += async (_, _) =>
        {
            var report = _sceneRoiReport; if (report is null) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存多 ROI 测量记录", SuggestedFileName = "camera.scene-sfr-rois.json",
                FileTypeChoices = [new("ROI 测量记录") { Patterns = ["*.scene-sfr-rois.json"] }] });
            if (file is null || _closed) return;
            try { await _application.CameraImaging.SaveSceneLinearRoisAsync(report, file.Path.LocalPath); }
            catch (Exception ex) { if (!_closed) _sceneRoiInfo.Text = "保存失败：" + ex.Message; }
        };
        _sceneRoiOpen.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开对应完整线性图的 ROI 记录",
                FileTypeFilter = [new("ROI 测量记录") { Patterns = ["*.scene-sfr-rois.json"] }] });
            if (files.Count > 0) await OpenSceneRoisAsync(files[0].Path.LocalPath);
        };
        var png = new Button { Content = "PNG", Name = "camera-roi-png" };
        png.Click += async (_, _) =>
        {
            if (_sceneRoiGraph.Content is not ResponsiveAnalysisPlotHost) return;
            try
            {
                using var bitmap = CaptureSceneRoiImage();
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出 ROI 曲线", SuggestedFileName = "scene-rois.png",
                    DefaultExtension = "png", FileTypeChoices = [new("PNG") { Patterns = ["*.png"] }] });
                if (file is not null) AnalysisPanel.SavePlotImage(bitmap, file.Path.LocalPath);
            }
            catch (Exception ex) { if (!_closed) _sceneRoiInfo.Text = "导出失败：" + ex.Message; }
        };
        UpdateSceneRoiControls();
        return Scroll(new StackPanel { Spacing = 10, Children =
        {
            LaboratoryPresentation.Commands(_sceneRoiTask, _sceneRoiAllTasks, _sceneRoiRun, _sceneRoiCancel, _sceneRoiSave, _sceneRoiOpen),
            _sceneRoiInfo,
            new TextBlock { Text = "在完整图上拖框，或填写原始像素坐标。每个 ROI 只放一条斜边，避开棋盘交点、邻边和遮挡；近竖直或近水平偏角须满足原估计器条件。选框不会缩放测量数据。", TextWrapping = TextWrapping.Wrap },
            _sceneRoiImage, _sceneRoiList,
            LaboratoryPresentation.Commands(_sceneRoiName, _sceneRoiDirection, _sceneRoiAdd, _sceneRoiUpdate, _sceneRoiDelete), fields, _sceneRoiDetail,
            LaboratoryPresentation.Commands(_sceneRoiProfile, png, AnalysisPanel.CreateCurveWidthButton(_sceneRoiGraph)), _sceneRoiGraph
        } });
    }

    private void EditSceneRoi(bool update)
    {
        if (_sceneRoiBusy || _sceneBatchImage is null) return;
        try
        {
            int N(string key) => ValidNumber(_sceneRoiNumbers[key], true) ? (int)_sceneRoiNumbers[key].Value!.Value : throw new ArgumentException("请填写整数像素坐标。");
            var r = new LinearSfrRoi(_sceneRoiName.Text?.Trim() ?? "", new(N("x"), N("y"), N("width"), N("height")),
                (LinearEdgeOrientation)_sceneRoiDirection.SelectedIndex, N("window"));
            var index = update ? _sceneRoiList.SelectedIndex : _sceneRois.Count;
            if (index < 0 || index > _sceneRois.Count || update && index == _sceneRois.Count) return;
            var next = _sceneRois.ToList(); if (update) next[index] = r; else next.Add(r);
            LinearImageRoiSfr.ValidateRegions(next); _sceneRois.Clear(); _sceneRois.AddRange(next);
            InvalidateSceneRoiMeasurement("ROI 已保存，可计算；无效位置或边缘会逐项显示原因。"); RefreshSceneRoiList(index);
            if (!update)
            {
                var number = 1; while (_sceneRois.Any(v => v.Id == $"ROI {number}")) number++;
                _sceneRoiName.Text = $"ROI {number}";
            }
        }
        catch (Exception ex) { _sceneRoiInfo.Text = "不能保存 ROI：" + ex.Message; }
    }

    private void InvalidateSceneRoiMeasurement(string? message = null)
    {
        _sceneRoiGeneration++; _sceneRoiOperation?.Cancel(); _sceneRoiOperation = null; _sceneRoiBusy = false;
        _sceneRoiReport = null; _sceneRoiGraph.Content = null; _sceneRoiDetail.Text = "";
        if (message is not null) _sceneRoiInfo.Text = message;
        _sceneRoiImage.SetRegions(_sceneRois, SelectedSceneRoi()?.Id);
        if (_sceneRoiNumbers.Count > 0) RefreshSceneRoiList(_sceneRoiList.SelectedIndex);
        UpdateSceneRoiControls();
    }
    private void ResetSceneRois()
    {
        InvalidateSceneRoiMeasurement(); _sceneRois.Clear(); _sceneRoiImage.SetImage(null);
        _sceneRoiApplying = true; try { _sceneRoiTask.ItemsSource = null; } finally { _sceneRoiApplying = false; }
        RefreshSceneRoiList(-1); _sceneRoiInfo.Text = "先在“全幅逐波段”生成或打开完整线性实验。";
    }
    private LinearSfrRoi? SelectedSceneRoi() => (uint)_sceneRoiList.SelectedIndex < _sceneRois.Count ? _sceneRois[_sceneRoiList.SelectedIndex] : null;
    private IReadOnlyList<LinearSfrRoiResult> CurrentSceneRoiResults() => _sceneRoiReport?.Tasks.FirstOrDefault(t => t.TaskIndex == _sceneBatchTask.SelectedIndex)?.Regions ?? [];

    private void RefreshSceneRoiList(int index)
    {
        var results = CurrentSceneRoiResults(); _sceneRoiApplying = true;
        try
        {
            _sceneRoiList.ItemsSource = _sceneRois.Select(r =>
            {
                var result = results.FirstOrDefault(v => v.Roi.Id == r.Id);
                var status = result is null ? "未测量" : result.Status == LinearSfrRoiStatus.Measured ? "已测量" : "失败";
                return $"{r.Id} · {(r.Orientation == LinearEdgeOrientation.NearVertical ? "近竖直" : "近水平")} · ({r.Region.X}, {r.Region.Y}) {r.Region.Width}×{r.Region.Height} · {status}";
            }).ToArray();
            _sceneRoiList.SelectedIndex = index;
        }
        finally { _sceneRoiApplying = false; }
        SelectSceneRoi(); UpdateSceneRoiControls();
    }
    private void SelectSceneRoi()
    {
        if (SelectedSceneRoi() is { } roi)
        {
            _sceneRoiName.Text = roi.Id; _sceneRoiDirection.SelectedIndex = (int)roi.Orientation;
            foreach (var (key, value) in new[] { ("x", roi.Region.X), ("y", roi.Region.Y), ("width", roi.Region.Width), ("height", roi.Region.Height), ("window", roi.HalfWindowPixels) })
                _sceneRoiNumbers[key].Value = value;
        }
        PresentSceneRoiResults(); UpdateSceneRoiControls();
    }
    private void UpdateSceneRoiSource()
    {
        _sceneRoiApplying = true;
        try
        {
            _sceneRoiTask.ItemsSource = _sceneBatchArtifact?.Manifest.Plan.Tasks.Select(SceneBatchTaskTitle).ToArray();
            _sceneRoiTask.SelectedIndex = _sceneBatchTask.SelectedIndex;
        }
        finally { _sceneRoiApplying = false; }
        _sceneRoiImage.SetImage(_sceneBatchImage?.Preview);
        if (_sceneRoiReport is null && _sceneBatchImage is { } image)
            _sceneRoiInfo.Text = $"{image.Width} × {image.Height} 原始像素 · {image.PixelPitchMicrometers:G6} µm · {image.Name}；测量输入为 float64 辐照度，预览为共用标尺。";
        RefreshSceneRoiList(_sceneRoiList.SelectedIndex); UpdateSceneRoiControls();
    }
    private void UpdateSceneRoiControls()
    {
        var ready = !_closed && !_busy && !_sfrBusy && !SceneImagingBusy;
        var image = _sceneBatchImage is not null; var selected = SelectedSceneRoi() is not null;
        ControlAvailability.Set(_sceneRoiAdd, ready && image && _sceneRois.Count < LinearImageRoiSfr.MaximumRegions, "先打开完整线性图；最多 32 个 ROI。");
        ControlAvailability.Set(_sceneRoiUpdate, ready && image && selected, "先选择一个 ROI。");
        ControlAvailability.Set(_sceneRoiDelete, ready && selected, "先选择一个 ROI。");
        var tasks = _sceneRoiAllTasks.IsChecked == true ? _sceneBatchArtifact?.Manifest.Plan.Tasks.Count ?? 0 : 1;
        ControlAvailability.Set(_sceneRoiRun, ready && image && _sceneRois.Count > 0 && tasks * _sceneRois.Count <= CameraSceneRoiSfrReport.MaximumMeasurements,
            "需要完整线性图和 ROI；合计最多 128 项测量。");
        ControlAvailability.Set(_sceneRoiSave, ready && _sceneRoiReport is not null, "先完成 ROI 测量。");
        ControlAvailability.Set(_sceneRoiOpen, ready && _sceneBatchArtifact is not null, "先打开记录对应的完整线性实验。");
        ControlAvailability.Set(_sceneRoiCancel, _sceneRoiBusy, "当前没有 ROI 测量。");
        ControlAvailability.Set(_sceneRoiTask, ready && _sceneBatchArtifact is not null, "先完成或打开全幅线性实验。");
        _sceneRoiImage.IsEnabled = !_sceneRoiBusy;
    }

    internal async Task RunSceneRoisAsync()
    {
        var artifact = _sceneBatchArtifact;
        if (_closed || _busy || _sfrBusy || SceneImagingBusy || artifact is null || _sceneRois.Count == 0) return;
        var tasks = _sceneRoiAllTasks.IsChecked == true ? Enumerable.Range(0, artifact.Manifest.Plan.Tasks.Count).ToArray() : [_sceneBatchTask.SelectedIndex];
        var request = new CameraSceneRoiSfrRequest(tasks, _sceneRois.ToArray());
        InvalidateSceneRoiMeasurement(); var generation = _sceneRoiGeneration;
        using var operation = new CancellationTokenSource(); _sceneRoiOperation = operation; _sceneRoiBusy = true; UpdateAvailability(); UpdateSceneRoiControls();
        try
        {
            var progress = new Progress<CameraSceneRoiSfrProgress>(p =>
            { if (!_closed && generation == _sceneRoiGeneration && _sceneRoiBusy) _sceneRoiInfo.Text = $"任务 {p.TaskIndex + 1} · 已测量 {p.CompletedRegions}/{p.TotalMeasurements} 个 ROI"; });
            var report = await _application.CameraImaging.MeasureSceneLinearRoisAsync(artifact, request, progress, operation.Token);
            if (!_closed && generation == _sceneRoiGeneration) { _sceneRoiReport = report; PresentSceneRoiReport(); }
        }
        catch (OperationCanceledException) { if (!_closed && generation == _sceneRoiGeneration) _sceneRoiInfo.Text = "测量已取消。"; }
        catch (Exception ex) { if (!_closed && generation == _sceneRoiGeneration) _sceneRoiInfo.Text = "测量失败：" + ex.Message; }
        finally
        {
            if (generation == _sceneRoiGeneration) { _sceneRoiOperation = null; _sceneRoiBusy = false; }
            UpdateAvailability(); UpdateSceneRoiControls();
        }
    }
    internal async Task<bool> OpenSceneRoisAsync(string path)
    {
        var artifact = _sceneBatchArtifact; if (artifact is null || _closed || _busy || _sfrBusy || SceneImagingBusy) return false;
        InvalidateSceneRoiMeasurement(); var generation = _sceneRoiGeneration;
        using var operation = new CancellationTokenSource(); _sceneRoiOperation = operation; _sceneRoiBusy = true; UpdateAvailability(); UpdateSceneRoiControls();
        try
        {
            var report = await _application.CameraImaging.OpenSceneLinearRoisAsync(artifact, path, operation.Token);
            if (_closed || generation != _sceneRoiGeneration) return false;
            _sceneRois.Clear(); _sceneRois.AddRange(report.Request.Regions); _sceneRoiReport = report; PresentSceneRoiReport(); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { if (!_closed && generation == _sceneRoiGeneration) _sceneRoiInfo.Text = "打开失败：" + ex.Message; return false; }
        finally
        {
            if (generation == _sceneRoiGeneration) { _sceneRoiOperation = null; _sceneRoiBusy = false; }
            UpdateAvailability(); UpdateSceneRoiControls();
        }
    }
    private void PresentSceneRoiReport()
    {
        if (_sceneRoiReport is not { } report) return;
        var all = report.Tasks.SelectMany(t => t.Regions).ToArray(); var good = all.Count(r => r.Status == LinearSfrRoiStatus.Measured);
        _sceneRoiInfo.Text = $"{report.Tasks.Count} 个波段任务 · {all.Length} 项测量 · {good} 项取得曲线，{all.Length - good} 项失败。失败项保留原因。\n测量通过表示 ROI 质量合格；独立工具对照、正式 MTF 与采样收敛尚待验证。";
        RefreshSceneRoiList(_sceneRois.Count > 0 ? Math.Clamp(_sceneRoiList.SelectedIndex, 0, _sceneRois.Count - 1) : -1);
    }
    private void PresentSceneRoiResults()
    {
        if (_sceneRoiApplying) return;
        var results = CurrentSceneRoiResults(); var selected = SelectedSceneRoi();
        _sceneRoiImage.SetRegions(_sceneRois, selected?.Id, results);
        var result = results.FirstOrDefault(r => r.Roi.Id == selected?.Id);
        static string F(double? v) => v?.ToString("G7", CultureInfo.InvariantCulture) ?? "Nyquist 内未交叉";
        _sceneRoiDetail.Text = result is { Edge: { } edge, Geometry: { } g }
            ? $"{result.Roi.Id} · 实测边缘像元 ({g.Column:G7}, {g.Row:G7})，像面 ({g.XMillimeters:G7}, {g.YMillimeters:G7}) mm；法线 ({g.NormalX:G6}, {g.NormalY:G6})，{g.NormalAngleDegrees:G6}°\n"
                + $"拟合残差 {edge.Measurement.EdgeFitRmsPixels:G6} px；线性对比差 {edge.Measurement.Contrast:G7} W/m²；投影样本 {g.ProjectedSamples}，最少每箱 {g.MinimumBinSamples}；ESF/LSF 各 {edge.Measurement.Esf.Count} 点\n"
                + $"MTF50 {F(edge.Measurement.Mtf50CyclesPerPixel)} cycles/pixel，{F(edge.Measurement.Mtf50CyclesPerPixel * 1000 / _sceneRoiReport!.PixelPitchMicrometers)} cycles/mm；MTF10 {F(edge.Measurement.Mtf10CyclesPerPixel)} cycles/pixel"
            : (result?.Failure ?? (selected is null ? "选择 ROI 查看质量、位置、方向与数值。" : "尚未测量此任务的 ROI。"))
                + (result is { Status: not LinearSfrRoiStatus.Measured, Fit: { } fit }
                    ? $"\n失败前拟合：斜率 {fit.EdgeSlope:G6}，残差 {fit.EdgeFitRmsPixels:G6} px，{fit.Scanlines} 条扫描线。" : "");
        _sceneRoiGraph.Content = null;
        if (_sceneRoiReport is not { } report || results.All(r => r.Edge is null)) return;
        var profile = _sceneRoiProfile.SelectedIndex; var series = new List<AnalysisSeriesDto>();
        foreach (var (item, i) in results.Select((r, i) => (r, i)).Where(p => p.r.Edge is not null))
        {
            var m = item.Edge!.Measurement;
            var points = profile == 0 ? m.Curve.Select(p => new AnalysisPointDto(p.CyclesPerPixel, p.Response))
                : (profile == 1 ? m.Esf : m.Lsf).Select(p => new AnalysisPointDto(p.DistancePixels, p.Value));
            var label = item.Roi.Id + (item.Roi.Orientation == LinearEdgeOrientation.NearVertical ? " · 近竖直" : " · 近水平");
            series.Add(new(profile == 0 ? "Spatial frequency" : "Edge-normal distance", profile == 0 ? "SFR" : profile == 1 ? "ESF" : "LSF",
                points.ToArray(), Name: label, ColorIndex: i, LegendKey: item.Roi.Id, LegendLabel: label,
                XQuantity: profile == 0 ? AnalysisAxisQuantity.SpatialFrequency : AnalysisAxisQuantity.Pixel,
                XUnit: profile == 0 ? AnalysisAxisUnit.CyclesPerPixel : AnalysisAxisUnit.Pixel,
                YQuantity: profile == 0 ? AnalysisAxisQuantity.Modulation : profile == 1 ? AnalysisAxisQuantity.Irradiance : AnalysisAxisQuantity.IrradianceGradient,
                YUnit: profile == 0 ? AnalysisAxisUnit.Dimensionless : profile == 1 ? AnalysisAxisUnit.WattsPerSquareMeter : AnalysisAxisUnit.WattsPerSquareMeterPerPixel));
        }
        var title = profile == 0 ? "多 ROI 斜边 SFR" : profile == 1 ? "多 ROI ESF" : "多 ROI LSF";
        var view = new AnalysisViewDto(title, [new("源任务", _sceneBatchImage?.Name ?? ""), new("输入", "原始 float64 辐照度"), new("正式 MTF / 收敛", "待验证")], "", series,
            new(XMinimum: profile == 0 ? 0 : null, XMaximum: profile == 0 ? .5 : null, YMinimum: profile == 0 ? 0 : null,
                YMaximum: profile == 0 ? Math.Max(1.05, series.SelectMany(s => s.Points).Max(p => p.Y) * 1.05) : null, ShowLegend: true, DottedGrid: true), [], 1);
        _sceneRoiGraph.Content = AnalysisPanel.CreateResultPlot(view, report.SourceDocument, DateTimeOffset.Now);
    }
    internal RenderTargetBitmap CaptureSceneRoiImage()
    {
        var page = _sceneRoiGraph.Content as ResponsiveAnalysisPlotHost ?? throw new InvalidOperationException("先显示 ROI 曲线。");
        page.UpdateLayout(); var content = page.Child ?? throw new InvalidOperationException("曲线尚未显示。");
        if (content is UniformDiagramPage source) source.ValidateForExport();
        return ExportImageFrame.CaptureConfigured(content, this);
    }
}
