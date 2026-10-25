using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>A bounded, deterministic non-sequential polygon volume between sequential ports.</summary>
public sealed class PolygonGroupGeometry : IGeometry
{
    public sealed record Face(IReadOnlyList<Vector3D> Vertices, bool Absorbing, bool Reflective);
    public string Kind => "polygon_group";
    public IReadOnlyList<Face> Faces { get; }
    public IMaterial VolumeMaterial { get; }
    public PolygonGroupGeometry(IReadOnlyList<Face> faces, IMaterial volumeMaterial)
    {
        if (faces.Count is < 4 or > 8192 || faces.Any(f => f.Vertices.Count is < 3 or > 4
                || f.Vertices.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))))
            throw new ArgumentException("Polygon group requires bounded triangular/quadrilateral faces.");
        // POB rectangles are two triangles, including the small crease when
        // decimal vertices are not exactly coplanar. Do not flatten that crease.
        var triangles = faces.SelectMany(f => f.Vertices.Count == 4
            ? new[] { f with { Vertices = [f.Vertices[0], f.Vertices[1], f.Vertices[2]] },
                f with { Vertices = [f.Vertices[0], f.Vertices[2], f.Vertices[3]] } }
            : new[] { f }).ToArray();
        if (triangles.Length > 8192) throw new ArgumentException("Polygon triangle budget exceeded.");
        foreach (var triangle in triangles)
        {
            var a=triangle.Vertices[1]-triangle.Vertices[0];var b=triangle.Vertices[2]-triangle.Vertices[0];
            var normal=new Vector3D(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
            if(!double.IsFinite(normal.Length)||normal.Length<=1e-14*a.Length*b.Length)
                throw new ArgumentException("Polygon volume contains a degenerate triangle.");
        }
        Faces = Array.AsReadOnly(triangles.Select(f => f with { Vertices = Array.AsReadOnly(f.Vertices.ToArray()) }).ToArray());
        VolumeMaterial = volumeMaterial.Clone();
    }
    public double Sag(double x, double y) => 0;
    public Vector3D SurfaceNormal(Vector3D point) => new(0, 0, 1);
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        new PlaneGeometry().DistanceToIntersection(origin, direction);
    public IGeometry Clone() => new PolygonGroupGeometry(Faces, VolumeMaterial);

    internal SurfaceRayTraceStateResult Trace(OpticalSurface port, RayState input,
        IMaterial ambient, double cumulativePath, double cumulativeOpd, bool ignoreAbsorption)
    {
        if (input.PolarizationMatrix is not null)
            throw new NotSupportedException("Polygon-group coherent polarization transport is not implemented.");
        var frame = port.CoordinateSystem;
        var ray = input with { Origin = frame.ToLocalPoint(input.Origin), Direction = frame.ToLocalDirection(input.Direction) };
        var currentMaterial = ambient;
        var path = cumulativePath;
        var opd = cumulativeOpd;
        var inside = false;
        for (var interaction = 0; interaction < 128; interaction++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var portHit = new PlaneGeometry().DistanceToIntersection(ray.Origin, ray.Direction);
            Face? selected = null;
            var nearest = double.PositiveInfinity;
            foreach (var face in Faces)
            {
                var hit = new PolygonFaceGeometry(face.Vertices).DistanceToIntersection(ray.Origin, ray.Direction);
                if (hit.IsHit && hit.Distance > 1e-8 && hit.Distance < nearest)
                { nearest = hit.Distance; selected = face; }
            }
            if (!inside && portHit.IsHit && portHit.Distance >= 0 && portHit.Distance < nearest)
            {
                var exit = new OpticalSurface { Geometry = new PlaneGeometry(), CoordinateSystem = CoordinateSystem.Global };
                var end = exit.TraceRayState(ray, currentMaterial, ambient, path, opd, ignorePhysicalAperture: true,
                    ignoreMaterialAbsorption: ignoreAbsorption);
                var globalRay = end.Ray with { Origin = frame.ToGlobalPoint(end.Ray.Origin), Direction = frame.ToGlobalDirection(end.Ray.Direction) };
                var sample = end.Sample with { SurfaceNumber = port.Number, SurfaceLabel = port.Label,
                    Position = globalRay.Origin, Direction = globalRay.Direction,
                    IncidentDirection = end.Sample.IncidentDirection is { } incoming ? frame.ToGlobalDirection(incoming) : null,
                    SegmentLength = end.CumulativePathLength - cumulativePath,
                    SegmentOpticalPathLength = end.CumulativeOpticalPathLength - cumulativeOpd };
                return end with { Ray = globalRay, Sample = sample };
            }
            if (selected is null) break;
            var surface = new OpticalSurface { Geometry = new PolygonFaceGeometry(selected.Vertices),
                CoordinateSystem = CoordinateSystem.Global, IsReflective = selected.Reflective };
            if (selected.Absorbing) break;
            var next = inside ? ambient : VolumeMaterial;
            var result = surface.TraceRayState(ray, currentMaterial, next, path, opd,
                ignorePhysicalAperture: true, ignoreMaterialAbsorption: ignoreAbsorption);
            if (result.StopTracing) break;
            if (result.Sample.InteractionKind == OpticalSystemDesign.Core.Interactions.RayInteractionKind.Transmitted) inside = !inside;
            ray = result.Ray;
            currentMaterial = result.OutgoingMaterial;
            path = result.CumulativePathLength;
            opd = result.CumulativeOpticalPathLength;
        }
        var missed = input with { Intensity = 0 };
        return new SurfaceRayTraceStateResult(missed,
            new RayTraceSampleValue(port.Number, port.Label, input.Origin, input.Direction, 0, true,
                CumulativePathLength: path, CumulativeOpticalPathLength: opd),
            ambient.RefractiveIndex(input.WavelengthNanometers), ambient, null, path, opd, true);
    }
}

internal sealed class PolygonFaceGeometry(IReadOnlyList<Vector3D> vertices) : IGeometry
{
    public string Kind => "polygon_face";
    public double Sag(double x, double y) => throw new NotSupportedException();
    public IGeometry Clone() => new PolygonFaceGeometry(vertices.ToArray());
    public Vector3D SurfaceNormal(Vector3D point)
    {
        var a = vertices[1] - vertices[0]; var b = vertices[2] - vertices[0];
        var n = Cross(a, b); return n / n.Length;
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        var normal = SurfaceNormal(vertices[0]);
        var denominator = Dot(normal, direction);
        if (Math.Abs(denominator) < 1e-15) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var distance = Dot(normal, vertices[0] - origin) / denominator;
        if (distance < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var point = origin + direction * distance;
        double sign = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var side = Dot(Cross(vertices[(i + 1) % vertices.Count] - vertices[i], point - vertices[i]), normal);
            if (Math.Abs(side) <= 1e-8) continue;
            if (sign != 0 && Math.Sign(side) != Math.Sign(sign)) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
            sign = side;
        }
        return new IntersectionResult(IntersectionStatus.Success, distance, point, normal, 0, 0, 1);
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
}
