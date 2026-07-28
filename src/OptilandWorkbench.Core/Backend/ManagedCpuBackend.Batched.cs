using System.Numerics;

namespace OptilandWorkbench.Core.Backend;

public sealed partial class ManagedCpuBackend
{
    private readonly ScalarBatchedNumericBackendAdapter _scalarBatchAdapter;

    public ManagedCpuBackend()
    {
        _scalarBatchAdapter = new ScalarBatchedNumericBackendAdapter(this);
    }

    public bool IsHardwareAccelerated => Vector.IsHardwareAccelerated;

    public int PreferredBatchWidth => Vector<double>.Count;

    void IBatchedNumericBackend.NormalizeDirections(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        ReadOnlySpan<double> z,
        Span<double> normalizedX,
        Span<double> normalizedY,
        Span<double> normalizedZ)
    {
        BatchValidation.EqualLengths(x.Length, y.Length, z.Length, normalizedX.Length, normalizedY.Length, normalizedZ.Length);
        var width = Vector<double>.Count;
        var index = 0;
        var epsilonSquared = new Vector<double>(Epsilon * Epsilon);
        var one = Vector<double>.One;
        for (; index <= x.Length - width; index += width)
        {
            var vx = new Vector<double>(x.Slice(index, width));
            var vy = new Vector<double>(y.Slice(index, width));
            var vz = new Vector<double>(z.Slice(index, width));
            var lengthSquared = (vx * vx) + (vy * vy) + (vz * vz);
            var valid = Vector.GreaterThan(lengthSquared, epsilonSquared);
            var inverseLength = one / Vector.SquareRoot(Vector.Max(lengthSquared, epsilonSquared));
            Vector.ConditionalSelect(valid, vx * inverseLength, Vector<double>.Zero)
                .CopyTo(normalizedX.Slice(index, width));
            Vector.ConditionalSelect(valid, vy * inverseLength, Vector<double>.Zero)
                .CopyTo(normalizedY.Slice(index, width));
            Vector.ConditionalSelect(valid, vz * inverseLength, one)
                .CopyTo(normalizedZ.Slice(index, width));
        }

        if (index < x.Length)
        {
            _scalarBatchAdapter.NormalizeDirections(
                x[index..],
                y[index..],
                z[index..],
                normalizedX[index..],
                normalizedY[index..],
                normalizedZ[index..]);
        }
    }

    void IBatchedNumericBackend.Propagate(
        ReadOnlySpan<double> originX,
        ReadOnlySpan<double> originY,
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        ReadOnlySpan<double> distance,
        Span<double> resultX,
        Span<double> resultY,
        Span<double> resultZ)
    {
        BatchValidation.EqualLengths(
            originX.Length,
            originY.Length,
            originZ.Length,
            directionX.Length,
            directionY.Length,
            directionZ.Length,
            distance.Length,
            resultX.Length,
            resultY.Length,
            resultZ.Length);
        var width = Vector<double>.Count;
        var index = 0;
        for (; index <= originX.Length - width; index += width)
        {
            var d = new Vector<double>(distance.Slice(index, width));
            (new Vector<double>(originX.Slice(index, width)) + (new Vector<double>(directionX.Slice(index, width)) * d))
                .CopyTo(resultX.Slice(index, width));
            (new Vector<double>(originY.Slice(index, width)) + (new Vector<double>(directionY.Slice(index, width)) * d))
                .CopyTo(resultY.Slice(index, width));
            (new Vector<double>(originZ.Slice(index, width)) + (new Vector<double>(directionZ.Slice(index, width)) * d))
                .CopyTo(resultZ.Slice(index, width));
        }

        if (index < originX.Length)
        {
            _scalarBatchAdapter.Propagate(
                originX[index..],
                originY[index..],
                originZ[index..],
                directionX[index..],
                directionY[index..],
                directionZ[index..],
                distance[index..],
                resultX[index..],
                resultY[index..],
                resultZ[index..]);
        }
    }

    void IBatchedNumericBackend.IntersectPlane(
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionZ,
        double planeZ,
        Span<double> distance,
        Span<bool> intersects)
    {
        BatchValidation.EqualLengths(originZ.Length, directionZ.Length, distance.Length, intersects.Length);
        var width = Vector<double>.Count;
        var index = 0;
        var plane = new Vector<double>(planeZ);
        for (; index <= originZ.Length - width; index += width)
        {
            var dz = new Vector<double>(directionZ.Slice(index, width));
            var candidate = (plane - new Vector<double>(originZ.Slice(index, width))) / dz;
            candidate.CopyTo(distance.Slice(index, width));
            for (var lane = 0; lane < width; lane++)
            {
                var valid = Math.Abs(directionZ[index + lane]) > Epsilon
                    && distance[index + lane] >= 0;
                intersects[index + lane] = valid;
                if (!valid)
                {
                    distance[index + lane] = double.NaN;
                }
            }
        }

        if (index < originZ.Length)
        {
            _scalarBatchAdapter.IntersectPlane(
                originZ[index..],
                directionZ[index..],
                planeZ,
                distance[index..],
                intersects[index..]);
        }
    }

    void IBatchedNumericBackend.IntersectStandard(
        ReadOnlySpan<double> originX,
        ReadOnlySpan<double> originY,
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        double radius,
        double conic,
        Span<double> distance,
        Span<bool> intersects) =>
        IntersectStandardVectorized(
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            radius,
            conic,
            distance,
            intersects);

    void IBatchedNumericBackend.ApplyCircularAperture(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        double radius,
        Span<bool> accepted)
    {
        BatchValidation.EqualLengths(x.Length, y.Length, accepted.Length);
        var width = Vector<double>.Count;
        var index = 0;
        var radiusSquared = new Vector<double>(radius * radius);
        for (; index <= x.Length - width; index += width)
        {
            var vx = new Vector<double>(x.Slice(index, width));
            var vy = new Vector<double>(y.Slice(index, width));
            var squared = (vx * vx) + (vy * vy);
            for (var lane = 0; lane < width; lane++)
            {
                accepted[index + lane] = squared[lane] <= radiusSquared[lane];
            }
        }

        if (index < x.Length)
        {
            _scalarBatchAdapter.ApplyCircularAperture(
                x[index..],
                y[index..],
                radius,
                accepted[index..]);
        }
    }

    void IBatchedNumericBackend.RefractOrReflect(
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        ReadOnlySpan<double> normalX,
        ReadOnlySpan<double> normalY,
        ReadOnlySpan<double> normalZ,
        ReadOnlySpan<double> refractiveIndexBefore,
        ReadOnlySpan<double> refractiveIndexAfter,
        bool forceReflection,
        Span<double> resultX,
        Span<double> resultY,
        Span<double> resultZ,
        Span<Interactions.RayInteractionKind> interactionKinds) =>
        RefractOrReflectVectorized(
            directionX,
            directionY,
            directionZ,
            normalX,
            normalY,
            normalZ,
            refractiveIndexBefore,
            refractiveIndexAfter,
            forceReflection,
            resultX,
            resultY,
            resultZ,
            interactionKinds);
}
