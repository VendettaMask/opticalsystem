using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

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

public sealed record Layout2DPoint(double Z, double Y);

public sealed record Layout2DSurfaceCurve(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    IReadOnlyList<Layout2DPoint> Points);

public sealed record Layout2DLensEdge(Layout2DPoint Start, Layout2DPoint End);

public sealed record Layout2DRayPath(
    int RayNumber,
    bool Vignetted,
    double FinalIntensity,
    IReadOnlyList<Layout2DPoint> Points);

public sealed record Layout2DScene(
    IReadOnlyList<Layout2DSurfaceCurve> Surfaces,
    IReadOnlyList<Layout2DLensEdge> LensEdges,
    IReadOnlyList<Layout2DRayPath> Rays,
    double ZMin,
    double ZMax,
    double YExtent);

public sealed class Layout2DBuilder
{
    private const int DefaultSurfaceSamples = 65;

    private readonly Optic _optic;

    public Layout2DBuilder(Optic optic)
    {
        _optic = optic;
    }

    public Layout2DScene Build(int surfaceSamples = DefaultSurfaceSamples)
    {
        surfaceSamples = Math.Max(3, surfaceSamples);
        if (surfaceSamples % 2 == 0)
        {
            surfaceSamples++;
        }

        var surfaces = BuildSurfaceCurves(surfaceSamples);
        var lensEdges = BuildLensEdges(surfaces);
        var rays = BuildRayPaths();
        var extents = CalculateExtents(surfaces, rays);

        return new Layout2DScene(
            surfaces,
            lensEdges,
            rays,
            extents.ZMin,
            extents.ZMax,
            extents.YExtent);
    }

    private IReadOnlyList<Layout2DSurfaceCurve> BuildSurfaceCurves(int surfaceSamples)
    {
        var curves = new List<Layout2DSurfaceCurve>();

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var points = new List<Layout2DPoint>(surfaceSamples);
            var vertexZ = surface.CoordinateSystem.Origin.Z;
            var semiDiameter = Math.Max(0.1, surface.SemiDiameter);

            for (var index = 0; index < surfaceSamples; index++)
            {
                var t = index / (double)(surfaceSamples - 1);
                var y = -semiDiameter + (2.0 * semiDiameter * t);
                var sag = surface.Geometry.Sag(0, y);
                if (!double.IsFinite(sag))
                {
                    sag = 0;
                }

                points.Add(new Layout2DPoint(vertexZ + sag, y));
            }

            curves.Add(new Layout2DSurfaceCurve(surface.Number, surface.Label, surface.IsStop, points));
        }

        return curves;
    }

    private IReadOnlyList<Layout2DLensEdge> BuildLensEdges(IReadOnlyList<Layout2DSurfaceCurve> curves)
    {
        var edges = new List<Layout2DLensEdge>();
        var surfaces = _optic.SurfaceGroup.Items;
        if (curves.Count < 2)
        {
            return edges;
        }

        for (var index = 0; index < curves.Count - 1; index++)
        {
            var surface = surfaces[index];
            var next = surfaces[index + 1];
            if (!ShouldConnectAsLensElement(surface, next, surfaces.Count))
            {
                continue;
            }

            edges.Add(new Layout2DLensEdge(curves[index].Points[0], curves[index + 1].Points[0]));
            edges.Add(new Layout2DLensEdge(curves[index].Points[^1], curves[index + 1].Points[^1]));
        }

        return edges;
    }

    private static bool ShouldConnectAsLensElement(OpticalSurface surface, OpticalSurface next, int surfaceCount)
    {
        if (surface.Number <= 0 || next.Number >= surfaceCount - 1)
        {
            return false;
        }

        if (surface.IsStop || surface.Thickness <= 1e-9)
        {
            return false;
        }

        return !surface.MaterialAfterName.Equals("Air", StringComparison.OrdinalIgnoreCase)
            || !surface.MaterialAfterName.Equals(surface.MaterialBefore.Name, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<Layout2DRayPath> BuildRayPaths()
    {
        var trace = _optic.SequentialRayTracer.Trace();
        var paths = new List<Layout2DRayPath>();

        for (var rayIndex = 0; rayIndex < trace.RayHistories.Count; rayIndex++)
        {
            var history = trace.RayHistories[rayIndex];
            var points = new List<Layout2DPoint>();
            var vignetted = false;
            var finalIntensity = 0.0;

            foreach (var sample in history)
            {
                points.Add(new Layout2DPoint(sample.Position.Z, sample.Position.Y));
                finalIntensity = sample.Intensity;

                if (sample.Vignetted || sample.Intensity <= 0)
                {
                    vignetted = true;
                    break;
                }
            }

            if (points.Count >= 2)
            {
                paths.Add(new Layout2DRayPath(rayIndex, vignetted, finalIntensity, points));
            }
        }

        return paths;
    }

    private (double ZMin, double ZMax, double YExtent) CalculateExtents(
        IReadOnlyList<Layout2DSurfaceCurve> surfaces,
        IReadOnlyList<Layout2DRayPath> rays)
    {
        var zValues = new List<double>();
        var yValues = new List<double>();

        foreach (var surface in surfaces)
        {
            foreach (var point in surface.Points)
            {
                zValues.Add(point.Z);
                yValues.Add(point.Y);
            }
        }

        foreach (var ray in rays)
        {
            foreach (var point in ray.Points)
            {
                zValues.Add(point.Z);
                yValues.Add(point.Y);
            }
        }

        if (zValues.Count == 0)
        {
            zValues.Add(0);
            zValues.Add(Math.Max(1, _optic.SurfaceGroup.TotalTrack));
        }

        var zMin = zValues.Min();
        var zMax = zValues.Max();
        var zSpan = Math.Max(1, zMax - zMin);
        zMin -= zSpan * 0.06;
        zMax += zSpan * 0.06;

        var yExtent = Math.Max(1, yValues.Select(Math.Abs).DefaultIfEmpty(1).Max());
        yExtent *= 1.2;

        return (zMin, zMax, yExtent);
    }
}
