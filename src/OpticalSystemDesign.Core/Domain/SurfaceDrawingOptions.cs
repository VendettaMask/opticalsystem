namespace OpticalSystemDesign.Core.Domain;

public enum SurfaceEdgeMode { SquareToNext, DirectToNext, None, FlatToNext }
public enum SurfaceDrawingPrecision { Standard, Medium, High, Presentation }

/// <summary>Presentation only. These settings never alter intersection, aiming or propagation.</summary>
public sealed record SurfaceDrawingOptions(
    double Opacity = 1,
    bool HideRays = false,
    bool SkipRayDrawing = false,
    bool HideSurface = false,
    bool HideEdges = false,
    bool DrawLocalAxes = false,
    bool DrawLocalOrigin = false,
    SurfaceEdgeMode EdgeMode = SurfaceEdgeMode.SquareToNext,
    SurfaceDrawingPrecision Precision = SurfaceDrawingPrecision.Standard,
    string? RowColor = null)
{
    public static SurfaceDrawingOptions Default { get; } = new();

    public SurfaceDrawingOptions Validated()
    {
        if (!double.IsFinite(Opacity) || Opacity < 0 || Opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity), "Drawing opacity must be between 0 and 1.");
        if (!Enum.IsDefined(EdgeMode) || !Enum.IsDefined(Precision))
            throw new ArgumentOutOfRangeException(nameof(EdgeMode), "Unknown surface drawing option.");
        return this with { RowColor = SurfaceDisplayColor.Normalize(RowColor) };
    }
}
