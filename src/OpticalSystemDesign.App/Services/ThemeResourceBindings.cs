using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OpticalSystemDesign.App.Services;

public static class ThemeResourceBindings
{
    public const string Surface = "OpticalSystemDesignSurfaceBrush";
    public const string SubtleSurface = "OpticalSystemDesignSubtleSurfaceBrush";
    public const string SettingsSurface = "OpticalSystemDesignSettingsSurfaceBrush";
    public const string SettingsOverlaySurface = "OpticalSystemDesignSettingsOverlaySurfaceBrush";
    public const string Workspace = "OpticalSystemDesignWorkspaceBrush";
    public const string Sidebar = "OpticalSystemDesignSidebarBrush";
    public const string HeaderBackground = "OpticalSystemDesignHeaderBackgroundBrush";
    public const string TableHeader = "OpticalSystemDesignTableHeaderBrush";
    public const string RibbonPageBackground = "OpticalSystemDesignRibbonPageBackgroundBrush";
    public const string StatusBarBackground = "OpticalSystemDesignStatusBarBackgroundBrush";
    public const string Border = "OpticalSystemDesignBorderBrush";
    public const string DockPreviewFill = "OpticalSystemDesignDockPreviewFillBrush";
    public const string DockPreviewBorder = "OpticalSystemDesignDockPreviewBorderBrush";
    public const string TextPrimary = "OpticalSystemDesignTextPrimaryBrush";
    public const string TextSecondary = "OpticalSystemDesignTextSecondaryBrush";
    public const string TextMuted = "OpticalSystemDesignTextMutedBrush";
    public const string TextDisabled = "OpticalSystemDesignTextDisabledBrush";
    public const string TextAccent = "OpticalSystemDesignTextAccentBrush";
    public const string TextOnAccent = "OpticalSystemDesignTextOnAccentBrush";
    public const string TextWarning = "OpticalSystemDesignTextWarningBrush";
    public const string TextError = "OpticalSystemDesignTextErrorBrush";
    public const string TextSuccess = "OpticalSystemDesignTextSuccessBrush";
    public const string WarningSurface = "OpticalSystemDesignWarningSurfaceBrush";
    public const string ErrorSurface = "OpticalSystemDesignErrorSurfaceBrush";
    public const string SuccessSurface = "OpticalSystemDesignSuccessSurfaceBrush";
    public const string MutedText = "OpticalSystemDesignMutedTextBrush";
    public const string Hover = "OpticalSystemDesignHoverBrush";
    public const string HoverBorder = "OpticalSystemDesignHoverBorderBrush";
    public const string RibbonHover = "OpticalSystemDesignRibbonHoverBrush";
    public const string RibbonHoverBorder = "OpticalSystemDesignRibbonHoverBorderBrush";
    public const string RibbonTabHover = "OpticalSystemDesignRibbonTabHoverBrush";
    public const string RibbonCommandBackground = "OpticalSystemDesignRibbonCommandBackgroundBrush";
    public const string RibbonCommandBorder = "OpticalSystemDesignRibbonCommandBorderBrush";
    public const string RibbonGroupBackground = "OpticalSystemDesignRibbonGroupBackgroundBrush";
    public const string RibbonGroupBorder = "OpticalSystemDesignRibbonGroupBorderBrush";
    public const string RibbonGroupCaptionBackground = "OpticalSystemDesignRibbonGroupCaptionBackgroundBrush";
    public const string RibbonGroupCaptionForeground = "OpticalSystemDesignRibbonGroupCaptionForegroundBrush";
    public const string SectionHeaderBackground = "OpticalSystemDesignSectionHeaderBackgroundBrush";
    public const string SectionHeaderForeground = "OpticalSystemDesignSectionHeaderForegroundBrush";
    public const string SectionHeaderEmphasizedBackground = "OpticalSystemDesignSectionHeaderEmphasizedBackgroundBrush";
    public const string SectionHeaderEmphasizedForeground = "OpticalSystemDesignSectionHeaderEmphasizedForegroundBrush";
    public const string SelectionBackground = "OpticalSystemDesignSelectionBackgroundBrush";
    public const string SelectionForeground = "OpticalSystemDesignSelectionForegroundBrush";
    public const string PlotBackground = "OpticalSystemDesignPlotBackgroundBrush";
    public const string PlotText = "OpticalSystemDesignPlotTextBrush";
    public const string PlotTick = "OpticalSystemDesignPlotTickBrush";
    public const string PlotGrid = "OpticalSystemDesignPlotGridBrush";
    public const string PlotAxis = "OpticalSystemDesignPlotAxisBrush";
    public const string PlotZeroLine = "OpticalSystemDesignPlotZeroLineBrush";
    public const string PlotHint = "OpticalSystemDesignPlotHintBrush";
    public const string PlotTooltipBackground = "OpticalSystemDesignPlotTooltipBackgroundBrush";
    public const string PlotTooltipBorder = "OpticalSystemDesignPlotTooltipBorderBrush";
    public const string PlotHoverMarkerFill = "OpticalSystemDesignPlotHoverMarkerFillBrush";
    public const string PlotBar = "OpticalSystemDesignPlotBarBrush";
    public const string AnalysisRealRayRowBackground = "OpticalSystemDesignAnalysisRealRayRowBackgroundBrush";
    public const string AnalysisRealRayRowForeground = "OpticalSystemDesignAnalysisRealRayRowForegroundBrush";
    public const string AnalysisParaxialRayRowBackground = "OpticalSystemDesignAnalysisParaxialRayRowBackgroundBrush";
    public const string AnalysisParaxialRayRowForeground = "OpticalSystemDesignAnalysisParaxialRayRowForegroundBrush";
    public const string SceneBackground = "OpticalSystemDesignSceneBackgroundBrush";
    public const string SceneBackgroundAlt = "OpticalSystemDesignSceneBackgroundAltBrush";
    public const string SceneLensFill = "OpticalSystemDesignSceneLensFillBrush";
    public const string SceneReference = "OpticalSystemDesignSceneReferenceBrush";
    public const string SceneAxis = "OpticalSystemDesignSceneAxisBrush";
    public const string SceneStop = "OpticalSystemDesignSceneStopBrush";
    public const string SceneApertureStop = "OpticalSystemDesignSceneApertureStopBrush";
    public const string SceneSurface = "OpticalSystemDesignSceneSurfaceBrush";
    public const string SceneLensEdge = "OpticalSystemDesignSceneLensEdgeBrush";
    public const string SceneTarget = "OpticalSystemDesignSceneTargetBrush";
    public const string SceneOrientationFill = "OpticalSystemDesignSceneOrientationFillBrush";
    public const string SceneOrientationBorder = "OpticalSystemDesignSceneOrientationBorderBrush";
    public static IDisposable BindThemeResource(
        this AvaloniaObject target,
        AvaloniaProperty property,
        string resourceKey) =>
        target.Bind(
            property,
            new DynamicResourceExtension(resourceKey));
}
