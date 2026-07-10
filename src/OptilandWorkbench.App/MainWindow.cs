using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.App.Panels;
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
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        _connector = new OptilandConnector(Optic.CreateDemo());

        Title = "Optiland 光学工作台";
        Width = 1280;
        Height = 820;
        MinWidth = 980;
        MinHeight = 640;
        Content = BuildShell();

        _connector.OpticLoaded += (_, _) => RefreshStatus();
        _connector.OpticChanged += (_, _) => RefreshStatus();
        RefreshStatus();
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
        var newItem = MenuItem("新建演示系统", (_, _) => _connector.NewDemo());
        var openItem = MenuItem("打开", async (_, _) => await OpenAsync());
        var saveItem = MenuItem("另存为", async (_, _) => await SaveAsAsync());
        var exitItem = MenuItem("退出", (_, _) => Close());

        var undoItem = MenuItem("撤销", (_, _) => _connector.Undo());
        var redoItem = MenuItem("重做", (_, _) => _connector.Redo());

        var fileMenu = new MenuItem
        {
            Header = "文件",
            ItemsSource = new object[] { newItem, openItem, saveItem, new Separator(), exitItem }
        };

        var editMenu = new MenuItem
        {
            Header = "编辑",
            ItemsSource = new object[] { undoItem, redoItem }
        };

        return new Menu
        {
            ItemsSource = new object[] { fileMenu, editMenu }
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
                Button("新建", (_, _) => _connector.NewDemo()),
                Button("打开", async (_, _) => await OpenAsync()),
                Button("保存", async (_, _) => await SaveAsAsync()),
                Button("撤销", (_, _) => _connector.Undo()),
                Button("重做", (_, _) => _connector.Redo())
            }
        };

        bar.Child = row;
        return bar;
    }

    private Control BuildWorkspace()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("520,*")
        };

        var leftTabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "镜头编辑器", Content = new LensEditorPanel(_connector) },
                new TabItem { Header = "系统属性", Content = new SystemPropertiesPanel(_connector) }
            }
        };

        var rightTabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "二维视图", Content = new ViewerPanel(_connector) },
                new TabItem { Header = "分析", Content = new AnalysisPanel(_connector) },
                new TabItem { Header = "优化", Content = new OptimizationPanel(_connector) }
            }
        };

        Grid.SetColumn(leftTabs, 0);
        Grid.SetColumn(rightTabs, 1);
        grid.Children.Add(leftTabs);
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

    private static MenuItem MenuItem(string header, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private static Button Button(string content, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = content, MinWidth = 72 };
        button.Click += handler;
        return button;
    }
}
