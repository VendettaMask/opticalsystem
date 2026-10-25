using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories;

/// <summary>Explicit, owned laboratory. Never instantiated by the startup document layout.</summary>
public sealed partial class CameraImagingWindow : Window
{
    private readonly IWorkbenchApplication _application;
    private readonly Dictionary<string, NumericUpDown> _numbers = new();
    private readonly ComboBox _source = Choice("camera-source", "彩色色卡", "分辨率测试图", "畸变网格", "西门子星", "外部图片");
    private readonly ComboBox _optics = Choice("camera-optics", "当前镜头", "理想光学对照");
    private readonly ComboBox _aberration = Choice("camera-aberration", "几何像差", "衍射像差");
    private readonly ComboBox _size = Choice("camera-size", "128 × 96", "256 × 192", "512 × 384");
    private readonly ComboBox _bayer = Choice("camera-bayer", "RGGB", "BGGR", "GRBG", "GBRG");
    private readonly CheckBox _noise = new() { Name = "camera-noise", Content = "启用噪声与非均匀性", IsChecked = true };
    private readonly CheckBox _isp = new() { Name = "camera-isp", Content = "启用基础 ISP", IsChecked = true };
    private readonly Button _run = new() { Name = "camera-run", Content = "运行仿真", Classes = { "accent" } };
    private readonly Button _cancel = new() { Name = "camera-cancel", Content = "取消" };
    private readonly Button _pin = new() { Name = "camera-pin", Content = "固定为 A 对照" };
    private readonly Button _export = new() { Name = "camera-export", Content = "导出对照包" };
    private readonly TextBlock _document = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _status = new() { Name = "camera-status", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _metrics = new() { Name = "camera-metrics", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _comparisonInfo = new() { Text = "运行后固定 A，再修改参数运行 B；同尺寸图像可查看差异。", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _fileInfo = new() { Text = "内置测试图", TextWrapping = TextWrapping.Wrap };
    private readonly UniformGrid _stages = new() { Columns = 2 };
    private readonly UniformGrid _comparisons = new() { Columns = 2 };
    private readonly CameraFrameView[] _stageViews = Enumerable.Range(0, 6).Select(_ => new CameraFrameView()).ToArray();
    private readonly CameraFrameView[] _comparisonViews = Enumerable.Range(0, 3).Select(_ => new CameraFrameView()).ToArray();
    private string _sourceFile = "";
    private CameraImagingResult? _result;
    private CameraImagingResult? _comparison;
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _closed;
    private bool _busy;
    internal CameraImagingResult? Result => _result;
    internal CameraImagingResult? Comparison => _comparison;

    public CameraImagingWindow(IWorkbenchApplication application)
    {
        _application = application;
        Title = "相机成像仿真 · 实验室";
        Width = 1280; Height = 900; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _aberration.SelectedIndex = 1;
        if (application.Modes.CurrentMode != OpticalWorkbenchMode.Sequential || application.Documents.GetSnapshot().SurfaceCount < 4)
            _optics.SelectedIndex = 1;
        var import = new Button { Content = "导入图片", Name = "camera-import" };
        _document.FontSize = DisplayTypography.BodySmall;
        _document.FontWeight = FontWeight.Normal;
        var header = LaboratoryPresentation.Header("相机成像仿真", "camera",
            "设置输入与曝光 → 检查成像阶段 → 比较 A/B 结果", LaboratoryPresentation.Muted(_document),
            import, _run, _cancel, _pin, _export);

        var settings = new StackPanel { Spacing = 12 };
        settings.Children.Add(Group("输入与光学", new Control[]
        {
            Field("源图像", _source), _fileInfo, Field("光学模型", _optics), Field("像差模型", _aberration),
            Field("输出像素", _size), Field("光谱模型", _spectralModel),
            new TextBlock { Text = "当前镜头沿用未保存处方；像面视场按像元尺寸与分辨率采样。", TextWrapping = TextWrapping.Wrap }
        }));
        settings.Children.Add(BuildSensorFilesCard());
        settings.Children.Add(BuildSceneSpectraCard());
        settings.Children.Add(Group("曝光与传感器", new Control[]
        {
            Number("exposure", "曝光 (ms)", 10, 0.001m, 10000, 1),
            Number("pitch", "像元边长 (µm)", 3, 0.1m, 100, 0.1m),
            Number("irradiance", "满白每波段辐照度 (W/m²)", 0.02m, 0, 1000, 0.005m),
            new TextBlock { Text = "这是像面线性值 1 对应的每波段标定量，非场景 lux 或实测光谱。", TextWrapping = TextWrapping.Wrap },
            Field("Bayer 排列", _bayer),
            Number("qe-r", "红 QE (0–1)", 0.5m, 0, 1, 0.05m),
            Number("qe-g", "绿 QE (0–1)", 0.6m, 0, 1, 0.05m),
            Number("qe-b", "蓝 QE (0–1)", 0.45m, 0, 1, 0.05m),
            Number("full-well", "满阱 (e⁻)", 10000, 1, 10000000, 1000),
            Number("gain", "系统增益 (DN/e⁻)", 0.35m, 0.0001m, 1000, 0.05m),
            Number("black", "黑电平 (DN)", 64, 0, 65534, 1),
            Number("bits", "ADC 位深", 12, 8, 16, 1),
            _noise,
            Number("read", "读出噪声 RMS (e⁻)", 2, 0, 10000, 1),
            Number("dark", "暗电流 (e⁻/s)", 0.1m, 0, 1000000, 0.1m),
            Number("prnu", "响应非均匀性 PRNU (%)", 1, 0, 30, 0.1m),
            Number("dsnu", "暗信号非均匀性 RMS (e⁻)", 0.5m, 0, 10000, 0.1m),
            Number("seed", "随机种子", 42, int.MinValue, int.MaxValue, 1)
        }));
        settings.Children.Add(Group("基础 ISP", new Control[]
        {
            _isp,
            new TextBlock { Text = "黑电平扣除与双线性去马赛克始终执行；关闭 ISP 后显示线性 RGB。", TextWrapping = TextWrapping.Wrap },
            Number("wb-r", "红增益", 1, 0.01m, 16, 0.1m),
            Number("wb-g", "绿增益", 1, 0.01m, 16, 0.1m),
            Number("wb-b", "蓝增益", 1, 0.01m, 16, 0.1m),
            Number("gamma", "输出 Gamma", 2.2m, 0.1m, 5, 0.1m),
            Number("denoise", "方框降噪 (0–1)", 0, 0, 1, 0.1m),
            Number("sharpen", "非锐化掩模 (0–2)", 0, 0, 2, 0.1m)
        }));
        var left = LaboratoryPresentation.Scroll(settings);
        left.Name = "CameraSettings";
        left.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        foreach (var (view, title) in _stageViews.Zip(new[]
        { "输入图 · sRGB 解码后预览", "光学像 · 固定 Gamma 2.2", "期望光电子 · 满阱固定标尺", "Bayer RAW · ADC 固定标尺", "无噪声对照 · 同曝光/ADC/ISP", "当前结果 B · ISP 输出" }))
        { view.Title = title; _stages.Children.Add(view); }
        foreach (var (view, title) in _comparisonViews.Zip(new[] { "固定结果 A", "当前结果 B", "显示 RGB 绝对差 ×4" }))
        { view.Title = title; _comparisons.Children.Add(view); }
        _metrics.FontSize = DisplayTypography.BodySmall;
        var metrics = LaboratoryPresentation.Card(new Border { Padding = new Thickness(12), Child = _metrics }, "CameraMetricsCard");
        metrics.IsVisible = !string.IsNullOrWhiteSpace(_metrics.Text);
        _metrics.PropertyChanged += (_, args) =>
        { if (args.Property == TextBlock.TextProperty) metrics.IsVisible = !string.IsNullOrWhiteSpace(_metrics.Text); };
        var tabs = new TabControl
        {
            Name = "CameraResults",
            ItemsSource = new[]
            {
                new TabItem { Header = "成像阶段", Content = Scroll(new StackPanel { Spacing = 12, Children = { metrics, _stages } }) },
                new TabItem { Header = "A/B 对照", Content = Scroll(new StackPanel { Spacing = 12, Children =
                { LaboratoryPresentation.Card(new Border { Padding = new Thickness(12), Child = _comparisonInfo }), _comparisons } }) },
                new TabItem { Header = "光谱响应", Name = "camera-response-tab", Content = BuildResponsePage() },
                new TabItem { Header = "光源与场景光谱", Name = "camera-scene-spectra-tab", Content = BuildSceneSpectraPage() },
                new TabItem { Header = "参考与实现范围", Content = Scroll(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "已实现：sRGB 输入解码、650/550/450 nm 三窄带、当前镜头共享 Core 成像或理想对照、光电子转换、Poisson 散粒噪声、读出噪声、暗电流、PRNU/DSNU、满阱、ADC、Bayer、双线性去马赛克、RGB 增益、方框降噪、非锐化掩模和 Gamma。\n\n"
                        + "支持 JSON 传感器参数和 CSV 绝对 QE 曲线。导入响应后，每类 CFA 像元对 650/550/450 nm 三输入波段分别响应并累加光电子；曲线替代固定 QE，不重复相乘。无曲线时沿用原三波段独立响应。\n\n"
                        + "连续光谱：导入光源 W/m²/nm、单一材质反射率和 QE；合并全部折点作分段三点 Gauss-Legendre 积分，每个积分波长调用共享光学。图片转线性灰度空间图样；不从 RGB 反演光谱。\n\n"
                        + "像面强度采用像素中心样本；尚无逐像素多材质光谱、像元面积积分、角度 QE、像素串扰、自动实测标定、3D 光源/BRDF、动态快门、杂散光耦合或厂商 ISP。导入文件保留来源与哈希，不代表已核实测量。\n\n"
                        + "对照：无噪声结果使用相同光学像、曝光、ADC 与 ISP，仅关闭随机噪声及固定非均匀性；A/B 保存各自参数与镜头修订。同种子可重复。电子图/RAW 不做逐图峰值归一化。\n\n"
                        + "公开功能参考（非数值认证）：\nDeepO Sensor & ISP — https://www.ai4optics.com/deepo/manual/image-simulation/sensor/\n"
                        + "Keysight ImSym — https://www.keysight.com/us/en/products/software/optical-solutions-software/optical-design-solutions/imsym-imaging-system-simulator.html\n"
                        + "Speos sensor examples — https://optics.ansys.com/hc/en-us/articles/20419350770067\n"
                        + "EMVA 1288 — https://www.emva.org/standards-technology/emva-1288/\n\n"
                        + "验收依据：独立光子/电子数公式、曝光与增益控制、暗场/平场统计、Bayer 已知色块及无噪声/A-B 对照。未宣称 EMVA 合规或与商业软件结果一致。"
                }) }
            }
        };
        var body = new Grid { Name = "CameraWorkspace", ColumnDefinitions = new ColumnDefinitions("310,*"), ColumnSpacing = 14 };
        foreach (var tab in ((TabItem[])tabs.ItemsSource!))
        { tab.FontSize = DisplayTypography.BodySmall; tab.Classes.Add("lab-tab"); tab.MinHeight = 40; }
        body.Children.Add(left); Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(16) };
        root.Children.Add(header); Grid.SetRow(body, 1); root.Children.Add(body);
        var status = LaboratoryPresentation.Status(_status); Grid.SetRow(status, 2); root.Children.Add(status);
        Content = MainWindowTitleBar.Wrap(this, root);
        ThemeChrome.ApplyDialogDecoration(this);
        tabs.SizeChanged += (_, _) =>
        {
            _stages.Columns = _comparisons.Columns = tabs.Bounds.Width >= 880 ? 3 : tabs.Bounds.Width >= 550 ? 2 : 1;
        };
        _run.Click += async (_, _) => await RunAsync(); _cancel.Click += (_, _) => Invalidate("计算已取消");
        _pin.Click += (_, _) => PinComparison(); _export.Click += async (_, _) => await ExportAsync();
        import.Click += async (_, _) => await ImportAsync();
        foreach (var (key, number) in _numbers) number.ValueChanged += (_, _) =>
        {
            if (_applyingSensor) return;
            if (!key.StartsWith("wb-") && !key.StartsWith("spectrum-") && key is not ("gamma" or "denoise" or "sharpen")) MarkSensorModified();
            Invalidate("参数已改变，请运行 B；A 对照保留。");
        };
        foreach (var choice in new[] { _source, _optics, _aberration, _size, _bayer, _spectralModel })
            choice.SelectionChanged += (_, _) =>
            {
                if (_applyingSensor) return; if (choice == _bayer) MarkSensorModified();
                Invalidate("设置已改变，请重新运行。");
            };
        _noise.IsCheckedChanged += (_, _) =>
        { if (!_applyingSensor) { MarkSensorModified(); Invalidate("噪声设置已改变，请重新运行。"); } };
        _isp.IsCheckedChanged += (_, _) => Invalidate("ISP 设置已改变，请重新运行。");
        application.Events.Changed += OnWorkspaceChanged; application.Modes.ModeChanged += OnModeChanged;
        Closed += (_, _) =>
        {
            _closed = true; Cancel(); application.Events.Changed -= OnWorkspaceChanged; application.Modes.ModeChanged -= OnModeChanged;
            foreach (var view in _stageViews.Concat(_comparisonViews)) view.Dispose();
        };
        LaboratoryPresentation.Apply(this); UpdateSource(); UpdateAvailability(); UpdateSensorFiles();
        _status.Text = "选择输入、曝光与传感器参数，点击运行仿真。";
    }

    private static ComboBox Choice(string name, params string[] labels) => new()
    { Name = name, ItemsSource = labels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private Control Number(string name, string label, decimal value, decimal min, decimal max, decimal increment)
    {
        var number = new NumericUpDown { Name = "camera-" + name, Value = value, Minimum = min, Maximum = max,
            Increment = increment, FormatString = name is "black" or "bits" or "seed" ? "0" : "0.###############", Width = 130 };
        AutomationProperties.SetName(number, label); _numbers[name] = number; return Field(label, number);
    }
    private static Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,130"), ColumnSpacing = 8, Margin = new Thickness(0, 3) };
        var caption = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        caption.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);
        grid.Children.Add(caption);
        Grid.SetColumn(input, 1); grid.Children.Add(input); return grid;
    }
    private static Control Group(string title, Control[] controls)
    {
        return LaboratoryPresentation.Section(title, title == "输入与光学" ? "camera" : "sliders-horizontal", controls);
    }
    private static ScrollViewer Scroll(Control content) => new()
    { Content = content, Margin = new Thickness(0, 10, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private sealed class CameraFrameView : Border, IDisposable
    {
        private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold,
            FontSize = DisplayTypography.BodySmall };
        private readonly Image _image = new() { Height = 180, Stretch = Stretch.Uniform };
        private readonly TextBlock _empty = LaboratoryPresentation.Hint("运行仿真后显示图像");
        private WriteableBitmap? _bitmap;
        public string Title { set => _title.Text = value; }
        public CameraFrameView()
        {
            Padding = new Thickness(12); Margin = new Thickness(0, 0, 10, 10);
            Classes.Add("lab-card");
            ThemeChrome.Apply(this, ThemeChromeRole.SurfaceCard, shadow: false);
            this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
            _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center;
            _empty.TextAlignment = TextAlignment.Center;
            var viewport = new Grid { Children = { _image, _empty } };
            var body = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 10 };
            body.Children.Add(_title); Grid.SetRow(viewport, 1); body.Children.Add(viewport); Child = body;
        }
        public void Set(CameraImage? frame)
        {
            _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
            _empty.IsVisible = frame is null;
            if (frame is null) return;
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = _bitmap.Lock())
            {
                var row = new byte[frame.Width * 4];
                for (var y = 0; y < frame.Height; y++)
                {
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var i = (y * frame.Width + x) * 3;
                        row[x * 4] = frame.Rgb24[i + 2]; row[x * 4 + 1] = frame.Rgb24[i + 1];
                        row[x * 4 + 2] = frame.Rgb24[i]; row[x * 4 + 3] = 255;
                    }
                    Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
                }
            }
            _image.Source = _bitmap; AutomationProperties.SetName(_image, $"{_title.Text} · {frame.Width} × {frame.Height}");
        }
        public void Dispose() => Set(null);
    }
}
