using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.NonSequential;
using BoxParameters = OptilandWorkbench.Core.NonSequential.BoxParameters;
using CylinderParameters = OptilandWorkbench.Core.NonSequential.CylinderParameters;
using DetectorRectangleParameters = OptilandWorkbench.Core.NonSequential.DetectorRectangleParameters;
using NonSequentialObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OptilandWorkbench.Core.NonSequential.PlaneRectangleParameters;
using SphereParameters = OptilandWorkbench.Core.NonSequential.SphereParameters;
using StandardLensParameters = OptilandWorkbench.Core.NonSequential.StandardLensParameters;
using MeshObjectParameters = OptilandWorkbench.Core.NonSequential.MeshObjectParameters;
using SourceRayParameters = OptilandWorkbench.Core.NonSequential.SourceRayParameters;
using SourcePointParameters = OptilandWorkbench.Core.NonSequential.SourcePointParameters;
using SourceRectangleParameters = OptilandWorkbench.Core.NonSequential.SourceRectangleParameters;
using SourceGaussianParameters = OptilandWorkbench.Core.NonSequential.SourceGaussianParameters;
using SourceEllipseParameters = OptilandWorkbench.Core.NonSequential.SourceEllipseParameters;
using SourceTwoAngleParameters = OptilandWorkbench.Core.NonSequential.SourceTwoAngleParameters;
using SourceRadialParameters = OptilandWorkbench.Core.NonSequential.SourceRadialParameters;
using SourceVolumeRectangleParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeRectangleParameters;
using SourceVolumeEllipseParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeEllipseParameters;
using SourceVolumeCylinderParameters = OptilandWorkbench.Core.NonSequential.SourceVolumeCylinderParameters;
using CoreTerminationReason = OptilandWorkbench.Core.Raytrace.NonSequentialTerminationReason;

namespace OptilandWorkbench.Application.Services;

internal static class NonSequentialVisualizationBuilder
{
    public static Scene3Dto Build(
        Optic optic,
        NonSequentialDocument document,
        VisualizationRequestDto request,
        IReadOnlyList<NonSequentialRayBranch>? databaseBranches = null)
    {
        var numberById = document.Objects.Select((item, index) => (item.Id, Number: index + 1))
            .ToDictionary(item => item.Id, item => item.Number);
        var surfaces = document.Objects.Where(item => item.Visible)
            .Select(item => BuildObject(document, item, numberById[item.Id]))
            .ToArray();
        var branches = databaseBranches ?? Array.Empty<NonSequentialRayBranch>();
        var geometryPoints = surfaces.SelectMany(surface => surface.Faces.SelectMany(face => face.Points)).ToArray();
        var escapeTailLength = EscapeTailLength(geometryPoints);
        var rays = branches.Select((branch, index) =>
        {
            var segments = branch.Segments.Select((segment, segmentIndex) => new SceneRaySegment3Dto(
                ToPoint(segment.Start),
                ToPoint(segment.End),
                ToDirection(segment.OutgoingDirection),
                SegmentType(segment.InteractionKind),
                InteractionType(segment.InteractionKind),
                segmentIndex == 0 ? null : branch.Segments[segmentIndex - 1].ObjectId is Guid previous
                    ? numberById.GetValueOrDefault(previous)
                    : null,
                segment.ObjectId is Guid current ? numberById.GetValueOrDefault(current) : null)).ToList();
            if (branch.TerminationReason == CoreTerminationReason.Escaped
                && EscapeState(branch) is { } escape)
            {
                var end = escape.Origin + escape.Direction * escapeTailLength;
                segments.Add(new SceneRaySegment3Dto(
                    ToPoint(escape.Origin),
                    ToPoint(end),
                    ToDirection(escape.Direction),
                    SceneRaySegmentType.Unspecified,
                    SceneRayInteractionType.None,
                    branch.Segments.LastOrDefault()?.ObjectId is Guid previous
                        ? numberById.GetValueOrDefault(previous)
                        : null,
                    null));
            }

            var points = segments.SelectMany(segment => new[] { segment.Start, segment.End })
                .Distinct().ToArray();
            return new SceneRay3Dto(
                index + 1,
                0,
                0,
                WavelengthIndex(document, branch),
                branch.Segments.FirstOrDefault()?.WavelengthNanometers ?? document.Wavelengths[0].Nanometers,
                false,
                branch.FinalIntensity,
                points,
                segments.ToArray());
        }).ToArray();
        var allPoints = surfaces.SelectMany(surface => surface.Faces.SelectMany(face => face.Points))
            .Concat(rays.SelectMany(ray => ray.Points)).ToArray();
        var extentX = allPoints.Length == 0 ? 10 : Math.Max(1, allPoints.Max(point => Math.Abs(point.X)));
        var extentY = allPoints.Length == 0 ? 10 : Math.Max(1, allPoints.Max(point => Math.Abs(point.Y)));
        var zMin = allPoints.Length == 0 ? -10 : allPoints.Min(point => point.Z);
        var zMax = allPoints.Length == 0 ? 10 : allPoints.Max(point => point.Z);
        return new Scene3Dto(surfaces, Array.Empty<SceneLensElement3Dto>(), rays, extentX, extentY, zMin, zMax);
    }

    private static (Vector3D Origin, Vector3D Direction)? EscapeState(NonSequentialRayBranch branch)
    {
        if (branch.FinalOrigin is Vector3D origin
            && branch.FinalDirection is Vector3D direction
            && direction.Length > 1e-15)
        {
            return (origin, direction / direction.Length);
        }

        return branch.Segments.LastOrDefault() is { } last && last.OutgoingDirection.Length > 1e-15
            ? (last.End, last.OutgoingDirection / last.OutgoingDirection.Length)
            : null;
    }

    private static double EscapeTailLength(IReadOnlyList<ScenePoint3Dto> geometryPoints)
    {
        if (geometryPoints.Count == 0)
        {
            return 10;
        }

        var spanX = geometryPoints.Max(point => point.X) - geometryPoints.Min(point => point.X);
        var spanY = geometryPoints.Max(point => point.Y) - geometryPoints.Min(point => point.Y);
        var spanZ = geometryPoints.Max(point => point.Z) - geometryPoints.Min(point => point.Z);
        return Math.Max(10, Math.Max(spanX, Math.Max(spanY, spanZ)) * 0.75);
    }

    private static SceneSurface3Dto BuildObject(
        NonSequentialDocument document,
        NonSequentialObjectDefinition item,
        int number)
    {
        var localFaces = item.Parameters switch
        {
            PlaneRectangleParameters value => Plane(value.WidthMillimeters, value.HeightMillimeters),
            DetectorRectangleParameters value => Plane(value.WidthMillimeters, value.HeightMillimeters),
            SphereParameters value => Sphere(value.RadiusMillimeters),
            CylinderParameters value => Cylinder(value.RadiusMillimeters, value.LengthMillimeters),
            BoxParameters value => Box(value.WidthMillimeters, value.HeightMillimeters, value.LengthMillimeters),
            StandardLensParameters value => Lens(value),
            MeshObjectParameters value => Mesh(document.FindMeshAsset(value.MeshAssetId)),
            SourceRayParameters or SourcePointParameters or SourceRadialParameters => Plane(0.5, 0.5),
            SourceRectangleParameters value => Plane(value.WidthMillimeters, value.HeightMillimeters),
            SourceGaussianParameters value => Ellipse(value.WaistXMillimeters * 2, value.WaistYMillimeters * 2),
            SourceEllipseParameters value => Ellipse(value.WidthMillimeters, value.HeightMillimeters),
            SourceTwoAngleParameters value => value.Shape == OptilandWorkbench.Core.NonSequential.NonSequentialSourceApertureShape.Ellipse
                ? Ellipse(value.WidthMillimeters, value.HeightMillimeters)
                : Plane(value.WidthMillimeters, value.HeightMillimeters),
            SourceVolumeRectangleParameters value => Box(value.WidthMillimeters, value.HeightMillimeters, value.DepthMillimeters),
            SourceVolumeEllipseParameters value => Ellipsoid(
                value.SemiAxisXMillimeters, value.SemiAxisYMillimeters, value.SemiAxisZMillimeters),
            SourceVolumeCylinderParameters value => Cylinder(
                value.RadiusXMillimeters, value.RadiusYMillimeters, value.LengthMillimeters),
            _ => Array.Empty<Vector3D[]>()
        };
        var faces = localFaces.Select(face => new SceneSurfaceFace3Dto(
            face.Select(point => ToPoint(document.ToWorldPoint(item.Id, point))).ToArray())).ToArray();
        var rim = localFaces.Length == 1 && localFaces[0].Length > 0
            ? localFaces[0].Append(localFaces[0][0])
                .Select(point => ToPoint(document.ToWorldPoint(item.Id, point))).ToArray()
            : Array.Empty<ScenePoint3Dto>();
        return new SceneSurface3Dto(
            number,
            item.Name,
            false,
            false,
            Material(item),
            rim,
            Array.Empty<ScenePoint3Dto>(),
            Array.Empty<ScenePoint3Dto>(),
            faces,
            RenderRole: item.Parameters switch
            {
                OptilandWorkbench.Core.NonSequential.SourceParameters => SceneSurfaceRenderRole.Source,
                DetectorRectangleParameters => SceneSurfaceRenderRole.Detector,
                _ => SceneSurfaceRenderRole.NonSequentialObject
            },
            DisplayWavelengthNanometers: SourceDisplayWavelength(document, item));
    }

    private static double? SourceDisplayWavelength(
        NonSequentialDocument document,
        NonSequentialObjectDefinition item)
    {
        if (item.Parameters is not OptilandWorkbench.Core.NonSequential.SourceParameters source
            || source.WavelengthNumber < 1
            || source.WavelengthNumber > document.Wavelengths.Count)
        {
            return null;
        }

        var wavelength = document.Wavelengths[source.WavelengthNumber - 1].Nanometers;
        return double.IsFinite(wavelength) && wavelength > 0 ? wavelength : null;
    }

    private static Vector3D[][] Plane(double width, double height) => new[]
    {
        new[]
        {
            new Vector3D(-width / 2, -height / 2, 0), new Vector3D(width / 2, -height / 2, 0),
            new Vector3D(width / 2, height / 2, 0), new Vector3D(-width / 2, height / 2, 0)
        }
    };

    private static Vector3D[][] Ellipse(double width, double height) => new[]
    {
        Enumerable.Range(0, 32).Select(index => new Vector3D(
            width / 2 * Math.Cos(2 * Math.PI * index / 32),
            height / 2 * Math.Sin(2 * Math.PI * index / 32),
            0)).ToArray()
    };

    private static Vector3D[][] Box(double width, double height, double length)
    {
        var x = width / 2; var y = height / 2; var z = length / 2;
        var p = new[]
        {
            new Vector3D(-x,-y,-z), new Vector3D(x,-y,-z), new Vector3D(x,y,-z), new Vector3D(-x,y,-z),
            new Vector3D(-x,-y,z), new Vector3D(x,-y,z), new Vector3D(x,y,z), new Vector3D(-x,y,z)
        };
        return new[]
        {
            new[] { p[0], p[3], p[2], p[1] }, new[] { p[4], p[5], p[6], p[7] },
            new[] { p[0], p[1], p[5], p[4] }, new[] { p[1], p[2], p[6], p[5] },
            new[] { p[2], p[3], p[7], p[6] }, new[] { p[3], p[0], p[4], p[7] }
        };
    }

    private static Vector3D[][] Cylinder(double radius, double length)
        => Cylinder(radius, radius, length);

    private static Vector3D[][] Cylinder(double radiusX, double radiusY, double length)
    {
        const int count = 32;
        var lower = Ring(radiusX, radiusY, -length / 2, count);
        var upper = Ring(radiusX, radiusY, length / 2, count);
        var faces = new List<Vector3D[]> { lower.Reverse().ToArray(), upper };
        for (var index = 0; index < count; index++)
        {
            var next = (index + 1) % count;
            faces.Add(new[] { lower[index], lower[next], upper[next], upper[index] });
        }
        return faces.ToArray();
    }

    private static Vector3D[][] Sphere(double radius)
        => Ellipsoid(radius, radius, radius);

    private static Vector3D[][] Ellipsoid(double radiusX, double radiusY, double radiusZ)
    {
        const int latitude = 12;
        const int longitude = 24;
        var faces = new List<Vector3D[]>();
        for (var lat = 0; lat < latitude; lat++)
        {
            var a0 = -Math.PI / 2 + Math.PI * lat / latitude;
            var a1 = -Math.PI / 2 + Math.PI * (lat + 1) / latitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                var p0 = 2 * Math.PI * lon / longitude;
                var p1 = 2 * Math.PI * (lon + 1) / longitude;
                faces.Add(new[] { Point(a0, p0), Point(a0, p1), Point(a1, p1), Point(a1, p0) });
            }
        }
        return faces.ToArray();
        Vector3D Point(double latitudeAngle, double longitudeAngle) => new(
            radiusX * Math.Cos(latitudeAngle) * Math.Cos(longitudeAngle),
            radiusY * Math.Cos(latitudeAngle) * Math.Sin(longitudeAngle),
            radiusZ * Math.Sin(latitudeAngle));
    }

    private static Vector3D[][] Lens(StandardLensParameters lens)
    {
        const int radial = 5;
        const int angular = 32;
        var front = new StandardGeometry(lens.FrontRadiusMillimeters, lens.FrontConic);
        var back = new StandardGeometry(lens.BackRadiusMillimeters, lens.BackConic);
        var frontRings = Rings(front, 0);
        var backRings = Rings(back, lens.CenterThicknessMillimeters);
        var faces = new List<Vector3D[]>();
        AddSurface(frontRings, reverse: true);
        AddSurface(backRings, reverse: false);
        var frontRim = frontRings[^1];
        var backRim = backRings[^1];
        for (var index = 0; index < angular; index++)
        {
            var next = (index + 1) % angular;
            faces.Add(new[] { frontRim[index], frontRim[next], backRim[next], backRim[index] });
        }
        return faces.ToArray();

        Vector3D[][] Rings(StandardGeometry geometry, double offset)
        {
            var values = new Vector3D[radial + 1][];
            for (var r = 0; r <= radial; r++)
            {
                var radius = lens.SemiDiameterMillimeters * r / radial;
                values[r] = Enumerable.Range(0, angular).Select(index =>
                {
                    var angle = 2 * Math.PI * index / angular;
                    var x = radius * Math.Cos(angle); var y = radius * Math.Sin(angle);
                    return new Vector3D(x, y, offset + geometry.Sag(x, y));
                }).ToArray();
            }
            return values;
        }
        void AddSurface(Vector3D[][] rings, bool reverse)
        {
            for (var r = 0; r < radial; r++)
                for (var index = 0; index < angular; index++)
                {
                    var next = (index + 1) % angular;
                    var face = new[] { rings[r][index], rings[r + 1][index], rings[r + 1][next], rings[r][next] };
                    faces.Add(reverse ? face.Reverse().ToArray() : face);
                }
        }
    }

    private static Vector3D[][] Mesh(NonSequentialMeshAsset asset)
    {
        const int maximumDisplayTriangles = 50_000;
        var geometry = asset.GetGeometry();
        var stride = Math.Max(1, (int)Math.Ceiling(geometry.Triangles.Count / (double)maximumDisplayTriangles));
        return geometry.Triangles
            .Where((_, index) => index % stride == 0)
            .Select(triangle => new[]
            {
                geometry.Vertices[triangle.A],
                geometry.Vertices[triangle.B],
                geometry.Vertices[triangle.C]
            })
            .ToArray();
    }

    private static Vector3D[] Ring(double radiusX, double radiusY, double z, int count) => Enumerable.Range(0, count)
        .Select(index => new Vector3D(radiusX * Math.Cos(2 * Math.PI * index / count), radiusY * Math.Sin(2 * Math.PI * index / count), z)).ToArray();

    private static string Material(NonSequentialObjectDefinition item) => item.Parameters switch
    {
        SphereParameters value => value.Material,
        CylinderParameters value => value.Material,
        BoxParameters value => value.Material,
        StandardLensParameters value => value.Material,
        MeshObjectParameters value => value.Material,
        DetectorRectangleParameters => "DETECTOR",
        PlaneRectangleParameters value => value.MaterialAfter,
        SourceRayParameters or SourcePointParameters or SourceRectangleParameters or SourceGaussianParameters
            or SourceEllipseParameters or SourceTwoAngleParameters or SourceRadialParameters
            or SourceVolumeRectangleParameters or SourceVolumeEllipseParameters
            or SourceVolumeCylinderParameters => "SOURCE",
        _ => string.Empty
    };

    private static int WavelengthIndex(NonSequentialDocument document, NonSequentialRayBranch branch)
    {
        var wavelength = branch.Segments.FirstOrDefault()?.WavelengthNanometers;
        if (wavelength is null) return 0;
        var index = document.Wavelengths.ToList().FindIndex(item => Math.Abs(item.Nanometers - wavelength.Value) < 1e-9);
        return Math.Max(0, index);
    }

    private static SceneRaySegmentType SegmentType(RayInteractionKind? kind) => kind switch
    {
        RayInteractionKind.Reflected => SceneRaySegmentType.Reflected,
        RayInteractionKind.TotalInternalReflection => SceneRaySegmentType.TotalInternalReflection,
        RayInteractionKind.Transmitted => SceneRaySegmentType.Transmitted,
        _ => SceneRaySegmentType.Incident
    };

    private static SceneRayInteractionType InteractionType(RayInteractionKind? kind) => kind switch
    {
        RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection => SceneRayInteractionType.Reflective,
        RayInteractionKind.Transmitted => SceneRayInteractionType.Refractive,
        _ => SceneRayInteractionType.None
    };

    private static ScenePoint3Dto ToPoint(Vector3D point) => new(point.X, point.Y, point.Z);
    private static SceneRayDirection3Dto ToDirection(Vector3D direction) => new(direction.X, direction.Y, direction.Z);
}
