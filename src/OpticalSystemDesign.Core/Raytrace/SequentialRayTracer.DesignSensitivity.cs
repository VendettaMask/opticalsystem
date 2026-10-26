using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Raytrace;

internal sealed class DesignRayUnavailableException(string message) : ArithmeticException(message);

internal sealed record DesignRaySensitivity(DifferentialScalar X, DifferentialScalar Y,
    DifferentialScalar DirectionZ, DifferentialScalar MinimumClearance, DifferentialScalar MinimumBendCosine)
{
    public DifferentialScalar BendPenalty { get; init; }
}

public sealed partial class SequentialRayTracer
{
    /// <summary>
    /// Tangent propagation along formal Core intersections. The nominal engine owns
    /// every root, direction and interaction. Missing roots, TIR, grazing roots or
    /// a value mismatch cannot be turned into a zero gradient or an accepted ray.
    /// </summary>
    internal DesignRaySensitivity TraceDesignSensitivity(RealRay source, OpticalDifferentialContext context,
        int stopIndex, DifferentialScalar targetX, DifferentialScalar targetY, CancellationToken cancellationToken,
        SequentialGpuSample[]? nominalSamples = null, int nominalOffset = 0, bool constrainStop = true,
        double bendCosineGate = 1)
    {
        if (nominalSamples is null) SequentialTraceMeasurement.Record(1);
        var ray = RayState.FromRealRay(source.Normalize());
        var material = ResolveMaterial("Air");
        var path = 0.0;
        var opticalPath = 0.0;
        var dimension = context.Dimension;
        var parameterCount = context.ParameterCount;
        var ox = constrainStop ? DifferentialScalar.Variable(ray.Origin.X, dimension, parameterCount) : (DifferentialScalar)ray.Origin.X;
        var oy = constrainStop ? DifferentialScalar.Variable(ray.Origin.Y, dimension, parameterCount + 1) : (DifferentialScalar)ray.Origin.Y;
        DifferentialScalar oz = ray.Origin.Z;
        DifferentialScalar dx = ray.Direction.X, dy = ray.Direction.Y, dz = ray.Direction.Z;
        DifferentialScalar stopX = double.NaN, stopY = double.NaN;
        DifferentialScalar minimumClearance = double.PositiveInfinity, minimumBend = 1;
        DifferentialScalar bendPenalty = 0;
        for (var index = 0; index < _optic.SurfaceGroup.Items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var surface = _optic.SurfaceGroup.Items[index];
            var result = nominalSamples is not null ? NominalResult(nominalSamples[nominalOffset + index], index, material) : TraceSequentialSurface(surface, index, ray, material, path, opticalPath,
                ignorePhysicalAperture: constrainStop, bypassCoating: true,
                allowVirtualIntersection: AllowsReferenceBacktrack(_optic.SurfaceGroup.Items, index),
                ignoreMaterialAbsorption: true);
            if (result.StopTracing || result.Sample.Vignetted || result.InteractionKind is RayInteractionKind.TotalInternalReflection)
                throw new DesignRayUnavailableException($"Design derivative has no transmitted formal ray at surface {index}.");
            if (index > 0)
            {
                var center = context.Position(index);
                var originX = surface.CoordinateSystem.Origin.X;
                var originY = surface.CoordinateSystem.Origin.Y;
                var t = result.Sample.SegmentLength;
                var qx = ox - originX + dx * t;
                var qy = oy - originY + dy * t;
                var qz = oz - center + dz * t;
                var profile = context.Profile(index, qx, qy);
                var denominator = dz.Value - 2 * profile.RadialSlope.Value * (qx.Value * dx.Value + qy.Value * dy.Value);
                if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-9)
                    throw new ArithmeticException("A grazing/singular intersection has no stable implicit derivative.");
                var distance = t - (qz - profile.Sag).WithValue(0) / denominator;
                qx = ox - originX + dx * distance;
                qy = oy - originY + dy * distance;
                qz = oz - center + dz * distance;
                var nominal = surface.CoordinateSystem.ToLocalPoint(result.Sample.Position);
                OpticalDifferentialContext.Verify(qx, nominal.X, "intersection X");
                OpticalDifferentialContext.Verify(qy, nominal.Y, "intersection Y");
                OpticalDifferentialContext.Verify(qz, nominal.Z, "intersection Z");
                qx = qx.WithValue(nominal.X);
                qy = qy.WithValue(nominal.Y);
                qz = qz.WithValue(nominal.Z);
                if (index == stopIndex) (stopX, stopY) = (qx, qy);
                if (surface.PhysicalAperture is CircularAperture aperture)
                {
                    var radial = DifferentialScalar.Sqrt(qx * qx + qy * qy);
                    // The calibrated stop follows the declared EPD, not a fixed old aperture.
                    var radius = constrainStop && index == stopIndex
                        ? DifferentialScalar.Abs(context.Paraxial().StopHeight) : (DifferentialScalar)aperture.Radius;
                    var clearance = radius - radial;
                    if (clearance.Value < minimumClearance.Value) minimumClearance = clearance;
                }
                var (nx, ny, nz) = context.Normal(index, qx, qy);
                if ((nx * dx + ny * dy + nz * dz).Value > 0) (nx, ny, nz) = (-nx, -ny, -nz);
                var eta = context.Index(index - 1, source.WavelengthNanometers) / context.Index(index, source.WavelengthNanometers);
                var cosine = -(nx * dx + ny * dy + nz * dz);
                var transmissionRoot = 1 - eta * eta * (1 - cosine * cosine);
                if (transmissionRoot.Value <= 1e-12) throw new ArithmeticException("The critical refraction derivative is singular.");
                var normalScale = eta * cosine - DifferentialScalar.Sqrt(transmissionRoot);
                var nextX = eta * dx + normalScale * nx;
                var nextY = eta * dy + normalScale * ny;
                var nextZ = eta * dz + normalScale * nz;
                var length = DifferentialScalar.Sqrt(nextX * nextX + nextY * nextY + nextZ * nextZ);
                nextX /= length;
                nextY /= length;
                nextZ /= length;
                OpticalDifferentialContext.Verify(nextX, result.Ray.Direction.X, $"refraction X at surface {index}");
                OpticalDifferentialContext.Verify(nextY, result.Ray.Direction.Y, $"refraction Y at surface {index}");
                OpticalDifferentialContext.Verify(nextZ, result.Ray.Direction.Z, $"refraction Z at surface {index}");
                var bend = dx * nextX + dy * nextY + dz * nextZ;
                if (bend.Value < minimumBend.Value) minimumBend = bend;
                if (!constrainStop) bendPenalty += DifferentialScalar.PositivePart((bendCosineGate - bend) / Math.Max(1 - bendCosineGate, 1e-12));
                dx = nextX.WithValue(result.Ray.Direction.X);
                dy = nextY.WithValue(result.Ray.Direction.Y);
                dz = nextZ.WithValue(result.Ray.Direction.Z);
                ox = (qx + originX).WithValue(result.Ray.Origin.X);
                oy = (qy + originY).WithValue(result.Ray.Origin.Y);
                oz = (qz + center).WithValue(result.Ray.Origin.Z);
            }
            ray = result.Ray;
            material = result.OutgoingMaterial;
            path = result.CumulativePathLength;
            opticalPath = result.CumulativeOpticalPathLength;
        }
        if (!constrainStop)
            return new(ox, oy, dz, minimumClearance, minimumBend) { BendPenalty = bendPenalty };
        if (!stopX.IsFinite || !stopY.IsFinite
            || Math.Abs(stopX.Value - targetX.Value) > 1e-6 || Math.Abs(stopY.Value - targetY.Value) > 1e-6)
            throw new ArithmeticException("The nominal stop aiming constraint did not converge.");
        var a = stopX.Derivative(parameterCount);
        var b = stopX.Derivative(parameterCount + 1);
        var c = stopY.Derivative(parameterCount);
        var d = stopY.Derivative(parameterCount + 1);
        var determinant = a * d - b * c;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
            throw new ArithmeticException("The stop aiming Jacobian is singular.");
        if (DifferentialTape.Current is not null)
        {
            var ex = targetX - stopX;
            var ey = targetY - stopY;
            var lx = (d * ex - b * ey) / determinant;
            var ly = (a * ey - c * ex) / determinant;
            return new(ConstrainTape(ox), ConstrainTape(oy), ConstrainTape(dz), ConstrainTape(minimumClearance), ConstrainTape(minimumBend));

            DifferentialScalar ConstrainTape(DifferentialScalar value) =>
                (value + value.Derivative(parameterCount) * lx + value.Derivative(parameterCount + 1) * ly).WithValue(value.Value);
        }
        var launchX = new double[parameterCount];
        var launchY = new double[parameterCount];
        for (var index = 0; index < parameterCount; index++)
        {
            var ex = targetX.Derivative(index) - stopX.Derivative(index);
            var ey = targetY.Derivative(index) - stopY.Derivative(index);
            launchX[index] = (d * ex - b * ey) / determinant;
            launchY[index] = (a * ey - c * ex) / determinant;
        }
        return new(Constrain(ox), Constrain(oy), Constrain(dz), Constrain(minimumClearance), Constrain(minimumBend));

        DifferentialScalar Constrain(DifferentialScalar value)
        {
            var gradient = new double[parameterCount];
            for (var index = 0; index < parameterCount; index++)
                gradient[index] = value.Derivative(index) + value.Derivative(parameterCount) * launchX[index]
                    + value.Derivative(parameterCount + 1) * launchY[index];
            return DifferentialScalar.FromGradient(value.Value, gradient);
        }

        SurfaceRayTraceStateResult NominalResult(SequentialGpuSample v, int index, IMaterial before)
        {
            var surface = _optic.SurfaceGroup.Items[index];
            var outgoing = index == 0 || v.Kind == (int)RayInteractionKind.Transmitted ? surface.MaterialAfter : before;
            var kind = v.Kind < 0 ? (RayInteractionKind?)null : (RayInteractionKind)v.Kind;
            var state = new RayState(v.Point, v.Direction, source.WavelengthNanometers, v.Intensity, v.Opd, null, true);
            return new(state, GpuSample(v, index), index == 0 ? outgoing.RefractiveIndex(source.WavelengthNanometers)
                : kind == RayInteractionKind.Transmitted ? v.After : v.Before, outgoing, kind, v.Path, v.OpticalPath,
                v.Present == 0 || v.Miss != 0 || v.Blocked != 0 || !state.CanTrace);
        }
    }
}
