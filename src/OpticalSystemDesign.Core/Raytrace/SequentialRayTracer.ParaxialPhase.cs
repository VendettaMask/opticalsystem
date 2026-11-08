using System.Collections.Concurrent;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class SequentialRayTracer
{
    // This path is exclusively for geometric aiming and phase integration. It
    // never enters the physical trace cache or supplies throughput/display data.
    private RayTraceSampleValue TraceIncidentGeometry(RealRay source, int target)
    {
        SequentialTraceMeasurement.Record(1);
        var ray = RayState.FromRealRay(source.Normalize());
        var material = ResolveMaterial("Air");
        double path = 0, opticalPath = 0;
        for (var index = 0; index <= target; index++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var result = TraceSequentialSurface(_optic.SurfaceGroup.Items[index], index, ray, material, path, opticalPath,
                ignorePhysicalAperture: true, stopBeforeInteraction: index == target,
                bypassCoating: true, allowVirtualIntersection: AllowsReferenceBacktrack(_optic.SurfaceGroup.Items, index),
                ignoreMaterialAbsorption: true, geometryOnly: true);
            if (result.StopTracing || result.Sample.Vignetted
                || result.InteractionKind == RayInteractionKind.TotalInternalReflection)
                throw new ArithmeticException($"近轴相位的入射光束在表面 {index} 无有效几何分支。");
            if (index == target) return result.Sample;
            ray = result.Ray; material = result.OutgoingMaterial;
            path = result.CumulativePathLength; opticalPath = result.CumulativeOpticalPathLength;
        }
        throw new ArgumentOutOfRangeException(nameof(target));
    }

    // One physical Trace call owns this bounded cache. No cross-revision state,
    // labels or omitted source coordinates can affect the beam's identity.
    private sealed class ParaxialPhaseTrace(SequentialRayTracer owner)
    {
        private readonly ConcurrentDictionary<BeamKey, Lazy<Chief>> _chiefs = new();
        private readonly record struct BeamKey(int Surface, Vector3D Source, double Wavelength, bool Infinite);
        private sealed record Chief(Vector3D Position, Vector3D Direction);

        public double Evaluate(RealRay source, int surfaceIndex, RealRay localRay, SurfaceInteractionContext context)
        {
            source = source.Normalize();
            var surface = owner._optic.SurfaceGroup.Items[surfaceIndex];
            var model = (ParaxialLensInteractionModel)surface.InteractionModel;
            if (model.OpdMode == 0 && !ParaxialPhaseRules.IsPlaneWavePrefix(owner._optic.SurfaceGroup.Items.Take(surfaceIndex).ToArray()))
                throw new NotSupportedException("模式 0 目前只验证了平面均匀介质链保持的准直光束；不能代替有限共轭或有像差的旁轴共轭计算。");
            var objectSurface = owner._optic.SurfaceGroup.Items[0];
            var infinite = ObjectConjugate.IsInfinite(objectSurface);
            if (!infinite && surfaceIndex == 1 && surface.IsStop && model.OpdMode == 1 && localRay.Direction.Z > 0
                && objectSurface is { Geometry: PlaneGeometry, InteractionModel: RefractiveReflectiveInteractionModel, IsReflective: false }
                && objectSurface.MaterialAfter.PropagationModel is HomogeneousPropagationModel
                && objectSurface.MaterialAfter.RefractiveIndex(source.WavelengthNanometers)
                    == owner.ResolveMaterial("Air").RefractiveIndex(source.WavelengthNanometers)
                && objectSurface.CoordinateSystem.ToLocalPoint(source.Origin).Z == 0)
            {
                var objectPoint = surface.CoordinateSystem.ToLocalPoint(source.Origin);
                if (objectPoint.X == 0 && objectPoint.Y == 0 && objectPoint.Z < 0)
                {
                    // The first stop sees the centered point directly. Along the
                    // chief-to-hit segment, the incident and outgoing slopes are
                    // t times their endpoint slopes. Integrate t/sqrt(1+t²q²)
                    // exactly for each direction, including unequal X/Y powers.
                    var x = localRay.Origin.X;
                    var y = localRay.Origin.Y;
                    var before = context.RefractiveIndexBefore;
                    var after = context.RefractiveIndexAfter;
                    var sx = localRay.Direction.X / localRay.Direction.Z;
                    var sy = localRay.Direction.Y / localRay.Direction.Z;
                    var tx = (before * sx - model.PowerX * x) / after;
                    var ty = (before * sy - model.PowerY * y) / after;
                    var phase = Primitive(tx, ty, after) - Primitive(sx, sy, before);
                    if (double.IsFinite(phase)) return phase;

                    double Primitive(double u, double v, double index) =>
                        index * (x * u + y * v) / (1 + double.Hypot(1, double.Hypot(u, v)));
                }
            }
            var key = new BeamKey(surfaceIndex, infinite ? source.Direction : source.Origin, source.WavelengthNanometers, infinite);
            var beam = new Beam(owner, source, infinite);
            var chief = _chiefs.GetOrAdd(key, _ => new Lazy<Chief>(() =>
            {
                var stop = owner._optic.SurfaceGroup.Items.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.IsStop).i;
                if (stop == 0) stop = Math.Min(1, owner._optic.SurfaceGroup.Items.Count - 1);
                var chiefSource = beam.Aim(stop, 0, 0).Source;
                var sample = owner.TraceIncidentGeometry(chiefSource, surfaceIndex);
                return new(surface.CoordinateSystem.ToLocalPoint(sample.Position),
                    surface.CoordinateSystem.ToLocalDirection(sample.Direction));
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            var delta = localRay.Origin - chief.Position;
            if (delta.X == 0 && delta.Y == 0) return 0;
            var values = new Dictionary<double, double>();
            double Integrand(double t)
            {
                if (values.TryGetValue(t, out var value)) return value;
                ComputationCancellation.ThrowIfCancellationRequested();
                var position = chief.Position + t * delta;
                var incident = model.OpdMode is 0 or 2 ? chief.Direction
                    : surface.CoordinateSystem.ToLocalDirection(beam.Aim(surfaceIndex, position.X, position.Y).Sample.Direction);
                var outgoing = model.Outgoing(incident, position, context.RefractiveIndexBefore, context.RefractiveIndexAfter);
                value = (context.RefractiveIndexAfter * outgoing.X - context.RefractiveIndexBefore * incident.X) * delta.X
                    + (context.RefractiveIndexAfter * outgoing.Y - context.RefractiveIndexBefore * incident.Y) * delta.Y;
                if (!double.IsFinite(value)) throw new ArithmeticException("近轴相位积分不是有限值。");
                values.Add(t, value); return value;
            }
            double Simpson(int steps)
            {
                var sum = Integrand(0) + Integrand(1);
                for (var i = 1; i < steps; i++) sum += (i % 2 == 0 ? 2 : 4) * Integrand((double)i / steps);
                return sum / (3 * steps);
            }
            var previous = Simpson(8);
            for (var steps = 16; steps <= 256; steps *= 2)
            {
                var current = Simpson(steps);
                if (Math.Abs(current - previous) <= 1e-10) return current;
                previous = current;
            }
            throw new ArithmeticException("近轴相位积分未收敛，不能返回近似完成的波前。");
        }

        private sealed class Beam
        {
            private readonly SequentialRayTracer _owner;
            private readonly RealRay _source;
            private readonly bool _infinite;
            private readonly Vector3D _x, _y;

            public Beam(SequentialRayTracer owner, RealRay source, bool infinite)
            {
                _owner = owner; _source = source; _infinite = infinite;
                var axis = Math.Abs(source.Direction.X) < .8 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
                var x = axis - Dot(axis, source.Direction) * source.Direction; _x = x / x.Length;
                _y = Cross(source.Direction, _x);
            }

            public (RealRay Source, RayTraceSampleValue Sample) Aim(int target, double targetX, double targetY)
            {
                var coordinates = _owner._optic.SurfaceGroup.Items[target].CoordinateSystem;
                RealRay Create(double x, double y) => _infinite
                    ? _source with { Origin = _source.Origin + x * _x + y * _y }
                    : _source with { Direction = _source.Direction + x * _x + y * _y, IsNormalized = false };
                RayTraceSampleValue Evaluate(double x, double y) => _owner.TraceIncidentGeometry(Create(x, y), target);
                double u = 0, v = 0;
                var step = _infinite ? 1e-4 : 1e-6;
                for (var iteration = 0; iteration < 16; iteration++)
                {
                    var sample = Evaluate(u, v); var p = coordinates.ToLocalPoint(sample.Position);
                    var ex = p.X - targetX; var ey = p.Y - targetY;
                    if (Math.Max(Math.Abs(ex), Math.Abs(ey)) <= 1e-10)
                        return (Create(u, v).Normalize(), sample);
                    var px = coordinates.ToLocalPoint(Evaluate(u + step, v).Position);
                    var py = coordinates.ToLocalPoint(Evaluate(u, v + step).Position);
                    var a = (px.X - p.X) / step; var b = (py.X - p.X) / step;
                    var c = (px.Y - p.Y) / step; var d = (py.Y - p.Y) / step;
                    var determinant = a * d - b * c;
                    if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-20)
                        throw new ArithmeticException("近轴相位光束的反向瞄准遇到奇异映射。");
                    var du = (d * ex - b * ey) / determinant; var dv = (a * ey - c * ex) / determinant;
                    var length = Math.Sqrt(du * du + dv * dv);
                    var limit = _infinite ? 10000 : 1;
                    var scale = length > limit ? limit / length : 1;
                    u -= scale * du; v -= scale * dv;
                }
                throw new ArithmeticException("近轴相位光束未瞄准目标面；保留失败，不返回零相位。");
            }
            private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            private static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
    }
}
