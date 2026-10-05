using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Propagation;

public sealed record GradientIndexIntegrationOptions(
    double MaximumStep = 0.1,
    double MinimumStep = 1e-12,
    double PositionTolerance = 1e-10,
    double DirectionTolerance = 1e-11,
    double OpticalPathTolerance = 1e-10,
    double RelativeTolerance = 1e-11,
    double SurfaceTolerance = 1e-10,
    int MaximumAttempts = 100_000,
    bool RetainPath = false);

public enum GradientIndexTermination
{
    DistanceReached,
    SurfaceReached,
    SurfaceTangent,
    PathLimitReached
}

public readonly record struct GradientIndexPathPoint(
    Vector3D Position, Vector3D Direction, double PathLength, double OpticalPathLength, double RefractiveIndex);

public sealed record GradientIndexTraceResult(
    GradientIndexTermination Termination,
    GradientIndexPathPoint End,
    IReadOnlyList<GradientIndexPathPoint> Path,
    int IntegrationAttempts,
    int DifferentialEvaluations,
    double? SurfaceResidual)
{
    public Vector3D ProfileLocalPosition { get; init; }
}

/// <summary>
/// Integrates d r/ds = u, d u/ds = (grad(n) - u (u·grad(n)))/n, d OPL/ds = n.
/// Uses the Dormand–Prince 5(4) pair in the index field's coordinate system.
/// This is geometric propagation only: boundary refraction, attenuation and
/// polarization are the caller's responsibility. No straight-ray fallback.
/// </summary>
public static class GradientIndexRayIntegrator
{
    public static GradientIndexTraceResult TraceDistance(
        ISpatialRefractiveIndex profile, CoordinateSystem profileCoordinates,
        Vector3D origin, Vector3D direction, double wavelengthNanometers, double distance,
        GradientIndexIntegrationOptions? options = null, CancellationToken cancellationToken = default) =>
        new Integration(profile, profileCoordinates, origin, direction, wavelengthNanometers,
            distance, options ?? new(), cancellationToken).Run(null, null);

    /// <summary>
    /// Finds a bracketed crossing of z - sag(x,y) on the curved path. Midpoint
    /// checks reduce missed crossings, but do not certify all roots for arbitrary
    /// oscillatory profiles/surfaces. Tangency is reported separately.
    /// </summary>
    public static GradientIndexTraceResult TraceToSurface(
        ISpatialRefractiveIndex profile, CoordinateSystem profileCoordinates,
        Vector3D origin, Vector3D direction, double wavelengthNanometers,
        IGeometry targetGeometry, CoordinateSystem targetCoordinates, double maximumPathLength,
        GradientIndexIntegrationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetGeometry);
        ArgumentNullException.ThrowIfNull(targetCoordinates);
        return new Integration(profile, profileCoordinates, origin, direction, wavelengthNanometers,
            maximumPathLength, options ?? new(), cancellationToken).Run(targetGeometry, targetCoordinates);
    }

    private readonly record struct State(Vector3D Position, Vector3D Direction, double OpticalPath)
    {
        public static State operator +(State a, State b) =>
            new(a.Position + b.Position, a.Direction + b.Direction, a.OpticalPath + b.OpticalPath);
        public static State operator -(State a, State b) =>
            new(a.Position - b.Position, a.Direction - b.Direction, a.OpticalPath - b.OpticalPath);
        public static State operator *(State a, double scale) =>
            new(a.Position * scale, a.Direction * scale, a.OpticalPath * scale);
    }

    private sealed class Integration
    {
        private readonly ISpatialRefractiveIndex _profile;
        private readonly CoordinateSystem _coordinates;
        private readonly double _wavelength;
        private readonly double _limit;
        private readonly GradientIndexIntegrationOptions _options;
        private readonly CancellationToken _token;
        private readonly List<GradientIndexPathPoint>? _path;
        private State _state;
        private double _length;
        private int _attempts;
        private int _evaluations;

        internal Integration(ISpatialRefractiveIndex profile, CoordinateSystem coordinates,
            Vector3D origin, Vector3D direction, double wavelength, double limit,
            GradientIndexIntegrationOptions options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(coordinates);
            SpatialIndexValidation.Input(origin, wavelength);
            CheckCoordinates(coordinates);
            if (!SpatialIndexValidation.Finite(direction) || !double.IsFinite(direction.Length) || direction.Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (!double.IsFinite(limit) || limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            ValidateOptions(options);
            _profile = profile;
            _coordinates = coordinates;
            _wavelength = wavelength;
            _limit = limit;
            _options = options;
            _token = token;
            _path = options.RetainPath ? [] : null;
            _state = new State(coordinates.ToLocalPoint(origin), coordinates.ToLocalDirection(direction / direction.Length), 0);
        }

        internal GradientIndexTraceResult Run(IGeometry? geometry, CoordinateSystem? targetCoordinates)
        {
            _token.ThrowIfCancellationRequested();
            if (targetCoordinates is not null) CheckCoordinates(targetCoordinates);
            AddPoint();
            // Validate the initial index even if no steps are requested or path retention is off.
            Sample(_state.Position);
            var gap = geometry is null ? 0 : Gap(_state, geometry, targetCoordinates!);
            if (geometry is not null && Math.Abs(gap) <= _options.SurfaceTolerance)
                return Finish(SurfaceStatus(_state, geometry, targetCoordinates!), gap);
            var h = Math.Min(_options.MaximumStep, _limit);
            while (_length < _limit)
            {
                _token.ThrowIfCancellationRequested();
                h = Math.Min(h, _limit - _length);
                if (!double.IsFinite(h) || h <= 0 || _length + h == _length)
                    throw new InvalidOperationException("GRIN 积分步长小于当前数值分辨率。");
                var (next, error) = Step(_state, h);
                if (error > 1)
                {
                    h = Reduce(h, error);
                    continue;
                }
                if (geometry is not null)
                {
                    var (middle, middleError) = Step(_state, h / 2);
                    if (middleError > 1)
                    {
                        h = Reduce(h, middleError);
                        continue;
                    }
                    var middleGap = Gap(middle, geometry, targetCoordinates!);
                    var nextGap = Gap(next, geometry, targetCoordinates!);
                    double lower = 0, upper = 0;
                    if (Crosses(gap, middleGap)) upper = h / 2;
                    else if (Crosses(middleGap, nextGap)) { lower = h / 2; upper = h; }
                    if (upper > 0)
                    {
                        var hit = LocateSurface(_state, lower, upper, lower == 0 ? gap : middleGap,
                            geometry, targetCoordinates!);
                        _state = Normalize(hit.State);
                        _length += hit.Distance;
                        AddPoint();
                        return Finish(SurfaceStatus(_state, geometry, targetCoordinates!),
                            Gap(_state, geometry, targetCoordinates!));
                    }
                    // Rounding can leave an endpoint infinitesimally on the
                    // starting side, especially when the path budget ends here.
                    if (Math.Abs(nextGap) <= _options.SurfaceTolerance)
                    {
                        _state = Normalize(next);
                        _length += h;
                        AddPoint();
                        return Finish(SurfaceStatus(_state, geometry, targetCoordinates!), nextGap);
                    }
                    gap = nextGap;
                }
                _state = Normalize(next);
                _length += h;
                AddPoint();
                h = Math.Min(_options.MaximumStep, h * (error == 0 ? 5 : Math.Clamp(0.9 * Math.Pow(error, -0.2), 0.2, 5)));
            }
            return Finish(geometry is null ? GradientIndexTermination.DistanceReached : GradientIndexTermination.PathLimitReached,
                geometry is null ? null : gap);
        }

        private (State State, double Distance) LocateSurface(State start, double lower, double upper,
            double lowerGap, IGeometry geometry, CoordinateSystem coordinates)
        {
            for (var i = 0; i < 80; i++)
            {
                var middle = lower + (upper - lower) / 2;
                var (state, error) = Step(start, middle);
                if (error > 1) throw new InvalidOperationException("GRIN 交点细化未达到积分精度；请减小最大步长。");
                var gap = Gap(state, geometry, coordinates);
                if (Math.Abs(gap) <= _options.SurfaceTolerance && upper - lower <= _options.SurfaceTolerance)
                    return (state, middle);
                if (Crosses(lowerGap, gap)) upper = middle;
                else { lower = middle; lowerGap = gap; }
            }
            throw new InvalidOperationException("GRIN 曲线与目标表面的交点未收敛。");
        }

        private (State State, double Error) Step(State start, double h)
        {
            _token.ThrowIfCancellationRequested();
            if (++_attempts > _options.MaximumAttempts)
                throw new InvalidOperationException("GRIN 积分超过尝试次数上限；未返回近似成功结果。");
            try { return ComputeStep(start, h); }
            catch (SpatialIndexDomainException)
            {
                // A large trial step can leave the valid domain even when the
                // actual curve does not. Reject and shrink it; never clamp n.
                return (start, double.PositiveInfinity);
            }
        }

        private (State State, double Error) ComputeStep(State start, double h)
        {
            var k1 = Derivative(start);
            var k2 = Derivative(start + k1 * (h / 5));
            var k3 = Derivative(start + (k1 * (3.0 / 40) + k2 * (9.0 / 40)) * h);
            var k4 = Derivative(start + (k1 * (44.0 / 45) - k2 * (56.0 / 15) + k3 * (32.0 / 9)) * h);
            var k5 = Derivative(start + (k1 * (19372.0 / 6561) - k2 * (25360.0 / 2187)
                + k3 * (64448.0 / 6561) - k4 * (212.0 / 729)) * h);
            var k6 = Derivative(start + (k1 * (9017.0 / 3168) - k2 * (355.0 / 33)
                + k3 * (46732.0 / 5247) + k4 * (49.0 / 176) - k5 * (5103.0 / 18656)) * h);
            var high = start + (k1 * (35.0 / 384) + k3 * (500.0 / 1113) + k4 * (125.0 / 192)
                - k5 * (2187.0 / 6784) + k6 * (11.0 / 84)) * h;
            var k7 = Derivative(high);
            var low = start + (k1 * (5179.0 / 57600) + k3 * (7571.0 / 16695) + k4 * (393.0 / 640)
                - k5 * (92097.0 / 339200) + k6 * (187.0 / 2100) + k7 * (1.0 / 40)) * h;
            var delta = high - low;
            var positionalScale = _options.PositionTolerance + _options.RelativeTolerance * (_length + h);
            var directionScale = _options.DirectionTolerance + _options.RelativeTolerance;
            var opticalScale = _options.OpticalPathTolerance + _options.RelativeTolerance * Math.Abs(high.OpticalPath);
            var error = Math.Max(MaxComponent(delta.Position) / positionalScale,
                Math.Max(MaxComponent(delta.Direction) / directionScale, Math.Abs(delta.OpticalPath) / opticalScale));
            if (!double.IsFinite(error) || !double.IsFinite(high.OpticalPath))
                throw new SpatialIndexDomainException("GRIN 积分产生非有限试探结果。");
            return (high, error);
        }

        private State Derivative(State state)
        {
            _evaluations++;
            var sample = Sample(state.Position);
            var along = Dot(state.Direction, sample.Gradient);
            return new State(state.Direction, (sample.Gradient - state.Direction * along) / sample.Index, sample.Index);
        }

        private SpatialIndexSample Sample(Vector3D position)
        {
            _token.ThrowIfCancellationRequested();
            if (!SpatialIndexValidation.Finite(position))
                throw new SpatialIndexDomainException("GRIN 试探位置超出有限坐标范围。");
            var sample = _profile.Evaluate(position, _wavelength);
            return SpatialIndexValidation.Sample(sample.Index, sample.Gradient);
        }

        private double Reduce(double step, double error)
        {
            var reduced = step * Math.Clamp(0.9 * Math.Pow(error, -0.2), 0.1, 0.5);
            if (reduced < _options.MinimumStep)
                throw new InvalidOperationException("GRIN 积分无法在最小步长内达到指定误差。");
            return reduced;
        }

        private GradientIndexPathPoint Point() => new(
            _coordinates.ToGlobalPoint(_state.Position), _coordinates.ToGlobalDirection(_state.Direction),
            _length, _state.OpticalPath, Sample(_state.Position).Index);

        private void AddPoint() { if (_path is not null) _path.Add(Point()); }

        private GradientIndexTraceResult Finish(GradientIndexTermination status, double? residual) =>
            new(status, Point(), _path?.AsReadOnly() ?? (IReadOnlyList<GradientIndexPathPoint>)Array.Empty<GradientIndexPathPoint>(),
                _attempts, _evaluations, residual)
            { ProfileLocalPosition = _state.Position };

        private double Gap(State state, IGeometry geometry, CoordinateSystem coordinates)
        {
            var local = ToTargetPoint(state.Position, coordinates);
            var gap = local.Z - geometry.Sag(local.X, local.Y);
            if (!double.IsFinite(gap)) throw new InvalidOperationException("GRIN 目标表面矢高超出定义域。");
            return gap;
        }

        private GradientIndexTermination SurfaceStatus(State state, IGeometry geometry, CoordinateSystem coordinates)
        {
            var point = ToTargetPoint(state.Position, coordinates);
            var direction = coordinates.ToLocalDirection(_coordinates.ToGlobalDirection(state.Direction));
            var normal = geometry.SurfaceNormal(point);
            if (!SpatialIndexValidation.Finite(normal) || !double.IsFinite(normal.Length) || normal.Length <= 0)
                throw new InvalidOperationException("GRIN 目标表面法线无效。");
            return Math.Abs(Dot(normal / normal.Length, direction)) <= 1e-10
                ? GradientIndexTermination.SurfaceTangent : GradientIndexTermination.SurfaceReached;
        }

        private Vector3D ToTargetPoint(Vector3D localPosition, CoordinateSystem target) =>
            // Subtract origins before adding the local displacement. Going via
            // an absolute global point would quantize intersections at large offsets.
            target.ToLocalDirection((_coordinates.Origin - target.Origin) + _coordinates.ToGlobalDirection(localPosition));
    }

    private static State Normalize(State state)
    {
        var length = state.Direction.Length;
        if (!double.IsFinite(length) || length <= 0) throw new InvalidOperationException("GRIN 光线方向无效。");
        return state with { Direction = state.Direction / length };
    }

    private static bool Crosses(double a, double b) => a == 0 || b == 0 || Math.Sign(a) != Math.Sign(b);
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double MaxComponent(Vector3D vector) => Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z)));

    private static void CheckCoordinates(CoordinateSystem coordinates)
    {
        if (!SpatialIndexValidation.Finite(coordinates.Origin) || !double.IsFinite(coordinates.RotationXDegrees)
            || !double.IsFinite(coordinates.RotationYDegrees) || !double.IsFinite(coordinates.RotationZDegrees))
            throw new ArgumentOutOfRangeException(nameof(coordinates));
    }

    internal static void ValidateOptions(GradientIndexIntegrationOptions options)
    {
        var positive = new[] { options.MaximumStep, options.MinimumStep, options.PositionTolerance,
            options.DirectionTolerance, options.OpticalPathTolerance, options.SurfaceTolerance };
        if (positive.Any(value => !double.IsFinite(value) || value <= 0)
            || !double.IsFinite(options.RelativeTolerance) || options.RelativeTolerance < 0
            || options.MinimumStep > options.MaximumStep || options.MaximumAttempts is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(options), "GRIN 积分容差、步长或次数上限无效。");
    }
}
