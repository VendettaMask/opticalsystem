using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;

namespace OpticalSystemDesign.Core.Visualization;

public sealed partial class Layout2DBuilder
{
    // A rectangular planar assembly has a shared mechanical footprint. Matching
    // local radii on tilted interfaces produces neither that footprint nor a solid.
    private static bool TryPlanarVolumes(IReadOnlyList<OpticalSurface> group,
        out IReadOnlyList<PlanarDisplayVolume> volumes)
    {
        volumes = [];
        if (group.Count < 2 || group[0].PhysicalAperture is not RectangularAperture rectangle
            || group.Any(s => !Flat(s) || s.Drawing.EdgeMode != SurfaceEdgeMode.SquareToNext)) return false;
        var frame = group[0].CoordinateSystem;
        var planes = new List<DisplayPlane>();
        foreach (var surface in group)
        {
            var normal = frame.ToLocalDirection(surface.CoordinateSystem.ToGlobalDirection(
                surface.Geometry.SurfaceNormal(new(0, 0, surface.Geometry.Sag(0, 0)))));
            if (Math.Abs(normal.Z) < 1e-9) return false;
            var origin = frame.ToLocalPoint(surface.CoordinateSystem.ToGlobalPoint(new(0, 0, surface.Geometry.Sag(0, 0))));
            planes.Add(new(origin.Z + normal.X * origin.X / normal.Z + normal.Y * origin.Y / normal.Z,
                -normal.X / normal.Z, -normal.Y / normal.Z));
        }
        if (planes.All(p => Math.Abs(p.X - planes[0].X) < 1e-9 && Math.Abs(p.Y - planes[0].Y) < 1e-9)) return false;
        var width = rectangle.HalfWidth + group[0].ChipZone;
        var height = rectangle.HalfHeight + group[0].ChipZone;
        var footprint = new[]
        {
            new FootprintPoint(rectangle.CenterX - width, rectangle.CenterY - height),
            new FootprintPoint(rectangle.CenterX + width, rectangle.CenterY - height),
            new FootprintPoint(rectangle.CenterX + width, rectangle.CenterY + height),
            new FootprintPoint(rectangle.CenterX - width, rectangle.CenterY + height)
        };
        var result = new List<PlanarDisplayVolume>();
        for (var i = 0; i + 1 < group.Count; i++)
        {
            var front = planes[i]; var back = planes[i + 1];
            double Gap(FootprintPoint p) => back.Z(p) - front.Z(p);
            if (footprint.All(p => Math.Abs(Gap(p)) < 1e-9)) continue; // Coincident interfaces have no glass volume.
            var sign = Math.CopySign(1, back.Z(new(rectangle.CenterX, rectangle.CenterY))
                - front.Z(new(rectangle.CenterX, rectangle.CenterY)));
            var clipped = new List<FootprintPoint>();
            for (var j = 0; j < footprint.Length; j++)
            {
                var a = footprint[j]; var b = footprint[(j + 1) % footprint.Length];
                var ga = sign * Gap(a); var gb = sign * Gap(b);
                if (ga >= -1e-9) clipped.Add(a);
                if (ga < -1e-9 && gb > 1e-9 || ga > 1e-9 && gb < -1e-9)
                {
                    var t = ga / (ga - gb);
                    clipped.Add(new(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
                }
            }
            if (clipped.Count < 3) continue;
            Layout3DPoint Point(DisplayPlane plane, FootprintPoint p) =>
                ToLayoutPoint(frame.ToGlobalPoint(new(p.X, p.Y, plane.Z(p))));
            var f = clipped.Select(p => Point(front, p)).ToArray();
            var bPoints = clipped.Select(p => Point(back, p)).ToArray();
            result.Add(new(group[i], group[i + 1], f.Append(f[0]).ToArray(), bPoints.Append(bPoints[0]).ToArray(),
                [new(f.Reverse().ToArray())], [new(bPoints)], ProjectedHull(f.Concat(bPoints))));
        }
        volumes = result;
        return true;
        static bool Flat(OpticalSurface s) => s.Geometry is PlaneGeometry
            || s.Geometry is StandardGeometry g && (g.Radius == 0 || double.IsInfinity(g.Radius));
    }

    private Dictionary<int, PlanarDisplayFace> PlanarDisplayFaces()
    {
        var faces = new Dictionary<int, PlanarDisplayFace>();
        foreach (var group in BuildLensGroups())
            if (TryPlanarVolumes(group, out var volumes))
                foreach (var volume in volumes)
                {
                    faces[volume.Front.Number] = new(volume.FrontRim, volume.FrontFaces);
                    faces[volume.Back.Number] = new(volume.BackRim, volume.BackFaces);
                }
        return faces;
    }

    private static IReadOnlyList<Layout2DPoint> ProjectedHull(IEnumerable<Layout3DPoint> points)
    {
        var sorted = points.Select(p => new Layout2DPoint(p.Z, p.Y)).Distinct().OrderBy(p => p.Z).ThenBy(p => p.Y).ToArray();
        if (sorted.Length <= 2) return sorted;
        var span = Math.Max(sorted[^1].Z - sorted[0].Z, sorted.Max(p => p.Y) - sorted.Min(p => p.Y));
        var collinearity = 1e-12 * span * span;
        var lower = new List<Layout2DPoint>(); var upper = new List<Layout2DPoint>();
        void Add(List<Layout2DPoint> hull, Layout2DPoint p)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= collinearity) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        foreach (var p in sorted) Add(lower, p);
        foreach (var p in sorted.Reverse()) Add(upper, p);
        return lower.Take(lower.Count - 1).Concat(upper.Take(upper.Count - 1)).ToArray();
        static double Cross(Layout2DPoint a, Layout2DPoint b, Layout2DPoint c) =>
            (b.Z - a.Z) * (c.Y - a.Y) - (b.Y - a.Y) * (c.Z - a.Z);
    }

    private readonly record struct FootprintPoint(double X, double Y);
    private readonly record struct DisplayPlane(double Offset, double X, double Y)
    {
        internal double Z(FootprintPoint p) => Offset + X * p.X + Y * p.Y;
    }
    private sealed record PlanarDisplayFace(IReadOnlyList<Layout3DPoint> Rim, IReadOnlyList<Layout3DSurfaceFace> Faces);
    private sealed record PlanarDisplayVolume(OpticalSurface Front, OpticalSurface Back,
        IReadOnlyList<Layout3DPoint> FrontRim, IReadOnlyList<Layout3DPoint> BackRim,
        IReadOnlyList<Layout3DSurfaceFace> FrontFaces, IReadOnlyList<Layout3DSurfaceFace> BackFaces,
        IReadOnlyList<Layout2DPoint> Outline);
}
