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

public sealed class MainWindow : Window
{
    private static readonly FilePickerFileType NativeOpticFileType = new("Optiland JSON 光学系统")
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

    private static readonly IReadOnlyList<AnalysisRibbonCommand> AnalysisRibbonCommands = new AnalysisRibbonCommand[]
    {
        new("analysis-first-order", "一级像差/一阶量", "一阶量", "ruler", "光线迹点"),
        new("analysis-prescription", "处方报告", "处方报告", "file-text", "光线迹点"),
        new("analysis-ray-fan", "光线像差图", "光线像差图", "chart-spline", "光线迹点"),
        new("analysis-spot", "标准点列图", "标准点列图", "chart-scatter", "光线迹点"),
        new("analysis-footprint", "光迹图", "光迹图", "scan-search", "光线迹点"),
        new("analysis-through-focus", "离焦点列图", "离焦点列图", "scan-line", "光线迹点"),
        new("analysis-distortion", "畸变", "畸变", "move-diagonal", "像差分析"),
        new("analysis-grid-distortion", "网格畸变", "网格畸变", "grid-3x3", "像差分析"),
        new("analysis-field-curvature", "场曲", "场曲", "chart-line", "像差分析"),
        new("analysis-encircled-energy", "圈入能量", "圈入能量", "circle-dot", "圈入能量"),
        new("analysis-pupil-aberration", "瞳孔像差", "瞳孔像差", "scan", "像差分析"),
        new("analysis-rms-field", "RMS-视场", "RMS-视场", "chart-line", "RMS"),
        new("analysis-rms-wavefront-field", "RMS 波前-视场", "RMS 波前-视场", "waves-horizontal", "RMS"),
        new("analysis-angle-pupil", "入射角-像高（扫描瞳孔）", "扫描瞳孔", "scan", "光线迹点"),
        new("analysis-angle-field", "入射角-像高（扫描视场）", "扫描视场", "scan-line", "光线迹点"),
        new("analysis-psf", "点扩散函数 PSF", "FFT PSF", "focus", "点扩散函数"),
        new("analysis-mmdft-psf", "矩阵乘法 DFT PSF", "MMDFT PSF", "grid-2x2", "点扩散函数"),
        new("analysis-huygens-psf", "惠更斯 PSF", "惠更斯 PSF", "circle-dot-dashed", "点扩散函数"),
        new("analysis-mtf", "MTF", "傅里叶 MTF", "chart-no-axes-combined", "MTF 曲线"),
        new("analysis-fourier-through-focus-mtf", "Fourier Through Focus MTF", "傅里叶离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-fourier-mtf-field", "Fourier MTF vs Field", "傅里叶 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-huygens-mtf", "惠更斯 MTF", "惠更斯 MTF", "waves-horizontal", "MTF 曲线"),
        new("analysis-huygens-through-focus-mtf", "Huygens Through Focus MTF", "惠更斯离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-huygens-mtf-field", "Huygens MTF vs Field", "惠更斯 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-geometric-mtf", "几何 MTF", "几何 MTF", "chart-spline", "MTF 曲线"),
        new("analysis-geometric-through-focus-mtf", "Geometric Through Focus MTF", "几何离焦 MTF", "scan-line", "MTF 曲线"),
        new("analysis-geometric-mtf-field", "Geometric MTF vs Field", "几何 MTF VS 视场", "chart-line", "MTF 曲线"),
        new("analysis-wavefront", "波前", "波前", "waves-horizontal", "波前"),
        new("analysis-centroid-wavefront", "质心参考球波前", "质心球波前", "circle-dot", "波前"),
        new("analysis-best-fit-wavefront", "最佳拟合球波前", "最佳拟合波前", "focus", "波前"),
        new("analysis-zernike", "Zernike 系数", "Zernike", "sigma", "波前"),
        new("analysis-jones-pupil", "Jones 瞳", "Jones 瞳", "scan", "波前"),
        new("analysis-relative-illumination", "相对照度", "相对照度", "sun-medium", "扩展图像分析"),
        new("analysis-incoherent-irradiance", "非相干照度", "非相干照度", "sun", "扩展图像分析"),
        new("analysis-radiant-intensity", "辐射强度", "辐射强度", "gauge", "扩展图像分析"),
        new("analysis-y-ybar", "Y-Ybar", "Y-Ybar", "chart-no-axes-column", "光线迹点"),
        new("analysis-image-simulation", "成像仿真", "成像仿真", "image", "扩展图像分析")
    };

    private static readonly string[] AnalysisRibbonGroupOrder =
    {
        "光线迹点",
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
            "analysis-ray-fan",
            "analysis-spot",
            "analysis-footprint",
            "analysis-through-focus",
            "analysis-first-order",
            "analysis-prescription",
            "analysis-y-ybar",
            "analysis-angle-pupil",
            "analysis-angle-field"
        }),
        new("像差分析", "像差分析", "chart-spline", new[]
        {
            "analysis-pupil-aberration",
            "analysis-field-curvature",
            "analysis-distortion",
            "analysis-grid-distortion"
        }),
        new("波前", "波前", "waves-horizontal", new[]
        {
            "analysis-wavefront",
            "analysis-centroid-wavefront",
            "analysis-best-fit-wavefront",
            "analysis-zernike",
            "analysis-jones-pupil"
        }),
        new("点扩散函数", "点扩散函数", "focus", new[]
        {
            "analysis-psf",
            "analysis-mmdft-psf",
            "analysis-huygens-psf"
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
            "analysis-rms-wavefront-field"
        }),
        new("圈入能量", "圈入能量", "circle-dot", new[]
        {
            "analysis-encircled-energy"
        }),
        new("扩展图像分析", "扩展图像分析", "image", new[]
        {
            "analysis-image-simulation",
            "analysis-relative-illumination",
            "analysis-incoherent-irradiance",
            "analysis-radiant-intensity"
        })
    };


    internal static IReadOnlyList<string> AnalysisRibbonCategories => AnalysisRibbonGroupOrder;

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

    internal event EventHandler? StartupCompleted;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        ConfigureDisplaySettings();
        _application = WorkbenchApplication.Create(InitialSample(), UserGlassCatalogDirectory());
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

    private static string? InitialSample()
    {
        var sampleArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--sample=", StringComparison.OrdinalIgnoreCase));
        return sampleArgument?.Split('=', 2)[1];
    }

    private void RegisterActions()
    {
        _actions.Register("new", "新建空白系统", "文件", () => SwitchDocumentAsync(_application.Documents.NewBlank));
        _actions.Register("new-demo", "新建 Cooke 三片式样例", "文件", () => SwitchDocumentAsync(_application.Documents.NewCooke));
        _actions.Register("new-tessar", "新建 Tessar F/4.5 四片式样例", "文件", () => SwitchDocumentAsync(_application.Documents.NewTessar));
        _actions.Register("open", "打开光学系统", "文件", OpenAsync);
        _actions.Register("import-zemax", "导入 Zemax ZMX", "文件", ImportZemaxAsync);
        _actions.Register("save-as", "另存为", "文件", SaveAsAsync);
        _actions.Register("export-python-json", "导出 Python Optiland JSON", "文件", ExportPythonJsonAsync);
        _actions.Register("exit", "退出", "文件", Close);
        _actions.Register("undo", "撤销", "编辑", () => _application.Documents.Undo());
        _actions.Register("redo", "重做", "编辑", () => _application.Documents.Redo());
        _actions.Register("show-lens-editor", "显示镜头编辑器", "面板", () => _panels.Show(WorkspacePanelId.LensEditor));
        _actions.Register("show-system", "显示系统属性", "面板", () => _panels.Show(WorkspacePanelId.SystemProperties));
        _actions.Register("display-settings", "显示格式设置", "设置", ShowDisplaySettingsAsync);
        _actions.Register("show-viewer", "显示系统视图", "面板", () => _panels.Show(WorkspacePanelId.Viewer));
        _actions.Register("show-viewer-2d", "显示二维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.TwoDimensional));
        _actions.Register("show-viewer-3d", "显示三维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.ThreeDimensional));
        _actions.Register("show-solid-model", "显示实体模型", "视图", _panels.ShowSolidModel);
        _actions.Register("show-material-library", "打开材料库", "数据库", _panels.ShowMaterialLibrary);
        _actions.Register("show-glass-catalog", "打开玻璃目录", "数据库", _panels.ShowGlassCatalog);
        _actions.Register("show-manufacturability", "可加工性评估", "加工与图纸", _panels.ShowManufacturability);
        _actions.Register(
            "show-optical-drawing-iso",
            "ISO 10110 光学制图",
            "加工与图纸",
            () => _panels.ShowOpticalDrawing(OpticalDrawingStandard.Iso10110));
        _actions.Register(
            "show-optical-drawing-gb",
            "GB/T 13323—2009 光学制图",
            "加工与图纸",
            () => _panels.ShowOpticalDrawing(OpticalDrawingStandard.GbT13323_2009));
        _actions.Register("show-analysis", "显示分析面板", "面板", () => _panels.Show(WorkspacePanelId.Analysis));
        _actions.Register("show-optimization", "显示优化面板", "面板", () => _panels.Show(WorkspacePanelId.Optimization));
        _actions.Register("show-tolerancing", "显示公差面板", "面板", () => _panels.Show(WorkspacePanelId.Tolerancing));
        _actions.Register("show-multiconfig", "显示多配置面板", "面板", () => _panels.Show(WorkspacePanelId.MultiConfiguration));
        _actions.Register("reset-layout", "恢复默认布局", "布局", ResetLayout);
        _actions.Register("save-layout-1", "保存布局到槽位 1", "布局", () => SaveLayoutSlot(1));
        _actions.Register("save-layout-2", "保存布局到槽位 2", "布局", () => SaveLayoutSlot(2));
        _actions.Register("load-layout-1", "加载布局槽位 1", "布局", () => LoadLayoutSlot(1));
        _actions.Register("load-layout-2", "加载布局槽位 2", "布局", () => LoadLayoutSlot(2));
        _actions.Register("analysis-dock-all", "所有页面排列为标签", "窗口", _panels.DockAllWindows);
        _actions.Register("dock-single-pane", "停靠到单一 Pane", "窗口", _panels.DockToSinglePane);
        _actions.Register("analysis-float-all", "浮动所有页面", "窗口", _panels.FloatAllWindows);
        _actions.Register("analysis-tile-all", "平铺所有页面", "窗口", _panels.TileAllWindows);
        _actions.Register("analysis-cascade-all", "层叠所有页面", "窗口", _panels.CascadeAllWindows);
        _actions.Register("analysis-clone", "克隆当前分析页", "窗口", _panels.CloneActiveAnalysis);
        _actions.Register("lock-page", "锁定当前页面更新", "窗口", () => _panels.SetActiveDocumentLocked(true));
        _actions.Register("unlock-page", "解锁当前页面更新", "窗口", () => _panels.SetActiveDocumentLocked(false));
        _actions.Register("close-all-pages", "关闭全部页面", "窗口", _panels.CloseAllDocuments);
        _actions.Register("save-default-layout", "保存默认布局", "窗口", () => _panels.SaveDefaultLayoutAsync());
        _actions.Register("restore-default-layout", "恢复默认布局", "窗口", () => _panels.RestoreDefaultLayoutAsync());
        _actions.Register("command-palette", "命令面板", "工具", ShowCommandPaletteAsync);
        _actions.Register("about", "关于 Optical System Design", "帮助", ShowAboutAsync);
        foreach (var analysis in AnalysisRibbonCommands)
        {
            _actions.Register(
                analysis.Id,
                analysis.Name,
                "分析",
                () => _panels.ShowAnalysis(analysis.Name));
        }
    }

    private Control BuildShell()
    {
        var root = new DockPanel();
        var ribbon = BuildRibbon();
        DockPanel.SetDock(ribbon, Avalonia.Controls.Dock.Top);
        root.Children.Add(ribbon);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(_panels.WorkspaceGrid);
        return root;
    }

    private Menu BuildMenu()
    {
        var fileMenu = new MenuItem
        {
            Header = "文件",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("new-demo")),
                MenuItem(_actions.Find("new-tessar")),
                new Separator(),
                MenuItem(_actions.Find("import-zemax")),
                new Separator(),
                MenuItem(_actions.Find("export-python-json")),
                new Separator(),
                MenuItem(_actions.Find("exit"))
            }
        };
        var editMenu = new MenuItem
        {
            Header = "编辑",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("undo")),
                MenuItem(_actions.Find("redo"))
            }
        };
        var designMenu = new MenuItem
        {
            Header = "设计",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("show-optimization")),
                MenuItem(_actions.Find("show-tolerancing"))
            }
        };
        var layoutMenu = new MenuItem
        {
            Header = "布局",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("save-layout-1")),
                MenuItem(_actions.Find("save-layout-2")),
                MenuItem(_actions.Find("load-layout-1")),
                MenuItem(_actions.Find("load-layout-2"))
            }
        };
        var helpMenu = new MenuItem
        {
            Header = "帮助",
            ItemsSource = new object[] { MenuItem(_actions.Find("about")) }
        };
        var settingsMenu = new MenuItem
        {
            Header = "设置",
            ItemsSource = new object[] { MenuItem(_actions.Find("display-settings")) }
        };
        return new Menu
        {
            ItemsSource = new object[]
            {
                fileMenu, editMenu, designMenu, layoutMenu, settingsMenu, helpMenu
            }
        };
    }

    private Control BuildRibbon()
    {
        var analysisGroups = AnalysisRibbonMenus
            .OrderBy(menu => AnalysisRibbonGroupOrder.IndexOf(menu.Group))
            .Select(menu => RibbonGroup(string.Empty, RibbonAnalysisMenuButton(menu)))
            .ToArray();
        var tabs = new TabControl
        {
            SelectedIndex = 1,
            ItemsSource = new object[]
            {
                RibbonTab("文件", BuildRibbonPage(
                    RibbonGroup("文件",
                        RibbonButton("new", "file-plus", "新建"),
                        RibbonButton("open", "folder-open", "打开"),
                        RibbonButton("import-zemax", "file-input", "Zemax 导入"),
                        RibbonButton("save-as", "save", "保存"),
                        RibbonButton("export-python-json", "upload", "导出")),
                    RibbonGroup("示例",
                        RibbonButton("new-demo", "aperture", "Cooke 示例"),
                        RibbonButton("new-tessar", "disc-2", "Tessar 示例")))),
                RibbonTab("设置", BuildRibbonPage(
                    RibbonGroup("系统",
                        RibbonButton("show-system", "settings", "系统选项"),
                        RibbonButton("show-lens-editor", "table-2", "镜头数据"),
                        RibbonButton("show-multiconfig", "panels-top-left", "多配置")),
                    RibbonGroup("显示",
                        RibbonButton("display-settings", "type", "格式与字体")))),
                RibbonTab("视图", BuildRibbonPage(
                    RibbonGroup("系统布局",
                        RibbonButton("show-viewer-2d", "panel-top", "2D视图"),
                        RibbonButton("show-viewer-3d", "box", "3D视图"),
                        RibbonButton("show-solid-model", "cylinder", "实体模型")))),
                RibbonTab("分析", BuildRibbonPage(analysisGroups)),
                RibbonTab("优化", BuildRibbonPage(
                    RibbonGroup("评价函数",
                        RibbonButton("show-optimization", "sparkles", "优化向导"),
                        RibbonButton("show-optimization", "target", "执行优化")))),
                RibbonTab("公差", BuildRibbonPage(
                    RibbonGroup("公差分析",
                        RibbonButton("show-tolerancing", "activity", "灵敏度"),
                        RibbonButton("show-tolerancing", "gauge", "蒙特卡洛")))),
                RibbonTab("加工与图纸", BuildRibbonPage(
                    RibbonGroup("制造准备",
                        RibbonButton("show-manufacturability", "clipboard-check", "可加工性评估")),
                    RibbonGroup("光学制图",
                        RibbonButton("show-optical-drawing-iso", "drafting-compass", "ISO 10110"),
                        RibbonButton("show-optical-drawing-gb", "ruler", "GB/T 13323")))),
                RibbonTab("数据库", BuildRibbonPage(
                    RibbonGroup("光学材料",
                        RibbonButton("show-material-library", "database", "材料库"),
                        RibbonButton("show-glass-catalog", "gem", "玻璃")))),
                RibbonTab("编程与工具", BuildRibbonPage(
                    RibbonGroup("编辑",
                        RibbonButton("undo", "undo-2", "撤销"),
                        RibbonButton("redo", "redo-2", "重做")),
                    RibbonGroup("命令",
                        RibbonButton("command-palette", "command", "命令面板")))),
                RibbonTab("窗口", BuildRibbonPage(
                    RibbonGroup("页面窗口布局",
                        RibbonButton("analysis-dock-all", "panel-top", "全部停靠"),
                        RibbonButton("dock-single-pane", "panels-top-left", "单一 Pane"),
                        RibbonButton("analysis-float-all", "picture-in-picture-2", "浮动全部"),
                        RibbonButton("analysis-tile-all", "grid-2x2", "平铺全部"),
                        RibbonButton("analysis-cascade-all", "rows-3", "层叠全部")),
                    RibbonGroup("页面",
                        RibbonButton("analysis-clone", "copy", "克隆分析"),
                        RibbonButton("lock-page", "lock", "锁定"),
                        RibbonButton("unlock-page", "lock-open", "解锁"),
                        RibbonButton("close-all-pages", "x", "关闭全部")),
                    RibbonGroup("布局",
                        RibbonButton("save-default-layout", "save", "保存默认"),
                        RibbonButton("restore-default-layout", "rotate-ccw", "恢复默认")))),
                RibbonTab("帮助", BuildRibbonPage(
                    RibbonGroup("支持",
                        RibbonButton("about", "circle-question-mark", "关于"))))
            }
        };
        tabs.Bind(TabControl.BackgroundProperty, new DynamicResourceExtension("OptilandSurfaceBrush"));
        var ribbon = new Border
        {
            Height = 144,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BoxShadow = BoxShadows.Parse("0 3 8 0 #14000000"),
            Child = tabs
        };
        ribbon.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandSurfaceBrush"));
        ribbon.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        return ribbon;
    }

    private static TabItem RibbonTab(string title, Control content)
    {
        return new TabItem
        {
            Header = title,
            Content = content,
            FontSize = 13,
            Padding = new Thickness(14, 7)
        };
    }

    private static Control BuildRibbonPage(params Control[] groups)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(4, 2, 4, 0)
        };
        foreach (var group in groups)
        {
            panel.Children.Add(group);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = panel
        };
    }

    private static Control RibbonGroup(string title, params Control[] commands)
    {
        var commandPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(5, 2, 5, 0)
        };
        foreach (var command in commands)
        {
            commandPanel.Children.Add(command);
        }

        var grid = new Grid { RowDefinitions = new RowDefinitions("68,18") };
        var caption = new TextBlock
        {
            Text = title,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
        Grid.SetRow(commandPanel, 0);
        Grid.SetRow(caption, 1);
        grid.Children.Add(commandPanel);
        grid.Children.Add(caption);

        var group = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = grid
        };
        group.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        return group;
    }

    private Button RibbonButton(string actionId, string iconName, string label)
    {
        var button = new Button
        {
            Width = 78,
            Height = 66,
            MinHeight = 66,
            Margin = new Thickness(1, 0, 1, 2),
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = RibbonCommandContent(iconName, label)
        };
        button.PointerEntered += (_, _) =>
        {
            button.Background = ThemeBrush(button, "OptilandHoverBrush");
            button.BorderBrush = ThemeBrush(button, "OptilandHoverBorderBrush");
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        var action = _actions.Find(actionId);
        button.Click += async (_, _) => await _actions.ExecuteAsync(action);
        return button;
    }

    private DropDownButton RibbonAnalysisMenuButton(AnalysisRibbonMenu menu)
    {
        var flyout = new MenuFlyout();
        foreach (var commandId in menu.CommandIds)
        {
            var command = AnalysisRibbonCommands.First(candidate =>
                string.Equals(candidate.Id, commandId, StringComparison.Ordinal));
            var action = _actions.Find(command.Id);
            var item = new MenuItem
            {
                Header = new LocalIconLabel(command.IconName, command.Label, 20),
                MinWidth = 190,
                Padding = new Thickness(10, 8)
            };
            item.Click += async (_, _) => await _actions.ExecuteAsync(action);
            flyout.Items.Add(item);
        }

        var button = new DropDownButton
        {
            Width = 92,
            Height = 66,
            MinHeight = 66,
            Margin = new Thickness(1, 0, 1, 2),
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
            Content = RibbonCommandContent(menu.IconName, menu.Label)
        };
        button.PointerEntered += (_, _) =>
        {
            button.Background = ThemeBrush(button, "OptilandHoverBrush");
            button.BorderBrush = ThemeBrush(button, "OptilandHoverBorderBrush");
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        ToolTip.SetTip(button, $"选择{menu.Label}分析类型");
        return button;
    }

    private static Control RibbonCommandContent(string iconName, string label)
    {
        var grid = new Grid
        {
            Width = 66,
            Height = 52,
            RowDefinitions = new RowDefinitions("29,23"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new LocalIcon
        {
            IconName = iconName,
            Width = 26,
            Height = 26,
            StrokeWidth = 1.8,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(icon, 0);
        Grid.SetRow(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }

    private Control BuildStatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        grid.Children.Add(StatusCell(_statusText, 0, 0));
        grid.Children.Add(StatusCell(_eflText, 1, 128));
        grid.Children.Add(StatusCell(_fNumberText, 2, 116));
        grid.Children.Add(StatusCell(_apertureText, 3, 130));
        grid.Children.Add(StatusCell(_trackText, 4, 120));

        var status = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid
        };
        status.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandSubtleSurfaceBrush"));
        status.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        return status;
    }

    private static Border StatusCell(TextBlock text, int column, double width)
    {
        text.FontSize = 11;
        text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));
        var border = new Border
        {
            MinWidth = width,
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
            Padding = new Thickness(9, 4),
            Child = text
        };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        Grid.SetColumn(border, column);
        return border;
    }

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开光学系统",
            AllowMultiple = false,
            FileTypeFilter = new[] { NativeOpticFileType, PythonOptilandJsonFileType, CommercialOpticFileType, PlainSequentialFileType }
        });
        if (files.Count > 0)
        {
            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
            if (_application.MultiConfiguration.GetRows().Count > 1)
            {
                _panels.Show(WorkspacePanelId.MultiConfiguration);
            }
        }
    }

    private async Task SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存光学系统",
            SuggestedFileName = "optiland-workbench.optiland.json",
            FileTypeChoices = new[] { NativeOpticFileType, PythonOptilandJsonFileType, CommercialOpticFileType, PlainSequentialFileType }
        });
        if (file is not null)
        {
            await _application.Documents.SaveAsync(file.Path.LocalPath);
        }
    }

    private async Task ExportPythonJsonAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 Python Optiland JSON",
            SuggestedFileName = "optic.optiland-python.json",
            FileTypeChoices = new[] { PythonOptilandJsonFileType }
        });
        if (file is not null)
        {
            await _application.Documents.SaveAsync(file.Path.LocalPath);
        }
    }

    private async Task ShowAboutAsync()
    {
        using var authorStream = Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://OptilandWorkbench.App/Assets/Author.jpg"));
        using var authorBitmap = new Bitmap(authorStream);
        var dialog = new Window
        {
            Title = "关于 Optical System Design",
            Width = 640,
            Height = 370,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button { Content = "关闭", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => dialog.Close();
        var details = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Optical System Design",
                    FontSize = 27,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "S.T.A.R. Labs 出品",
                    FontSize = 17,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255))
                },
                new TextBlock
                {
                    Text = "面向光学系统设计、光线追迹与像质分析的桌面软件。",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = ".NET 10  ·  Avalonia 12  ·  Managed CPU",
                    Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102))
                }
            }
        };
        var main = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 24
        };
        var portrait = new Border
        {
            Width = 180,
            Height = 180,
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 238)),
            Child = new Image
            {
                Source = authorBitmap,
                Stretch = Stretch.UniformToFill
            }
        };
        Grid.SetColumn(details, 1);
        main.Children.Add(portrait);
        main.Children.Add(details);

        var root = new Grid
        {
            Margin = new Thickness(26),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 18
        };
        Grid.SetRow(closeButton, 1);
        root.Children.Add(main);
        root.Children.Add(closeButton);
        dialog.Content = root;
        await dialog.ShowDialog(this);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        var commandModifier = args.KeyModifiers.HasFlag(KeyModifiers.Control)
            || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (args.Key == Key.K && commandModifier)
        {
            args.Handled = true;
            try
            {
                await ShowCommandPaletteAsync();
            }
            catch (Exception exception)
            {
                if (!_closed)
                {
                    _statusText.Text = $"命令面板打开失败：{exception.Message}";
                }
            }
        }
    }

    private void RefreshStatus()
    {
        var snapshot = _application.Documents.GetSnapshot();
        Title = $"{snapshot.Name} - Optical System Design";
        _statusText.Text = $"{snapshot.Status}   |   {snapshot.SurfaceCount} 个表面   |   {snapshot.FieldCount} 个视场   |   {snapshot.WavelengthCount} 个波长";
        _eflText.Text = $"EFFL: {FormatMetric(snapshot.EffectiveFocalLength)}";
        _fNumberText.Text = $"F/#: {FormatMetric(snapshot.FNumber)}";
        _apertureText.Text = $"APER: {NumericDisplayFormatter.Format(snapshot.ApertureValue)}";
        _trackText.Text = $"TOTR: {FormatMetric(snapshot.TotalTrack)}";
    }

    private async Task SwitchDocumentAsync(Action createDocument)
    {
        await _panels.SaveCurrentSessionAsync();
        createDocument();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void OnWorkspacePersistenceFailed(object? sender, WorkspacePersistenceFailedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
            _statusText.Text = $"工作区自动保存失败：{args.Exception.Message}");
    }

    private static string FormatMetric(double value)
    {
        return NumericDisplayFormatter.Format(value);
    }

    private void ConfigureDisplaySettings()
    {
        _settings.NormalizeDisplaySettings();
        NumericDisplayFormatter.Configure(new NumericDisplayOptions(
            _settings.DecimalPlaces,
            _settings.UpperScientificExponent,
            _settings.LowerScientificExponent));
        DisplayTypography.Configure(_settings);
    }

    private async Task ShowDisplaySettingsAsync()
    {
        var dialog = new DisplaySettingsWindow(_settings);
        if (!await dialog.ShowDialog<bool>(this))
        {
            return;
        }

        ConfigureDisplaySettings();
        ApplyTheme(save: false);
        DisplayTypography.Apply(this);
        _panels.ApplyDisplaySettings();
        RefreshStatus();
        InvalidateVisual();
    }

    private void ApplyTheme(bool save = true)
    {
        Avalonia.Application.Current!.RequestedThemeVariant = _settings.Theme switch
        {
            "Dark" => ThemeVariant.Dark,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
        if (save)
        {
            _settings.Save();
        }
    }

    private static IBrush ThemeBrush(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value)
        && value is IBrush brush
            ? brush
            : Brushes.Transparent;

    private void ResetLayout()
    {
        Width = 1440;
        Height = 900;
        _panels.ResetLayout();
        SaveLayout();
    }

    private Task SaveLayoutSlot(int slot)
    {
        return _panels.SaveLayoutSlotAsync(slot);
    }

    private async Task LoadLayoutSlot(int slot)
    {
        await _panels.LoadLayoutSlotAsync(slot);
        SaveLayout();
    }

    private async Task ShowCommandPaletteAsync()
    {
        await new CommandPaletteWindow(_actions).ShowDialog(this);
    }

    private void SaveLayout()
    {
        _settings.WindowWidth = Math.Max(MinWidth, Width);
        _settings.WindowHeight = Math.Max(MinHeight, Height);
        _settings.ApplyLayout(_panels.CaptureLayout());
        _settings.Save();
    }

    private MenuItem MenuItem(AppAction action)
    {
        var item = new MenuItem { Header = action.Text };
        item.Click += async (_, _) => await _actions.ExecuteAsync(action);
        return item;
    }

    private async void OnActionExecutionFailed(object? sender, ActionExecutionFailedEventArgs args)
    {
        if (_closed || _closeInProgress)
        {
            return;
        }

        var dialog = new Window
        {
            Title = "操作失败",
            Width = 560,
            Height = 260,
            MinWidth = 420,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = args.Action.Text,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBox
                {
                    Text = args.Exception.Message,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 120
                },
                closeButton
            }
        };
        try
        {
            await dialog.ShowDialog(this);
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                _statusText.Text = $"操作失败：{args.Exception.Message}；错误窗口未能显示：{exception.Message}";
            }
        }
    }

    private async Task ImportZemaxAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 Zemax 光学系统",
            AllowMultiple = false,
            FileTypeFilter = new[] { ZemaxOpticFileType }
        });
        if (files.Count > 0)
        {
            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
            if (_application.MultiConfiguration.GetRows().Count > 1)
            {
                _panels.Show(WorkspacePanelId.MultiConfiguration);
            }
        }
    }

    private static string UserGlassCatalogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptilandWorkbench",
        "glass-catalogs");

    private sealed record AnalysisRibbonCommand(
        string Id,
        string Name,
        string Label,
        string IconName,
        string Group);

    private sealed record AnalysisRibbonMenu(
        string Group,
        string Label,
        string IconName,
        IReadOnlyList<string> CommandIds);

}
