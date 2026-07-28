using System.Buffers;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

internal sealed class PooledDirectionBatch : IDisposable
{
    private double[]? _x;
    private double[]? _y;
    private double[]? _z;

    private PooledDirectionBatch(double[] x, double[] y, double[] z, int count)
    {
        _x = x;
        _y = y;
        _z = z;
        Count = count;
    }

    public int Count { get; }

    public Vector3D this[int index]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_x is null, this);
            return new Vector3D(_x![index], _y![index], _z![index]);
        }
    }

    public static PooledDirectionBatch Create(
        RealRayBundle bundle,
        IBatchedNumericBackend backend,
        bool useBatchedBackend)
    {
        var count = bundle.Rays.Count;
        var x = ArrayPool<double>.Shared.Rent(Math.Max(1, count));
        var y = ArrayPool<double>.Shared.Rent(Math.Max(1, count));
        var z = ArrayPool<double>.Shared.Rent(Math.Max(1, count));
        for (var index = 0; index < count; index++)
        {
            var direction = bundle.Rays[index].Direction;
            x[index] = direction.X;
            y[index] = direction.Y;
            z[index] = direction.Z;
        }

        if (useBatchedBackend)
        {
            backend.NormalizeDirections(
                x.AsSpan(0, count),
                y.AsSpan(0, count),
                z.AsSpan(0, count),
                x.AsSpan(0, count),
                y.AsSpan(0, count),
                z.AsSpan(0, count));
        }
        else
        {
            for (var index = 0; index < count; index++)
            {
                var length = Math.Sqrt((x[index] * x[index]) + (y[index] * y[index]) + (z[index] * z[index]));
                if (length <= 1e-12)
                {
                    x[index] = 0;
                    y[index] = 0;
                    z[index] = 1;
                }
                else
                {
                    x[index] /= length;
                    y[index] /= length;
                    z[index] /= length;
                }
            }
        }

        return new PooledDirectionBatch(x, y, z, count);
    }

    public void Dispose()
    {
        var x = Interlocked.Exchange(ref _x, null);
        var y = Interlocked.Exchange(ref _y, null);
        var z = Interlocked.Exchange(ref _z, null);
        if (x is not null)
        {
            ArrayPool<double>.Shared.Return(x, clearArray: true);
        }

        if (y is not null)
        {
            ArrayPool<double>.Shared.Return(y, clearArray: true);
        }

        if (z is not null)
        {
            ArrayPool<double>.Shared.Return(z, clearArray: true);
        }
    }
}
