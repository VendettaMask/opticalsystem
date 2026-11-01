using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Geometries;

internal sealed partial class RotationalSurfaceDomain
{
    public IntersectionResult Intersect(Vector3D origin, Vector3D direction)
    {
        double lo = 0, hi = double.PositiveInfinity;
        var a = direction.X * direction.X + direction.Y * direction.Y;
        var b = 2 * (origin.X * direction.X + origin.Y * direction.Y);
        var c = origin.X * origin.X + origin.Y * origin.Y - ApertureRadius * ApertureRadius;
        if (a == 0) { if (c > 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot); }
        else
        {
            var discriminant = b * b - 4 * a * c; if (discriminant < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
            var q = -.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
            var t0 = q == 0 ? -b / (2 * a) : q / a; var t1 = q == 0 ? t0 : c / q;
            lo = Math.Max(lo, Math.Min(t0, t1)); hi = Math.Min(hi, Math.Max(t0, t1));
        }
        if (Math.Abs(direction.Z) < 1e-30)
        {
            if (origin.Z < SagBounds.Low || origin.Z > SagBounds.High) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        }
        else
        {
            var t0 = (SagBounds.Low - origin.Z) / direction.Z; var t1 = (SagBounds.High - origin.Z) / direction.Z;
            lo = Math.Max(lo, Math.Min(t0, t1)); hi = Math.Min(hi, Math.Max(t0, t1));
        }
        if (hi < lo || !double.IsFinite(hi)) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var transverse = Math.Sqrt(a);
        if (Math.Abs(direction.Z) > MaximumSlope * transverse + 1e-12)
        {
            var shared = Geometry.DistanceToIntersection(origin, direction);
            if (shared.IsHit && shared.Distance >= lo - 1e-11 && shared.Distance <= hi + 1e-11
                && shared.Point.X * shared.Point.X + shared.Point.Y * shared.Point.Y <= ApertureRadius * ApertureRadius * (1 + 1e-13))
                return Refine(shared.Distance,lo,hi,shared.Iterations);
        }
        // Radial interval bounds isolate the first root in the finite cap.
        // Ambiguous tangencies or exhausted work are errors, not silent misses.
        var pending = new Stack<(double Lo, double Hi, int Depth)>(); pending.Push((lo, hi, 0)); int work = 0;
        while (pending.Count > 0)
        {
            if ((work++ & 63) == 0) ComputationCancellation.ThrowIfCancellationRequested();
            if (work > 8192) throw new InvalidDataException("Bounded asphere intersection exceeded its isolation budget.");
            var segment = pending.Pop(); var p0 = origin + direction * segment.Lo; var p1 = origin + direction * segment.Hi;
            var closest = a == 0 ? segment.Lo : Math.Clamp(-(origin.X * direction.X + origin.Y * direction.Y) / a, segment.Lo, segment.Hi);
            var pc = origin + direction * closest;
            var r0 = Math.Min(ApertureRadius, Math.Sqrt(pc.X * pc.X + pc.Y * pc.Y));
            var r1 = Math.Min(ApertureRadius, Math.Sqrt(Math.Max(p0.X * p0.X + p0.Y * p0.Y, p1.X * p1.X + p1.Y * p1.Y)));
            var slope=SlopeFactorBound(r0,r1);
            var radial0=p0.X*direction.X+p0.Y*direction.Y;var radial1=p1.X*direction.X+p1.Y*direction.Y;
            var derivatives=new[]{slope.Low*radial0,slope.Low*radial1,slope.High*radial0,slope.High*radial1};
            var derivativeLow=direction.Z-derivatives.Max();var derivativeHigh=direction.Z-derivatives.Min();
            if(derivativeLow>0||derivativeHigh<0)
            {
                var f0=p0.Z-Geometry.Sag(p0.X,p0.Y);var f1=p1.Z-Geometry.Sag(p1.X,p1.Y);
                if(f0==0)return Refine(segment.Lo,segment.Lo,segment.Hi,work);
                if(f1==0)return Refine(segment.Hi,segment.Lo,segment.Hi,work);
                if(Math.Sign(f0)==Math.Sign(f1))continue;
                var left=segment.Lo;var right=segment.Hi;
                for(int iteration=0;iteration<64;iteration++)
                {
                    var center=left+(right-left)/2;var p=origin+direction*center;var f=p.Z-Geometry.Sag(p.X,p.Y);
                    if(f==0){left=right=center;break;}
                    if(Math.Sign(f)==Math.Sign(f0)){left=center;f0=f;}else right=center;
                }
                var root=Refine(left+(right-left)/2,segment.Lo,segment.Hi,work);
                if(root.IsHit)return root;
                throw new InvalidDataException("A bracketed rotational intersection failed verification.");
            }
            var sag = Bound(r0, r1); var lower = Math.Min(p0.Z, p1.Z) - sag.High; var upper = Math.Max(p0.Z, p1.Z) - sag.Low;
            if (lower > 0 || upper < 0) continue;
            var middle = segment.Lo + (segment.Hi - segment.Lo) / 2;
            if (segment.Hi - segment.Lo <= 1e-12 * Math.Max(1, Math.Abs(middle)))
            {
                var result = Refine(middle,segment.Lo,segment.Hi,work);
                if (result.IsHit) return result;
                if(result.Status==IntersectionStatus.NoRoot)continue;
                throw new InvalidDataException("The bounded asphere root could not be verified.");
            }
            if (segment.Depth >= 64) throw new InvalidDataException("Bounded asphere intersection did not converge.");
            pending.Push((middle, segment.Hi, segment.Depth + 1)); pending.Push((segment.Lo, middle, segment.Depth + 1));
        }
        return IntersectionResult.Failure(IntersectionStatus.NoRoot);

        IntersectionResult Refine(double distance,double lower,double upper,int iterations)
        {
            for(int iteration=0;iteration<64;iteration++)
            {
                var point=origin+direction*distance;var residual=point.Z-Geometry.Sag(point.X,point.Y);var normal=Geometry.SurfaceNormal(point);
                if(!double.IsFinite(residual)||Math.Abs(normal.Z)<1e-15)break;
                var derivative=(normal.X*direction.X+normal.Y*direction.Y+normal.Z*direction.Z)/normal.Z;
                if(!double.IsFinite(derivative)||Math.Abs(derivative)<1e-14)break;
                var proposed=distance-residual/derivative;
                if(proposed<lower||proposed>upper)return IntersectionResult.Failure(IntersectionStatus.NoRoot);
                if(proposed==distance)break;
                distance=proposed;
            }
            var verified=GeometryIntersectionSolver.CreateVerifiedResult(origin,direction,distance,Geometry.Sag,Geometry.SurfaceNormal,iterations);
            if(!verified.IsHit)return verified;
            // A shared sequential solver may accept a ~1e-10 residual. An NSC
            // continuation must start on the actual surface, otherwise it can
            // immediately hit the same boundary again in the wrong medium.
            var snapped=verified.Point with{Z=Geometry.Sag(verified.Point.X,verified.Point.Y)};
            var finalNormal=Geometry.SurfaceNormal(snapped);finalNormal/=finalNormal.Length;
            return verified with{Point=snapped,Normal=finalNormal,Residual=0};
        }
    }
}
