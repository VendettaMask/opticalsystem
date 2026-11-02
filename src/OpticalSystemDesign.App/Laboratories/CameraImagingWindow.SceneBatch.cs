using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly Dictionary<string, CameraNumberInput> _sceneBatchNumbers = [];
    private readonly ComboBox _sceneBatchMode = new() { Name = "camera-scene-batch-mode", Width = 310,
        ItemsSource = new[] { "当前镜头各波长分别生成", "原文三种源谱形状 · 当前镜头", "文件中的自定义源" }, SelectedIndex = 0 };
    private readonly CheckBox _sceneBatchWeights = new() { Content = "将镜头权重作为物方源辐亮度倍率", IsChecked = true };
    private readonly CheckBox _sceneBatchFresnel = new() { Content = "裸界面 Fresnel 损失", IsChecked = true };
    private readonly Button _sceneBatchRun = new() { Name = "camera-scene-batch-run", Content = "生成全幅线性实验" };
    private readonly Button _sceneBatchReplay = new() { Content = "按冻结条件另存复算" };
    private readonly Button _sceneBatchOpen = new() { Content = "打开全幅线性实验" };
    private readonly Button _sceneBatchCancel = new() { Content = "取消" };
    private readonly TextBlock _sceneBatchBudget = new() { Name = "camera-scene-batch-budget", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sceneBatchInfo = new() { Name = "camera-scene-batch-info", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sceneBatchProgress = new() { Name = "camera-scene-batch-progress", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _sceneBatchTask = new() { Name = "camera-scene-batch-task", Width = 350 };
    private readonly CameraFrameView _sceneBatchPreview = new() { Title = "完整传感器 · 线性预览 · 全任务共用标尺" };
    private IReadOnlyList<SceneLinearSource>? _sceneBatchExplicitSources;
    private CameraSceneLinearBatchPlan? _sceneBatchPrepared;
    private CameraSceneLinearBatchArtifact? _sceneBatchArtifact;
    private CameraSceneLinearTaskImage? _sceneBatchImage;
    private CancellationTokenSource? _sceneBatchOperation;
    private bool _sceneBatchBusy, _sceneBatchApplying, _sceneBatchFrozen;
    private int _sceneBatchGeneration, _sceneBatchReadSerial;
    private double? _sceneBatchRequestedPngMaximum;
    private bool SceneImagingBusy => _sceneSfrBusy || _sceneBatchBusy;
    internal CameraSceneLinearBatchArtifact? SceneBatchArtifact => _sceneBatchArtifact;
    internal CameraSceneLinearTaskImage? SceneBatchImage => _sceneBatchImage;

    private Control BuildSceneBatchPage()
    {
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        void Number(string key, string label, decimal value, decimal min, decimal max, bool integer = false)
        {
            var input = new CameraNumberInput { Name = "camera-scene-batch-" + key, Value = value, Minimum = min, Maximum = max,
                Increment = integer ? 1 : .1m, IntegerOnly = integer, Width = 108, FormatString = "0.########" };
            AutomationProperties.SetName(input, label); _sceneBatchNumbers[key] = input;
            fields.Children.Add(new StackPanel { Spacing = 4, Margin = new(0, 0, 16, 10), Children = { new TextBlock { Text = label }, input } });
            input.ValueChanged += (_, _) => { if (!_sceneBatchApplying) ClearSceneBatch("条件已改变，请重新生成全幅实验。"); };
            input.PropertyChanged += (_, e) => { if (e.Property == NumericUpDown.TextProperty && !_sceneBatchApplying) ClearSceneBatch("请核对全幅实验参数。"); };
        }
        Number("distance", "靶标距离 (mm)", 100000, 1.001m, 1000000);
        Number("columns", "棋盘列数", 14, 1, 4096, true); Number("rows", "棋盘行数", 14, 1, 4096, true);
        Number("cell", "格边长 (mm)", 2000, .001m, 100000);
        Number("x", "靶标中心 X (mm)", (decimal)(-Math.Sin(Math.PI / 36) * 1000), -100000, 100000);
        Number("y", "靶标中心 Y (mm)", (decimal)(Math.Cos(Math.PI / 36) * 1000), -100000, 100000);
        Number("rx", "靶标 X 旋转 (°)", 0, -180, 180); Number("ry", "靶标 Y 旋转 (°)", 0, -180, 180); Number("rz", "靶标 Z 旋转 (°)", 5, -180, 180);
        Number("bright", "亮格带宽辐亮度 (W/m²/sr)", 1, 0, 1000000); Number("dark", "暗格带宽辐亮度 (W/m²/sr)", .1m, 0, 1000000);
        Number("width", "完整传感器宽 (px)", 128, 4, 16384, true); Number("height", "完整传感器高 (px)", 128, 4, 16384, true);
        Number("pitch", "像元边长 (µm)", 3, .1m, 100);
        Number("fill", "实际感光面积比例", 1, .0001m, 1);
        Number("pixels", "像元每边采样", 1, 1, 4, true); Number("rings", "瞳面径向环数", 2, 1, 8, true); Number("arms", "每环方位点数", 4, 4, 32, true);
        Number("order", "源光谱积分阶数", 2, 2, 3, true); Number("strips", "每块完整行数", 16, 1, 256, true);
        _sceneBatchRun.Classes.Add("accent");
        foreach (var check in new[] { _sceneBatchWeights, _sceneBatchFresnel })
            check.IsCheckedChanged += (_, _) => { if (!_sceneBatchApplying) ClearSceneBatch("光源或界面设置已改变。"); };
        _sceneBatchMode.SelectionChanged += (_, _) => { if (!_sceneBatchApplying) ClearSceneBatch("逐波段模式已改变。"); };
        _sceneBatchTask.SelectionChanged += async (_, _) => { if (!_sceneBatchApplying) await ShowSceneBatchTaskAsync(); };
        _sceneBatchCancel.Click += (_, _) => ClearSceneBatch("全幅任务已取消，目标文件保留。");
        _sceneBatchRun.Click += async (_, _) => await PickSceneBatchDestinationAsync(false);
        _sceneBatchReplay.Click += async (_, _) => await PickSceneBatchDestinationAsync(true);
        _sceneBatchOpen.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开全幅线性实验",
                FileTypeFilter = [new("全幅线性实验") { Patterns = ["*.scene-linear.zip"] }] });
            if (files.Count > 0) await OpenSceneBatchFromPathAsync(files[0].Path.LocalPath);
        };
        UpdateSceneBatchControls();
        return Scroll(new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "全幅线性成像与逐波段任务", FontWeight = FontWeight.Bold, FontSize = 18 },
            new TextBlock { Text = "同一冻结镜头、场景与完整传感器，分别生成各源谱的图像。浮点数据直接保存为功率和辐照度，附带同一标尺的 16 位线性 PNG。默认采样用于流程检查；正式 MTF 与收敛另行验证。", TextWrapping = TextWrapping.Wrap },
            _sceneBatchMode, _sceneBatchWeights, fields, _sceneBatchFresnel, _sceneBatchBudget,
            LaboratoryPresentation.Commands(_sceneBatchRun, _sceneBatchCancel, _sceneBatchOpen, _sceneBatchReplay),
            _sceneBatchProgress, _sceneBatchInfo, _sceneBatchTask, _sceneBatchPreview
        } });
    }

    private static bool SceneBatchInteger(string key) => key is "columns" or "rows" or "width" or "height" or "pixels" or "rings" or "arms" or "order" or "strips";
    private CameraSceneLinearBatchRequest ReadSceneBatchRequest()
    {
        if (_sceneBatchMode.SelectedIndex == 2 && _sceneBatchExplicitSources is null)
            throw new InvalidOperationException("请先打开包含自定义源谱的实验，或选择当前镜头/原文源谱形状。");
        double V(string key) => ValidNumber(_sceneBatchNumbers[key], SceneBatchInteger(key)) ? (double)_sceneBatchNumbers[key].Value!.Value
            : throw new InvalidOperationException("请输入有效的全幅实验参数。");
        return new(new((int)V("columns"), (int)V("rows"), V("cell"), V("distance"), V("x"), V("y"), V("rx"), V("ry"), V("rz")),
            (int)V("width"), (int)V("height"), V("pitch"), (int)V("pixels"), V("fill"), (int)V("rings"), (int)V("arms"), _sceneBatchFresnel.IsChecked == true,
            (SceneLinearBatchMode)_sceneBatchMode.SelectedIndex, _sceneBatchWeights.IsChecked == true, V("bright"), V("dark"), (int)V("order"),
            _sceneBatchMode.SelectedIndex == 2 ? _sceneBatchExplicitSources : null, (int)V("strips"), _sceneBatchRequestedPngMaximum);
    }

    private void UpdateSceneBatchControls()
    {
        if (_sceneBatchNumbers.Count == 0) return;
        if (!_sceneBatchBusy && !_sceneBatchApplying)
        {
            _sceneBatchPrepared = null;
            try
            {
                _sceneBatchPrepared = _application.CameraImaging.PrepareSceneLinearBatch(ReadSceneBatchRequest());
                var p = _sceneBatchPrepared;
                _sceneBatchBudget.Text = $"完整 {p.Request.Width} × {p.Request.Height}；{p.Tasks.Count} 个独立任务、{p.Work.SpectralNodes} 个光谱节点；合计 {p.Work.Rays:N0} 条光线、最多 {p.Work.MaximumInteractions:N0} 次表面传播，浮点数据 {p.Float64Values * 8 / 1048576d:F2} MiB。\n"
                    + (p.WithinBudget ? "计算量在现有限额内；分块不改变坐标或重置预算。" : "未运行：" + p.BudgetFailure)
                    + (_sceneBatchRequestedPngMaximum is { } scale ? $"\n沿用文件中固定的共用标尺 {scale:G8} W/m²。" : "\nPNG 标尺采用所有任务的共同峰值。");
            }
            catch (Exception ex) { _sceneBatchBudget.Text = "不能开始：" + ex.Message; }
        }
        var ready = !_closed && !_busy && !_sfrBusy && !SceneImagingBusy;
        ControlAvailability.Set(_sceneBatchRun, ready && _sceneBatchPrepared?.WithinBudget == true, _sceneBatchPrepared?.BudgetFailure ?? "需要有效顺序镜头、参数和统一预算。");
        ControlAvailability.Set(_sceneBatchCancel, _sceneBatchBusy, "当前没有全幅任务。");
        ControlAvailability.Set(_sceneBatchReplay, ready && _sceneBatchArtifact is not null, "先完成或打开全幅线性实验。");
        ControlAvailability.Set(_sceneBatchOpen, ready, "请先等待计算完成。");
        ControlAvailability.Set(_sceneBatchWeights, _sceneBatchMode.SelectedIndex == 0, "源谱形状任务使用声明的总带宽辐亮度，不套用镜头波长权重。");
        ControlAvailability.Set(_sceneBatchTask, !_sceneBatchBusy && _sceneBatchArtifact is not null, "先完成或打开全幅线性实验。");
    }

    private void ClearSceneBatch(string message, bool lensChange = false)
    {
        if (lensChange && _sceneBatchFrozen && !_sceneBatchBusy) { UpdateSceneBatchControls(); return; }
        _sceneBatchGeneration++; _sceneBatchReadSerial++; _sceneBatchOperation?.Cancel(); _sceneBatchOperation = null;
        _sceneBatchBusy = false; _sceneBatchFrozen = false; _sceneBatchArtifact = null; _sceneBatchImage = null; _sceneBatchPreview.Set(null);
        _sceneBatchInfo.Text = message; _sceneBatchProgress.Text = ""; _sceneBatchApplying = true;
        try { _sceneBatchTask.ItemsSource = null; } finally { _sceneBatchApplying = false; }
        UpdateSceneBatchControls();
    }

    private async Task PickSceneBatchDestinationAsync(bool replay)
    {
        if (_closed || _busy || _sfrBusy || SceneImagingBusy) return;
        var plan = replay ? _sceneBatchArtifact?.Manifest.Plan : _sceneBatchPrepared;
        if (plan is null || !plan.WithinBudget) return;
        var generation = _sceneBatchGeneration;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存全幅线性实验", SuggestedFileName = "camera.scene-linear.zip",
            FileTypeChoices = [new("全幅线性实验") { Patterns = ["*.scene-linear.zip"] }] });
        if (file is not null && !_closed && generation == _sceneBatchGeneration) await RunSceneBatchPlanAsync(plan, file.Path.LocalPath, replay);
    }

    internal Task RunSceneBatchToPathAsync(string path, bool replay = false)
    {
        var plan = replay ? _sceneBatchArtifact?.Manifest.Plan : _application.CameraImaging.PrepareSceneLinearBatch(ReadSceneBatchRequest());
        return plan is null ? Task.CompletedTask : RunSceneBatchPlanAsync(plan, path, replay);
    }

    private async Task RunSceneBatchPlanAsync(CameraSceneLinearBatchPlan plan, string path, bool replay)
    {
        if (_closed || _busy || _sfrBusy || SceneImagingBusy) return;
        ClearSceneBatch("正在生成各波段的完整线性图…"); var generation = _sceneBatchGeneration;
        var operation = new CancellationTokenSource(); _sceneBatchOperation = operation; _sceneBatchBusy = true; UpdateAvailability();
        try
        {
            var progress = new Progress<CameraSceneLinearBatchProgress>(p =>
            {
                if (_closed || generation != _sceneBatchGeneration) return;
                var phase = p.Phase switch { "Tracing" => "追迹完整像元", "ExchangeImages" => "生成线性交换图", "Packaging" => "保存完整实验", _ => "完成" };
                _sceneBatchProgress.Text = $"{phase} · 任务 {p.TaskIndex + 1}/{plan.Tasks.Count} · {p.CompletedRows}/{p.Height} 行 · 光线 {p.CompletedRays:N0}/{p.TotalRays:N0}";
            });
            var artifact = await _application.CameraImaging.RunSceneLinearBatchAsync(plan, path, progress, operation.Token);
            if (!_closed && generation == _sceneBatchGeneration) { _sceneBatchFrozen = replay; await PresentSceneBatchAsync(artifact); }
        }
        catch (OperationCanceledException) { if (generation == _sceneBatchGeneration) _sceneBatchInfo.Text = "全幅任务已取消，目标文件保留。"; }
        catch (Exception ex) { if (generation == _sceneBatchGeneration) _sceneBatchInfo.Text = "全幅任务失败：" + ex.Message; }
        finally { if (generation == _sceneBatchGeneration) { _sceneBatchOperation = null; _sceneBatchBusy = false; } operation.Dispose(); UpdateAvailability(); }
    }

    private async Task PresentSceneBatchAsync(CameraSceneLinearBatchArtifact artifact)
    {
        _sceneBatchArtifact = artifact; var m = artifact.Manifest; var p = m.Plan;
        _sceneBatchInfo.Text = (_sceneBatchFrozen ? "冻结实验 · 与当前工程独立\n" : "当前镜头生成 · 工程改变后清除\n")
            + $"{p.SourceDocument.Name} · 修订 {p.SourceDocument.Revision}；完整 {p.Request.Width} × {p.Request.Height}，{p.Request.PixelPitchMicrometers:G6} µm\n"
            + $"{p.Tasks.Count} 个源谱任务，{p.Work.SpectralNodes} 个节点；合计 {m.CompletedRays:N0} 条光线；各 PNG 共用 0–{m.Png.CommonMaximumWattsPerSquareMeter:G8} W/m² 标尺，16 位线性，无 Gamma/ICC。\n"
            + string.Join("\n", p.Tasks.Select((t, i) => $"{SceneBatchTaskTitle(t)} · 源倍率 {t.Source.SourceMultiplier:G8} · {t.Nodes.Count} 节点 · 功率 {m.Outputs[i].TotalPowerWatts:G8} W"))
            + "\n浮点功率、辐照度和完整分辨率交换图已保存。尚无正式 MTF 或收敛通过结论。\n" + artifact.ArchivePath;
        _sceneBatchApplying = true;
        try { _sceneBatchTask.ItemsSource = p.Tasks.Select(SceneBatchTaskTitle).ToArray(); _sceneBatchTask.SelectedIndex = 0; }
        finally { _sceneBatchApplying = false; }
        await ShowSceneBatchTaskAsync(); UpdateSceneBatchControls();
    }

    private static string SceneBatchTaskTitle(SceneLinearFrozenTask task) => task.Source.LensWavelengthNumber is { } number
        && task.Source.WavelengthNanometers is { } wave ? $"W{number} · {wave:G8} nm" : task.Source.Name;

    internal async Task ShowSceneBatchTaskAsync()
    {
        var artifact = _sceneBatchArtifact; var index = _sceneBatchTask.SelectedIndex; if (artifact is null || index < 0) return;
        var serial = ++_sceneBatchReadSerial; var generation = _sceneBatchGeneration; _sceneBatchImage = null; _sceneBatchPreview.Set(null);
        try
        {
            var image = await _application.CameraImaging.ReadSceneLinearTaskAsync(artifact, index);
            if (_closed || generation != _sceneBatchGeneration || serial != _sceneBatchReadSerial) return;
            _sceneBatchImage = image; _sceneBatchPreview.Set(image.Preview);
        }
        catch (Exception ex) { if (!_closed && serial == _sceneBatchReadSerial) _sceneBatchInfo.Text += "\n读取像元失败：" + ex.Message; }
    }

    internal async Task<bool> OpenSceneBatchFromPathAsync(string path)
    {
        if (_closed || _busy || _sfrBusy || SceneImagingBusy) return false;
        var generation = _sceneBatchGeneration;
        try
        {
            var artifact = await _application.CameraImaging.OpenSceneLinearBatchAsync(path);
            if (_closed || generation != _sceneBatchGeneration) return false;
            ClearSceneBatch(""); _sceneBatchApplying = true;
            try
            {
                var r = artifact.Manifest.Plan.Request; var c = r.Chart!; _sceneBatchExplicitSources = r.Sources; _sceneBatchRequestedPngMaximum = r.PngMaximumIrradiance;
                foreach (var (key, value) in new (string, double)[] { ("distance", c.DistanceMillimeters), ("columns", c.Columns), ("rows", c.Rows), ("cell", c.CellSizeMillimeters),
                    ("x", c.CenterXMillimeters), ("y", c.CenterYMillimeters), ("rx", c.RotationXDegrees), ("ry", c.RotationYDegrees), ("rz", c.RotationZDegrees),
                    ("bright", r.BrightBandRadiance), ("dark", r.DarkBandRadiance), ("width", r.Width), ("height", r.Height), ("pitch", r.PixelPitchMicrometers),
                    ("pixels", r.PixelSamplesPerAxis), ("fill", r.FillFactor), ("rings", r.PupilRings), ("arms", r.PupilArms), ("order", r.SpectralQuadratureOrder), ("strips", r.RowsPerStrip) }) _sceneBatchNumbers[key].Value = (decimal)value;
                _sceneBatchMode.SelectedIndex = (int)r.Mode; _sceneBatchWeights.IsChecked = r.UseLensWeightsAsSourceMultipliers; _sceneBatchFresnel.IsChecked = r.BareInterfaceFresnel;
            }
            finally { _sceneBatchApplying = false; }
            _sceneBatchFrozen = true; await PresentSceneBatchAsync(artifact); return true;
        }
        catch (Exception ex) { if (!_closed) _sceneBatchInfo.Text = "打开全幅线性实验失败：" + ex.Message; return false; }
    }
}
