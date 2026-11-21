using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Application.Services;

internal static partial class WorkbenchMapper
{
    internal static SurfaceDrawingOptionsDto ToDrawingDto(SurfaceDrawingOptions options) => new(
        options.Opacity, options.HideRays, options.SkipRayDrawing, options.HideSurface, options.HideEdges,
        options.DrawLocalAxes, options.DrawLocalOrigin, (SurfaceEdgeModeDto)(int)options.EdgeMode,
        (SurfaceDrawingPrecisionDto)(int)options.Precision, options.RowColor);

    internal static SurfaceDrawingOptions ToDrawingOptions(SurfaceDrawingOptionsDto options) => new SurfaceDrawingOptions(
        options.Opacity, options.HideRays, options.SkipRayDrawing, options.HideSurface, options.HideEdges,
        options.DrawLocalAxes, options.DrawLocalOrigin, (SurfaceEdgeMode)(int)options.EdgeMode,
        (SurfaceDrawingPrecision)(int)options.Precision, options.RowColor).Validated();
}
