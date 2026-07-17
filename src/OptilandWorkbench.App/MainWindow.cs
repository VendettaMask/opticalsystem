using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Core;

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

    private static readonly FilePickerFileType PlainSequentialFileType = new("序列光学文本")
    {
        Patterns = new[] { "*.lens", "*.dat", "*.txt" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly IReadOnlyList<AnalysisRibbonCommand> AnalysisRibbonCommands = new AnalysisRibbonCommand[]
    {
        new("analysis-first-order", "一级像差/一阶量", "一阶量", "Ⅰ", "基础"),
        new("analysis-prescription", "处方报告", "处方报告", "≡", "基础"),
        new("analysis-spot", "点列图", "点列图", "⁙", "几何像质"),
        new("analysis-ray-fan", "光线扇形图", "光线扇形图", "⌁", "几何像质"),
        new("analysis-best-fit-ray-fan", "最佳拟合光线扇形图", "最佳拟合扇形图", "≈", "几何像质"),
        new("analysis-distortion", "畸变", "畸变", "↝", "几何像质"),
        new("analysis-grid-distortion", "网格畸变", "网格畸变", "▦", "几何像质"),
        new("analysis-field-curvature", "场曲", "场曲", "⌒", "几何像质"),
        new("analysis-encircled-energy", "包围能量", "包围能量", "◎", "几何像质"),
        new("analysis-pupil-aberration", "瞳孔像差", "瞳孔像差", "◉", "几何像质"),
        new("analysis-rms-field", "RMS-视场", "RMS-视场", "R", "几何像质"),
        new("analysis-rms-wavefront-field", "RMS 波前-视场", "RMS 波前-视场", "W", "几何像质"),
        new("analysis-through-focus", "离焦扫描", "离焦扫描", "↔", "扫描"),
        new("analysis-through-focus-mtf", "离焦 MTF", "离焦 MTF", "M", "扫描"),
        new("analysis-angle-pupil", "入射角-像高（扫描瞳孔）", "像高-扫描瞳孔", "∠", "扫描"),
        new("analysis-angle-field", "入射角-像高（扫描视场）", "像高-扫描视场", "∢", "扫描"),
        new("analysis-psf", "点扩散函数 PSF", "PSF", "✦", "PSF / MTF"),
        new("analysis-mmdft-psf", "矩阵乘法 DFT PSF", "MMDFT PSF", "▦", "PSF / MTF"),
        new("analysis-huygens-psf", "惠更斯 PSF", "惠更斯 PSF", "◌", "PSF / MTF"),
        new("analysis-mtf", "调制传递函数 MTF", "MTF", "≋", "PSF / MTF"),
        new("analysis-huygens-mtf", "惠更斯 MTF", "惠更斯 MTF", "〰", "PSF / MTF"),
        new("analysis-geometric-mtf", "几何 MTF", "几何 MTF", "▥", "PSF / MTF"),
        new("analysis-sampled-mtf", "采样 MTF", "采样 MTF", "▤", "PSF / MTF"),
        new("analysis-wavefront", "波前", "波前", "∿", "波前"),
        new("analysis-centroid-wavefront", "质心参考球波前", "质心球波前", "◯", "波前"),
        new("analysis-best-fit-wavefront", "最佳拟合球波前", "最佳拟合波前", "◉", "波前"),
        new("analysis-zernike", "Zernike 系数", "Zernike", "Z", "波前"),
        new("analysis-jones-pupil", "Jones 瞳", "Jones 瞳", "J", "波前"),
        new("analysis-incoherent-irradiance", "非相干照度", "非相干照度", "☼", "照明与成像"),
        new("analysis-radiant-intensity", "辐射强度", "辐射强度", "✺", "照明与成像"),
        new("analysis-y-ybar", "Y-Ybar", "Y-Ybar", "Y", "照明与成像"),
        new("analysis-image-simulation", "成像仿真", "成像仿真", "▣", "照明与成像")
    };

    private readonly OptilandConnector _connector;
    private readonly AppSettings _settings;
    private readonly ActionManager _actions = new();
    private readonly PanelManager _panels;
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _eflText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fNumberText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _apertureText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _trackText = new() { VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _connector = new OptilandConnector(CreateInitialOptic());
        _panels = new PanelManager(_connector, _settings);
        RegisterActions();

        Title = "Optiland 光学工作台";
        Width = Math.Clamp(_settings.WindowWidth, 980, 4096);
        Height = Math.Clamp(_settings.WindowHeight, 640, 2160);
        MinWidth = 1100;
        MinHeight = 640;
        Content = BuildShell();
        SetTheme(_settings.Theme, save: false);

        _connector.OpticLoaded += (_, _) => RefreshStatus();
        _connector.OpticChanged += (_, _) => RefreshStatus();
        Closed += (_, _) => SaveLayout();
        KeyDown += OnWindowKeyDown;
        RefreshStatus();
    }

    private static Optic CreateInitialOptic()
    {
        var sampleArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--sample=", StringComparison.OrdinalIgnoreCase));
        return sampleArgument?.Split('=', 2)[1].ToLowerInvariant() switch
        {
            "cooke" => Optic.CreateCookeTriplet(),
            "tessar" => Optic.CreateTessarLens(),
            _ => Optic.CreateBlank()
        };
    }

    private void RegisterActions()
    {
        _actions.Register("new", "新建空白系统", "文件", _connector.NewBlank);
        _actions.Register("new-demo", "新建 Cooke 三片式样例", "文件", _connector.NewDemo);
        _actions.Register("new-tessar", "新建 Tessar F/4.5 四片式样例", "文件", _connector.NewTessar);
        _actions.Register("open", "打开光学系统", "文件", OpenAsync);
        _actions.Register("save-as", "另存为", "文件", SaveAsAsync);
        _actions.Register("export-python-json", "导出 Python Optiland JSON", "文件", ExportPythonJsonAsync);
        _actions.Register("exit", "退出", "文件", Close);
        _actions.Register("undo", "撤销", "编辑", () => _connector.Undo());
        _actions.Register("redo", "重做", "编辑", () => _connector.Redo());
        _actions.Register("show-lens-editor", "显示镜头编辑器", "面板", () => _panels.Show(WorkspacePanelId.LensEditor));
        _actions.Register("show-system", "显示系统属性", "面板", () => _panels.Show(WorkspacePanelId.SystemProperties));
        _actions.Register("show-viewer", "显示系统视图", "面板", () => _panels.Show(WorkspacePanelId.Viewer));
        _actions.Register("show-viewer-2d", "显示二维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.TwoDimensional));
        _actions.Register("show-viewer-3d", "显示三维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.ThreeDimensional));
        _actions.Register("show-analysis", "显示分析面板", "面板", () => _panels.Show(WorkspacePanelId.Analysis));
        _actions.Register("show-optimization", "显示优化面板", "面板", () => _panels.Show(WorkspacePanelId.Optimization));
        _actions.Register("show-tolerancing", "显示公差面板", "面板", () => _panels.Show(WorkspacePanelId.Tolerancing));
        _actions.Register("show-multiconfig", "显示多配置面板", "面板", () => _panels.Show(WorkspacePanelId.MultiConfiguration));
        _actions.Register("theme-light", "浅色主题", "视图", () => SetTheme("Light"));
        _actions.Register("theme-dark", "深色主题", "视图", () => SetTheme("Dark"));
        _actions.Register("reset-layout", "恢复默认布局", "布局", ResetLayout);
        _actions.Register("save-layout-1", "保存布局到槽位 1", "布局", () => SaveLayoutSlot(1));
        _actions.Register("save-layout-2", "保存布局到槽位 2", "布局", () => SaveLayoutSlot(2));
        _actions.Register("load-layout-1", "加载布局槽位 1", "布局", () => LoadLayoutSlot(1));
        _actions.Register("load-layout-2", "加载布局槽位 2", "布局", () => LoadLayoutSlot(2));
        _actions.Register("analysis-dock-all", "分析窗口排列为标签", "窗口", _panels.DockAnalysisWindows);
        _actions.Register("analysis-float-all", "浮动所有分析窗口", "窗口", _panels.FloatAnalysisWindows);
        _actions.Register("analysis-tile-all", "平铺所有分析窗口", "窗口", _panels.TileAnalysisWindows);
        _actions.Register("analysis-cascade-all", "层叠所有分析窗口", "窗口", _panels.CascadeAnalysisWindows);
        _actions.Register("command-palette", "命令面板", "工具", ShowCommandPaletteAsync);
        _actions.Register("about", "关于 Optiland Workbench", "帮助", ShowAboutAsync);
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
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
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
        var appearanceMenu = new MenuItem
        {
            Header = "外观",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("theme-light")),
                MenuItem(_actions.Find("theme-dark"))
            }
        };
        var helpMenu = new MenuItem
        {
            Header = "帮助",
            ItemsSource = new object[] { MenuItem(_actions.Find("about")) }
        };
        return new Menu
        {
            Background = Brushes.White,
            ItemsSource = new object[]
            {
                fileMenu, editMenu, designMenu, layoutMenu, appearanceMenu, helpMenu
            }
        };
    }

    private Control BuildRibbon()
    {
        var analysisGroups = AnalysisRibbonCommands
            .GroupBy(command => command.Group)
            .Select(group => RibbonGroup(
                group.Key,
                group.Select(command => RibbonButton(command.Id, command.Glyph, command.Label)).ToArray()))
            .ToArray();
        var tabs = new TabControl
        {
            SelectedIndex = 1,
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            ItemsSource = new object[]
            {
                RibbonTab("文件", BuildRibbonPage(
                    RibbonGroup("文件",
                        RibbonButton("new", "□", "新建"),
                        RibbonButton("open", "▣", "打开"),
                        RibbonButton("save-as", "▤", "保存"),
                        RibbonButton("export-python-json", "⇧", "导出")),
                    RibbonGroup("示例",
                        RibbonButton("new-demo", "◫", "Cooke 示例"),
                        RibbonButton("new-tessar", "◩", "Tessar 示例")))),
                RibbonTab("设置", BuildRibbonPage(
                    RibbonGroup("系统",
                        RibbonButton("show-system", "⚙", "系统选项"),
                        RibbonButton("show-lens-editor", "▦", "镜头数据"),
                        RibbonButton("show-multiconfig", "▥", "多配置")),
                    RibbonGroup("外观",
                        RibbonButton("theme-light", "☀", "浅色"),
                        RibbonButton("theme-dark", "◐", "深色")))),
                RibbonTab("视图", BuildRibbonPage(
                    RibbonGroup("系统布局",
                        RibbonButton("show-viewer-2d", "▱", "二维布局"),
                        RibbonButton("show-viewer-3d", "◇", "三维布局")))),
                RibbonTab("分析", BuildRibbonPage(analysisGroups)),
                RibbonTab("优化", BuildRibbonPage(
                    RibbonGroup("评价函数",
                        RibbonButton("show-optimization", "↗", "评价函数"),
                        RibbonButton("show-optimization", "◎", "执行优化")))),
                RibbonTab("公差", BuildRibbonPage(
                    RibbonGroup("公差分析",
                        RibbonButton("show-tolerancing", "±", "灵敏度"),
                        RibbonButton("show-tolerancing", "∿", "蒙特卡洛")))),
                RibbonTab("数据与零件", BuildRibbonPage(
                    RibbonGroup("数据",
                        RibbonButton("show-system", "▦", "材料与系统")),
                    RibbonGroup("零件",
                        RibbonButton("show-lens-editor", "◫", "表面与组件")))),
                RibbonTab("编程与工具", BuildRibbonPage(
                    RibbonGroup("编辑",
                        RibbonButton("undo", "↶", "撤销"),
                        RibbonButton("redo", "↷", "重做")),
                    RibbonGroup("命令",
                        RibbonButton("command-palette", "⌘", "命令面板")),
                    RibbonGroup("布局",
                        RibbonButton("load-layout-1", "1", "布局 1"),
                        RibbonButton("load-layout-2", "2", "布局 2"),
                        RibbonButton("reset-layout", "▧", "恢复布局")))),
                RibbonTab("窗口", BuildRibbonPage(
                    RibbonGroup("分析窗口布局",
                        RibbonButton("analysis-dock-all", "▤", "标签排列"),
                        RibbonButton("analysis-float-all", "▣", "浮动全部"),
                        RibbonButton("analysis-tile-all", "▦", "平铺全部"),
                        RibbonButton("analysis-cascade-all", "▱", "层叠全部")))),
                RibbonTab("STAR", BuildRibbonPage(
                    RibbonGroup("结构热分析",
                        RibbonButton("show-analysis", "✦", "STAR 工作区")))),
                RibbonTab("帮助", BuildRibbonPage(
                    RibbonGroup("支持",
                        RibbonButton("about", "?", "关于"))))
            }
        };
        return new Border
        {
            Height = 132,
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BoxShadow = BoxShadows.Parse("0 3 8 0 #14000000"),
            Child = tabs
        };
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

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,20") };
        var caption = new TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 86, 98)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(commandPanel, 0);
        Grid.SetRow(caption, 1);
        grid.Children.Add(commandPanel);
        grid.Children.Add(caption);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 211, 218)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = grid
        };
    }

    private Button RibbonButton(string actionId, string glyph, string label)
    {
        var button = new Button
        {
            Width = 78,
            Height = 76,
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = glyph,
                        FontSize = 25,
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        var action = _actions.Find(actionId);
        ToolTip.SetTip(button, action.Text);
        button.Click += async (_, _) => await action.ExecuteAsync();
        return button;
    }

    private Control BuildStatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        grid.Children.Add(StatusCell(_statusText, 0, 0));
        grid.Children.Add(StatusCell(_eflText, 1, 128));
        grid.Children.Add(StatusCell(_fNumberText, 2, 116));
        grid.Children.Add(StatusCell(_apertureText, 3, 130));
        grid.Children.Add(StatusCell(_trackText, 4, 120));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(242, 242, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid
        };
    }

    private static Border StatusCell(TextBlock text, int column, double width)
    {
        text.FontSize = 11;
        text.Foreground = new SolidColorBrush(Color.FromRgb(72, 72, 74));
        var border = new Border
        {
            MinWidth = width,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
            Padding = new Thickness(9, 4),
            Child = text
        };
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
            await _connector.LoadAsync(files[0].Path.LocalPath);
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
            await _connector.SaveAsync(file.Path.LocalPath);
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
            await _connector.SaveAsync(file.Path.LocalPath);
        }
    }

    private async Task ShowAboutAsync()
    {
        var dialog = new Window
        {
            Title = "关于 Optiland Workbench",
            Width = 520,
            Height = 280,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button { Content = "关闭", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Optiland 光学工作台", FontSize = 24, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "纯 .NET/Avalonia 光学设计工作台，架构与工作流对齐 Optiland GUI。",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = ".NET 10    Avalonia 12    Managed CPU backend" },
                closeButton
            }
        };
        await dialog.ShowDialog(this);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        var commandModifier = args.KeyModifiers.HasFlag(KeyModifiers.Control)
            || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (args.Key == Key.K && commandModifier)
        {
            args.Handled = true;
            await ShowCommandPaletteAsync();
        }
    }

    private void RefreshStatus()
    {
        var optic = _connector.CurrentOptic;
        Title = $"{optic.Name} - Optiland 光学工作台";
        _statusText.Text = $"{_connector.Status}   |   {optic.SurfaceGroup.Items.Count} 个表面   |   {optic.Fields.Count} 个视场   |   {optic.Wavelengths.Count} 个波长";
        _eflText.Text = $"EFFL: {FormatMetric(optic.Paraxial.EstimateEffectiveFocalLength())}";
        _fNumberText.Text = $"F/#: {FormatMetric(optic.Paraxial.EstimateFNumber())}";
        _apertureText.Text = $"APER: {optic.Aperture.Value:0.####}";
        _trackText.Text = $"TOTR: {FormatMetric(optic.SurfaceGroup.TotalTrack)}";
    }

    private static string FormatMetric(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.####") : "∞";
    }

    private void SetTheme(string theme, bool save = true)
    {
        var normalized = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        Application.Current!.RequestedThemeVariant = normalized == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        _settings.Theme = normalized;
        if (save)
        {
            _settings.Save();
        }
    }

    private void ResetLayout()
    {
        Width = 1440;
        Height = 900;
        _panels.ResetLayout();
        SaveLayout();
    }

    private void SaveLayoutSlot(int slot)
    {
        _settings.SaveLayoutSlot(slot, _panels.CaptureLayout());
    }

    private void LoadLayoutSlot(int slot)
    {
        var layout = _settings.LoadLayoutSlot(slot);
        if (layout is not null)
        {
            _panels.ApplyLayout(layout);
            SaveLayout();
        }
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

    private static MenuItem MenuItem(AppAction action)
    {
        var item = new MenuItem { Header = action.Text };
        item.Click += async (_, _) => await action.ExecuteAsync();
        return item;
    }

    private sealed record AnalysisRibbonCommand(
        string Id,
        string Name,
        string Label,
        string Glyph,
        string Group);

}
