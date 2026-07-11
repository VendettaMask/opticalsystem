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
        var lensElements = BuildLensElements(surfaceSamples);
        var lensEdges = BuildLensEdges(surfaceSamples);
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
        var lensElements = Build3DLensElements(rimSamples);
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
            curves.Add(new Layout2DSurfaceCurve(
                surface.Number,
                surface.Label,
                surface.IsStop,
                IsReferencePlane(surface, surfaceCount),
                BuildSurfaceCurvePoints(surface, surfaceSamples, SurfaceExtent(surface))));
        }

        return curves;
    }

    private IReadOnlyList<Layout2DLensElement> BuildLensElements(int surfaceSamples)
    {
        var elements = new List<Layout2DLensElement>();

        foreach (var group in BuildLensGroups())
        {
            var maxExtent = group.Max(SurfaceExtent);
            var curves = group
                .Select(surface => BuildExtendedSurfaceCurve(surface, surfaceSamples, maxExtent))
                .ToList();

            for (var index = 0; index < curves.Count - 1; index++)
            {
                var surface = group[index];
                var next = group[index + 1];
                var boundary = new List<Layout2DPoint>(curves[index].Count + curves[index + 1].Count);
                boundary.AddRange(curves[index]);
                for (var pointIndex = curves[index + 1].Count - 1; pointIndex >= 0; pointIndex--)
                {
                    boundary.Add(curves[index + 1][pointIndex]);
                }

                elements.Add(new Layout2DLensElement(
                    surface.Number,
                    next.Number,
                    surface.MaterialAfterName,
                    boundary));
            }
        }

        return elements;
    }

    private IReadOnlyList<Layout2DLensEdge> BuildLensEdges(int surfaceSamples)
    {
        var edges = new List<Layout2DLensEdge>();

        foreach (var group in BuildLensGroups())
        {
            var maxExtent = group.Max(SurfaceExtent);
            var curves = group
                .Select(surface => BuildExtendedSurfaceCurve(surface, surfaceSamples, maxExtent))
                .ToList();

            for (var index = 0; index < curves.Count - 1; index++)
            {
                edges.Add(new Layout2DLensEdge(curves[index][0], curves[index + 1][0]));
                edges.Add(new Layout2DLensEdge(curves[index][^1], curves[index + 1][^1]));
            }
        }

        return edges;
    }

    private IReadOnlyList<Layout3DSurfacePrimitive> Build3DSurfaces(int surfaceSamples, int rimSamples)
    {
        var primitives = new List<Layout3DSurfacePrimitive>();
        var surfaceCount = _optic.SurfaceGroup.Items.Count;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var semiDiameter = SurfaceExtent(surface);
            var rim = BuildSurfaceRim(surface, semiDiameter, rimSamples);
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

    private IReadOnlyList<Layout3DLensElement> Build3DLensElements(int rimSamples)
    {
        var elements = new List<Layout3DLensElement>();

        foreach (var group in BuildLensGroups())
        {
            var maxExtent = group.Max(SurfaceExtent);
            var rims = group
                .Select(surface => BuildSurfaceRim(surface, maxExtent, rimSamples))
                .ToList();

            for (var index = 0; index < rims.Count - 1; index++)
            {
                var surface = group[index];
                var next = group[index + 1];
                elements.Add(new Layout3DLensElement(
                    surface.Number,
                    next.Number,
                    surface.MaterialAfterName,
                    rims[index],
                    rims[index + 1]));
            }
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

    private IReadOnlyList<IReadOnlyList<OpticalSurface>> BuildLensGroups()
    {
        var surfaces = _optic.SurfaceGroup.Items;
        var groups = new List<IReadOnlyList<OpticalSurface>>();
        var lensSurfaces = new List<OpticalSurface>();

        for (var index = 1; index < surfaces.Count - 1; index++)
        {
            var surface = surfaces[index];
            if (surface.IsStop)
            {
                continue;
            }

            if (surface.IsReflective)
            {
                if (lensSurfaces.Count > 0)
                {
                    lensSurfaces.Add(surface);
                    groups.Add(lensSurfaces.ToArray());
                    lensSurfaces.Clear();
                }

                continue;
            }

            var currentIsGlass = HasOpticalMaterialAfter(surface);
            var previousIsGlass = index > 0 && HasOpticalMaterialAfter(surfaces[index - 1]);
            if (currentIsGlass)
            {
                lensSurfaces.Add(surface);
            }
            else if (previousIsGlass && lensSurfaces.Count > 0)
            {
                lensSurfaces.Add(surface);
                groups.Add(lensSurfaces.ToArray());
                lensSurfaces.Clear();
            }
        }

        if (lensSurfaces.Count > 1)
        {
            groups.Add(lensSurfaces.ToArray());
        }

        return groups;
    }

    private IReadOnlyList<Layout2DPoint> BuildExtendedSurfaceCurve(
        OpticalSurface surface,
        int surfaceSamples,
        double targetExtent)
    {
        var surfaceExtent = SurfaceExtent(surface);
        var points = BuildSurfaceCurvePoints(surface, surfaceSamples, surfaceExtent);
        if (targetExtent <= surfaceExtent + 1e-9)
        {
            return points;
        }

        var extended = new List<Layout2DPoint>(points.Count + 2)
        {
            new(points[0].Z, -targetExtent)
        };
        extended.AddRange(points);
        extended.Add(new Layout2DPoint(points[^1].Z, targetExtent));
        return extended;
    }

    private static IReadOnlyList<Layout2DPoint> BuildSurfaceCurvePoints(
        OpticalSurface surface,
        int surfaceSamples,
        double extent)
    {
        var points = new List<Layout2DPoint>(surfaceSamples);
        var vertexZ = surface.CoordinateSystem.Origin.Z;

        for (var index = 0; index < surfaceSamples; index++)
        {
            var t = index / (double)(surfaceSamples - 1);
            var y = -extent + (2.0 * extent * t);
            points.Add(new Layout2DPoint(vertexZ + SafeSag(surface, 0, y), y));
        }

        return points;
    }

    private static IReadOnlyList<Layout3DPoint> BuildSurfaceRim(
        OpticalSurface surface,
        double extent,
        int rimSamples)
    {
        var rim = new List<Layout3DPoint>(rimSamples + 1);
        for (var index = 0; index <= rimSamples; index++)
        {
            var angle = (2.0 * Math.PI * index) / rimSamples;
            var x = Math.Cos(angle) * extent;
            var y = Math.Sin(angle) * extent;
            rim.Add(ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
                new Vector3D(x, y, SafeSag(surface, x, y)))));
        }

        return rim;
    }

    private bool HasOpticalMaterialAfter(OpticalSurface surface)
    {
        var material = _optic.Materials.Resolve(surface.MaterialAfterName);
        return material.RefractiveIndex(PrimaryWavelengthNanometers()) > 1.0001;
    }

    private static double SurfaceExtent(OpticalSurface surface)
    {
        return Math.Max(0.1, surface.SemiDiameter);
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
