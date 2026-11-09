using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private Button? _undoButton;
    private Button? _redoButton;
    private readonly List<MenuItem> _languageMenuItems = new();

    private Control BuildShell()
    {
        _ribbonHost.Content = BuildRibbon();
        return BuildShellLayout(_ribbonHost, _panels.WorkspaceGrid, BuildStatusBar());
    }

    internal static DockPanel BuildShellLayout(Control ribbon, Control workspace, Control status)
    {
        var root = new DockPanel();
        root.Bind(Panel.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.ShellBackground));
        DockPanel.SetDock(ribbon, Avalonia.Controls.Dock.Top);
        root.Children.Add(ribbon);
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(status);
        var workspaceLayer = new Grid
        {
            Children =
            {
                workspace,
                new ThemeChromeOverlay { Role = ThemeChromeRole.Workspace }
            }
        };
        workspaceLayer.Bind(Grid.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.WorkspaceContentMargin));
        root.Children.Add(workspaceLayer);
        return root;
    }

    private Control BuildRibbon() => _application.Modes.CurrentMode == OpticalWorkbenchMode.NonSequential
        ? BuildNonSequentialRibbon()
        : BuildSequentialRibbon();

    private Control BuildSequentialRibbon()
    {
        var tabs = new TabControl
        {
            SelectedIndex = 0,
            ItemsSource = new object[]
            {
                RibbonTab("文件", BuildFileRibbonPage(includeSequentialCommands: true)),
                RibbonTab("设置", BuildRibbonPage(
                    RibbonGroup("工作模式",
                        RibbonButton("enter-non-sequential-mode", "nonsequential-mode", "非序列模式")),
                    RibbonGroup("系统",
                        RibbonButton("show-system", "system-settings", "系统选项"),
                        RibbonButton("show-lens-editor", "lens-prescription", "镜头数据"),
                        RibbonButton("show-multiconfig", "configuration-matrix", "多重结构")),
                    RibbonGroup("显示",
                        RibbonButton("display-settings", "format-font", "格式与字体")),
                    RibbonGroup("诊断",
                        RibbonButton("performance-test", "gauge", "性能检验")),
                    RibbonGroup("语言", RibbonLanguageMenuButton()))),
                RibbonTab("视图", BuildRibbonPage(
                    RibbonGroup("系统布局",
                        RibbonButton("show-viewer-2d", "layout-2d", "2D视图"),
                        RibbonButton("show-viewer-3d", "layout-3d", "3D视图"),
                        RibbonButton("show-solid-model", "solid-model", "实体模型")))),
                RibbonTab("分析", BuildSequentialAnalysisRibbonPage(_actions)),
                RibbonTab("优化", BuildRibbonPage(
                    RibbonGroup("手动调整",
                        RibbonButton("quick-focus", "optimization-focus", "快速聚焦"),
                        RibbonButton("quick-adjust", "optimization-adjust", "快速调整"),
                        RibbonButton("optimization-slider", "settings-general", "滑块"),
                        RibbonButton(
                            "show-visual-optimizer",
                            "optimization-visual",
                            "可视化优化器")),
                    RibbonGroup("自动优化",
                        RibbonButton("show-merit-editor", "merit-function-editor", "评价函数编辑器"),
                        RibbonButton(
                            "show-optimization-wizard",
                            "optimization-wizard",
                            "优化向导"),
                        RibbonButton("run-optimization", "action-run", "执行优化"),
                        RibbonButton(
                            "clear-optimization-variables",
                            "optimization-variables-clear",
                            "移除所有变量"),
                        RibbonButton(
                            "set-all-radius-variables",
                            "radius-variables",
                            "设全部半径变量"),
                        RibbonButton(
                            "set-all-thickness-variables",
                            "thickness-variables",
                            "设全部厚度变量")),
                    RibbonGroup("搜索算法",
                        RibbonButton("run-random-perturbation", "optimization-random-search", "随机扰动搜索"),
                        RibbonButton(
                            "glass-replacement-template",
                            "glass-replacement",
                            "玻璃替换模板")))),
                RibbonTab("公差", BuildRibbonPage(
                    RibbonGroup("公差分析",
                        RibbonButton("show-tolerancing", "tolerance-data-editor", "公差数据编辑器"),
                        RibbonButton("show-tolerance-wizard", "tolerance-wizard", "公差向导"),
                        RibbonButton("run-tolerancing", "tolerance-analysis", "公差分析")),
                    RibbonGroup("公差数据可视化",
                        RibbonButton("show-tolerance-data-viewer", "tolerance-data-viewer", "公差数据查看器"),
                        RibbonButton("show-tolerance-histogram", "tolerance-histogram", "直方图"),
                        RibbonButton("show-tolerance-yield", "tolerance-yield", "良率"),
                        RibbonButton("show-tolerance-report", "tolerance-report", "公差报告")))),
                RibbonTab("加工与图纸", BuildRibbonPage(
                    RibbonGroup("制造准备",
                        RibbonButton("show-manufacturability", "manufacturability-review", "可加工性评估")),
                    RibbonGroup("光学制图",
                        RibbonButton("show-optical-drawing", "optical-drawing", "光学制图")))),
                RibbonTab("数据库", BuildRibbonPage(
                    RibbonGroup("光学材料",
                        RibbonButton("show-material-library", "materials-library", "材料库"),
                        RibbonMaterialAnalysisMenuButton()),
                    RibbonGroup("镜头设计",
                        RibbonButton("show-lens-library", "lens-library", "镜头库"),
                        RibbonButton("show-stock-lens-catalog", "stock-lens-catalog", "库存镜头查看"),
                        RibbonButton("show-stock-lens-matching", "stock-lens-match", "库存镜头匹配")),
                    RibbonGroup("设计报告",
                        RibbonButton("export-markdown-report", "file-export-report", "导出报告")))),
                BuildLaboratoryRibbonTab(),
                RibbonTab("窗口", BuildRibbonPage(
                    RibbonGroup("页面窗口布局",
                        RibbonWindowLayoutMenuButton()),
                    RibbonGroup("页面",
                        RibbonButton("analysis-clone", "clone-analysis", "克隆分析"),
                        RibbonButton("toggle-page-lock", "toggle-page-lock", "切换锁定"),
                        RibbonButton("close-all-pages", "close-other-pages", "关闭其他页")),
                    RibbonGroup("布局",
                        RibbonButton("save-default-layout", "save-default-layout", "保存默认"),
                        RibbonButton("restore-default-layout", "load-default-layout", "载入默认")))),
                RibbonTab("帮助", BuildRibbonPage(
                    RibbonGroup("支持",
                        RibbonButton("show-user-guide", "user-guide", "用户指南"),
                        RibbonButton("show-operand-help", "operand-help", "操作数帮助"),
                        RibbonButton("recover-document", "recover-unsaved-project", "恢复未保存工程"),
                        RibbonButton("about", "about", "关于"))))
            }
        };
        return BuildRibbonFrame(tabs);
    }

    private Control BuildNonSequentialRibbon()
    {
        var analysisGroups = WorkbenchAnalysisCatalog.NonSequentialRibbonMenus
            .OrderBy(menu => Array.IndexOf(WorkbenchAnalysisCatalog.NonSequentialRibbonGroupOrder, menu.Group))
            .Select(menu => RibbonGroup(string.Empty, false, RibbonAnalysisMenuButton(menu)))
            .ToArray();
        var tabs = new TabControl
        {
            SelectedIndex = 0,
            ItemsSource = new object[]
            {
                RibbonTab("文件", BuildFileRibbonPage(includeSequentialCommands: false)),
                RibbonTab("非序列", BuildRibbonPage(
                    RibbonGroup("工作模式",
                        RibbonButton("enter-sequential-mode", "sequential-mode", "顺序模式")),
                    RibbonGroup("对象",
                        RibbonButton("show-non-sequential-objects", "nonsequential-objects", "对象数据")))),
                RibbonTab("设置", BuildRibbonPage(
                    RibbonGroup("语言", RibbonLanguageMenuButton()))),
                RibbonTab("视图", BuildRibbonPage(
                    RibbonGroup("非序列布局",
                        RibbonButton("show-viewer-3d", "layout-3d", "3D视图"),
                        RibbonButton("show-solid-model", "solid-model", "实体模型")))),
                RibbonTab("分析", BuildRibbonPage(analysisGroups)),
                BuildLaboratoryRibbonTab(),
                RibbonTab("窗口", BuildRibbonPage(
                    RibbonGroup("页面窗口布局",
                        RibbonWindowLayoutMenuButton()),
                    RibbonGroup("页面",
                        RibbonButton("analysis-clone", "clone-analysis", "克隆分析"),
                        RibbonButton("toggle-page-lock", "toggle-page-lock", "切换锁定"),
                        RibbonButton("close-all-pages", "close-other-pages", "关闭其他页")))),
                RibbonTab("帮助", BuildRibbonPage(
                    RibbonGroup("支持",
                        RibbonButton("show-user-guide", "user-guide", "用户指南"),
                        RibbonButton("show-operand-help", "operand-help", "操作数帮助"),
                        RibbonButton("about", "about", "关于"))))
            }
        };
        return BuildRibbonFrame(tabs);
    }

    internal static Border BuildRibbonFrame(TabControl tabs)
    {
        tabs.Background = Brushes.Transparent;
        tabs.Padding = new Thickness(0);
        tabs.Classes.Add("ribbon-tabs");
        var ribbonLayer = new Grid();
        ribbonLayer.Children.Add(new ThemeChromeOverlay { Role = ThemeChromeRole.Ribbon, RibbonTabs = tabs });
        ribbonLayer.Children.Add(tabs);
        ribbonLayer.Children.Add(new ThemeChromeOverlay
            { Role = ThemeChromeRole.Ribbon, RibbonTabs = tabs, ForegroundDecoration = true });
        var ribbon = new Border
        {
            Child = ribbonLayer,
            // Border uses its theme CornerRadius for the child clip as well:
            // the white command-page background must not cover the lower arcs.
            ClipToBounds = true
        };
        ribbon.Bind(Border.MinHeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonMinHeight));
        ribbon.Bind(Border.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonWorkspaceMargin));
        ThemeChrome.Apply(ribbon, ThemeChromeRole.Ribbon);
        ribbon.Bind(Border.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.HeaderBackground));
        return ribbon;
    }

    private Control BuildFileRibbonPage(bool includeSequentialCommands)
    {
        var groups = new List<Control>
        {
            RibbonGroup("文件",
                RibbonButton("new", "file-new", "新建"),
                RibbonButton("open", "file-open", "打开"),
                RibbonButton("save-as", "file-save", "保存"))
        };
        if (includeSequentialCommands)
        {
            groups.Add(RibbonGroup("交换与示例",
                RibbonMenuButton("import", "file-import", "导入",
                    ("import-zemax", "file-import", "Zemax ZMX")),
                RibbonMenuButton("export", "file-export", "导出",
                    ("export-cad", "file-export-cad", "CAD（STEP）")),
                RibbonSequentialExamplesButton()));
        }

        _undoButton = RibbonButton("undo", "action-undo", "撤销");
        _redoButton = RibbonButton("redo", "action-redo", "重做");
        var snapshot = _application.Documents.GetSnapshot();
        UpdateHistoryButtons(_undoButton, _redoButton, snapshot.CanUndo, snapshot.CanRedo);
        groups.Add(RibbonGroup("编辑", false, _undoButton, _redoButton));
        return BuildRibbonPage(groups.ToArray());
    }

    internal static void UpdateHistoryButtons(Button? undo, Button? redo, bool canUndo, bool canRedo)
    {
        foreach (var (button, enabled, reason) in new[]
        {
            (undo, canUndo, "当前没有可撤销的操作"),
            (redo, canRedo, "当前没有可重做的操作")
        })
        {
            if (button is null) continue;
            ControlAvailability.Set(button, enabled, reason);
            if (enabled) ToolTip.SetTip(button, AutomationProperties.GetName(button));
        }
    }

    private TabItem BuildLaboratoryRibbonTab() => RibbonTab("实验室", BuildRibbonPage(
        RibbonGroup("结构生成",
            RibbonButton("launch-initial-structure-lab", "initial-structure-design", "AI 初始结构")),
        RibbonGroup("镀膜设计",
            RibbonButton("launch-coating-design-lab", "coating-design", "光学镀膜设计")),
        RibbonGroup("装调计算",
            RibbonButton("open-optical-assembly-lab", "optical-alignment", "光学装调")),
        RibbonGroup("成像系统",
            RibbonButton("open-camera-imaging-lab", "camera-imaging", "相机成像仿真"))));

    private static TabItem RibbonTab(string title, Control content)
    {
        var tab = new TabItem
        {
            Header = new AccessText
            {
                Text = title switch
                {
                    "文件" => "文件(_F)",
                    "设置" => "设置(_S)",
                    "视图" => "视图(_V)",
                    "分析" => "分析(_A)",
                    "优化" => "优化(_O)",
                    "公差" => "公差(_T)",
                    "加工与图纸" => "加工与图纸(_M)",
                    "数据库" => "数据库(_D)",
                    "实验室" => "实验室(_L)",
                    "窗口" => "窗口(_W)",
                    "帮助" => "帮助(_H)",
                    _ => title
                }
            },
            Content = content,
            FontSize = DisplayTypography.Body,
            MinHeight = 30
        };
        tab.Bind(TabItem.PaddingProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonTabPadding));
        tab.Bind(TabItem.HeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonTabHeight));
        AutomationProperties.SetName(tab, title);
        tab.Classes.Add("ribbon-tab");
        return tab;
    }

    private static Control BuildRibbonPage(params Control[] groups)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        panel.Bind(StackPanel.SpacingProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonPageSpacing));
        panel.Bind(StackPanel.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonPageMargin));
        foreach (var group in groups)
        {
            panel.Children.Add(group);
        }

        var page = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = panel
        };
        page.BindThemeResource(ScrollViewer.BackgroundProperty, ThemeResourceBindings.RibbonPageBackground);
        return page;
    }

    private static Control RibbonGroup(string title, params Control[] commands) =>
        RibbonGroup(title, true, commands);

    private static Control RibbonGroup(string title, bool showDivider, params Control[] commands)
    {
        var commandPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        commandPanel.Bind(StackPanel.SpacingProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonGroupSpacing));
        commandPanel.Bind(StackPanel.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonGroupCommandMargin));
        foreach (var command in commands)
        {
            commandPanel.Children.Add(command);
        }

        var groupLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        groupLayout.Children.Add(commandPanel);
        if (showDivider)
        {
            var divider = new Border
            {
                Name = "RibbonGroupDivider",
                Width = 1,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            divider.Bind(Border.HeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonGroupSeparatorHeight));
            divider.Bind(Border.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonGroupSeparatorMargin));
            divider.Bind(Border.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.RibbonGroupBorder));
            Grid.SetColumn(divider, 1);
            groupLayout.Children.Add(divider);
        }
        var group = new Border
        {
            Child = groupLayout
        };
        AutomationProperties.SetName(group, title);
        group.Bind(Border.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonGroupMargin));
        group.Bind(Border.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.RibbonGroupBackground));
        group.Bind(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeResourceBindings.RibbonGroupBorder));
        return group;
    }

    private Button RibbonButton(string actionId, string iconName, string label)
    {
        var content = RibbonCommandContent(iconName, label);
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content
        };
        BindRibbonCommandLayout(button);
        BindRibbonCommandChrome(button);
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, $"ribbon-{actionId}");
        button.Classes.Add("ribbon-command");
        AttachRibbonCommandHover(button, content);
        var action = _actions.Find(actionId);
        ToolTip.SetTip(button, action.Text);
        button.Click += async (_, _) => await _actions.ExecuteAsync(action);
        return button;
    }

    internal static Control BuildSequentialAnalysisRibbonPage(ActionManager actions) => BuildRibbonPage(
        AnalysisRibbonMenus.OrderBy(menu => AnalysisRibbonGroupOrder.IndexOf(menu.Group))
            .Select((menu, index) => RibbonGroup(string.Empty, index is 1 or 3 or 7,
                BuildAnalysisMenuButton(menu, actions))).ToArray());

    private DropDownButton RibbonAnalysisMenuButton(AnalysisRibbonMenu menu) => BuildAnalysisMenuButton(menu, _actions);

    internal static DropDownButton BuildAnalysisMenuButton(AnalysisRibbonMenu menu, ActionManager actions)
    {
        var flyout = new MenuFlyout();
        foreach (var commandId in menu.CommandIds)
        {
            if (string.Equals(commandId, "-", StringComparison.Ordinal))
            {
                flyout.Items.Add(new Separator());
                continue;
            }

            var command = WorkbenchAnalysisCatalog.AllRibbonCommands.First(candidate =>
                string.Equals(candidate.Id, commandId, StringComparison.Ordinal));
            var action = actions.Find(command.Id);
            var header = new LocalIconLabel(command.IconName, command.Label, 20);
            var item = new MenuItem
            {
                Header = header,
                MinWidth = 190,
                Padding = new Thickness(10, 8)
            };
            AutomationProperties.SetName(item, command.Label);
            AutomationProperties.SetAutomationId(item, $"ribbon-{command.Id}");
            item.Classes.Add("ribbon-menu-item");
            AttachRibbonMenuItemHover(item, header);
            item.Click += async (_, _) => await actions.ExecuteAsync(action);
            flyout.Items.Add(item);
        }

        var content = RibbonAnalysisCommandContent(menu.IconName, menu.Label);
        var button = new DropDownButton
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
            Content = content
        };
        BindRibbonCommandLayout(button);
        BindRibbonCommandChrome(button);
        AutomationProperties.SetName(button, menu.Label);
        AutomationProperties.SetAutomationId(button, $"ribbon-menu-{menu.IconName}");
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        ToolTip.SetTip(button, $"选择{menu.Label}分析类型");
        return button;
    }

    private DropDownButton RibbonWindowLayoutMenuButton() => RibbonMenuButton(
        "window-layout", "window-layout", "窗口布局",
        ("analysis-dock-all", "layout-dock", "保留分栏停靠"),
        ("dock-single-pane", "layout-merge", "合并单窗格"),
        ("analysis-float-all", "layout-float", "全部独立浮动"),
        ("analysis-tile-all", "layout-tile", "平铺全部"),
        ("analysis-cascade-all", "layout-cascade", "层叠全部"));

    private DropDownButton RibbonMaterialAnalysisMenuButton() => RibbonMenuButton(
        "material-analysis", "material-dispersion-chart", "材料分析",
        ("show-material-dispersion-diagram", "material-dispersion-chart", "色散图"),
        ("show-material-glass-map", "material-glass-map", "玻璃图"),
        ("show-material-athermal-map", "material-athermal-map", "无热化玻璃图"),
        ("show-material-transmission", "material-transmission", "内部透过率 vs. 波长"),
        ("show-material-dispersion-wavelength", "material-dispersion-wavelength", "色散 vs. 波长"));

    private DropDownButton? _languageMenuButton;

    private DropDownButton RibbonLanguageMenuButton()
    {
        var flyout = new MenuFlyout();
        _languageMenuItems.Clear();
        foreach (var (code, displayName) in UiLocalization.SupportedLanguages)
        {
            var item = new MenuItem
            {
                Header = displayName,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = string.Equals(UiLocalization.CurrentLanguage, code, StringComparison.Ordinal),
                MinWidth = 150,
                Padding = new Thickness(10, 8)
            };
            AutomationProperties.SetName(item, displayName);
            AutomationProperties.SetAutomationId(item, $"language-{code}");
            item.Click += (_, _) => ChangeLanguage(code);
            _languageMenuItems.Add(item);
            flyout.Items.Add(item);
        }

        var content = RibbonDropDownCommandContent("settings-language", "语言");
        var button = new DropDownButton
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
            Content = content
        };
        BindRibbonCommandLayout(button);
        BindRibbonCommandChrome(button);
        AutomationProperties.SetName(button, "语言");
        AutomationProperties.SetAutomationId(button, "ribbon-language");
        ToolTip.SetTip(button, "选择界面语言");
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        _languageMenuButton = button;
        RefreshLanguageMenu();
        return button;
    }

    private void RefreshLanguageMenu()
    {
        var locked = ThemeRegistry.FromSettings(_settings.Theme).FixedLanguage is not null;
        if (_languageMenuButton is { } button)
        {
            button.IsEnabled = !locked;
            button.Classes.Set(ThemeRegistry.LockedSettingClass, locked);
            if (button.Content is Grid content)
                foreach (var arrow in content.Children.OfType<Polygon>())
                    arrow.Bind(Shape.FillProperty, new DynamicResourceExtension(locked
                        ? ThemeResourceBindings.LockedSettingForeground : ThemeResourceBindings.MutedText));
            ToolTip.SetTip(button, locked
                ? "Isekai uses English. Change the theme to choose another language."
                : UiLocalization.Text("选择界面语言"));
        }
        foreach (var item in _languageMenuItems)
        {
            item.IsEnabled = !locked;
            item.IsChecked = string.Equals(AutomationProperties.GetAutomationId(item),
                $"language-{UiLocalization.CurrentLanguage}", StringComparison.Ordinal);
        }
    }

    private void ChangeLanguage(string language)
    {
        if (ThemeRegistry.FromSettings(_settings.Theme).FixedLanguage is not null) return;
        var normalized = UiLocalization.NormalizeLanguage(language);
        if (string.Equals(_settings.Language, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Language = normalized;
        UiLocalization.Apply(normalized);
        RefreshStatus();
        RefreshLanguageMenu();

        if (!_settings.TrySave(out var errorMessage))
        {
            _statusText.Text = UiLocalization.Text("保存设置失败：") + errorMessage;
        }
        else
        {
            _statusText.Text = UiLocalization.Text("设置已保存");
        }
    }

    private DropDownButton RibbonMenuButton(string menuId, string menuIcon, string menuLabel,
        params (string ActionId, string IconName, string Label)[] commands)
    {
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
            AutomationProperties.SetName(item, label);
            AutomationProperties.SetAutomationId(item, $"ribbon-{actionId}");
            item.Classes.Add("ribbon-menu-item");
            AttachRibbonMenuItemHover(item, header);
            item.Click += async (_, _) => await _actions.ExecuteAsync(action);
            flyout.Items.Add(item);
        }

        var content = RibbonDropDownCommandContent(menuIcon, menuLabel);
        var button = new DropDownButton
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
            Content = content
        };
        BindRibbonCommandLayout(button);
        BindRibbonCommandChrome(button);
        AutomationProperties.SetName(button, menuLabel);
        AutomationProperties.SetAutomationId(button, $"ribbon-menu-{menuId}");
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        ToolTip.SetTip(button, menuLabel);
        return button;
    }

    private static Control RibbonCommandContent(string iconName, string label)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new LocalIcon
        {
            IconName = iconName,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindRibbonCommandIconLayout(icon);
        var text = new TextBlock
        {
            Text = label,
            FontSize = DisplayTypography.Body,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        BindRibbonCommandContentLayout(grid, text);
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }

    private static Control RibbonDropDownCommandContent(string iconName, string label)
    {
        var grid = (Grid)RibbonCommandContent(iconName, label);
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
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
            VerticalAlignment = VerticalAlignment.Center
        };
        arrow.Bind(Shape.FillProperty, new DynamicResourceExtension("OpticalSystemDesignMutedTextBrush"));

        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);
        return grid;
    }

    private static Control RibbonAnalysisCommandContent(string iconName, string label)
    {
        var grid = (Grid)RibbonDropDownCommandContent(iconName, label);
        if (!AnalysisCategoryIcon.Supports(iconName)) return grid;
        var previous = grid.Children.OfType<LocalIcon>().Single();
        grid.Children.Remove(previous);
        grid.Children.Insert(0, new AnalysisCategoryIcon(iconName)
        {
            Width = DisplayTypography.Scale(22), Height = DisplayTypography.Scale(22),
            VerticalAlignment = VerticalAlignment.Center
        });
        return grid;
    }

    private static void BindRibbonCommandLayout(Button button)
    {
        button.Bind(Button.MinWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandMinWidth));
        button.Bind(Button.WidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandWidth));
        button.Bind(Button.MinHeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandMinHeight));
        button.Bind(Button.MarginProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandMargin));
        button.Bind(Button.PaddingProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandPadding));
        button.Bind(Button.BorderThicknessProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandBorderThickness));
    }

    private static void BindRibbonCommandContentLayout(Grid grid, TextBlock text)
    {
        grid.Bind(Grid.ColumnSpacingProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandContentSpacing));
        grid.Bind(Grid.MinWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandContentMinWidth));
        grid.Bind(Grid.MinHeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandContentMinHeight));
        text.Bind(TextBlock.MinWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandContentMinWidth));
        text.Bind(TextBlock.MaxWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandTextMaxWidth));
    }

    private static void BindRibbonCommandIconLayout(LocalIcon icon)
    {
        icon.Bind(LocalIcon.WidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandIconSize));
        icon.Bind(LocalIcon.HeightProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandIconSize));
        icon.Bind(LocalIcon.StrokeWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.RibbonCommandStrokeWidth));
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
        if (text is null)
        {
            return;
        }

        // Template state selectors own hover/pressed/open; never replace resource bindings in pointer events.
        text.Classes.Add("ribbon-command-label");
        icon?.Classes.Add("ribbon-command-icon");
    }

    private static void BindRibbonCommandChrome(Button button)
    {
        button.Bind(
            Button.BackgroundProperty,
            new DynamicResourceExtension(ThemeResourceBindings.RibbonCommandBackground));
        button.Bind(
            Button.BorderBrushProperty,
            new DynamicResourceExtension(ThemeResourceBindings.RibbonCommandBorder));
    }

    private static void AttachRibbonMenuItemHover(MenuItem item, LocalIconLabel header)
    {
        var icon = header.Children.OfType<LocalIcon>().FirstOrDefault();
        var text = header.Children.OfType<TextBlock>().FirstOrDefault();
        if (icon is null || text is null)
        {
            return;
        }

        text.Classes.Add("ribbon-menu-label");
        icon.Classes.Add("ribbon-menu-icon");
    }

    private Control BuildStatusBar() => BuildStatusBarFrame(_statusText, _eflText, _fNumberText, _apertureText, _trackText);

    internal static Border BuildStatusBarFrame(TextBlock statusText, TextBlock eflText, TextBlock fNumberText, TextBlock apertureText, TextBlock trackText)
    {
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        grid.Children.Add(StatusCell(statusText, 0, 0));
        grid.Children.Add(StatusCell(eflText, 1, 128));
        grid.Children.Add(StatusCell(fNumberText, 2, 128));
        grid.Children.Add(StatusCell(apertureText, 3, 128));
        grid.Children.Add(StatusCell(trackText, 4, 128));

        var status = new Border
        {
            Child = grid
        };
        ThemeChrome.Apply(status, ThemeChromeRole.StatusBar, shadow: false);
        status.Bind(Border.BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.StatusBarBackground));
        return status;
    }

    private static Border StatusCell(TextBlock text, int column, double width)
    {
        if (column > 0) text.Classes.Add(ProductFonts.NumericClass);
        text.FontSize = DisplayTypography.CompactBody;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextSecondary));
        var border = new Border
        {
            MinWidth = width,
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
            Padding = new Thickness(10, 3),
            Child = text
        };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("OpticalSystemDesignBorderBrush"));
        if (column > 0) border.Bind(Border.MinWidthProperty, new DynamicResourceExtension(ThemeLayoutResources.StatusMetricMinWidth));
        Grid.SetColumn(border, column);
        return border;
    }
}
