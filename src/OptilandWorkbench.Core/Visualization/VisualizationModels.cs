using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Visualization;

public sealed record LayoutSurfacePrimitive(
    int SurfaceNumber,
    string Label,
    Vector3D Center,
    double SemiDiameter,
    string Material);

public sealed record RayPathPrimitive(IReadOnlyList<Vector3D> Points, double Intensity);

public sealed record OpticLayoutScene(
    IReadOnlyList<LayoutSurfacePrimitive> Surfaces,
    IReadOnlyList<RayPathPrimitive> Rays,
    string Theme);

public sealed class VisualizationTheme
{
    public string Name { get; init; } = "light";

    public string Background { get; init; } = "#FAFCFE";

    public string Lens { get; init; } = "#3A78A8";

    public string Ray { get; init; } = "#D68C2D";
}
