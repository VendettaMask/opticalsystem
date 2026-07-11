using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

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

public sealed record Layout3DPoint(double X, double Y, double Z);

public sealed record Layout2DSurfaceCurve(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    bool IsReferencePlane,
    IReadOnlyList<Layout2DPoint> Points);

public sealed record Layout2DLensEdge(Layout2DPoint Start, Layout2DPoint End);

public sealed record Layout2DLensElement(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    IReadOnlyList<Layout2DPoint> Boundary);

public sealed record Layout2DRayPath(
    int RayNumber,
    int FieldIndex,
    int PupilIndex,
    bool Vignetted,
    double FinalIntensity,
    IReadOnlyList<Layout2DPoint> Points);

public sealed record Layout2DScene(
    IReadOnlyList<Layout2DSurfaceCurve> Surfaces,
    IReadOnlyList<Layout2DLensElement> LensElements,
    IReadOnlyList<Layout2DLensEdge> LensEdges,
    IReadOnlyList<Layout2DRayPath> Rays,
    double ZMin,
    double ZMax,
    double YExtent);

public sealed record Layout3DSurfacePrimitive(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    bool IsReferencePlane,
    string Material,
    IReadOnlyList<Layout3DPoint> Rim,
    IReadOnlyList<Layout3DPoint> MeridianY,
    IReadOnlyList<Layout3DPoint> MeridianX);

public sealed record Layout3DLensElement(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    IReadOnlyList<Layout3DPoint> FrontRim,
    IReadOnlyList<Layout3DPoint> BackRim);

public sealed record Layout3DRayPath(
    int RayNumber,
    int FieldIndex,
    int PupilIndex,
    bool Vignetted,
    double FinalIntensity,
    IReadOnlyList<Layout3DPoint> Points);

public sealed record Layout3DScene(
    IReadOnlyList<Layout3DSurfacePrimitive> Surfaces,
    IReadOnlyList<Layout3DLensElement> LensElements,
    IReadOnlyList<Layout3DRayPath> Rays,
    double XExtent,
    double YExtent,
    double ZMin,
    double ZMax);

public sealed class Layout2DBuilder
{
    private const int DefaultSurfaceSamples = 65;
    private const int DefaultRimSamples = 64;

    private static readonly (double X, double Y)[] TwoDimensionalPupilSamples =
    {
        (0, -0.85),
        (0, 0),
        (0, 0.85)
    };

    private static readonly (double X, double Y)[] ThreeDimensionalPupilSamples =
    {
        (0, -0.85),
        (0, 0),
        (0, 0.85),
        (-0.55, 0),
        (0.55, 0)
    };

    private readonly Optic _optic;

    public Layout2DBuilder(Optic optic)
    {
        _optic = optic;
    }

    public Layout2DScene Build(int surfaceSamples = DefaultSurfaceSamples)
    {
        surfaceSamples = NormalizeSampleCount(surfaceSamples);

        var surfaces = BuildSurfaceCurves(surfaceSamples);
        var lensElements = BuildLensElements(surfaces);
        var lensEdges = BuildLensEdges(surfaces);
        var rays = BuildRayPaths(includeDepth: false)
            .Select(path => new Layout2DRayPath(
                path.RayNumber,
                path.FieldIndex,
                path.PupilIndex,
                path.Vignetted,
                path.FinalIntensity,
                path.Points.Select(point => new Layout2DPoint(point.Z, point.Y)).ToList()))
            .ToList();
        var extents = CalculateExtents(surfaces, rays);

        return new Layout2DScene(
            surfaces,
            lensElements,
            lensEdges,
            rays,
            extents.ZMin,
            extents.ZMax,
            extents.YExtent);
    }

    public Layout3DScene Build3D(
        int surfaceSamples = DefaultSurfaceSamples,
        int rimSamples = DefaultRimSamples)
    {
        surfaceSamples = NormalizeSampleCount(surfaceSamples);
        rimSamples = Math.Max(12, rimSamples);

        var surfaces = Build3DSurfaces(surfaceSamples, rimSamples);
        var lensElements = Build3DLensElements(surfaces);
        var rays = BuildRayPaths(includeDepth: true);
        var extents = Calculate3DExtents(surfaces, rays);

        return new Layout3DScene(
            surfaces,
            lensElements,
            rays,
            extents.XExtent,
            extents.YExtent,
            extents.ZMin,
            extents.ZMax);
    }

    private IReadOnlyList<Layout2DSurfaceCurve> BuildSurfaceCurves(int surfaceSamples)
    {
        var curves = new List<Layout2DSurfaceCurve>();
        var surfaceCount = _optic.SurfaceGroup.Items.Count;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var points = new List<Layout2DPoint>(surfaceSamples);
            var vertexZ = surface.CoordinateSystem.Origin.Z;
            var semiDiameter = Math.Max(0.1, surface.SemiDiameter);

            for (var index = 0; index < surfaceSamples; index++)
            {
                var t = index / (double)(surfaceSamples - 1);
                var y = -semiDiameter + (2.0 * semiDiameter * t);
                points.Add(new Layout2DPoint(vertexZ + SafeSag(surface, 0, y), y));
            }

            curves.Add(new Layout2DSurfaceCurve(
                surface.Number,
                surface.Label,
                surface.IsStop,
                IsReferencePlane(surface, surfaceCount),
                points));
        }

        return curves;
    }

    private IReadOnlyList<Layout2DLensElement> BuildLensElements(IReadOnlyList<Layout2DSurfaceCurve> curves)
    {
        var elements = new List<Layout2DLensElement>();
        var surfaces = _optic.SurfaceGroup.Items;

        for (var index = 0; index < curves.Count - 1; index++)
        {
            var surface = surfaces[index];
            var next = surfaces[index + 1];
            if (!ShouldConnectAsLensElement(surface, next, surfaces.Count))
            {
                continue;
            }

            var boundary = new List<Layout2DPoint>(curves[index].Points.Count + curves[index + 1].Points.Count);
            boundary.AddRange(curves[index].Points);
            for (var pointIndex = curves[index + 1].Points.Count - 1; pointIndex >= 0; pointIndex--)
            {
                boundary.Add(curves[index + 1].Points[pointIndex]);
            }

            elements.Add(new Layout2DLensElement(
                surface.Number,
                next.Number,
                surface.MaterialAfterName,
                boundary));
        }

        return elements;
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

    private IReadOnlyList<Layout3DSurfacePrimitive> Build3DSurfaces(int surfaceSamples, int rimSamples)
    {
        var primitives = new List<Layout3DSurfacePrimitive>();
        var surfaceCount = _optic.SurfaceGroup.Items.Count;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var semiDiameter = Math.Max(0.1, surface.SemiDiameter);
            var rim = new List<Layout3DPoint>(rimSamples + 1);
            for (var index = 0; index <= rimSamples; index++)
            {
                var angle = (2.0 * Math.PI * index) / rimSamples;
                var x = Math.Cos(angle) * semiDiameter;
                var y = Math.Sin(angle) * semiDiameter;
                rim.Add(ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
                    new Vector3D(x, y, SafeSag(surface, x, y)))));
            }

            var meridianY = new List<Layout3DPoint>(surfaceSamples);
            var meridianX = new List<Layout3DPoint>(surfaceSamples);
            for (var index = 0; index < surfaceSamples; index++)
            {
                var t = index / (double)(surfaceSamples - 1);
                var value = -semiDiameter + (2.0 * semiDiameter * t);
                meridianY.Add(ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
                    new Vector3D(0, value, SafeSag(surface, 0, value)))));
                meridianX.Add(ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
                    new Vector3D(value, 0, SafeSag(surface, value, 0)))));
            }

            primitives.Add(new Layout3DSurfacePrimitive(
                surface.Number,
                surface.Label,
                surface.IsStop,
                IsReferencePlane(surface, surfaceCount),
                surface.MaterialAfterName,
                rim,
                meridianY,
                meridianX));
        }

        return primitives;
    }

    private IReadOnlyList<Layout3DLensElement> Build3DLensElements(IReadOnlyList<Layout3DSurfacePrimitive> surfaces)
    {
        var elements = new List<Layout3DLensElement>();
        var opticalSurfaces = _optic.SurfaceGroup.Items;

        for (var index = 0; index < surfaces.Count - 1; index++)
        {
            var surface = opticalSurfaces[index];
            var next = opticalSurfaces[index + 1];
            if (!ShouldConnectAsLensElement(surface, next, opticalSurfaces.Count))
            {
                continue;
            }

            elements.Add(new Layout3DLensElement(
                surface.Number,
                next.Number,
                surface.MaterialAfterName,
                surfaces[index].Rim,
                surfaces[index + 1].Rim));
        }

        return elements;
    }

    private IReadOnlyList<Layout3DRayPath> BuildRayPaths(bool includeDepth)
    {
        var specs = BuildViewerRays(includeDepth);
        if (specs.Count == 0)
        {
            return Array.Empty<Layout3DRayPath>();
        }

        var trace = _optic.SequentialRayTracer.Trace(new RealRayBundle(specs.Select(spec => spec.Ray)));
        var paths = new List<Layout3DRayPath>();

        for (var rayIndex = 0; rayIndex < trace.RayHistories.Count; rayIndex++)
        {
            var history = trace.RayHistories[rayIndex];
            var points = new List<Layout3DPoint>();
            var vignetted = false;
            var finalIntensity = 0.0;

            foreach (var sample in history)
            {
                points.Add(ToLayoutPoint(sample.Position));
                finalIntensity = sample.Intensity;

                if (sample.Vignetted || sample.Intensity <= 0)
                {
                    vignetted = true;
                    break;
                }
            }

            if (points.Count >= 2)
            {
                var spec = specs[rayIndex];
                paths.Add(new Layout3DRayPath(
                    rayIndex,
                    spec.FieldIndex,
                    spec.PupilIndex,
                    vignetted,
                    finalIntensity,
                    points));
            }
        }

        return paths;
    }

    private IReadOnlyList<ViewerRaySpec> BuildViewerRays(bool includeDepth)
    {
        var surfaces = _optic.SurfaceGroup.Items;
        if (surfaces.Count == 0)
        {
            return Array.Empty<ViewerRaySpec>();
        }

        var first = surfaces[0];
        var stop = surfaces.FirstOrDefault(surface => surface.IsStop)
            ?? surfaces.Skip(1).FirstOrDefault()
            ?? first;
        var firstZ = first.CoordinateSystem.Origin.Z;
        var stopZ = stop.CoordinateSystem.Origin.Z;
        var deltaZ = stopZ - firstZ;
        if (Math.Abs(deltaZ) < 1e-6)
        {
            deltaZ = Math.Max(1, _optic.SurfaceGroup.TotalTrack * 0.2);
            stopZ = firstZ + deltaZ;
        }

        var stopRadius = Math.Max(0.5, stop.SemiDiameter);
        var wavelength = PrimaryWavelengthNanometers();
        var pupilSamples = includeDepth ? ThreeDimensionalPupilSamples : TwoDimensionalPupilSamples;
        var fields = new List<(double AngleDegrees, double Weight)>();
        if (_optic.Fields.Count == 0)
        {
            fields.Add((0, 1));
        }
        else
        {
            foreach (var field in _optic.Fields)
            {
                fields.Add((field.YAngleDegrees, Math.Max(0.05, field.Weight)));
            }
        }
        var specs = new List<ViewerRaySpec>();

        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            var tangent = Math.Tan(DegreesToRadians(field.AngleDegrees));

            for (var pupilIndex = 0; pupilIndex < pupilSamples.Length; pupilIndex++)
            {
                var sample = pupilSamples[pupilIndex];
                var target = new Vector3D(
                    sample.X * stopRadius,
                    sample.Y * stopRadius,
                    stopZ);
                var origin = new Vector3D(
                    target.X,
                    target.Y - (tangent * deltaZ),
                    firstZ);
                var direction = Normalize(target - origin);
                specs.Add(new ViewerRaySpec(
                    new RealRay(origin, direction, wavelength, field.Weight),
                    fieldIndex,
                    pupilIndex));
            }
        }

        return specs;
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

    private static bool IsReferencePlane(OpticalSurface surface, int surfaceCount)
    {
        return surface.Number == 0 || surface.Number == surfaceCount - 1;
    }

    private static int NormalizeSampleCount(int surfaceSamples)
    {
        surfaceSamples = Math.Max(3, surfaceSamples);
        return surfaceSamples % 2 == 0 ? surfaceSamples + 1 : surfaceSamples;
    }

    private static double SafeSag(OpticalSurface surface, double x, double y)
    {
        var sag = surface.Geometry.Sag(x, y);
        return double.IsFinite(sag) ? sag : 0;
    }

    private double PrimaryWavelengthNanometers()
    {
        return _optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)?.Nanometers
            ?? _optic.Wavelengths.FirstOrDefault()?.Nanometers
            ?? 587.6;
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static Layout3DPoint ToLayoutPoint(Vector3D point)
    {
        return new Layout3DPoint(point.X, point.Y, point.Z);
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

    private (double XExtent, double YExtent, double ZMin, double ZMax) Calculate3DExtents(
        IReadOnlyList<Layout3DSurfacePrimitive> surfaces,
        IReadOnlyList<Layout3DRayPath> rays)
    {
        var xValues = new List<double>();
        var yValues = new List<double>();
        var zValues = new List<double>();

        void Add(Layout3DPoint point)
        {
            xValues.Add(point.X);
            yValues.Add(point.Y);
            zValues.Add(point.Z);
        }

        foreach (var surface in surfaces)
        {
            foreach (var point in surface.Rim)
            {
                Add(point);
            }

            foreach (var point in surface.MeridianY)
            {
                Add(point);
            }

            foreach (var point in surface.MeridianX)
            {
                Add(point);
            }
        }

        foreach (var ray in rays)
        {
            foreach (var point in ray.Points)
            {
                Add(point);
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

        return (
            Math.Max(1, xValues.Select(Math.Abs).DefaultIfEmpty(1).Max()) * 1.2,
            Math.Max(1, yValues.Select(Math.Abs).DefaultIfEmpty(1).Max()) * 1.2,
            zMin,
            zMax);
    }

    private sealed record ViewerRaySpec(RealRay Ray, int FieldIndex, int PupilIndex);
}
