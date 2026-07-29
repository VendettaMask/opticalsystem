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
        _actions.Register("export-python-json", "导出 Python Optiland JSON", "文件", ExportPythonJsonAsync);
        _actions.Register("export-cad", "导出 CAD（STEP）", "文件", ExportCadAsync);
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
        _actions.Register("show-lens-library", "打开镜头库", "数据库", _panels.ShowLensLibrary);
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
                analysis.Label,
                "分析",
                analysis.Kind switch
                {
                    AnalysisRibbonCommandKind.ImaBimViewer => OpenImaBimViewerAsync,
                    AnalysisRibbonCommandKind.BitmapViewer => OpenBitmapViewerAsync,
                    _ => () =>
                    {
                        _panels.ShowAnalysis(analysis.Name);
                        return Task.CompletedTask;
                    }
                });
        }
    }
}
