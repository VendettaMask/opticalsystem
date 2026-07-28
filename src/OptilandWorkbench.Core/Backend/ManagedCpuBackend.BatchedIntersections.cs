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
                double? candidate;
                if (Math.Abs(a[lane]) < 1e-15)
                {
                    candidate = Math.Abs(b[lane]) < 1e-15 ? null : -c[lane] / b[lane];
                }
                else if (discriminant[lane] < 0)
                {
                    candidate = null;
                }
                else
                {
                    var firstValue = first[lane];
                    var secondValue = second[lane];
                    candidate = firstValue >= -1e-12 && secondValue >= -1e-12
                        ? Math.Min(firstValue, secondValue)
                        : firstValue >= -1e-12
                            ? firstValue
                            : secondValue >= -1e-12 ? secondValue : null;
                }

                var valid = candidate is >= -1e-12;
                intersects[index + lane] = valid;
                distance[index + lane] = valid
                    ? Math.Max(0, candidate!.Value)
                    : double.NaN;
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
}
