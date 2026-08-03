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
using Avalonia.Controls.Shapes;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
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

    private Control BuildRibbon()
    {
        var analysisGroups = AnalysisRibbonMenus
            .OrderBy(menu => AnalysisRibbonGroupOrder.IndexOf(menu.Group))
            .Select(menu => RibbonGroup(string.Empty, false, RibbonAnalysisMenuButton(menu)))
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
                        RibbonButton("export-python-json", "upload", "导出"),
                        RibbonButton("export-cad", "box", "导出 CAD")),
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
                    RibbonGroup("手动调整",
                        RibbonButton("quick-focus", "focus", "快速聚焦"),
                        RibbonButton("quick-adjust", "scan-search", "快速调整"),
                        RibbonButton("optimization-slider", "sliders-horizontal", "滑块"),
                        RibbonButton(
                            "show-visual-optimizer",
                            "chart-no-axes-combined",
                            "可视化优化器")),
                    RibbonGroup("自动优化",
                        RibbonButton("show-merit-editor", "list-tree", "评价函数编辑器"),
                        RibbonButton(
                            "show-optimization-wizard",
                            "wand-sparkles",
                            "优化向导"),
                        RibbonButton("run-optimization", "play", "执行优化"),
                        RibbonButton(
                            "clear-optimization-variables",
                            "trash-2",
                            "移除所有变量"),
                        RibbonButton(
                            "set-all-radius-variables",
                            "circle-dot",
                            "设全部半径变量"),
                        RibbonButton(
                            "set-all-thickness-variables",
                            "move-vertical",
                            "设全部厚度变量")),
                    RibbonGroup("全局优化",
                        RibbonButton("run-global-optimization", "globe", "全局优化"),
                        RibbonButton("run-hammer-optimization", "hammer", "锤形优化"),
                        RibbonButton(
                            "glass-replacement-template",
                            "replace",
                            "玻璃替换模板")))),
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
                        RibbonMaterialAnalysisMenuButton(),
                        RibbonButton("show-glass-catalog", "gem", "玻璃")),
                    RibbonGroup("镜头设计",
                        RibbonButton("show-lens-library", "telescope", "镜头库")))),
                RibbonTab("窗口", BuildRibbonPage(
                    RibbonGroup("页面窗口布局",
                        RibbonButton("analysis-dock-all", "panel-top", "保留分栏停靠"),
                        RibbonButton("dock-single-pane", "panels-top-left", "合并单窗格"),
                        RibbonButton("analysis-float-all", "picture-in-picture-2", "全部独立浮动"),
                        RibbonButton("analysis-tile-all", "grid-2x2", "平铺全部"),
                        RibbonButton("analysis-cascade-all", "rows-3", "层叠全部")),
                    RibbonGroup("页面",
                        RibbonButton("analysis-clone", "copy", "克隆分析"),
                        RibbonButton("toggle-page-lock", "lock-keyhole", "切换锁定"),
                        RibbonButton("close-all-pages", "x", "关闭其他页")),
                    RibbonGroup("布局",
                        RibbonButton("save-default-layout", "save", "保存默认"),
                        RibbonButton("restore-default-layout", "rotate-ccw", "载入默认")))),
                RibbonTab("帮助", BuildRibbonPage(
                    RibbonGroup("支持",
                        RibbonButton("about", "circle-question-mark", "关于"))))
            }
        };
        tabs.Background = Brushes.Transparent;
        var ribbonLayer = new Grid();
        ribbonLayer.Children.Add(new IsekaiRibbonChrome());
        ribbonLayer.Children.Add(tabs);
        var ribbon = new Border
        {
            MinHeight = 126,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BoxShadow = BoxShadows.Parse("0 3 8 0 #14000000"),
            Child = ribbonLayer
        };
        ribbon.Bind(Border.BackgroundProperty, new DynamicResourceExtension("OptilandSurfaceBrush"));
        ribbon.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        return ribbon;
    }

    private static TabItem RibbonTab(string title, Control content)
    {
        var tab = new TabItem
        {
            Header = title,
            Content = content,
            FontSize = 13,
            Padding = new Thickness(14, 7)
        };
        tab.Classes.Add("ribbon-tab");
        return tab;
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

    private static Control RibbonGroup(string title, params Control[] commands) =>
        RibbonGroup(title, true, commands);

    private static Control RibbonGroup(string title, bool showDivider, params Control[] commands)
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

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,18") };
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
            BorderThickness = showDivider
                ? new Thickness(0, 0, 1, 0)
                : new Thickness(0),
            Child = grid
        };
        group.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OptilandBorderBrush"));
        return group;
    }

    private Button RibbonButton(string actionId, string iconName, string label)
    {
        var content = RibbonCommandContent(iconName, label);
        var button = new Button
        {
            MinWidth = 78,
            MinHeight = 66,
            Margin = new Thickness(1, 0, 1, 2),
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content
        };
        button.Classes.Add("ribbon-command");
        AttachRibbonCommandHover(button, content);
        var action = _actions.Find(actionId);
        button.Click += async (_, _) => await _actions.ExecuteAsync(action);
        return button;
    }

    private DropDownButton RibbonAnalysisMenuButton(AnalysisRibbonMenu menu)
    {
        var flyout = new MenuFlyout();
        foreach (var commandId in menu.CommandIds)
        {
            if (string.Equals(commandId, "-", StringComparison.Ordinal))
            {
                flyout.Items.Add(new Separator());
                continue;
            }

            var command = AnalysisRibbonCommands.First(candidate =>
                string.Equals(candidate.Id, commandId, StringComparison.Ordinal));
            var action = _actions.Find(command.Id);
            var header = new LocalIconLabel(command.IconName, command.Label, 20);
            var item = new MenuItem
            {
                Header = header,
                MinWidth = 190,
                Padding = new Thickness(10, 8)
            };
            item.Classes.Add("ribbon-menu-item");
            AttachRibbonMenuItemHover(item, header);
            item.Click += async (_, _) => await _actions.ExecuteAsync(action);
            flyout.Items.Add(item);
        }

        var content = RibbonDropDownCommandContent(menu.IconName, menu.Label);
        var button = new DropDownButton
        {
            MinWidth = 78,
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
            Content = content
        };
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        ToolTip.SetTip(button, $"选择{menu.Label}分析类型");
        return button;
    }

    private DropDownButton RibbonMaterialAnalysisMenuButton()
    {
        var commands = new[]
        {
            ("show-material-dispersion-diagram", "chart-scatter", "色散图"),
            ("show-material-glass-map", "gem", "玻璃图"),
            ("show-material-athermal-map", "thermometer-sun", "无热化玻璃图"),
            ("show-material-transmission", "arrow-right-left", "内部透过率 vs. 波长"),
            ("show-material-dispersion-wavelength", "chart-line", "色散 vs. 波长")
        };
        var flyout = new MenuFlyout();
        foreach (var (actionId, iconName, label) in commands)
        {
            var action = _actions.Find(actionId);
            var header = new LocalIconLabel(iconName, label, 20);
            var item = new MenuItem
            {
                Header = header,
                MinWidth = 230,
                Padding = new Thickness(10, 8)
            };
            item.Classes.Add("ribbon-menu-item");
            AttachRibbonMenuItemHover(item, header);
            item.Click += async (_, _) => await _actions.ExecuteAsync(action);
            flyout.Items.Add(item);
        }

        var content = RibbonDropDownCommandContent("chart-scatter", "材料分析");
        var button = new DropDownButton
        {
            MinWidth = 78,
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
            Content = content
        };
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        ToolTip.SetTip(button, "选择材料分析类型");
        return button;
    }

    private static Control RibbonCommandContent(string iconName, string label)
    {
        var grid = new Grid
        {
            MinWidth = 66,
            MinHeight = 52,
            RowDefinitions = new RowDefinitions("29,Auto"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new LocalIcon
        {
            IconName = iconName,
            Width = 26,
            Height = 26,
            StrokeWidth = 1.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextAccent);
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            MinWidth = 66,
            MaxWidth = 132,
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

    private static Control RibbonDropDownCommandContent(string iconName, string label)
    {
        var grid = new Grid
        {
            MinWidth = 66,
            MinHeight = 52,
            RowDefinitions = new RowDefinitions("27,Auto,7"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new LocalIcon
        {
            IconName = iconName,
            Width = 26,
            Height = 26,
            StrokeWidth = 1.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextAccent);
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            MinWidth = 66,
            MaxWidth = 132,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        var arrow = new Polygon
        {
            Width = 6,
            Height = 4,
            Opacity = 0.72,
            Points = new Points
            {
                new Point(0, 0),
                new Point(6, 0),
                new Point(3, 4)
            },
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        arrow.Bind(Shape.FillProperty, new DynamicResourceExtension("OptilandMutedTextBrush"));

        Grid.SetRow(icon, 0);
        Grid.SetRow(text, 1);
        Grid.SetRow(arrow, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(arrow);
        return grid;
    }

    private static void AttachRibbonCommandHover(Button button, Control content)
    {
        if (content is not Grid grid)
        {
            return;
        }

        var icon = grid.Children.OfType<LocalIcon>().FirstOrDefault();
        var text = grid.Children.OfType<TextBlock>().FirstOrDefault();
        var arrow = grid.Children.OfType<Polygon>().FirstOrDefault();
        if (icon is null || text is null)
        {
            return;
        }

        button.PointerEntered += (_, _) =>
        {
            var accent = ThemeBrush(button, "AccentFillColorDefaultBrush");
            button.Background = ThemeBrush(button, ThemeResourceBindings.RibbonHover);
            button.BorderBrush = ThemeBrush(button, ThemeResourceBindings.RibbonHoverBorder);
            icon.StrokeWidth = 2.05;
            text.Foreground = accent;
            if (arrow is not null)
            {
                arrow.Fill = accent;
                arrow.Opacity = 1;
            }
        };
        button.PointerExited += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            icon.StrokeWidth = 1.8;
            text.ClearValue(TextBlock.ForegroundProperty);
            if (arrow is not null)
            {
                arrow.Fill = ThemeBrush(button, "OptilandMutedTextBrush");
                arrow.Opacity = 0.72;
            }
        };
    }

    private static void AttachRibbonMenuItemHover(MenuItem item, LocalIconLabel header)
    {
        var icon = header.Children.OfType<LocalIcon>().FirstOrDefault();
        var text = header.Children.OfType<TextBlock>().FirstOrDefault();
        if (icon is null || text is null)
        {
            return;
        }

        item.PointerEntered += (_, _) =>
        {
            var accent = ThemeBrush(item, "AccentFillColorDefaultBrush");
            icon.Stroke = accent;
            icon.StrokeWidth = 2.05;
            text.Foreground = accent;
        };
        item.PointerExited += (_, _) =>
        {
            icon.Stroke = ThemeBrush(item, ThemeResourceBindings.MutedText);
            icon.StrokeWidth = 2;
            text.ClearValue(TextBlock.ForegroundProperty);
        };
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
}
