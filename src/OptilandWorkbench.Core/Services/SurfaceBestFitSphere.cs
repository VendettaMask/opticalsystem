using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Services;

public sealed record SurfaceBestFitSphereResult(double Curvature, double VertexOffset, double MaximumRemoval,
    double RemovalVolume, double MaximumSlopeDifference, double RmsRemoval, double RmsSlopeDifference)
{
    public double Value(int data) => data switch
    {
        0 => Curvature,
        1 => Curvature == 0 ? double.PositiveInfinity : 1 / Curvature,
        2 => VertexOffset,
        3 => MaximumRemoval,
        4 => RemovalVolume,
        5 => MaximumSlopeDifference,
        6 => RmsRemoval,
        7 => RmsSlopeDifference,
        _ => throw new ArgumentOutOfRangeException(nameof(data), "BFSD Data 必须在 0..7。")
    };
}

/// <summary>Coaxial manufacturing sphere enclosing a rotational sag profile on its air side.
/// Minimizes removed volume over the requested annulus. Lengths are mm; volume is mm³.
/// The bounded extremum search and radial RMS sampling are not certified native-equivalent.</summary>
public static class SurfaceBestFitSphere
{
    private const int RadialIntervals = 1000;
    private const int CurvatureIntervals = 64;

    public static SurfaceBestFitSphereResult MinimumVolume(OpticalSurface surface,
        double minimumRadius = 0, double maximumRadius = 0)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry or OddAsphereGeometry))
            throw new NotSupportedException("BFSD 当前支持旋转对称标准面及偶/奇次非球面。");
        var airBefore = surface.MaterialBefore is AirMaterial;
        var airAfter = surface.MaterialAfter is AirMaterial;
        if (surface.IsReflective || airBefore == airAfter)
            throw new NotSupportedException("BFSD 当前需要明确的空气/材料边界；反射及同类介质边界的加工方向尚未核实。");
        if (minimumRadius == 0 && maximumRadius == 0) maximumRadius = surface.SemiDiameter;
        if (!double.IsFinite(minimumRadius) || !double.IsFinite(maximumRadius)
            || minimumRadius < 0 || maximumRadius <= minimumRadius)
            throw new ArgumentOutOfRangeException(nameof(minimumRadius), "BFSD 径向范围需要 0 ≤ MinR < MaxR；两者为零时采用净半口径。");
        return new Fit(surface, minimumRadius, maximumRadius, airBefore ? -1 : 1).Run();
    }

    private sealed class Fit
    {
        private readonly OpticalSurface _surface;
        private readonly double _min, _max, _side, _area, _originalIntegral;
        private readonly double[] _radii = new double[RadialIntervals + 1];
        private readonly double[] _sags = new double[RadialIntervals + 1];
        private readonly double[] _slopes = new double[RadialIntervals + 1];
        private readonly Dictionary<double, Candidate> _candidates = new();
        private sealed record Candidate(double Curvature, double Offset, double Volume, double MaximumRemoval);

        internal Fit(OpticalSurface surface, double min, double max, int side)
        {
            _surface = surface; _min = min; _max = max; _side = side;
            _area = Math.PI * (max * max - min * min);
            _originalIntegral = ElementVolumeMetrics.RadialSagIntegral(surface.Geometry, min, max);
            if (!double.IsFinite(_area) || _area <= 0 || !double.IsFinite(_originalIntegral))
                throw new InvalidOperationException("BFSD 径向面积或矢高积分无效。");
            for (var i = 0; i <= RadialIntervals; i++)
            {
                var r = min + (max - min) * i / RadialIntervals;
                _radii[i] = r; _sags[i] = surface.Geometry.Sag(r, 0);
                _slopes[i] = SurfaceDifferentialMetrics.At(surface, r, 0).Dx;
                if (!double.IsFinite(_sags[i]) || !double.IsFinite(_slopes[i]))
                    throw new InvalidOperationException("BFSD 范围超出有限面形/斜率定义域。");
            }
        }

        internal SurfaceBestFitSphereResult Run()
        {
            var basis = _surface.Geometry switch
            {
                StandardGeometry s => s,
                EvenAsphereGeometry e => e.Base,
                OddAsphereGeometry o => o.Base,
                _ => null
            };
            var baseCurvature = basis is null || double.IsInfinity(basis.Radius) || Math.Abs(basis.Radius) < 1e-12
                ? 0 : 1 / basis.Radius;
            var best = Evaluate(0);
            if (Math.Abs(baseCurvature * _max) < 1) best = Better(best, Evaluate(baseCurvature * _max));
            // A true sphere/plane is already the unique zero-volume solution; do not perturb it numerically.
            var exactSphere = _surface.Geometry is PlaneGeometry or StandardGeometry { Conic: 0 };
            if (!exactSphere)
            {
                var grid = Enumerable.Range(0, CurvatureIntervals + 1)
                    .Select(i => Evaluate((-1 + 2.0 * i / CurvatureIntervals) * (1 - 1e-10))).ToArray();
                foreach (var candidate in grid) best = Better(best, candidate);
                for (var i = 1; i < CurvatureIntervals; i++)
                {
                    if (grid[i].Volume > grid[i - 1].Volume || grid[i].Volume > grid[i + 1].Volume) continue;
                    best = Better(best, Minimize(grid[i - 1].Curvature * _max, grid[i + 1].Curvature * _max));
                }
                if (Math.Abs(best.Curvature * _max) > 1 - 1e-8)
                    throw new InvalidOperationException("BFSD 最优解触及半球边界，当前范围没有已验证的有限斜率拟合球面。");
            }
            var sphere = Sphere(best.Curvature); var maximumSlope = 0.0; var depthSquares = 0.0; var slopeSquares = 0.0;
            for (var i = 0; i <= RadialIntervals; i++)
            {
                var depth = _side * (sphere.Sag(_radii[i], 0) + best.Offset - _sags[i]);
                var slope = SphereSlope(best.Curvature, _radii[i]) - _slopes[i];
                var weight = i is 0 or RadialIntervals ? .5 : 1;
                depthSquares += weight * depth * depth; slopeSquares += weight * slope * slope;
                maximumSlope = Math.Max(maximumSlope, Math.Abs(slope));
            }
            return new(best.Curvature, best.Offset, best.MaximumRemoval, best.Volume, maximumSlope,
                Math.Sqrt(depthSquares / RadialIntervals), Math.Sqrt(slopeSquares / RadialIntervals));
        }

        private Candidate Evaluate(double normalizedCurvature)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (_candidates.TryGetValue(normalizedCurvature, out var cached)) return cached;
            if (_candidates.Count >= 4096) throw new InvalidOperationException("BFSD 拟合超出工作预算。");
            var c = normalizedCurvature / _max; var sphere = Sphere(c);
            var low = double.PositiveInfinity; var high = double.NegativeInfinity;
            void Include(double radius, double sag)
            {
                var difference = _side * (sag - sphere.Sag(radius, 0));
                low = Math.Min(low, difference); high = Math.Max(high, difference);
            }
            for (var i = 0; i <= RadialIntervals; i++)
            {
                Include(_radii[i], _sags[i]);
                if (i == 0) continue;
                var left = _radii[i - 1]; var right = _radii[i];
                var fl = _slopes[i - 1] - SphereSlope(c, left);
                var fr = _slopes[i] - SphereSlope(c, right);
                if (fl == 0 || fr == 0 || Math.Sign(fl) == Math.Sign(fr)) continue;
                for (var iteration = 0; iteration < 36; iteration++)
                {
                    var middle = (left + right) / 2;
                    var fm = SurfaceDifferentialMetrics.At(_surface, middle, 0).Dx - SphereSlope(c, middle);
                    if (Math.Sign(fm) == Math.Sign(fl)) { left = middle; fl = fm; }
                    else right = middle;
                }
                var root = (left + right) / 2; Include(root, _surface.Geometry.Sag(root, 0));
            }
            var offset = _side * high;
            var volume = _side * (ElementVolumeMetrics.RadialSagIntegral(sphere, _min, _max) - _originalIntegral) + high * _area;
            if (!double.IsFinite(volume) || volume < -1e-9 * _area * Math.Max(1, high - low))
                throw new InvalidOperationException("BFSD 加工包络或体积不是有限非负量。");
            var result = new Candidate(c, offset, Math.Max(0, volume), high - low);
            _candidates.Add(normalizedCurvature, result); return result;
        }

        private Candidate Minimize(double a, double b)
        {
            const double ratio = .6180339887498948482;
            var x = b - ratio * (b - a); var y = a + ratio * (b - a);
            var fx = Evaluate(x); var fy = Evaluate(y);
            for (var iteration = 0; iteration < 80 && b - a > 1e-13; iteration++)
            {
                if (fx.Volume <= fy.Volume)
                { b = y; y = x; fy = fx; x = b - ratio * (b - a); fx = Evaluate(x); }
                else
                { a = x; x = y; fx = fy; y = a + ratio * (b - a); fy = Evaluate(y); }
            }
            return Better(fx, fy);
        }

        private static Candidate Better(Candidate a, Candidate b) => b.Volume < a.Volume ? b : a;
        private static StandardGeometry Sphere(double curvature) => new(curvature == 0 ? double.PositiveInfinity : 1 / curvature);
        private static double SphereSlope(double curvature, double radius) => curvature * radius
            / Math.Sqrt(1 - curvature * curvature * radius * radius);
    }
}
