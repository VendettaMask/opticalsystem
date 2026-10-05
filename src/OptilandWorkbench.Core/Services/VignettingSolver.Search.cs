using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public static partial class VignettingSolver
{
    private sealed class PupilSearch(Func<double, double, bool> trace, int precision)
    {
        private readonly double _tolerance = precision switch { 0 => 1e-7, 1 => 1e-5, _ => 1e-3 };
        private readonly int _density = precision switch { 0 => 64, 1 => 32, _ => 16 };
        private readonly Dictionary<(double X, double Y), bool> _samples = new();

        public PupilVignetting Solve(double angle)
        {
            if (Pass(-1, 0) && Pass(1, 0) && Pass(0, -1) && Pass(0, 1))
                return new(0, 0, 0, 0, angle);
            var (x, y) = Seed();
            var converged = false;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                var previous = (x, y);
                var horizontal = Bounds(x, y, horizontal: true);
                x = (horizontal.Low + horizontal.High) / 2;
                var vertical = Bounds(y, x, horizontal: false);
                y = (vertical.Low + vertical.High) / 2;
                if (Math.Max(Math.Abs(x - previous.x), Math.Abs(y - previous.y)) <= _tolerance)
                { converged = true; break; }
            }
            if (!converged) throw new InvalidOperationException("边缘光线搜索未收敛；不能返回可靠渐晕因子。");
            var boundsX = Bounds(x, y, true); var boundsY = Bounds(y, x, false);
            var radiusX = Math.Min(x - boundsX.Low, boundsX.High - x);
            var radiusY = Math.Min(y - boundsY.Low, boundsY.High - y);
            if (radiusX <= _tolerance || radiusY <= _tolerance)
                throw new InvalidOperationException("可通光区域小于当前搜索精度，请提高精度或检查孔径。");
            var result = new PupilVignetting(x, y, 1 - radiusX, 1 - radiusY, angle);
            // Recheck the actual published arithmetic, including subtraction roundoff.
            if (!Pass(x - (1 - result.CompressionX), y) || !Pass(x + (1 - result.CompressionX), y)
                || !Pass(x, y - (1 - result.CompressionY)) || !Pass(x, y + (1 - result.CompressionY)))
                throw new InvalidOperationException("最终四条边缘光线没有全部通过孔径。");
            return result;
        }

        private (double X, double Y) Seed()
        {
            if (Pass(0, 0)) return (0, 0);
            // Expanding rings retain small shifted pupils missed by a single chief ray.
            // No ray found at this finite sampling resolution is an error, never zero merit.
            for (var radiusIndex = 1; radiusIndex <= _density; radiusIndex++)
            {
                var radius = radiusIndex / (double)_density;
                var count = Math.Max(8, radiusIndex * 6);
                for (var direction = 0; direction < count; direction++)
                {
                    var (sin, cos) = Math.SinCos(direction * 2 * Math.PI / count);
                    var x = radius * cos; var y = radius * sin;
                    if (Pass(x, y)) return (x, y);
                }
            }
            throw new InvalidOperationException("在当前采样精度内未找到通光区域；请检查孔径或提高精度。");
        }

        private (double Low, double High) Bounds(double center, double other, bool horizontal)
        {
            var limit = Math.Sqrt(Math.Max(0, 1 - other * other));
            bool Inside(double coordinate) => horizontal ? Pass(coordinate, other) : Pass(other, coordinate);
            double Edge(int sign)
            {
                var good = center;
                while (true)
                {
                    var next = Math.Clamp(good + sign * 2.0 / _density, -limit, limit);
                    if (Inside(next))
                    {
                        good = next;
                        if (next == sign * limit) return good;
                        continue;
                    }
                    var bad = next;
                    for (var iteration = 0; iteration < 64 && Math.Abs(bad - good) > _tolerance; iteration++)
                    {
                        var middle = (good + bad) / 2;
                        if (Inside(middle)) good = middle; else bad = middle;
                    }
                    return good;
                }
            }
            if (!Inside(center)) throw new InvalidOperationException("搜索中心不能通过孔径。");
            return (Edge(-1), Edge(1));
        }

        private bool Pass(double x, double y)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(x) || !double.IsFinite(y) || x * x + y * y > 1 + 1e-12) return false;
            if (_samples.TryGetValue((x, y), out var cached)) return cached;
            if (_samples.Count >= 200000) throw new InvalidOperationException("自动渐晕达到光线采样上限，请检查孔径或降低精度。");
            return _samples[(x, y)] = trace(x, y);
        }
    }
}
