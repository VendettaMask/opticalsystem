using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using static OpticalSystemDesign.Core.NonSequential.OpticalAssemblySurfaceFrame;

namespace OpticalSystemDesign.Core.NonSequential;

internal readonly record struct OpticalAssemblyBoundaryHit(double Distance, Vector3D Point, Vector3D Normal, int FaceNumber);

/// <summary>Exact caps and ruled rims, with a common injectivity certificate for the whole piecewise solid.</summary>
internal sealed class OpticalAssemblyBoundaryModel
{
    private sealed record Cap(NonSequentialOpticalInterface Face, RotationalSurfaceDomain Domain, Vector3D U, Vector3D V, Vector3D EdgeCenter);
    private readonly Cap[] _caps;
    private readonly RuledCircularRim[] _rims;
    public Vector3D Minimum { get; }
    public Vector3D Maximum { get; }
    public double CertificateMargin { get; }

    public OpticalAssemblyBoundaryModel(IReadOnlyList<NonSequentialOpticalInterface> faces)
    {
        _caps = new Cap[faces.Count];
        var previousX = faces[0].Frame!.AxisX;
        var minima = new List<Vector3D>(); var maxima = new List<Vector3D>();
        for (int i = 0; i < faces.Count; i++)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested();
            var face = faces[i]; var frame = face.Frame!; var radius = face.MechanicalSemiDiameterMillimeters;
            var domain = new RotationalSurfaceDomain(face.Geometry, radius);
            if (frame.AxisZ.Z - domain.MaximumSlope * Math.Sqrt(frame.AxisZ.X * frame.AxisZ.X + frame.AxisZ.Y * frame.AxisZ.Y) <= 1e-8)
                throw new NotSupportedException($"S{face.SourceSurfaceNumber} 不能在组件参考轴上保持正则单值曲面。");
            // Parallel transport the rim seam. Aperture roll stays in the actual
            // surface frame; it must not create an artificial twist of a circular edge.
            var x = previousX - frame.AxisZ * Dot(previousX, frame.AxisZ);
            if (x.Length <= 1e-10) throw new NotSupportedException("相邻机械圆边无法建立正则连接。");
            x /= x.Length; var y = Cross(frame.AxisZ, x); previousX = x;
            _caps[i] = new(face, domain, x * radius, y * radius, frame.ToAssemblyPoint(new(0, 0, face.Geometry.Sag(radius, 0))));
            var sag = domain.SagBounds;
            (double Low, double High) Bound(double origin, double ax, double ay, double az)
            {
                var extent = radius * Math.Sqrt(ax * ax + ay * ay); var a = az * sag.Low; var b = az * sag.High;
                return (origin - extent + Math.Min(a, b), origin + extent + Math.Max(a, b));
            }
            var bx = Bound(frame.Origin.X, frame.AxisX.X, frame.AxisY.X, frame.AxisZ.X);
            var by = Bound(frame.Origin.Y, frame.AxisX.Y, frame.AxisY.Y, frame.AxisZ.Y);
            var bz = Bound(frame.Origin.Z, frame.AxisX.Z, frame.AxisY.Z, frame.AxisZ.Z);
            minima.Add(new(bx.Low, by.Low, bz.Low)); maxima.Add(new(bx.High, by.High, bz.High));
        }
        Minimum = new(minima.Min(p => p.X), minima.Min(p => p.Y), minima.Min(p => p.Z));
        Maximum = new(maxima.Max(p => p.X), maxima.Max(p => p.Y), maxima.Max(p => p.Z));
        CertificateMargin = Certify();
        _rims = Enumerable.Range(0, faces.Count - 1).Select(i => new RuledCircularRim(_caps[i].EdgeCenter, _caps[i].U, _caps[i].V,
            _caps[i + 1].EdgeCenter, _caps[i + 1].U, _caps[i + 1].V)).ToArray();
    }

    private double Certify()
    {
        // F(x,y,z) linearly joins each pair of exact rotational disks on a
        // common normalized disk x²+y²<=1. A single positive diagonal metric
        // certifies the symmetric Jacobian on every piece. Strong monotonicity
        // then excludes self-intersections across the entire assembly, including air gaps.
        var referenceRadius = _caps.Max(c => c.Face.MechanicalSemiDiameterMillimeters);
        var regions = new List<(double[] Diagonal, double[,] Off)>();
        for (int i = 0; i < _caps.Length - 1; i++)
        {
            var a = _caps[i]; var b = _caps[i + 1]; var na = a.Face.Frame!.AxisZ; var nb = b.Face.Frame!.AxisZ;
            var delta = b.Face.Frame.Origin - a.Face.Frame.Origin; var dz = delta.Z;
            if (dz <= 1e-9) throw new InvalidDataException("逐面参考顶点必须沿组件参考轴正序，间隔大于1e-9 mm。");
            var du = b.U - a.U; var dv = b.V - a.V;
            var la = a.Domain.MaximumSlope * a.Face.MechanicalSemiDiameterMillimeters;
            var lb = b.Domain.MaximumSlope * b.Face.MechanicalSemiDiameterMillimeters;
            double Lower(double va, double wa, double vb, double wb) => Math.Min(va - Math.Abs(wa) * la, vb - Math.Abs(wb) * lb);
            double Upper(double va, double wa, double vb, double wb) => Math.Max(Math.Abs(va) + Math.Abs(wa) * la, Math.Abs(vb) + Math.Abs(wb) * lb);
            var sx = RotationalSurfaceDomain.Difference(a.Domain, na.X, b.Domain, nb.X);
            var sy = RotationalSurfaceDomain.Difference(a.Domain, na.Y, b.Domain, nb.Y);
            var sz = RotationalSurfaceDomain.Difference(a.Domain, na.Z, b.Domain, nb.Z);
            var diagonal = new[] { Lower(a.U.X, na.X, b.U.X, nb.X) / referenceRadius, Lower(a.V.Y, na.Y, b.V.Y, nb.Y) / referenceRadius,
                (dz - Math.Sqrt(du.Z * du.Z + dv.Z * dv.Z) + sz.Low) / dz };
            var off = new double[3, 3];
            off[0, 1] = Upper(a.V.X, na.X, b.V.X, nb.X) / referenceRadius;
            off[1, 0] = Upper(a.U.Y, na.Y, b.U.Y, nb.Y) / referenceRadius;
            off[2, 0] = Upper(a.U.Z, na.Z, b.U.Z, nb.Z); off[2, 1] = Upper(a.V.Z, na.Z, b.V.Z, nb.Z);
            off[0, 2] = (Math.Abs(delta.X) + Math.Sqrt(du.X * du.X + dv.X * dv.X) + Math.Max(Math.Abs(sx.Low), Math.Abs(sx.High))) / (dz * referenceRadius);
            off[1, 2] = (Math.Abs(delta.Y) + Math.Sqrt(du.Y * du.Y + dv.Y * dv.Y) + Math.Max(Math.Abs(sy.Low), Math.Abs(sy.High))) / (dz * referenceRadius);
            regions.Add((diagonal, off));
        }
        double best = double.NegativeInfinity;
        for (int exponent = -40; exponent <= 40; exponent++)
        {
            var metric = new[] { 1d, 1d, referenceRadius * Math.Pow(2, exponent / 2d) }; double margin = double.PositiveInfinity;
            foreach (var region in regions) for (int row = 0; row < 3; row++)
            {
                double radius = 0;
                for (int col = 0; col < 3; col++) if (col != row)
                    radius += .5 * (region.Off[row, col] * metric[col] / metric[row] + region.Off[col, row] * metric[row] / metric[col]);
                margin = Math.Min(margin, region.Diagonal[row] - radius);
            }
            best = Math.Max(best, margin);
        }
        if (!double.IsFinite(best) || best <= 1e-10)
            throw new NotSupportedException("完整机械口径的非球面/偏心/倾斜连接未通过全组件无交叠证书；不发布近似或部分镜组。");
        return best;
    }

    public OpticalAssemblyBoundaryHit? Intersect(Vector3D origin, Vector3D direction, OpticalAssemblyParameters assembly)
    {
        OpticalAssemblyBoundaryHit? nearest = null; double best = double.PositiveInfinity;
        for (int i = 0; i < _caps.Length; i++)
        {
            if ((i & 31) == 0) Services.ComputationCancellation.ThrowIfCancellationRequested();
            var hit = CapHit(i, origin, direction);
            if (hit is { } found && found.Distance < best) { nearest = found; best = found.Distance; }
        }
        for (int i = 0; i < _rims.Length; i++)
        {
            if (assembly.IsAmbientRegion(i)) continue;
            var hit = _rims[i].Intersect(origin, direction, best);
            if (hit is { } found) { best = found.Distance; nearest = new(best, found.Point, found.Normal, _caps.Length + i + 1); }
        }
        return nearest;
    }

    public Vector3D SurfacePoint(int faceIndex,double radiusFraction,double azimuth)
    {
        var cap=_caps[faceIndex];var radius=cap.Face.MechanicalSemiDiameterMillimeters*radiusFraction;
        return cap.Face.Frame!.Origin+(cap.U*Math.Cos(azimuth)+cap.V*Math.Sin(azimuth))*radiusFraction
            +cap.Face.Frame.AxisZ*cap.Face.Geometry.Sag(radius,0);
    }

    private OpticalAssemblyBoundaryHit? CapHit(int index, Vector3D origin, Vector3D direction)
    {
        var cap = _caps[index]; var frame = cap.Face.Frame!;
        var hit = cap.Domain.Intersect(frame.ToSurfacePoint(origin), frame.ToSurfaceDirection(direction));
        if (!hit.IsHit || hit.Distance <= 0) return null;
        var normal = hit.Normal.Z < 0 ? -hit.Normal : hit.Normal;
        return new(hit.Distance, frame.ToAssemblyPoint(hit.Point), frame.ToAssemblyDirection(normal) * (index == 0 ? -1 : 1), index + 1);
    }

    public int? RegionAt(Vector3D point, OpticalAssemblyParameters assembly)
    {
        if (point.X < Minimum.X || point.X > Maximum.X || point.Y < Minimum.Y || point.Y > Maximum.Y || point.Z < Minimum.Z || point.Z > Maximum.Z) return null;
        var direction = new Vector3D(0, 0, 1);
        for (int i = 0; i < _rims.Length; i++)
        {
            if (assembly.IsAmbientRegion(i)) continue;
            var a = CapHit(i, point, direction); var b = CapHit(i + 1, point, direction);
            var side = _rims[i].Intersect(point, direction); double best = double.PositiveInfinity; double exitCosine = 0;
            if (a is { } ha) { best = ha.Distance; exitCosine = Dot(direction, ha.Normal) * (i == 0 ? 1 : -1); }
            if (b is { } hb && hb.Distance < best) { best = hb.Distance; exitCosine = Dot(direction, hb.Normal); }
            if (side is { } hs && hs.Distance < best) { best = hs.Distance; exitCosine = Dot(direction, hs.Normal); }
            if (double.IsFinite(best) && exitCosine > 0) return i;
        }
        return null;
    }
}
