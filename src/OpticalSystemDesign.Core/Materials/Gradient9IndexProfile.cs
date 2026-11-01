using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Dispersive radial profile n = (B+C/λ²)[1 − (K0+K1/λ²+K2/λ⁴)² r²/2], λ in µm.</summary>
public sealed class Gradient9IndexProfile : ISpatialRefractiveIndex
{
    public Gradient9IndexProfile(double b, double c, double k0, double k1, double k2)
    {
        if (new[] { b, c, k0, k1, k2 }.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Gradient 9 coefficients must be finite.");
        B = b; C = c; K0 = k0; K1 = k1; K2 = k2;
    }
    public double B { get; }
    public double C { get; }
    public double K0 { get; }
    public double K1 { get; }
    public double K2 { get; }
    public (double BaseIndex, double RadialQuadratic) Coefficients(double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(Vector3D.Zero, wavelengthNanometers);
        var inverseSquare = 1 / Math.Pow(wavelengthNanometers * .001, 2);
        var n0 = B + C * inverseSquare;
        var k = K0 + inverseSquare * (K1 + K2 * inverseSquare);
        return (n0, -.5 * n0 * k * k);
    }
    public double RefractiveIndex(Vector3D position, double wavelengthNanometers) => Evaluate(position, wavelengthNanometers).Index;
    public SpatialIndexSample Evaluate(Vector3D position, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(position, wavelengthNanometers);
        var (n0, nr2) = Coefficients(wavelengthNanometers);
        return SpatialIndexValidation.Sample(n0 + nr2 * (position.X * position.X + position.Y * position.Y),
            new(2 * nr2 * position.X, 2 * nr2 * position.Y, 0));
    }
}
