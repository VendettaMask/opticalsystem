using System.Numerics;

namespace OptilandWorkbench.Core.Backend;

public sealed partial class ManagedCpuBackend
{
    private void IntersectStandardVectorized(
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
            ((IBatchedNumericBackend)this).IntersectPlane(
                originZ,
                directionZ,
                0,
                distance,
                intersects);
            return;
        }

        var width = Vector<double>.Count;
        var index = 0;
        var conicFactor = new Vector<double>(1 + conic);
        var radiusVector = new Vector<double>(radius);
        var two = new Vector<double>(2);
        var four = new Vector<double>(4);
        for (; index <= originX.Length - width; index += width)
        {
            var ox = new Vector<double>(originX.Slice(index, width));
            var oy = new Vector<double>(originY.Slice(index, width));
            var oz = new Vector<double>(originZ.Slice(index, width));
            var dx = new Vector<double>(directionX.Slice(index, width));
            var dy = new Vector<double>(directionY.Slice(index, width));
            var dz = new Vector<double>(directionZ.Slice(index, width));
            var a = (dx * dx) + (dy * dy) + (conicFactor * dz * dz);
            var b = two * (
                (ox * dx)
                + (oy * dy)
                - (radiusVector * dz)
                + (conicFactor * oz * dz));
            var c = (ox * ox)
                + (oy * oy)
                - (two * radiusVector * oz)
                + (conicFactor * oz * oz);
            var discriminant = (b * b) - (four * a * c);
            var root = Vector.SquareRoot(Vector.Max(Vector<double>.Zero, discriminant));
            var denominator = two * a;
            var first = (-b - root) / denominator;
            var second = (-b + root) / denominator;
            for (var lane = 0; lane < width; lane++)
            {
                double? validDistance;
                if (Math.Abs(a[lane]) < 1e-15)
                {
                    validDistance = Math.Abs(b[lane]) < 1e-15
                        ? null
                        : ValidateStandardExplicitSagDistance(
                            -c[lane] / b[lane],
                            originX[index + lane],
                            originY[index + lane],
                            originZ[index + lane],
                            directionX[index + lane],
                            directionY[index + lane],
                            directionZ[index + lane],
                            radius,
                            conic);
                }
                else if (discriminant[lane] < 0)
                {
                    validDistance = null;
                }
                else
                {
                    validDistance = SelectStandardExplicitSagDistance(
                        first[lane],
                        second[lane],
                        originX[index + lane],
                        originY[index + lane],
                        originZ[index + lane],
                        directionX[index + lane],
                        directionY[index + lane],
                        directionZ[index + lane],
                        radius,
                        conic);
                }

                intersects[index + lane] = validDistance.HasValue;
                distance[index + lane] = validDistance ?? double.NaN;
            }
        }

        if (index < originX.Length)
        {
            _scalarBatchAdapter.IntersectStandard(
                originX[index..],
                originY[index..],
                originZ[index..],
                directionX[index..],
                directionY[index..],
                directionZ[index..],
                radius,
                conic,
                distance[index..],
                intersects[index..]);
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
}
