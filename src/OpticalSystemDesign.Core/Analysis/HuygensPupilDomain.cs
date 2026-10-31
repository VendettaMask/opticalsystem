using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Exact aperture preimages for the unpowered, homogeneous planar prefix.
/// Geometry probes establish coordinates only; integration rays retain all real apertures.</summary>
internal sealed class HuygensPupilDomain
{
    private readonly record struct Point(double X, double Y)
    {
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, double b) => new(a.X * b, a.Y * b);
        public double Length => Math.Sqrt(X * X + Y * Y);
    }
    private sealed record Boundary(Point A, Point B, double Radius = 0)
    {
        public bool Circle => Radius > 0;
    }
    private readonly List<Boundary> boundaries = new() { new(new(0, 0), default, 1) };
    private readonly List<Func<Point, bool>> masks = new();
    private double[] angles = Array.Empty<double>();
    public int BoundaryCount => boundaries.Count;

    public static HuygensPupilDomain? TryCreate(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, bool aimAtStop)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (!ObjectConjugate.IsInfinite(surfaces.FirstOrDefault()) || optic.FieldDefinition != FieldDefinitionKind.Angle)
            return null;
        static bool Planar(OpticalSurface s) => s.Geometry is PlaneGeometry
            || s.Geometry is StandardGeometry g && (g.Radius == 0 || double.IsInfinity(g.Radius));
        static bool AffineInteraction(OpticalSurface s) => s.InteractionModel is RefractiveReflectiveInteractionModel
            && s.MaterialBefore.PropagationModel is HomogeneousPropagationModel
            && s.MaterialAfter.PropagationModel is HomogeneousPropagationModel;
        var stop = surfaces.ToList().FindIndex(s => s.IsStop);
        if (aimAtStop && (stop < 1 || surfaces.Skip(1).Take(stop).Any(s => !Planar(s) || !AffineInteraction(s))))
            return null;
        var domain = new HuygensPupilDomain();
        var probes = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.Hx, field.Hy,
            wavelength.Micrometers, new[] { new PupilSample(0, 0, 1), new(1, 0, 1), new(0, 1, 1) }, aimAtStop);
        for (var i = 1; i < surfaces.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var surface = surfaces[i];
            if (!Planar(surface)) break;
            if (surface.PhysicalAperture is { } aperture)
            {
                var positions = probes.Rays.Select(ray => optic.SequentialRayTracer.TraceToSurface(ray, i))
                    .Select(sample => sample is null ? (Vector3D?)null : surface.CoordinateSystem.ToLocalPoint(sample.Position)).ToArray();
                if (positions.Any(p => p is null)) return null;
                var o = new Point(positions[0]!.Value.X, positions[0]!.Value.Y);
                var a = new Point(positions[1]!.Value.X, positions[1]!.Value.Y) - o;
                var b = new Point(positions[2]!.Value.X, positions[2]!.Value.Y) - o;
                var determinant = Cross(a, b);
                if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-20) return null;
                Point Inverse(Point p) => new(Cross(p - o, b) / determinant, Cross(a, p - o) / determinant);
                var scale = a.Length;
                var similarity = Math.Abs(a.Length - b.Length) <= 1e-11 * scale
                    && Math.Abs(a.X * b.X + a.Y * b.Y) <= 1e-11 * scale * scale;
                // Leave a provably nonrestricting circular aperture out of the partition.
                if (aperture is CircularAperture full && o.Length + Math.Max(a.Length, b.Length) <= full.Radius && similarity)
                {
                    if (!AffineInteraction(surface)) break;
                    continue;
                }
                var start = domain.boundaries.Count;
                bool AddShape(IPhysicalAperture shape)
                {
                    void Circle(double x, double y, double radius)
                    {
                        if (radius > 0) domain.boundaries.Add(new(Inverse(new(x, y)), default, radius / scale));
                    }
                    void Polygon(IReadOnlyList<(double X, double Y)> vertices)
                    {
                        for (var v = 0; v < vertices.Count; v++)
                        {
                            var p = vertices[v]; var q = vertices[(v + 1) % vertices.Count];
                            domain.boundaries.Add(new(Inverse(new(p.X, p.Y)), Inverse(new(q.X, q.Y))));
                        }
                    }
                    switch (shape)
                    {
                        case CircularAperture c when similarity: Circle(0, 0, c.Radius + CircularAperture.BoundaryTolerance(c.Radius)); break;
                        case AnnularAperture c when similarity: Circle(0, 0, c.OuterRadius); Circle(0, 0, c.InnerRadius); break;
                        case OffsetRadialAperture c when similarity: Circle(c.OffsetX, c.OffsetY, c.OuterRadius); Circle(c.OffsetX, c.OffsetY, c.InnerRadius); break;
                        case RectangularAperture r:
                            Polygon(new[] { (r.XMinimum, r.YMinimum), (r.XMaximum, r.YMinimum), (r.XMaximum, r.YMaximum), (r.XMinimum, r.YMaximum) }); break;
                        case PolygonAperture p when p.Vertices.Count >= 3 && p.Vertices.Count <= 128: Polygon(p.Vertices); break;
                        case BooleanAperture c: return AddShape(c.Left) && AddShape(c.Right);
                        default: return false;
                    }
                    return domain.boundaries.Count <= 128;
                }
                if (AddShape(aperture))
                    domain.masks.Add(p => { var local = o + a * p.X + b * p.Y; return aperture.Contains(new(local.X, local.Y, 0)); });
                else domain.boundaries.RemoveRange(start, domain.boundaries.Count - start);
            }
            if (!AffineInteraction(surface)) break;
        }
        if (domain.masks.Count == 0) return null;
        domain.BuildAngles();
        return domain;
    }

    // The one-dimensional rule comes from the same Core Gauss-Legendre implementation.
    private static (double T, double W)[] UnitRule(int order) => ApertureSampler.GenerateHuygensGaussianQuadrature(order, 4, 0)
        .Where((_, i) => i % 4 == 0).Select(p => (p.X * p.X, p.Weight * 4)).ToArray();

    public IReadOnlyList<PupilSample> Generate(int radial, int angular, bool alternate, int maximumNodes)
    {
        var radialRule = UnitRule(radial);
        var nodes = new List<PupilSample>();
        void AtAngle(double phi, double angularWeight)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var direction = new Point(Math.Cos(phi), Math.Sin(phi));
            var cuts = new List<double> { 0, 1 };
            foreach (var edge in boundaries)
            {
                if (edge.Circle)
                {
                    var along = edge.A.X * direction.X + edge.A.Y * direction.Y;
                    var perpendicular = Cross(edge.A, direction);
                    var discriminant = edge.Radius * edge.Radius - perpendicular * perpendicular;
                    if (discriminant >= 0) { var root = Math.Sqrt(discriminant); Cut(along - root); Cut(along + root); }
                }
                else
                {
                    var segment = edge.B - edge.A; var denominator = Cross(direction, segment);
                    if (Math.Abs(denominator) > 1e-18)
                    {
                        var t = Cross(edge.A, direction) / denominator;
                        if (t >= 0 && t <= 1) Cut(Cross(edge.A, segment) / denominator);
                    }
                }
            }
            cuts.Sort();
            for (var j = 1; j < cuts.Count; j++)
            {
                var lo = cuts[j - 1]; var hi = cuts[j];
                if (hi - lo <= 1e-14 || !masks.All(mask => mask(direction * ((lo + hi) / 2)))) continue;
                if (radial > maximumNodes - nodes.Count) throw new HuygensNodeBudgetException();
                foreach (var (t, w) in radialRule)
                {
                    // Integrate r directly, with the polar Jacobian. Using r²
                    // introduces a sqrt endpoint into asymmetric complex fields.
                    var r = lo + (hi - lo) * t;
                    nodes.Add(new(direction.X * r, direction.Y * r, angularWeight * 2 * r * (hi - lo) * w));
                }
            }
            void Cut(double r) { if (r > 0 && r < 1) cuts.Add(r); }
        }
        if (angles.Length == 2)
        {
            for (var i = 0; i < angular; i++) AtAngle((i + (alternate ? .5 : 0)) * 2 * Math.PI / angular, 1d / angular);
        }
        else for (var sector = 1; sector < angles.Length; sector++)
        {
            var lo = angles[sector - 1]; var span = angles[sector] - lo;
            // Cosine endpoint map removes square-root circle-tangency singularities.
            // Alternate Gauss order is an independent interior rule, never a shift across a boundary.
            var order = Math.Max(Math.Max(4, (angular + 7) / 8),
                (int)Math.Ceiling(angular * span / (2 * Math.PI))) + (alternate ? 1 : 0);
            foreach (var (u, w) in UnitRule(order))
                AtAngle(lo + span * (1 - Math.Cos(Math.PI * u)) / 2, w * span * Math.Sin(Math.PI * u) / 4);
        }
        return nodes;
    }

    private static double Cross(Point a, Point b) => a.X * b.Y - a.Y * b.X;
    private void BuildAngles()
    {
        var seeds = new List<double> { 0, 2 * Math.PI };
        void Angle(Point p) { if (p.Length > 1e-14) Seed(Math.Atan2(p.Y, p.X)); }
        void Seed(double a) { a %= 2 * Math.PI; if (a < 0) a += 2 * Math.PI; seeds.Add(a); }
        foreach (var edge in boundaries)
        {
            if (!edge.Circle) { Angle(edge.A); Angle(edge.B); }
            else if (edge.A.Length > edge.Radius)
            {
                var phi = Math.Atan2(edge.A.Y, edge.A.X); var delta = Math.Asin(edge.Radius / edge.A.Length);
                Seed(phi - delta); Seed(phi + delta);
            }
        }
        for (var i = 0; i < boundaries.Count; i++) for (var j = i + 1; j < boundaries.Count; j++)
        {
            var a = boundaries[i]; var b = boundaries[j];
            if (a.Circle && b.Circle)
            {
                var d = b.A - a.A; var length = d.Length;
                if (length <= 1e-14 || length > a.Radius + b.Radius || length < Math.Abs(a.Radius - b.Radius)) continue;
                var along = (a.Radius * a.Radius - b.Radius * b.Radius + length * length) / (2 * length);
                var height = Math.Sqrt(Math.Max(0, a.Radius * a.Radius - along * along));
                var center = a.A + d * (along / length); var normal = new Point(-d.Y, d.X) * (height / length);
                Angle(center + normal); Angle(center - normal);
            }
            else if (!a.Circle && !b.Circle)
            {
                var da = a.B - a.A; var db = b.B - b.A; var denominator = Cross(da, db);
                if (Math.Abs(denominator) < 1e-18) continue;
                var t = Cross(b.A - a.A, db) / denominator; var u = Cross(b.A - a.A, da) / denominator;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) Angle(a.A + da * t);
            }
            else
            {
                var circle = a.Circle ? a : b; var line = a.Circle ? b : a;
                var d = line.B - line.A; var o = line.A - circle.A;
                var aa = d.X * d.X + d.Y * d.Y; var bb = 2 * (o.X * d.X + o.Y * d.Y);
                var cc = o.X * o.X + o.Y * o.Y - circle.Radius * circle.Radius;
                var discriminant = bb * bb - 4 * aa * cc;
                if (aa == 0 || discriminant < 0) continue;
                foreach (var t in new[] { (-bb - Math.Sqrt(discriminant)) / (2 * aa), (-bb + Math.Sqrt(discriminant)) / (2 * aa) })
                    if (t >= 0 && t <= 1) Angle(line.A + d * t);
            }
        }
        seeds.Sort(); var unique = new List<double>();
        foreach (var a in seeds) if (unique.Count == 0 || a - unique[^1] > 1e-13) unique.Add(a);
        if (2 * Math.PI - unique[^1] > 1e-13) unique.Add(2 * Math.PI);
        angles = unique.ToArray();
    }
}

internal sealed class HuygensNodeBudgetException : Exception { }
