using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

public sealed class ForbesNormalSlopeGeometry : IGeometry
{
    private readonly (double F, double G, double H)[] _recurrence;

    public ForbesNormalSlopeGeometry(double radius, double conic, double normalizationRadius,
        IReadOnlyList<double> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (!double.IsFinite(normalizationRadius) || normalizationRadius <= 0 ||
            coefficients.Count is < 1 or > 100 || coefficients.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Invalid Forbes normal-slope radius or coefficients.");

        Base = new StandardGeometry(radius, conic);
        NormalizationRadius = normalizationRadius;
        Coefficients = Array.AsReadOnly(coefficients.ToArray());
        _recurrence = new (double, double, double)[coefficients.Count];
        _recurrence[0] = (2, 0, 0);
        if (_recurrence.Length > 1) _recurrence[1] = (Math.Sqrt(19) / 2, -0.5, 0);

        // Forbes, Optics Express 18, 19700–19712 (2010), Appendix A.
        for (var order = 2; order < _recurrence.Length; order++)
        {
            var previous = _recurrence[order - 1];
            var h = -order * (order - 1) / (2 * _recurrence[order - 2].F);
            var g = -(1 + previous.G * h) / previous.F;
            _recurrence[order] = (Math.Sqrt(order * (order + 1) + 3 - g * g - h * h), g, h);
        }
    }

    public string Kind => "forbes_normal_slope";
    public StandardGeometry Base { get; }
    public double NormalizationRadius { get; }
    public IReadOnlyList<double> Coefficients { get; }

    public double Sag(double x, double y)
    {
        var radiusSquared = x * x + y * y;
        var normalizedSquared = radiusSquared / (NormalizationRadius * NormalizationRadius);
        var weighted = Coefficients[0];
        if (Coefficients.Count > 1)
        {
            var previousP = 2.0;
            var currentP = 6 - 8 * normalizedSquared;
            var previousQ = 1.0;
            var currentQ = (13 - 16 * normalizedSquared) / Math.Sqrt(19);
            weighted += Coefficients[1] * currentQ;
            for (var order = 2; order < Coefficients.Count; order++)
            {
                var nextP = (2 - 4 * normalizedSquared) * currentP - previousP;
                var (f, g, h) = _recurrence[order];
                var nextQ = (nextP - g * currentQ - h * previousQ) / f;
                weighted += Coefficients[order] * nextQ;
                (previousP, currentP) = (currentP, nextP);
                (previousQ, currentQ) = (currentQ, nextQ);
            }
        }

        // Convert the normal departure to sag over the reference conic.
        var curvature = Math.Abs(Base.Radius) <= 1e-14 ? 0 : 1 / Base.Radius;
        var crSquared = curvature * curvature * radiusSquared;
        var projection = Math.Sqrt((1 - Base.Conic * crSquared) / (1 - (1 + Base.Conic) * crSquared));
        return Base.Sag(x, y) + projection * normalizedSquared * (1 - normalizedSquared) * weighted;
    }

    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        StandardGeometry.NewtonSolveDistance(origin, direction, Sag, SurfaceNormal);

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new ForbesNormalSlopeGeometry(Base.Radius, Base.Conic, NormalizationRadius, Coefficients);
}
