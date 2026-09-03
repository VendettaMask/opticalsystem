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

public sealed partial class MainWindow
{
    private void RegisterActions()
    {
        _actions.Register("new", "新建空白系统", "文件", () => SwitchDocumentAsync(_application.Documents.NewBlank));
        _actions.Register("new-demo", "新建 Cooke 三片式样例", "文件", () => SwitchDocumentAsync(_application.Documents.NewCooke));
        _actions.Register("new-tessar", "新建 Tessar F/4.5 四片式样例", "文件", () => SwitchDocumentAsync(_application.Documents.NewTessar));
        _actions.Register("open", "打开光学系统", "文件", OpenAsync);
        _actions.Register("import-zemax", "导入 Zemax ZMX", "文件", ImportZemaxAsync);
        _actions.Register("save-as", "保存项目", "文件", SaveProjectAsync);
        _actions.Register("export-cad", "导出 CAD（STEP）", "文件", ExportCadAsync);
        _actions.Register("exit", "退出", "文件", Close);
        _actions.Register("undo", "撤销", "编辑", () => _application.Documents.Undo());
        _actions.Register("redo", "重做", "编辑", () => _application.Documents.Redo());
        _actions.Register("show-lens-editor", "显示镜头编辑器", "面板", () => _panels.Show(WorkspacePanelId.LensEditor));
        _actions.Register(
            "show-non-sequential-objects",
            "显示非序列对象数据",
            "面板",
            () => _panels.Show(WorkspacePanelId.NonSequentialObjectEditor));
        _actions.Register(
            "enter-sequential-mode",
            "进入顺序模式",
            "模式",
            () => SwitchWorkbenchModeAsync(OpticalWorkbenchMode.Sequential));
        _actions.Register(
            "enter-non-sequential-mode",
            "进入非序列模式",
            "模式",
            () => SwitchWorkbenchModeAsync(OpticalWorkbenchMode.NonSequential));
        _actions.Register("show-system", "显示系统属性", "面板", () => _panels.Show(WorkspacePanelId.SystemProperties));
        _actions.Register("display-settings", "显示格式设置", "设置", ShowDisplaySettingsAsync);
        _actions.Register("show-viewer", "显示系统视图", "面板", () => _panels.Show(WorkspacePanelId.Viewer));
        _actions.Register("show-viewer-2d", "显示二维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.TwoDimensional));
        _actions.Register("show-viewer-3d", "显示三维布局", "视图", () => _panels.ShowViewer(OpticSceneViewMode.ThreeDimensional));
        _actions.Register("show-solid-model", "显示实体模型", "视图", _panels.ShowSolidModel);
        _actions.Register("show-material-library", "打开材料库", "数据库", _panels.ShowMaterialLibrary);
        _actions.Register("show-lens-library", "打开镜头库", "数据库", _panels.ShowLensLibrary);
        _actions.Register(
            "show-stock-lens-catalog",
            "打开库存镜头查看",
            "数据库",
            _panels.ShowStockLensCatalog);
        _actions.Register(
            "show-stock-lens-matching",
            "打开库存镜头匹配",
            "数据库",
            _panels.ShowStockLensMatching);
        _actions.Register("show-glass-catalog", "打开玻璃目录", "数据库", _panels.ShowGlassCatalog);
        _actions.Register(
            "show-material-dispersion-diagram",
            "色散图",
            "数据库",
            () => _panels.ShowMaterialAnalysis(MaterialAnalysisKind.DispersionDiagram));
        _actions.Register(
            "show-material-glass-map",
            "玻璃图",
            "数据库",
            () => _panels.ShowMaterialAnalysis(MaterialAnalysisKind.GlassMap));
        _actions.Register(
            "show-material-athermal-map",
            "无热化玻璃图",
            "数据库",
            () => _panels.ShowMaterialAnalysis(MaterialAnalysisKind.AthermalGlassMap));
        _actions.Register(
            "show-material-transmission",
            "内部透过率 vs. 波长",
            "数据库",
            () => _panels.ShowMaterialAnalysis(MaterialAnalysisKind.InternalTransmission));
        _actions.Register(
            "show-material-dispersion-wavelength",
            "色散 vs. 波长",
            "数据库",
            () => _panels.ShowMaterialAnalysis(MaterialAnalysisKind.DispersionVsWavelength));
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
        _actions.Register(
            "show-optical-drawing-gb-1991",
            "GB/T 13323—1991 光学制图",
            "加工与图纸",
            () => _panels.ShowOpticalDrawing(OpticalDrawingStandard.GbT13323_1991));
        _actions.Register("show-analysis", "显示分析面板", "面板", () => _panels.Show(WorkspacePanelId.Analysis));
        _actions.Register("show-optimization", "显示优化面板", "面板", () => _panels.Show(WorkspacePanelId.Optimization));
        _actions.Register("quick-focus", "快速聚焦", "优化", QuickFocusAsync);
        _actions.Register(
            "quick-adjust",
            "快速调整",
            "优化",
            () => _panels.Show(WorkspacePanelId.LensEditor));
        _actions.Register("optimization-slider", "滑块", "优化", ShowOptimizationSliderAsync);
        _actions.Register(
            "show-visual-optimizer",
            "可视化优化器",
            "优化",
            () => _panels.Show(WorkspacePanelId.Optimization));
        _actions.Register(
            "show-merit-editor",
            "评价函数编辑器",
            "优化",
            () => _panels.Show(WorkspacePanelId.Optimization));
        _actions.Register(
            "show-optimization-wizard",
            "优化向导",
            "优化",
            ShowOptimizationWizardAsync);
        _actions.Register(
            "run-optimization",
            "执行优化",
            "优化",
            () => RunRibbonOptimizationAsync("Damped Least Squares", 80, "阻尼最小二乘优化"));
        _actions.Register(
            "clear-optimization-variables",
            "移除所有变量",
            "优化",
            () => UpdateAllOptimizationVariables(
                OptimizationVariableUpdateMode.ClearAll,
                "已移除所有变量"));
        _actions.Register(
            "set-all-radius-variables",
            "设全部半径变量",
            "优化",
            () => UpdateAllOptimizationVariables(
                OptimizationVariableUpdateMode.SetAllRadii,
                "已设置全部半径变量"));
        _actions.Register(
            "set-all-thickness-variables",
            "设全部厚度变量",
            "优化",
            () => UpdateAllOptimizationVariables(
                OptimizationVariableUpdateMode.SetAllThicknesses,
                "已设置全部厚度变量"));
        _actions.Register(
            "run-random-perturbation",
            "随机扰动搜索",
            "优化",
            () => RunRibbonOptimizationAsync(
                "Greedy Random Perturbation",
                120,
                "贪心随机扰动搜索"));
        _actions.Register(
            "glass-replacement-template",
            "玻璃替换模板",
            "优化",
            OpenGlassReplacementTemplate);
        _actions.Register("show-tolerancing", "显示公差面板", "面板", () => _panels.Show(WorkspacePanelId.Tolerancing));
        _actions.Register("run-tolerancing", "运行公差分析", "公差", () => _panels.RunTolerancingAsync(this));
        _actions.Register("show-tolerance-data-viewer", "显示公差数据查看器", "公差", _panels.ShowTolerancingDataViewer);
        _actions.Register("show-tolerance-report", "显示公差报告", "公差", _panels.ShowTolerancingReport);
        _actions.Register("show-tolerance-histogram", "显示公差直方图", "公差", _panels.ShowTolerancingHistogram);
        _actions.Register("show-tolerance-yield", "显示公差良率", "公差", _panels.ShowTolerancingYield);
        _actions.Register("show-multiconfig", "显示多配置面板", "面板", () => _panels.Show(WorkspacePanelId.MultiConfiguration));
        _actions.Register("reset-layout", "重置为系统初始布局", "布局", ResetLayout);
        _actions.Register("save-layout-1", "保存布局到槽位 1", "布局", () => SaveLayoutSlot(1));
        _actions.Register("save-layout-2", "保存布局到槽位 2", "布局", () => SaveLayoutSlot(2));
        _actions.Register("load-layout-1", "加载布局槽位 1", "布局", () => LoadLayoutSlot(1));
        _actions.Register("load-layout-2", "加载布局槽位 2", "布局", () => LoadLayoutSlot(2));
        _actions.Register("analysis-dock-all", "重新停靠所有页面并保留分栏", "窗口", _panels.DockAllWindows);
        _actions.Register("dock-single-pane", "合并所有页面到单一窗格", "窗口", _panels.DockToSinglePane);
        _actions.Register("analysis-float-all", "将所有页面分别独立浮动", "窗口", _panels.FloatAllWindows);
        _actions.Register("analysis-tile-all", "平铺所有页面", "窗口", _panels.TileAllWindows);
        _actions.Register("analysis-cascade-all", "层叠所有页面", "窗口", _panels.CascadeAllWindows);
        _actions.Register("analysis-clone", "克隆当前分析页", "窗口", _panels.CloneActiveAnalysis);
        _actions.Register("toggle-page-lock", "切换当前页面更新锁定", "窗口", _panels.ToggleActiveDocumentLocked);
        _actions.Register("close-all-pages", "关闭镜头数据以外的页面", "窗口", _panels.CloseAllDocuments);
        _actions.Register("save-default-layout", "保存默认布局", "窗口", () => _panels.SaveDefaultLayoutAsync());
        _actions.Register("restore-default-layout", "载入已保存的默认布局", "窗口", RestoreDefaultLayoutAsync);
        _actions.Register("command-palette", "命令面板", "工具", ShowCommandPaletteAsync);
        _actions.Register("show-operand-help", "操作数帮助", "帮助", _panels.ShowOperandHelp);
        _actions.Register("about", "关于 Optical System Design", "帮助", ShowAboutAsync);
        foreach (var analysis in WorkbenchAnalysisCatalog.AllRibbonCommands)
        {
            _actions.Register(
                analysis.Id,
                analysis.Name,
                "分析",
                analysis.Kind switch
                {
                    AnalysisRibbonCommandKind.ImaBimViewer => OpenImaBimViewerAsync,
                    AnalysisRibbonCommandKind.BitmapViewer => OpenBitmapViewerAsync,
                    AnalysisRibbonCommandKind.NonSequentialTraceControl => OpenNonSequentialTraceControlAsync,
                    AnalysisRibbonCommandKind.NonSequentialDetectorViewer => OpenNonSequentialDetectorViewerAsync,
                    AnalysisRibbonCommandKind.NonSequentialClearDetectors => ClearNonSequentialDetectorsAsync,
                    AnalysisRibbonCommandKind.NonSequentialRayDatabaseViewer => () => OpenNonSequentialDatabaseAsync(false),
                    AnalysisRibbonCommandKind.NonSequentialPathAnalysis => () => OpenNonSequentialDatabaseAsync(true),
                    AnalysisRibbonCommandKind.NonSequentialLayout => OpenNonSequentialLayoutAsync,
                    _ => () =>
                    {
                        _panels.ShowAnalysis(analysis.Name);
                        return Task.CompletedTask;
                    }
                });
        }
    }

    private async Task OpenNonSequentialTraceControlAsync()
    {
        await new Panels.NonSequentialTraceControlWindow(
            _application.NonSequential,
            _application.NonSequentialAnalysis).ShowDialog(this);
    }

    private Task OpenNonSequentialLayoutAsync()
    {
        _panels.ShowViewer(OpticSceneViewMode.ThreeDimensional);
        return Task.CompletedTask;
    }

    private Task OpenNonSequentialDetectorViewerAsync()
    {
        _panels.ShowNonSequentialDetectorViewer();
        return Task.CompletedTask;
    }

    private async Task ClearNonSequentialDetectorsAsync()
    {
        await _application.NonSequentialAnalysis.ClearDetectorsAsync();
        _statusText.Text = "非序列探测器和当前追迹结果已清空";
    }

    private async Task OpenNonSequentialDatabaseAsync(bool showPathAnalysis)
    {
        var path = _application.NonSequentialAnalysis.GetCurrentSession()?.RayDatabasePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "打开非序列光线数据库",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("STAR 光线数据库") { Patterns = new[] { "*.starrdb" } }
                }
            });
            if (files.Count == 0) return;
            path = files[0].Path.LocalPath;
        }
        await new Panels.NonSequentialRayDatabaseWindow(
            _application.NonSequentialAnalysis,
            path,
            showPathAnalysis).ShowDialog(this);
    }

    private async Task SwitchWorkbenchModeAsync(OpticalWorkbenchMode mode)
    {
        if (_application.Modes.CurrentMode == mode
            || !await ConfirmUnsavedToleranceChangesAsync(
                mode == OpticalWorkbenchMode.NonSequential ? "进入非序列模式" : "返回顺序模式"))
        {
            return;
        }

        await _panels.SaveCurrentSessionAsync();
        _application.Modes.SwitchTo(mode);
        _settings.WorkbenchMode = mode.ToString();
        _panels.SwitchMode();
        _ribbonHost.Content = BuildRibbon();
        DisplayTypography.Apply(this);
        RefreshStatus();
        if (!_settings.TrySave(out var errorMessage))
        {
            _statusText.Text = $"模式已切换；设置未保存：{errorMessage}";
        }
    }
}
