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
            double? value;
            if (Math.Abs(a) < 1e-15)
            {
                value = Math.Abs(b) < 1e-15 ? null : -c / b;
            }
            else
            {
                var discriminant = (b * b) - (4.0 * a * c);
                if (discriminant < 0)
                {
                    value = null;
                }
                else
                {
                    var root = Math.Sqrt(discriminant);
                    var first = (-b - root) / (2.0 * a);
                    var second = (-b + root) / (2.0 * a);
                    value = first >= -1e-12 && second >= -1e-12
                        ? Math.Min(first, second)
                        : first >= -1e-12 ? first : second >= -1e-12 ? second : null;
                }
            }

            intersects[index] = value is >= -1e-12;
            distance[index] = intersects[index] ? Math.Max(0, value!.Value) : double.NaN;
        }
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
