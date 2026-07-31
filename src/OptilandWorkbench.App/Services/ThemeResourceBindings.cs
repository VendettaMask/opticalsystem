using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OptilandWorkbench.App.Services;

public static class ThemeResourceBindings
{
    public const string Surface = "OptilandSurfaceBrush";
    public const string SubtleSurface = "OptilandSubtleSurfaceBrush";
    public const string Workspace = "OptilandWorkspaceBrush";
    public const string Border = "OptilandBorderBrush";
    public const string TextPrimary = "OptilandTextPrimaryBrush";
    public const string TextSecondary = "OptilandTextSecondaryBrush";
    public const string TextMuted = "OptilandTextMutedBrush";
    public const string TextDisabled = "OptilandTextDisabledBrush";
    public const string TextAccent = "OptilandTextAccentBrush";
    public const string TextOnAccent = "OptilandTextOnAccentBrush";
    public const string TextWarning = "OptilandTextWarningBrush";
    public const string TextError = "OptilandTextErrorBrush";
    public const string TextSuccess = "OptilandTextSuccessBrush";
    public const string MutedText = "OptilandMutedTextBrush";
    public const string Hover = "OptilandHoverBrush";
    public const string HoverBorder = "OptilandHoverBorderBrush";
    public const string RibbonHover = "OptilandRibbonHoverBrush";
    public const string RibbonHoverBorder = "OptilandRibbonHoverBorderBrush";
    public const string RibbonTabHover = "OptilandRibbonTabHoverBrush";
    public const string PlotBackground = "OptilandPlotBackgroundBrush";
    public const string PlotText = "OptilandPlotTextBrush";
    public const string PlotTick = "OptilandPlotTickBrush";
    public const string PlotGrid = "OptilandPlotGridBrush";
    public const string PlotAxis = "OptilandPlotAxisBrush";
    public const string PlotZeroLine = "OptilandPlotZeroLineBrush";
    public const string PlotHint = "OptilandPlotHintBrush";
    public const string PlotTooltipBackground = "OptilandPlotTooltipBackgroundBrush";
    public const string PlotTooltipBorder = "OptilandPlotTooltipBorderBrush";
    public const string PlotHoverMarkerFill = "OptilandPlotHoverMarkerFillBrush";
    public const string PlotBar = "OptilandPlotBarBrush";
    public const string SceneBackground = "OptilandSceneBackgroundBrush";
    public const string SceneBackgroundAlt = "OptilandSceneBackgroundAltBrush";
    public const string SceneLensFill = "OptilandSceneLensFillBrush";
    public const string SceneReference = "OptilandSceneReferenceBrush";
    public const string SceneAxis = "OptilandSceneAxisBrush";
    public const string SceneStop = "OptilandSceneStopBrush";
    public const string SceneApertureStop = "OptilandSceneApertureStopBrush";
    public const string SceneSurface = "OptilandSceneSurfaceBrush";
    public const string SceneLensEdge = "OptilandSceneLensEdgeBrush";
    public const string SceneTarget = "OptilandSceneTargetBrush";
    public const string SceneOrientationFill = "OptilandSceneOrientationFillBrush";
    public const string SceneOrientationBorder = "OptilandSceneOrientationBorderBrush";
    public static IDisposable BindThemeResource(
        this AvaloniaObject target,
        AvaloniaProperty property,
        string resourceKey) =>
        target.Bind(
            property,
            new DynamicResourceExtension(resourceKey));
}
