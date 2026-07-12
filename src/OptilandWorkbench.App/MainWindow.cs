using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using OptilandWorkbench.App.Connectors;
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

    private readonly OptilandConnector _connector;
    private readonly AppSettings _settings;
    private readonly ActionManager _actions = new();
    private readonly PanelManager _panels;
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _connector = new OptilandConnector(CreateInitialOptic());
        _panels = new PanelManager(_connector, _settings);
        RegisterActions();

        Title = "Optiland 光学工作台";
        Width = Math.Clamp(_settings.WindowWidth, 980, 4096);
        Height = Math.Clamp(_settings.WindowHeight, 640, 2160);
        MinWidth = 980;
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
        _actions.Register("command-palette", "命令面板", "工具", ShowCommandPaletteAsync);
        _actions.Register("about", "关于 Optiland Workbench", "帮助", ShowAboutAsync);
    }

    private Control BuildShell()
    {
        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

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
                MenuItem(_actions.Find("new")),
                MenuItem(_actions.Find("new-demo")),
                MenuItem(_actions.Find("new-tessar")),
                new Separator(),
                MenuItem(_actions.Find("open")),
                MenuItem(_actions.Find("save-as")),
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
        var viewMenu = new MenuItem
        {
            Header = "视图",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("show-lens-editor")),
                MenuItem(_actions.Find("show-system")),
                new Separator(),
                MenuItem(_actions.Find("show-viewer")),
                MenuItem(_actions.Find("show-analysis")),
                MenuItem(_actions.Find("show-optimization")),
                MenuItem(_actions.Find("show-tolerancing")),
                MenuItem(_actions.Find("show-multiconfig")),
                new Separator(),
                MenuItem(_actions.Find("theme-light")),
                MenuItem(_actions.Find("theme-dark"))
            }
        };
        var toolsMenu = new MenuItem
        {
            Header = "工具",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("command-palette")),
                new Separator(),
                MenuItem(_actions.Find("save-layout-1")),
                MenuItem(_actions.Find("save-layout-2")),
                MenuItem(_actions.Find("load-layout-1")),
                MenuItem(_actions.Find("load-layout-2")),
                MenuItem(_actions.Find("reset-layout"))
            }
        };
        var helpMenu = new MenuItem
        {
            Header = "帮助",
            ItemsSource = new object[] { MenuItem(_actions.Find("about")) }
        };
        return new Menu { ItemsSource = new object[] { fileMenu, editMenu, viewMenu, toolsMenu, helpMenu } };
    }

    private Control BuildToolbar()
    {
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 242, 247)),
            Padding = new Thickness(10, 8)
        };
        bar.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button(_actions.Find("new"), "新建"),
                Button(_actions.Find("open"), "打开"),
                Button(_actions.Find("save-as"), "保存"),
                Button(_actions.Find("undo"), "撤销"),
                Button(_actions.Find("redo"), "重做"),
                Button(_actions.Find("command-palette"), "命令"),
                Button(_actions.Find("load-layout-1"), "1", 40),
                Button(_actions.Find("load-layout-2"), "2", 40)
            }
        };
        return bar;
    }

    private Control BuildStatusBar()
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 235)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6),
            Child = _statusText
        };
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
        _statusText.Text = $"{_connector.CurrentOptic.Name}    {_connector.Status}    撤销: {(_connector.CanUndo ? "可用" : "不可用")}    重做: {(_connector.CanRedo ? "可用" : "不可用")}";
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
        Width = 1280;
        Height = 820;
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

    private static Button Button(AppAction action, string content, double minWidth = 72)
    {
        var button = new Button { Content = content, MinWidth = minWidth };
        button.Click += async (_, _) => await action.ExecuteAsync();
        return button;
    }
}
