namespace OpticalSystemDesign.Core.Differentiation;

/// <summary>Forward geometry queries on one private, immutable optical/parameter snapshot.
/// Each worker owns its workspace; cached profiles never cross coordinates, tapes or evaluations.</summary>
public sealed class DifferentiableGeometryWorkspace
{
    private const int MaximumCachedProfiles = 8192;
    private readonly Optic _optic;
    private readonly OpticalDifferentialContext _context;
    private readonly CancellationToken _token;
    private readonly Dictionary<int, DifferentialScalar> _positions = [];
    private readonly Dictionary<(int Surface, long Radius, bool Relaxed), (DifferentialScalar Sag, DifferentialScalar Slope)> _profiles = [];
    public long Requests { get; private set; }
    public long ProfileEvaluations { get; private set; }
    public long ProfileCacheHits { get; private set; }

    public DifferentiableGeometryWorkspace(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic); ArgumentNullException.ThrowIfNull(parameters);
        _token = cancellationToken; Check();
        _optic = Optic.FromSnapshot(optic.ToSnapshot());
        _context = new(_optic, parameters.ToArray());
    }

    public DifferentialScalar AxialSeparation(int firstSurface, int secondSurface, double radius,
        bool relaxedDomain = false, bool maskOutsideProfile = false)
    {
        Check(); Requests++;
        // Keep the scalar API's arithmetic order, including signed zero and parameter tangent order.
        return Position(secondSurface) - Position(firstSurface)
            + Profile(secondSurface, Mask(secondSurface, radius, maskOutsideProfile), relaxedDomain).Sag
            - Profile(firstSurface, Mask(firstSurface, radius, maskOutsideProfile), relaxedDomain).Sag;
    }

    public DifferentialScalar SurfaceSlope(int surfaceIndex, double radius,
        bool relaxedDomain = false, bool maskOutsideProfile = false)
    {
        Check(); Requests++;
        radius = Mask(surfaceIndex, radius, maskOutsideProfile);
        return 2 * radius * Profile(surfaceIndex, radius, relaxedDomain).Slope;
    }

    private double Mask(int surface, double radius, bool mask) => mask && !DifferentiableOpticalMetrics.InsideProfile(_optic, surface, radius) ? 0 : radius;
    private DifferentialScalar Position(int surface)
    {
        if (!_positions.TryGetValue(surface, out var value)) _positions.Add(surface, value = _context.Position(surface));
        return value;
    }
    private (DifferentialScalar Sag, DifferentialScalar Slope) Profile(int surface, double radius, bool relaxed)
    {
        var key = (surface, BitConverter.DoubleToInt64Bits(radius), relaxed);
        if (_profiles.TryGetValue(key, out var value)) { ProfileCacheHits++; return value; }
        var measured = _context.Profile(surface, radius, 0, relaxed);
        value = (measured.Sag, measured.RadialSlope); ProfileEvaluations++;
        if (_profiles.Count >= MaximumCachedProfiles) _profiles.Clear();
        _profiles.Add(key, value); return value;
    }
    private void Check()
    {
        _token.ThrowIfCancellationRequested();
        if (DifferentialTape.Current is not null) throw new NotSupportedException("A geometry workspace requires forward tangents outside a reverse-tape lifetime.");
    }
}
