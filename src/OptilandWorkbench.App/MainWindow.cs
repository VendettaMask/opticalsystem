using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Panels;
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
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private Grid? _workspaceGrid;
    private TabControl? _leftTabs;
    private TabControl? _rightTabs;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _connector = new OptilandConnector(Optic.CreateDemo());
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
        KeyDown += async (_, args) =>
        {
            if (args.Key == Key.P && args.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                args.Handled = true;
                await ShowCommandPaletteAsync();
            }
        };
        RefreshStatus();
    }

    private void RegisterActions()
    {
        _actions.Register("new-demo", "新建演示系统", "文件", () => _connector.NewDemo());
        _actions.Register("open", "打开光学系统", "文件", OpenAsync);
        _actions.Register("save-as", "另存为", "文件", SaveAsAsync);
        _actions.Register("exit", "退出", "文件", Close);
        _actions.Register("undo", "撤销", "编辑", () => _connector.Undo());
        _actions.Register("redo", "重做", "编辑", () => _connector.Redo());
        _actions.Register("show-lens-editor", "显示镜头编辑器", "面板", () => SelectPanel(leftIndex: 0));
        _actions.Register("show-system", "显示系统属性", "面板", () => SelectPanel(leftIndex: 1));
        _actions.Register("show-viewer", "显示系统视图", "面板", () => SelectPanel(rightIndex: 0));
        _actions.Register("show-analysis", "显示分析面板", "面板", () => SelectPanel(rightIndex: 1));
        _actions.Register("show-optimization", "显示优化面板", "面板", () => SelectPanel(rightIndex: 2));
        _actions.Register("show-tolerancing", "显示公差面板", "面板", () => SelectPanel(rightIndex: 3));
        _actions.Register("show-multiconfig", "显示多配置面板", "面板", () => SelectPanel(rightIndex: 4));
        _actions.Register("theme-light", "浅色主题", "视图", () => SetTheme("Light"));
        _actions.Register("theme-dark", "深色主题", "视图", () => SetTheme("Dark"));
        _actions.Register("reset-layout", "恢复默认布局", "视图", ResetLayout);
        _actions.Register("command-palette", "命令面板", "工具", ShowCommandPaletteAsync);
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

        root.Children.Add(BuildWorkspace());
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
                MenuItem(_actions.Find("open")),
                MenuItem(_actions.Find("save-as")),
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
                MenuItem(_actions.Find("theme-dark")),
                MenuItem(_actions.Find("reset-layout"))
            }
        };

        var toolsMenu = new MenuItem
        {
            Header = "工具",
            ItemsSource = new object[]
            {
                MenuItem(_actions.Find("command-palette"))
            }
        };

        return new Menu
        {
            ItemsSource = new object[] { fileMenu, editMenu, viewMenu, toolsMenu }
        };
    }

    private Control BuildToolbar()
    {
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 242, 247)),
            Padding = new Avalonia.Thickness(10, 8)
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button(_actions.Find("new-demo"), "新建"),
                Button(_actions.Find("open"), "打开"),
                Button(_actions.Find("save-as"), "保存"),
                Button(_actions.Find("undo"), "撤销"),
                Button(_actions.Find("redo"), "重做"),
                Button(_actions.Find("command-palette"), "命令"),
                Button(_actions.Find("theme-light"), "浅色"),
                Button(_actions.Find("theme-dark"), "深色")
            }
        };

        bar.Child = row;
        return bar;
    }

    private Control BuildWorkspace()
    {
        var leftPaneWidth = Math.Clamp(_settings.LeftPaneWidth, 360, 900);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{leftPaneWidth},6,*")
        };
        _workspaceGrid = grid;

        var leftTabs = new TabControl
        {
            SelectedIndex = Math.Clamp(_settings.LeftTabIndex, 0, 1),
            ItemsSource = new object[]
            {
                new TabItem { Header = "镜头编辑器", Content = new LensEditorPanel(_connector) },
                new TabItem { Header = "系统属性", Content = new SystemPropertiesPanel(_connector) }
            }
        };
        _leftTabs = leftTabs;
        leftTabs.SelectionChanged += (_, _) =>
        {
            _settings.LeftTabIndex = Math.Max(0, leftTabs.SelectedIndex);
            _settings.Save();
        };

        var splitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(210, 218, 228))
        };

        var rightTabs = new TabControl
        {
            SelectedIndex = Math.Clamp(_settings.RightTabIndex, 0, 4),
            ItemsSource = new object[]
            {
                new TabItem { Header = "系统视图", Content = new ViewerPanel(_connector) },
                new TabItem { Header = "分析", Content = new AnalysisPanel(_connector) },
                new TabItem { Header = "优化", Content = new OptimizationPanel(_connector) },
                new TabItem { Header = "公差", Content = new TolerancingPanel(_connector) },
                new TabItem { Header = "多配置", Content = new MultiConfigurationPanel(_connector) }
            }
        };
        _rightTabs = rightTabs;
        rightTabs.SelectionChanged += (_, _) =>
        {
            _settings.RightTabIndex = Math.Max(0, rightTabs.SelectedIndex);
            _settings.Save();
        };

        Grid.SetColumn(leftTabs, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(rightTabs, 2);
        grid.Children.Add(leftTabs);
        grid.Children.Add(splitter);
        grid.Children.Add(rightTabs);
        return grid;
    }

    private Control BuildStatusBar()
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 235)),
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Padding = new Avalonia.Thickness(10, 6),
            Child = _statusText
        };
    }

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开光学系统",
            AllowMultiple = false,
            FileTypeFilter = new[] { NativeOpticFileType, CommercialOpticFileType, PlainSequentialFileType }
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
            FileTypeChoices = new[] { NativeOpticFileType, CommercialOpticFileType, PlainSequentialFileType }
        });

        if (file is not null)
        {
            await _connector.SaveAsync(file.Path.LocalPath);
        }
    }

    private void RefreshStatus()
    {
        _statusText.Text = $"{_connector.CurrentOptic.Name}    {_connector.Status}    撤销: {(_connector.CanUndo ? "可用" : "不可用")}    重做: {(_connector.CanRedo ? "可用" : "不可用")}";
    }

    private void SelectPanel(int? leftIndex = null, int? rightIndex = null)
    {
        if (leftIndex.HasValue && _leftTabs is not null)
        {
            _leftTabs.SelectedIndex = leftIndex.Value;
        }

        if (rightIndex.HasValue && _rightTabs is not null)
        {
            _rightTabs.SelectedIndex = rightIndex.Value;
        }
    }

    private void SetTheme(string theme, bool save = true)
    {
        var normalized = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        Application.Current!.RequestedThemeVariant = normalized == "Dark"
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
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
        SelectPanel(leftIndex: 0, rightIndex: 0);
        if (_workspaceGrid?.ColumnDefinitions.Count > 0)
        {
            _workspaceGrid.ColumnDefinitions[0].Width = new GridLength(520);
        }

        SaveLayout();
    }

    private async Task ShowCommandPaletteAsync()
    {
        var palette = new CommandPaletteWindow(_actions);
        await palette.ShowDialog(this);
    }

    private void SaveLayout()
    {
        _settings.WindowWidth = Math.Max(MinWidth, Width);
        _settings.WindowHeight = Math.Max(MinHeight, Height);
        if (_workspaceGrid?.ColumnDefinitions.Count > 0)
        {
            _settings.LeftPaneWidth = Math.Clamp(_workspaceGrid.ColumnDefinitions[0].ActualWidth, 360, 900);
        }

        if (_leftTabs is not null)
        {
            _settings.LeftTabIndex = Math.Max(0, _leftTabs.SelectedIndex);
        }

        if (_rightTabs is not null)
        {
            _settings.RightTabIndex = Math.Max(0, _rightTabs.SelectedIndex);
        }

        _settings.Save();
    }

    private static MenuItem MenuItem(AppAction action)
    {
        var item = new MenuItem { Header = action.Text };
        item.Click += async (_, _) => await action.ExecuteAsync();
        return item;
    }

    private static Button Button(AppAction action, string content)
    {
        var button = new Button { Content = content, MinWidth = 72 };
        button.Click += async (_, _) => await action.ExecuteAsync();
        return button;
    }
}
