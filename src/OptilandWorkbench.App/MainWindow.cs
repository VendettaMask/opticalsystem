using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType NativeOpticFileType = new("STAROPT 光学设计项目")
    {
        Patterns = new[] { "*.staropt" },
        AppleUniformTypeIdentifiers = new[] { "public.data" },
        MimeTypes = new[] { "application/vnd.starlabs.staropt" }
    };

    private static readonly FilePickerFileType LegacyOpticJsonFileType = new("旧版 Optiland JSON（兼容导入）")
    {
        Patterns = new[] { "*.optiland.json", "*.optic.json", "*.json", "*.optiland" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static readonly FilePickerFileType PythonOptilandJsonFileType = new("Python Optiland 0.5.8 JSON")
    {
        Patterns = new[] { "*.optiland-python.json", "*.python-optiland.json" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static readonly FilePickerFileType CommercialOpticFileType = new("序列光学格式")
    {
        Patterns = new[] { "*.zmx", "*.seq", "*.len" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType ZemaxOpticFileType = new("Zemax 光学系统")
    {
        Patterns = new[] { "*.zmx" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType PlainSequentialFileType = new("序列光学文本")
    {
        Patterns = new[] { "*.lens", "*.dat", "*.txt" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType StepCadFileType = new("STEP CAD 模型")
    {
        Patterns = new[] { "*.step", "*.stp" },
        AppleUniformTypeIdentifiers = new[] { "public.item" },
        MimeTypes = new[] { "model/step", "application/step" }
    };

    private static readonly IReadOnlyList<AnalysisRibbonCommand> AnalysisRibbonCommands = new AnalysisRibbonCommand[]
    {
        new("analysis-single-ray-trace", "单光线追迹", "单光线追迹", "scan-line", "光线迹点"),
        new("analysis-first-order", "一级像差/一阶量", "一阶量", "ruler", "系统报告"),
        new("analysis-prescription", "处方报告", "处方报告", "file-text", "系统报告"),
        new("analysis-ray-fan", "光线像差图", "光线像差图", "chart-spline", "像差分析"),
        new("analysis-spot", "标准点列图", "标准点列图", "chart-scatter", "光线迹点"),
        new("analysis-footprint", "光迹图", "光迹图", "scan-search", "光线迹点"),
        new("analysis-through-focus", "离焦点列图", "离焦点列图", "scan-line", "光线迹点"),
        new("analysis-full-field-spot", "全视场点列图", "全视场点列图", "chart-scatter", "光线迹点"),
        new("analysis-matrix-spot", "矩阵点列图", "矩阵点列图", "grid-3x3", "光线迹点"),
        new("analysis-configuration-matrix-spot", "结构矩阵点列图", "结构矩阵点列图", "panels-top-left", "光线迹点"),
        new("analysis-cardinal-points", "基面数据", "基面数据", "ruler", "光线迹点"),
        new("analysis-vignetting", "渐晕图", "渐晕图", "scan", "光线迹点"),
        new("analysis-distortion", "畸变", "畸变", "triangle", "像差分析"),
        new("analysis-grid-distortion", "网格畸变", "网格畸变", "grid-3x3", "像差分析"),
        new("analysis-field-curvature", "场曲/畸变", "场曲/畸变", "chart-line", "像差分析"),
        new("analysis-axial-aberration", "轴向像差", "轴向像差", "triangle", "像差分析"),
        new("analysis-lateral-color", "垂轴色差", "垂轴色差", "triangle", "像差分析"),
        new("analysis-color-focus", "色焦移", "色焦移", "triangle", "像差分析"),
        new("analysis-seidel", "赛德尔系数", "赛德尔系数", "sigma", "像差分析"),
        new("analysis-seidel-diagram", "赛德尔图", "赛德尔图", "chart-no-axes-column", "像差分析"),
        new("analysis-diffraction-encircled-energy", "\u884d\u5c04", "\u884d\u5c04", "circle-dot-dashed", "\u5708\u5165\u80fd\u91cf"),
        new("analysis-encircled-energy", "\u51e0\u4f55", "\u51e0\u4f55", "circle-dot", "\u5708\u5165\u80fd\u91cf"),
        new("analysis-geometric-line-edge-spread", "\u51e0\u4f55\u7ebf/\u8fb9\u7f18\u6269\u6563", "\u51e0\u4f55\u7ebf/\u8fb9\u7f18\u6269\u6563", "chart-spline", "\u5708\u5165\u80fd\u91cf"),
        new("analysis-extended-source-encircled-energy", "\u6269\u5c55\u5149\u6e90", "\u6269\u5c55\u5149\u6e90", "scan", "\u5708\u5165\u80fd\u91cf"),
        new("analysis-pupil-aberration", "光瞳像差", "光瞳像差", "scan", "像差分析"),
        new("analysis-full-field-aberration", "全视场像差", "全视场像差", "scan", "像差分析"),
        new("analysis-rms-field", "RMS vs. 视场", "RMS vs. 视场", "chart-line", "RMS"),
        new("analysis-rms-wavelength", "RMS vs. 波长", "RMS vs. 波长", "chart-line", "RMS"),
        new("analysis-rms-focus", "RMS vs. 离焦", "RMS vs. 离焦", "chart-line", "RMS"),
        new("analysis-rms-field-map", "二维视场RMS图", "二维视场RMS图", "map", "RMS"),
        new("analysis-angle-height", "入射角 vs. 像高", "入射角 vs. 像高", "scan-line", "光线迹点"),
        new("analysis-psf", "FFT PSF", "FFT PSF", "focus", "点扩散函数"),
        new("analysis-psf-cross-section", "FFT PSF Cross Section", "FFT PSF截面图", "chart-line", "点扩散函数"),
        new("analysis-line-edge-spread", "FFT Line Edge Spread", "FFT 线/边缘扩散", "chart-spline", "点扩散函数"),
        new("analysis-huygens-psf", "Huygens PSF", "惠更斯PSF", "circle-dot-dashed", "点扩散函数"),
        new("analysis-huygens-psf-cross-section", "Huygens PSF Cross Section", "惠更斯PSF截面图", "chart-line", "点扩散函数"),
        new("analysis-mtf", "MTF", "傅里叶 MTF", "chart-no-axes-combined", "MTF 曲线"),
        new("analysis-fourier-through-focus-mtf", "Fourier Through Focus MTF", "傅里叶离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-fourier-mtf-field", "Fourier MTF vs Field", "傅里叶 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-huygens-mtf", "惠更斯 MTF", "惠更斯 MTF", "waves-horizontal", "MTF 曲线"),
        new("analysis-huygens-through-focus-mtf", "Huygens Through Focus MTF", "惠更斯离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-huygens-mtf-field", "Huygens MTF vs Field", "惠更斯 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-geometric-mtf", "几何 MTF", "几何 MTF", "chart-spline", "MTF 曲线"),
        new("analysis-geometric-through-focus-mtf", "Geometric Through Focus MTF", "几何离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-geometric-mtf-field", "Geometric MTF vs Field", "几何 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-wavefront", "光程差图", "光程差图", "waves-horizontal", "像差分析"),
        new("analysis-wavefront-map", "波前图", "波前图", "circle-dot", "波前"),
        new("analysis-interferogram", "干涉图", "干涉图", "waves", "波前"),
        new("analysis-foucault", "傅科分析", "傅科分析", "scan", "波前"),
        new("analysis-contrast-loss", "对比度损失图", "对比度损失图", "gauge", "波前"),
        new("analysis-centroid-wavefront", "质心参考球波前", "质心球波前", "circle-dot", "波前"),
        new("analysis-best-fit-wavefront", "最佳拟合球波前", "最佳拟合波前", "focus", "波前"),
        new("analysis-zernike", "Zernike 系数", "Zernike", "sigma", "波前"),
        new("analysis-zernike-fringe", "Zernike Fringe系数", "Zernike Fringe系数", "sigma", "波前"),
        new("analysis-zernike-standard", "Zernike Standard系数", "Zernike Standard系数", "sigma", "波前"),
        new("analysis-zernike-annular", "Zernike Annular系数", "Zernike Annular系数", "sigma", "波前"),
        new("analysis-zernike-field", "Zernike系数 vs. 视场", "Zernike系数 vs. 视场", "chart-line", "波前"),
        new("analysis-jones-pupil", "Jones 瞳", "Jones 瞳", "scan", "波前"),
        new("analysis-image-simulation", "Image Simulation", "图像模拟", "image", "扩展图像分析"),
        new("analysis-geometric-image", "Geometric Image Analysis", "几何图像分析", "letter-text", "扩展图像分析"),
        new("analysis-geometric-bitmap-image", "Geometric Bitmap Image Analysis", "几何位图图像分析", "image", "扩展图像分析"),
        new("analysis-light-source", "Light Source Analysis", "光源分析", "flashlight", "扩展图像分析"),
        new("analysis-partially-coherent-image", "Partially Coherent Image Analysis", "部分相干图像分析", "blend", "扩展图像分析"),
        new("analysis-extended-diffraction-image", "Extended Diffraction Image Analysis", "扩展图像分析", "scan-search", "扩展图像分析"),
        new("analysis-relative-illumination", "Relative Illumination", "相对照度", "sun-medium", "扩展图像分析"),
        new("analysis-incoherent-irradiance", "非相干照度", "非相干照度", "sun", "扩展图像分析"),
        new("analysis-radiant-intensity", "辐射强度", "辐射强度", "gauge", "扩展图像分析"),
        new("viewer-ima-bim", "IMA/BIM Image Viewer", "IMA和BIM图片浏览器", "file-image", "扩展图像分析", AnalysisRibbonCommandKind.ImaBimViewer),
        new("viewer-bitmap", "Bitmap File Viewer", "位图文件查看器", "palette", "扩展图像分析", AnalysisRibbonCommandKind.BitmapViewer),
        new("analysis-y-ybar", "Y-Ybar", "Y-Ybar", "chart-no-axes-column", "光线迹点"),
    };

    private static readonly string[] AnalysisRibbonGroupOrder =
    {
        "光线迹点",
        "系统报告",
        "像差分析",
        "波前",
        "点扩散函数",
        "MTF 曲线",
        "RMS",
        "圈入能量",
        "扩展图像分析"
    };

    private static readonly IReadOnlyList<AnalysisRibbonMenu> AnalysisRibbonMenus = new AnalysisRibbonMenu[]
    {
        new("光线迹点", "光线迹点", "chart-scatter", new[]
        {
            "analysis-single-ray-trace",
            "-",
            "analysis-spot",
            "analysis-footprint",
            "analysis-through-focus",
            "analysis-full-field-spot",
            "analysis-matrix-spot",
            "analysis-configuration-matrix-spot",
            "-",
            "analysis-cardinal-points",
            "analysis-y-ybar",
            "-",
            "analysis-vignetting",
            "analysis-angle-height"
        }),
        new("系统报告", "系统报告", "file-text", new[]
        {
            "analysis-first-order",
            "analysis-prescription"
        }),
        new("像差分析", "像差分析", "chart-spline", new[]
        {
            "analysis-ray-fan",
            "analysis-wavefront",
            "analysis-pupil-aberration",
            "analysis-full-field-aberration",
            "-",
            "analysis-field-curvature",
            "analysis-grid-distortion",
            "analysis-axial-aberration",
            "analysis-distortion",
            "analysis-lateral-color",
            "analysis-color-focus",
            "-",
            "analysis-seidel",
            "analysis-seidel-diagram"
        }),
        new("波前", "波前", "waves-horizontal", new[]
        {
            "analysis-wavefront",
            "analysis-wavefront-map",
            "analysis-interferogram",
            "analysis-foucault",
            "analysis-contrast-loss",
            "-",
            "analysis-full-field-aberration",
            "-",
            "analysis-zernike-fringe",
            "analysis-zernike-standard",
            "analysis-zernike-annular",
            "analysis-zernike-field"
        }),
        new("点扩散函数", "点扩散函数", "focus", new[]
        {
            "analysis-psf",
            "analysis-psf-cross-section",
            "analysis-line-edge-spread",
            "-",
            "analysis-huygens-psf",
            "analysis-huygens-psf-cross-section"
        }),
        new("MTF 曲线", "MTF 曲线", "chart-no-axes-combined", new[]
        {
            "analysis-mtf",
            "analysis-fourier-through-focus-mtf",
            "analysis-fourier-mtf-field",
            "analysis-huygens-mtf",
            "analysis-huygens-through-focus-mtf",
            "analysis-huygens-mtf-field",
            "analysis-geometric-mtf",
            "analysis-geometric-through-focus-mtf",
            "analysis-geometric-mtf-field"
        }),
        new("RMS", "RMS", "chart-line", new[]
        {
            "analysis-rms-field",
            "analysis-rms-wavelength",
            "analysis-rms-focus",
            "analysis-rms-field-map"
        }),
        new("圈入能量", "圈入能量", "circle-dot", new[]
        {
            "analysis-diffraction-encircled-energy",
            "analysis-encircled-energy",
            "analysis-geometric-line-edge-spread",
            "analysis-extended-source-encircled-energy"
        }),
        new("扩展图像分析", "扩展图像分析", "image", new[]
        {
            "analysis-image-simulation",
            "analysis-geometric-image",
            "analysis-geometric-bitmap-image",
            "analysis-light-source",
            "analysis-partially-coherent-image",
            "analysis-extended-diffraction-image",
            "analysis-relative-illumination",
            "-",
            "viewer-ima-bim",
            "viewer-bitmap"
        })
    };


    internal static IReadOnlyList<string> AnalysisRibbonCategories => AnalysisRibbonGroupOrder;

    internal static IReadOnlyList<string> NativeProjectFilePatterns =>
        NativeOpticFileType.Patterns ?? Array.Empty<string>();

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonCommandsByCategory =>
        AnalysisRibbonCommands
            .GroupBy(command => command.Group)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(command => command.Id).ToArray());

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonMenusByCategory =>
        AnalysisRibbonMenus
            .GroupBy(menu => menu.Group)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(menu => menu.Label).ToArray());

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonCommandsByMenu =>
        AnalysisRibbonMenus.ToDictionary(
            menu => menu.Label,
            menu => (IReadOnlyList<string>)menu.CommandIds
                .Where(commandId => !string.Equals(commandId, "-", StringComparison.Ordinal))
                .Select(commandId => AnalysisRibbonCommands.First(command => command.Id == commandId).Label)
                .ToArray());

    private readonly IWorkbenchApplication _application;
    private readonly AppSettings _settings;
    private readonly ActionManager _actions = new();
    private readonly PanelManager _panels;
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _eflText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fNumberText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _apertureText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _trackText = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _closeAfterPersistence;
    private bool _closeInProgress;
    private bool _closed;
    private bool _startupCompleted;
    private readonly string? _startupDocumentPath;

    internal event EventHandler? StartupCompleted;

    public MainWindow()
    {
        var startup = StartupRequest.Parse(Environment.GetCommandLineArgs().Skip(1));
        _startupDocumentPath = startup.DocumentPath;
        _settings = AppSettings.Load();
        ConfigureDisplaySettings();
        _application = WorkbenchApplication.Create(
            startup.Sample,
            UserGlassCatalogDirectory(),
            BundledLensLibraryDirectory());
        _panels = new PanelManager(_application, _settings);
        RegisterActions();
        _actions.ExecutionFailed += OnActionExecutionFailed;
        _panels.PersistenceFailed += OnWorkspacePersistenceFailed;

        Title = "Optical System Design";
        Icon = BrandAssets.LoadWindowIcon();
        Width = Math.Clamp(_settings.WindowWidth, 980, 4096);
        Height = Math.Clamp(_settings.WindowHeight, 640, 2160);
        MinWidth = 1100;
        MinHeight = 640;
        ApplyTheme(save: false);
        Content = BuildShell();
        DisplayTypography.Apply(this);

        _application.Events.Changed += OnWorkspaceChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        KeyDown += OnWindowKeyDown;
        RefreshStatus();
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        try
        {
            if (_startupDocumentPath is not null)
            {
                await _application.Documents.OpenAsync(_startupDocumentPath);
            }

            await _panels.InitializeAsync();
        }
        catch (Exception exception)
        {
            _panels.ResetLayout();
            _statusText.Text = $"工作区恢复失败：{exception.Message}";
        }
        finally
        {
            if (!_startupCompleted)
            {
                _startupCompleted = true;
                StartupCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeAfterPersistence)
        {
            return;
        }

        args.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            SaveLayout();
            await _panels.SaveCurrentSessionAsync();
        }
        catch (Exception exception)
        {
            _statusText.Text = $"关闭前保存失败：{exception.Message}";
        }
        finally
        {
            _closeAfterPersistence = true;
            Close();
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        Opened -= OnOpened;
        Closing -= OnClosing;
        Closed -= OnClosed;
        KeyDown -= OnWindowKeyDown;
        _actions.ExecutionFailed -= OnActionExecutionFailed;
        _panels.PersistenceFailed -= OnWorkspacePersistenceFailed;
        _application.Events.Changed -= OnWorkspaceChanged;
        _panels.Dispose();
        _application.Dispose();
    }

}
