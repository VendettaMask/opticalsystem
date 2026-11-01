using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Conservative radial bounds around the existing exact rotational geometry, not a fitted surface.</summary>
internal sealed partial class RotationalSurfaceDomain
{
    private readonly double _radius, _conic;
    private readonly double[] _coefficients;
    private readonly int[] _powers;
    public IGeometry Geometry { get; }
    public double ApertureRadius { get; }
    public double MaximumSlope { get; }
    public (double Low, double High) SagBounds { get; }

    public RotationalSurfaceDomain(IGeometry geometry, double apertureRadius)
    {
        Geometry = geometry; ApertureRadius = apertureRadius;
        (_radius, _conic, _coefficients, _powers) = geometry switch
        {
            PlaneGeometry => (0d, 0d, Array.Empty<double>(), Array.Empty<int>()),
            StandardGeometry g => (g.Radius, g.Conic, Array.Empty<double>(), Array.Empty<int>()),
            EvenAsphereGeometry g => (g.Base.Radius, g.Base.Conic, g.Coefficients.ToArray(), Enumerable.Range(1, g.Coefficients.Count).Select(i => i * 2).ToArray()),
            OddAsphereGeometry g => (g.Base.Radius, g.Base.Conic, g.Coefficients.ToArray(), Enumerable.Range(1, g.Coefficients.Count).ToArray()),
            _ => throw new NotSupportedException("A certified radial domain requires a rotational surface.")
        };
        var curvature = Math.Abs(_radius) < 1e-12 || double.IsInfinity(_radius) ? 0 : 1 / _radius;
        var minimumArgument = Math.Min(1, 1 - (1 + _conic) * curvature * curvature * apertureRadius * apertureRadius);
        if (minimumArgument <= 1e-12) throw new InvalidDataException("机械口径越过旋转曲面的正则实数定义域。");
        MaximumSlope = Math.Abs(curvature) * apertureRadius / Math.Sqrt(minimumArgument);
        for (int i = 0; i < _coefficients.Length; i++)
            MaximumSlope += _powers[i] * Math.Abs(_coefficients[i]) * Math.Pow(apertureRadius, _powers[i] - 1);
        SagBounds = Bound(0, apertureRadius);
        if (!double.IsFinite(MaximumSlope) || !double.IsFinite(SagBounds.Low) || !double.IsFinite(SagBounds.High))
            throw new InvalidDataException("非球面在完整机械口径上的高度或导数界非有限。");
    }

    public (double Low, double High) Bound(double lowRadius, double highRadius)
    {
        var basis = new StandardGeometry(_radius, _conic);
        var a = basis.Sag(lowRadius, 0); var b = basis.Sag(highRadius, 0);
        double low = Math.Min(a, b), high = Math.Max(a, b);
        for (int i = 0; i < _coefficients.Length; i++)
        {
            a = _coefficients[i] * Math.Pow(lowRadius, _powers[i]); b = _coefficients[i] * Math.Pow(highRadius, _powers[i]);
            low += Math.Min(a, b); high += Math.Max(a, b);
        }
        return Expand(low, high);
    }

    private (double Low,double High) SlopeFactorBound(double lowRadius,double highRadius)
    {
        var curvature=Math.Abs(_radius)<1e-12||double.IsInfinity(_radius)?0:1/_radius;
        var factor=(1+_conic)*curvature*curvature;
        var a=curvature/Math.Sqrt(1-factor*lowRadius*lowRadius);var b=curvature/Math.Sqrt(1-factor*highRadius*highRadius);
        double low=Math.Min(a,b),high=Math.Max(a,b);
        for(int i=0;i<_coefficients.Length;i++)
        {
            if(_coefficients[i]==0)continue;
            var coefficient=_powers[i]*_coefficients[i];var power=_powers[i]-2;
            a=coefficient*Math.Pow(lowRadius,power);b=coefficient*Math.Pow(highRadius,power);
            low+=Math.Min(a,b);high+=Math.Max(a,b);
        }
        var rounding=64*2.2204460492503131e-16*Math.Max(Math.Abs(low),Math.Abs(high));
        return(Math.BitDecrement(low-rounding),Math.BitIncrement(high+rounding));
    }

    public static (double Low, double High) Difference(RotationalSurfaceDomain a, double weightA, RotationalSurfaceDomain b, double weightB)
    {
        // Correlate identical bases and every polynomial power; a conformal thin
        // layer is not rejected merely because both individual sag ranges are large.
        double low, high;
        if (a._radius == b._radius && a._conic == b._conic && a.ApertureRadius == b.ApertureRadius)
        {
            var edge = new StandardGeometry(a._radius, a._conic).Sag(a.ApertureRadius, 0) * (weightB - weightA);
            low = Math.Min(0, edge); high = Math.Max(0, edge);
        }
        else
        {
            var ar = new StandardGeometry(a._radius, a._conic).Sag(a.ApertureRadius, 0) * -weightA;
            var br = new StandardGeometry(b._radius, b._conic).Sag(b.ApertureRadius, 0) * weightB;
            low = Math.Min(0, ar) + Math.Min(0, br); high = Math.Max(0, ar) + Math.Max(0, br);
        }
        var terms = new Dictionary<int, double>();
        for (int i = 0; i < a._powers.Length; i++) terms[a._powers[i]] = terms.GetValueOrDefault(a._powers[i]) - weightA * a._coefficients[i] * Math.Pow(a.ApertureRadius, a._powers[i]);
        for (int i = 0; i < b._powers.Length; i++) terms[b._powers[i]] = terms.GetValueOrDefault(b._powers[i]) + weightB * b._coefficients[i] * Math.Pow(b.ApertureRadius, b._powers[i]);
        foreach (var term in terms.Values) { low += Math.Min(0, term); high += Math.Max(0, term); }
        return Expand(low, high);
    }

    private static (double Low, double High) Expand(double low, double high)
    {
        var rounding = 64 * 2.2204460492503131e-16 * Math.Max(1, Math.Max(Math.Abs(low), Math.Abs(high)));
        return (low - rounding, high + rounding);
    }
}
