using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Backend;

public interface IBatchedNumericBackend
{
    bool IsHardwareAccelerated { get; }

    int PreferredBatchWidth { get; }

    void NormalizeDirections(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        ReadOnlySpan<double> z,
        Span<double> normalizedX,
        Span<double> normalizedY,
        Span<double> normalizedZ);

    void Propagate(
        ReadOnlySpan<double> originX,
        ReadOnlySpan<double> originY,
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        ReadOnlySpan<double> distance,
        Span<double> resultX,
        Span<double> resultY,
        Span<double> resultZ);

    void IntersectPlane(
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionZ,
        double planeZ,
        Span<double> distance,
        Span<bool> intersects);

    void IntersectStandard(
        ReadOnlySpan<double> originX,
        ReadOnlySpan<double> originY,
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        double radius,
        double conic,
        Span<double> distance,
        Span<bool> intersects);

    void ApplyCircularAperture(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        double radius,
        Span<bool> accepted);

    void RefractOrReflect(
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
        Span<RayInteractionKind> interactionKinds);
}

internal sealed class ScalarBatchedNumericBackendAdapter : IBatchedNumericBackend
{
    private readonly INumericBackend _backend;

    public ScalarBatchedNumericBackendAdapter(INumericBackend backend)
    {
        _backend = backend;
    }

    public bool IsHardwareAccelerated => false;

    public int PreferredBatchWidth => 1;

    public void NormalizeDirections(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        ReadOnlySpan<double> z,
        Span<double> normalizedX,
        Span<double> normalizedY,
        Span<double> normalizedZ)
    {
        BatchValidation.EqualLengths(x.Length, y.Length, z.Length, normalizedX.Length, normalizedY.Length, normalizedZ.Length);
        for (var index = 0; index < x.Length; index++)
        {
            var normalized = _backend.Normalize(new Vector3D(x[index], y[index], z[index]));
            normalizedX[index] = normalized.X;
            normalizedY[index] = normalized.Y;
            normalizedZ[index] = normalized.Z;
        }
    }

    public void Propagate(
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
        for (var index = 0; index < originX.Length; index++)
        {
            resultX[index] = originX[index] + (directionX[index] * distance[index]);
            resultY[index] = originY[index] + (directionY[index] * distance[index]);
            resultZ[index] = originZ[index] + (directionZ[index] * distance[index]);
        }
    }

    public void IntersectPlane(
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionZ,
        double planeZ,
        Span<double> distance,
        Span<bool> intersects)
    {
        BatchValidation.EqualLengths(originZ.Length, directionZ.Length, distance.Length, intersects.Length);
        for (var index = 0; index < originZ.Length; index++)
        {
            var candidate = Math.Abs(directionZ[index]) > _backend.Epsilon
                ? (planeZ - originZ[index]) / directionZ[index]
                : double.NaN;
            var valid = candidate >= 0;
            intersects[index] = valid;
            distance[index] = valid ? candidate : double.NaN;
        }
    }

    public void IntersectStandard(
        ReadOnlySpan<double> originX,
        ReadOnlySpan<double> originY,
        ReadOnlySpan<double> originZ,
        ReadOnlySpan<double> directionX,
        ReadOnlySpan<double> directionY,
        ReadOnlySpan<double> directionZ,
        double radius,
        double conic,
        Span<double> distance,
        Span<bool> intersects)
    {
        BatchValidation.EqualLengths(
            originX.Length,
            originY.Length,
            originZ.Length,
            directionX.Length,
            directionY.Length,
            directionZ.Length,
            distance.Length,
            intersects.Length);
        if (Math.Abs(radius) < 1e-12 || double.IsInfinity(radius))
        {
            IntersectPlane(originZ, directionZ, 0, distance, intersects);
            return;
        }

        var conicFactor = 1.0 + conic;
        for (var index = 0; index < originX.Length; index++)
        {
            var a = (directionX[index] * directionX[index])
                + (directionY[index] * directionY[index])
                + (conicFactor * directionZ[index] * directionZ[index]);
            var b = 2.0 * (
                (originX[index] * directionX[index])
                + (originY[index] * directionY[index])
                - (radius * directionZ[index])
                + (conicFactor * originZ[index] * directionZ[index]));
            var c = (originX[index] * originX[index])
                + (originY[index] * originY[index])
                - (2.0 * radius * originZ[index])
                + (conicFactor * originZ[index] * originZ[index]);
            double? validDistance;
            if (Math.Abs(a) < 1e-15)
            {
                validDistance = Math.Abs(b) < 1e-15
                    ? null
                    : ValidateStandardExplicitSagDistance(
                        -c / b,
                        originX[index],
                        originY[index],
                        originZ[index],
                        directionX[index],
                        directionY[index],
                        directionZ[index],
                        radius,
                        conic);
            }
            else
            {
                var discriminant = (b * b) - (4.0 * a * c);
                if (discriminant < 0)
                {
                    validDistance = null;
                }
                else
                {
                    var root = Math.Sqrt(discriminant);
                    var first = (-b - root) / (2.0 * a);
                    var second = (-b + root) / (2.0 * a);
                    validDistance = SelectStandardExplicitSagDistance(
                        first,
                        second,
                        originX[index],
                        originY[index],
                        originZ[index],
                        directionX[index],
                        directionY[index],
                        directionZ[index],
                        radius,
                        conic);
                }
            }

            intersects[index] = validDistance.HasValue;
            distance[index] = validDistance ?? double.NaN;
        }
    }

    private static double? SelectStandardExplicitSagDistance(
        double first,
        double second,
        double originX,
        double originY,
        double originZ,
        double directionX,
        double directionY,
        double directionZ,
        double radius,
        double conic)
    {
        var firstDistance = ValidateStandardExplicitSagDistance(
            first,
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            radius,
            conic);
        var secondDistance = ValidateStandardExplicitSagDistance(
            second,
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            radius,
            conic);
        if (!firstDistance.HasValue)
        {
            return secondDistance;
        }

        if (!secondDistance.HasValue)
        {
            return firstDistance;
        }

        return Math.Min(firstDistance.Value, secondDistance.Value);
    }

    private static double? ValidateStandardExplicitSagDistance(
        double? candidate,
        double originX,
        double originY,
        double originZ,
        double directionX,
        double directionY,
        double directionZ,
        double radius,
        double conic)
    {
        const double tolerance = 1e-12;
        if (candidate is not { } rawDistance || !double.IsFinite(rawDistance) || rawDistance < -tolerance)
        {
            return null;
        }

        var distance = Math.Max(0, rawDistance);
        var x = originX + (directionX * distance);
        var y = originY + (directionY * distance);
        var z = originZ + (directionZ * distance);
        var curvature = 1.0 / radius;
        var r2 = (x * x) + (y * y);
        var rootArgument = 1.0 - ((1.0 + conic) * curvature * curvature * r2);
        if (rootArgument < -tolerance)
        {
            return null;
        }

        var sag = curvature * r2 / (1.0 + Math.Sqrt(Math.Max(0, rootArgument)));
        var sagTolerance = 1e-8 * Math.Max(1.0, Math.Max(Math.Abs(z), Math.Abs(sag)));
        return Math.Abs(z - sag) <= sagTolerance ? distance : null;
    }

    public void ApplyCircularAperture(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        double radius,
        Span<bool> accepted)
    {
        BatchValidation.EqualLengths(x.Length, y.Length, accepted.Length);
        var radiusSquared = radius * radius;
        for (var index = 0; index < x.Length; index++)
        {
            accepted[index] = ((x[index] * x[index]) + (y[index] * y[index])) <= radiusSquared;
        }
    }

    public void RefractOrReflect(
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
        Span<RayInteractionKind> interactionKinds)
    {
        BatchValidation.EqualLengths(
            directionX.Length,
            directionY.Length,
            directionZ.Length,
            normalX.Length,
            normalY.Length,
            normalZ.Length,
            refractiveIndexBefore.Length,
            refractiveIndexAfter.Length,
            resultX.Length,
            resultY.Length,
            resultZ.Length,
            interactionKinds.Length);

        for (var index = 0; index < directionX.Length; index++)
        {
            var direction = _backend.Normalize(new Vector3D(directionX[index], directionY[index], directionZ[index]));
            var normal = _backend.Normalize(new Vector3D(normalX[index], normalY[index], normalZ[index]));
            if (_backend.Dot(direction, normal) > 0)
            {
                normal = -normal;
            }

            var dot = _backend.Dot(direction, normal);
            Vector3D outgoing;
            RayInteractionKind kind;
            if (forceReflection)
            {
                outgoing = direction - (2 * dot * normal);
                kind = RayInteractionKind.Reflected;
            }
            else
            {
                var eta = refractiveIndexBefore[index] / Math.Max(1e-9, refractiveIndexAfter[index]);
                var cosI = -dot;
                var sinT2 = eta * eta * (1 - (cosI * cosI));
                if (sinT2 > 1)
                {
                    outgoing = direction - (2 * dot * normal);
                    kind = RayInteractionKind.TotalInternalReflection;
                }
                else
                {
                    var cosT = Math.Sqrt(Math.Max(0, 1 - sinT2));
                    outgoing = (eta * direction) + ((eta * cosI - cosT) * normal);
                    kind = RayInteractionKind.Transmitted;
                }
            }

            outgoing = _backend.Normalize(outgoing);
            resultX[index] = outgoing.X;
            resultY[index] = outgoing.Y;
            resultZ[index] = outgoing.Z;
            interactionKinds[index] = kind;
        }
    }
}

internal static class BatchValidation
{
    public static void EqualLengths(params int[] lengths)
    {
        if (lengths.Length > 1 && lengths.Any(length => length != lengths[0]))
        {
            throw new ArgumentException("All batch spans must have the same length.");
        }
    }
}
