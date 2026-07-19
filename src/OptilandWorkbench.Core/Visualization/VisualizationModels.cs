using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Raytrace;
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

public sealed record Layout3DSurfaceFace(IReadOnlyList<Layout3DPoint> Points);

public sealed record Layout2DSurfaceCurve(
    int SurfaceNumber,
    string Label,
    bool IsStop,
    bool IsReferencePlane,
    IReadOnlyList<Layout2DPoint> Points);

public sealed record Layout2DLensEdge(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    Layout2DPoint Start,
    Layout2DPoint End);

public sealed record LayoutBuildOptions(
    int? FirstSurface = null,
    int? LastSurface = null,
    int? FieldIndex = null,
    int? WavelengthIndex = null,
    bool IncludeAllWavelengths = false,
    int RayCount = 3,
    double LowerPupil = -0.85,
    double UpperPupil = 0.85,
    bool DeleteVignetted = false,
    bool MarginalAndChiefOnly = false);

public sealed record Layout2DLensElement(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    IReadOnlyList<Layout2DPoint> Boundary);

public sealed record Layout2DRayPath(
    int RayNumber,
    int FieldIndex,
    int PupilIndex,
    int WavelengthIndex,
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
    IReadOnlyList<Layout3DPoint> MeridianX,
    IReadOnlyList<Layout3DSurfaceFace> Faces);

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
    int WavelengthIndex,
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

    private readonly Optic _optic;

    public Layout2DBuilder(Optic optic)
    {
        _optic = optic;
    }

    public Layout2DScene Build(
        int surfaceSamples = DefaultSurfaceSamples,
        LayoutBuildOptions? options = null)
    {
        surfaceSamples = NormalizeSampleCount(surfaceSamples);
        options ??= new LayoutBuildOptions();

        var surfaces = BuildSurfaceCurves(surfaceSamples)
            .Where(surface => IsSurfaceSelected(surface.SurfaceNumber, options))
            .ToList();
        var lensElements = BuildLensElements(surfaceSamples)
            .Where(element => IsSurfaceSelected(element.FrontSurfaceNumber, options)
                && IsSurfaceSelected(element.BackSurfaceNumber, options))
            .ToList();
        var lensEdges = BuildLensEdges(surfaceSamples)
            .Where(edge => IsSurfaceSelected(edge.FrontSurfaceNumber, options)
                && IsSurfaceSelected(edge.BackSurfaceNumber, options))
            .ToList();
        var rays = BuildRayPaths(includeDepth: false, options)
            .Select(path => new Layout2DRayPath(
                path.RayNumber,
                path.FieldIndex,
                path.PupilIndex,
                path.WavelengthIndex,
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
        int rimSamples = DefaultRimSamples,
        LayoutBuildOptions? options = null)
    {
        surfaceSamples = NormalizeSampleCount(surfaceSamples);
        rimSamples = Math.Max(12, rimSamples);
        options ??= new LayoutBuildOptions(RayCount: 5);

        var surfaces = Build3DSurfaces(surfaceSamples, rimSamples)
            .Where(surface => IsSurfaceSelected(surface.SurfaceNumber, options))
            .ToList();
        var lensElements = Build3DLensElements(rimSamples)
            .Where(element => IsSurfaceSelected(element.FrontSurfaceNumber, options)
                && IsSurfaceSelected(element.BackSurfaceNumber, options))
            .ToList();
        var rays = BuildRayPaths(includeDepth: true, options);
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
            for (var index = 0; index < group.Count - 1; index++)
            {
                var surface = group[index];
                var next = group[index + 1];
                var extent = ElementExtent(surface, next);
                var frontCurve = BuildExtendedSurfaceCurve(surface, surfaceSamples, extent);
                var backCurve = BuildExtendedSurfaceCurve(next, surfaceSamples, extent);
                var boundary = new List<Layout2DPoint>(frontCurve.Count + backCurve.Count);
                boundary.AddRange(frontCurve);
                for (var pointIndex = backCurve.Count - 1; pointIndex >= 0; pointIndex--)
                {
                    boundary.Add(backCurve[pointIndex]);
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
            for (var index = 0; index < group.Count - 1; index++)
            {
                var extent = ElementExtent(group[index], group[index + 1]);
                var frontCurve = BuildExtendedSurfaceCurve(group[index], surfaceSamples, extent);
                var backCurve = BuildExtendedSurfaceCurve(group[index + 1], surfaceSamples, extent);
                edges.Add(new Layout2DLensEdge(
                    group[index].Number,
                    group[index + 1].Number,
                    frontCurve[0],
                    backCurve[0]));
                edges.Add(new Layout2DLensEdge(
                    group[index].Number,
                    group[index + 1].Number,
                    frontCurve[^1],
                    backCurve[^1]));
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
                meridianX,
                BuildSurfaceFaces(surface, semiDiameter, surfaceSamples, rimSamples)));
        }

        return primitives;
    }

    private IReadOnlyList<Layout3DLensElement> Build3DLensElements(int rimSamples)
    {
        var elements = new List<Layout3DLensElement>();

        foreach (var group in BuildLensGroups())
        {
            for (var index = 0; index < group.Count - 1; index++)
            {
                var surface = group[index];
                var next = group[index + 1];
                var extent = ElementExtent(surface, next);
                elements.Add(new Layout3DLensElement(
                    surface.Number,
                    next.Number,
                    surface.MaterialAfterName,
                    BuildSurfaceRim(surface, extent, rimSamples),
                    BuildSurfaceRim(next, extent, rimSamples)));
            }
        }

        return elements;
    }

    private IReadOnlyList<Layout3DRayPath> BuildRayPaths(bool includeDepth, LayoutBuildOptions options)
    {
        var specs = BuildViewerRays(includeDepth, options);
        if (specs.Count == 0)
        {
            return Array.Empty<Layout3DRayPath>();
        }

        var trace = _optic.SequentialRayTracer.Trace(new RealRayBundle(specs.Select(spec => spec.Ray)));
        var paths = new List<Layout3DRayPath>();

        for (var rayIndex = 0; rayIndex < trace.RayHistories.Count; rayIndex++)
        {
            var history = trace.RayHistories[rayIndex];
            var points = new List<Layout3DPoint> { ToLayoutPoint(specs[rayIndex].Ray.Origin) };
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
                if (options.DeleteVignetted && vignetted)
                {
                    continue;
                }

                points = SelectRaySegment(points, options).ToList();
                if (points.Count < 2)
                {
                    continue;
                }

                paths.Add(new Layout3DRayPath(
                    rayIndex,
                    spec.FieldIndex,
                    spec.PupilIndex,
                    spec.WavelengthIndex,
                    vignetted,
                    finalIntensity,
                    points));
            }
        }

        return paths;
    }

    private IReadOnlyList<ViewerRaySpec> BuildViewerRays(bool includeDepth, LayoutBuildOptions options)
    {
        if (_optic.SurfaceGroup.Items.Count == 0)
        {
            return Array.Empty<ViewerRaySpec>();
        }

        var wavelengths = SelectedWavelengths(options);
        var pupilSamples = BuildPupilSamples(includeDepth, options);
        var fields = new List<(int Index, double NormalizedX, double NormalizedY, double Weight)>();
        var maxX = _optic.Fields.Select(field => Math.Abs(field.XAngleDegrees)).DefaultIfEmpty(0).Max();
        var maxY = _optic.Fields.Select(field => Math.Abs(field.YAngleDegrees)).DefaultIfEmpty(0).Max();
        if (_optic.Fields.Count == 0)
        {
            fields.Add((0, 0, 0, 1));
        }
        else
        {
            for (var fieldIndex = 0; fieldIndex < _optic.Fields.Count; fieldIndex++)
            {
                var field = _optic.Fields[fieldIndex];
                if (options.FieldIndex is int selectedField && selectedField != fieldIndex)
                {
                    continue;
                }

                fields.Add((
                    fieldIndex,
                    maxX <= 1e-12 ? 0 : field.XAngleDegrees / maxX,
                    maxY <= 1e-12 ? 0 : field.YAngleDegrees / maxY,
                    Math.Max(0.05, field.Weight)));
            }
        }
        var specs = new List<ViewerRaySpec>();

        foreach (var field in fields)
        {
            foreach (var wavelength in wavelengths)
            {
                for (var pupilIndex = 0; pupilIndex < pupilSamples.Count; pupilIndex++)
                {
                    var sample = pupilSamples[pupilIndex];
                    var ray = _optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                        field.NormalizedX,
                        field.NormalizedY,
                        sample.X,
                        sample.Y,
                        RayGenerator.NanometersToMicrometers(wavelength.Nanometers)).Rays.Single() with
                    {
                        Intensity = field.Weight
                    };
                    specs.Add(new ViewerRaySpec(
                        ray,
                        field.Index,
                        pupilIndex,
                        wavelength.Index));
                }
            }
        }

        return specs;
    }

    private IReadOnlyList<(int Index, double Nanometers)> SelectedWavelengths(LayoutBuildOptions options)
    {
        if (_optic.Wavelengths.Count == 0)
        {
            return new[] { (0, 587.6) };
        }

        if (options.WavelengthIndex is int selectedIndex)
        {
            selectedIndex = Math.Clamp(selectedIndex, 0, _optic.Wavelengths.Count - 1);
            return new[] { (selectedIndex, _optic.Wavelengths[selectedIndex].Nanometers) };
        }

        if (options.IncludeAllWavelengths)
        {
            return _optic.Wavelengths
                .Select((wavelength, index) => (index, wavelength.Nanometers))
                .ToArray();
        }

        var primaryIndex = Enumerable.Range(0, _optic.Wavelengths.Count)
            .FirstOrDefault(index => _optic.Wavelengths[index].IsPrimary);
        return new[] { (primaryIndex, _optic.Wavelengths[primaryIndex].Nanometers) };
    }

    private static IReadOnlyList<(double X, double Y)> BuildPupilSamples(
        bool includeDepth,
        LayoutBuildOptions options)
    {
        var lower = Math.Clamp(Math.Min(options.LowerPupil, options.UpperPupil), -1, 1);
        var upper = Math.Clamp(Math.Max(options.LowerPupil, options.UpperPupil), -1, 1);
        if (options.MarginalAndChiefOnly)
        {
            return new[] { (0.0, lower), (0.0, 0.0), (0.0, upper) }
                .Distinct()
                .ToArray();
        }

        var count = Math.Clamp(options.RayCount, 1, 101);
        if (!includeDepth)
        {
            if (count == 1)
            {
                return new[] { (0.0, 0.0) };
            }

            return Enumerable.Range(0, count)
                .Select(index => (0.0, lower + ((upper - lower) * index / (count - 1))))
                .ToArray();
        }

        if (count == 1)
        {
            return new[] { (0.0, 0.0) };
        }

        var center = (lower + upper) / 2.0;
        var radiusY = Math.Max(0.01, (upper - lower) / 2.0);
        var samples = new List<(double X, double Y)> { (0, center) };
        for (var index = 0; index < count - 1; index++)
        {
            var angle = 2.0 * Math.PI * index / (count - 1);
            samples.Add((Math.Cos(angle) * 0.85, center + (Math.Sin(angle) * radiusY)));
        }

        return samples;
    }

    private static bool IsSurfaceSelected(int surfaceNumber, LayoutBuildOptions options) =>
        (!options.FirstSurface.HasValue || surfaceNumber >= options.FirstSurface.Value)
        && (!options.LastSurface.HasValue || surfaceNumber <= options.LastSurface.Value);

    private static IReadOnlyList<Layout3DPoint> SelectRaySegment(
        IReadOnlyList<Layout3DPoint> points,
        LayoutBuildOptions options)
    {
        if (!options.FirstSurface.HasValue && !options.LastSurface.HasValue)
        {
            return points;
        }

        var firstSurface = Math.Max(0, options.FirstSurface ?? 0);
        var lastSurface = Math.Max(firstSurface, options.LastSurface ?? int.MaxValue);
        var firstPoint = Math.Clamp(firstSurface + 1, 1, points.Count - 1);
        var lastPoint = Math.Clamp(lastSurface + 1, firstPoint, points.Count - 1);
        return points.Skip(firstPoint).Take((lastPoint - firstPoint) + 1).ToArray();
    }

    private IReadOnlyList<IReadOnlyList<OpticalSurface>> BuildLensGroups()
    {
        var surfaces = _optic.SurfaceGroup.Items;
        var groups = new List<IReadOnlyList<OpticalSurface>>();
        var lensSurfaces = new List<OpticalSurface>();

        for (var index = 1; index < surfaces.Count - 1; index++)
        {
            var surface = surfaces[index];
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
        for (var index = 0; index < surfaceSamples; index++)
        {
            var t = index / (double)(surfaceSamples - 1);
            var y = -extent + (2.0 * extent * t);
            var global = surface.CoordinateSystem.ToGlobalPoint(new Vector3D(0, y, SafeSag(surface, 0, y)));
            points.Add(new Layout2DPoint(global.Z, global.Y));
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

    private static IReadOnlyList<Layout3DSurfaceFace> BuildSurfaceFaces(
        OpticalSurface surface,
        double extent,
        int surfaceSamples,
        int rimSamples)
    {
        var radialSegments = Math.Clamp((surfaceSamples - 1) / 8, 4, 10);
        var angularSegments = Math.Clamp(rimSamples / 2, 16, 40);
        var center = ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
            new Vector3D(0, 0, SafeSag(surface, 0, 0))));
        var rings = new List<IReadOnlyList<Layout3DPoint>>(radialSegments);

        for (var radialIndex = 1; radialIndex <= radialSegments; radialIndex++)
        {
            var radius = extent * radialIndex / radialSegments;
            var ring = new List<Layout3DPoint>(angularSegments);
            for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
            {
                var angle = (2.0 * Math.PI * angularIndex) / angularSegments;
                var x = Math.Cos(angle) * radius;
                var y = Math.Sin(angle) * radius;
                ring.Add(ToLayoutPoint(surface.CoordinateSystem.ToGlobalPoint(
                    new Vector3D(x, y, SafeSag(surface, x, y)))));
            }

            rings.Add(ring);
        }

        var faces = new List<Layout3DSurfaceFace>(radialSegments * angularSegments);
        for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
        {
            var nextAngularIndex = (angularIndex + 1) % angularSegments;
            faces.Add(new Layout3DSurfaceFace(new[]
            {
                center,
                rings[0][angularIndex],
                rings[0][nextAngularIndex]
            }));
        }

        for (var radialIndex = 1; radialIndex < rings.Count; radialIndex++)
        {
            var inner = rings[radialIndex - 1];
            var outer = rings[radialIndex];
            for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
            {
                var nextAngularIndex = (angularIndex + 1) % angularSegments;
                faces.Add(new Layout3DSurfaceFace(new[]
                {
                    inner[angularIndex],
                    outer[angularIndex],
                    outer[nextAngularIndex],
                    inner[nextAngularIndex]
                }));
            }
        }

        return faces;
    }

    private bool HasOpticalMaterialAfter(OpticalSurface surface)
    {
        return surface.MaterialAfter.RefractiveIndex(PrimaryWavelengthNanometers()) > 1.0001;
    }

    private static double SurfaceExtent(OpticalSurface surface)
    {
        var extent = Math.Max(0.1, surface.SemiDiameter);
        if (surface.Geometry is StandardGeometry standard && 1.0 + standard.Conic > 0)
        {
            var realDomain = Math.Abs(standard.Radius) / Math.Sqrt(1.0 + standard.Conic);
            extent = Math.Min(extent, realDomain * 0.98);
        }

        return extent;
    }

    private static double ElementExtent(OpticalSurface front, OpticalSurface back)
    {
        var target = Math.Max(SurfaceExtent(front), SurfaceExtent(back));
        var previous = 0.0;
        const int searchSteps = 256;
        const double minimumGap = 1e-6;

        for (var index = 1; index <= searchSteps; index++)
        {
            var current = target * index / searchSteps;
            if (ElementGap(front, back, current) > minimumGap)
            {
                previous = current;
                continue;
            }

            var low = previous;
            var high = current;
            for (var iteration = 0; iteration < 48; iteration++)
            {
                var middle = (low + high) / 2.0;
                if (ElementGap(front, back, middle) > minimumGap)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return Math.Max(0.1, low * 0.995);
        }

        return target;
    }

    private static double ElementGap(OpticalSurface front, OpticalSurface back, double y)
    {
        return Math.Min(
            SurfaceZ(back, y) - SurfaceZ(front, y),
            SurfaceZ(back, -y) - SurfaceZ(front, -y));
    }

    private static double SurfaceZ(OpticalSurface surface, double y)
    {
        var extent = SurfaceExtent(surface);
        var sampledY = Math.Clamp(y, -extent, extent);
        return surface.CoordinateSystem.ToGlobalPoint(
            new Vector3D(0, sampledY, SafeSag(surface, 0, sampledY))).Z;
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

            foreach (var face in surface.Faces)
            {
                foreach (var point in face.Points)
                {
                    Add(point);
                }
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

    private sealed record ViewerRaySpec(
        RealRay Ray,
        int FieldIndex,
        int PupilIndex,
        int WavelengthIndex);
}
