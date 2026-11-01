namespace OpticalSystemDesign.Core.Phase;

/// <summary>Independent normalized radial phases, with continuous phase at the zone boundary.</summary>
public sealed class DualZoneRadialPhaseProfile : IPhaseProfile
{
    public DualZoneRadialPhaseProfile(double boundaryRadius, double outerNormalizationRadius,
        IReadOnlyList<double> innerCoefficients, IReadOnlyList<double> outerCoefficients, int innerOrder = 1, int outerOrder = 1)
    {
        if (outerNormalizationRadius <= boundaryRadius || innerCoefficients.Count > 60 || outerCoefficients.Count > 60)
            throw new ArgumentException("Dual zones require 0 < A1 < A2 and at most 60 terms per zone.");
        Inner = new(boundaryRadius, innerCoefficients, innerOrder); Outer = new(outerNormalizationRadius, outerCoefficients, outerOrder);
    }
    public string Kind => "dual_zone_radial";
    public double Efficiency => 1;
    public NormalizedRadialPhaseProfile Inner { get; }
    public NormalizedRadialPhaseProfile Outer { get; }
    public double PhaseOffset => Inner.Phase(Inner.NormalizationRadius, 0, 550) - Outer.Phase(Inner.NormalizationRadius, 0, 550);
    private bool IsInner(double x, double y) => double.Hypot(x, y) <= Inner.NormalizationRadius;
    public double Phase(double x, double y, double wavelengthNanometers) => IsInner(x, y) ? Inner.Phase(x, y, wavelengthNanometers)
        : Outer.Phase(x, y, wavelengthNanometers) + PhaseOffset;
    public (double Dx, double Dy) Gradient(double x, double y, double wavelengthNanometers) => IsInner(x, y)
        ? Inner.Gradient(x, y, wavelengthNanometers) : Outer.Gradient(x, y, wavelengthNanometers);
    public double ParaxialGradient(double y, double wavelengthNanometers) => Inner.ParaxialGradient(y, wavelengthNanometers);
    public IPhaseProfile Clone() => new DualZoneRadialPhaseProfile(Inner.NormalizationRadius, Outer.NormalizationRadius,
        Inner.Coefficients, Outer.Coefficients, Inner.Order, Outer.Order);
}
