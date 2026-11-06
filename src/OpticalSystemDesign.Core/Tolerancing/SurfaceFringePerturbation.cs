using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Tolerancing;

public enum SurfaceFringeMode { Power, Irregularity }

/// <summary>Double-pass inspection fringes applied as real surface geometry.</summary>
public sealed class SurfaceFringePerturbation : IScaledRangePerturbation
{
    public const double DefaultTestWavelengthMicrometers = .6328;
    private readonly int _surfaceNumber;
    private readonly SurfaceFringeMode _mode;
    private readonly double _testWavelengthMillimeters;
    private readonly bool _normalDistribution;
    private IGeometry? _original;

    public SurfaceFringePerturbation(string name, int surfaceNumber, SurfaceFringeMode mode,
        double testWavelengthMicrometers, double minimum, double maximum, bool normalDistribution)
    {
        if (surfaceNumber <= 0 || !Enum.IsDefined(mode) || !IsValidTestWavelength(testWavelengthMicrometers)
            || !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
            throw new ArgumentException("Invalid surface, test wavelength or fringe bounds.");
        Name = OptimizationGuards.RequireName(name, nameof(name));
        _surfaceNumber = surfaceNumber;
        _mode = mode;
        _testWavelengthMillimeters = testWavelengthMicrometers * .001;
        Minimum = minimum;
        Maximum = maximum;
        _normalDistribution = normalDistribution;
    }

    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }

    public static bool IsValidTestWavelength(double micrometers) =>
        double.IsFinite(micrometers) && micrometers is >= .001 and <= 1000;

    public static bool Supports(OpticalSurface surface) =>
        surface.InteractionModel is RefractiveReflectiveInteractionModel
        && (surface.Geometry is StandardGeometry or PlaneGeometry { ReferenceIncidenceNormal: null })
        && (surface.PhysicalAperture is null or CircularAperture)
        && double.IsFinite(ApertureRadius(surface)) && ApertureRadius(surface) > 0;

    public static bool IsValidPowerChange(OpticalSurface surface, double testWavelengthMicrometers, double fringes,
        double? referenceRadius = null) =>
        TryPowerCurvature(surface, testWavelengthMicrometers * .001, fringes, out _, referenceRadius);

    public void Apply(Optic optic) => Apply(optic, new Random(1234));

    public void Apply(Optic optic, Random random)
    {
        var (value, angle) = DrawFringes(random);
        ApplyFringes(optic, value, angle);
    }

    public void ApplyMinimum(Optic optic) => ApplyScaledMinimum(optic, 1);
    public void ApplyMaximum(Optic optic) => ApplyScaledMaximum(optic, 1);
    public void ApplyScaledMinimum(Optic optic, double scale) =>
        ApplyFringes(optic, Minimum * VariableRangePerturbation.ValidateScale(scale), 0);
    public void ApplyScaledMaximum(Optic optic, double scale) =>
        ApplyFringes(optic, Maximum * VariableRangePerturbation.ValidateScale(scale), 0);

    public void Revert(Optic optic)
    {
        if (_original is null) return;
        Target(optic).Geometry = _original;
        _original = null;
    }

    internal (double Fringes, double Angle) DrawFringes(Random random)
    {
        var value = VariableRangePerturbation.SampleDelta(random, Minimum, Maximum, _normalDistribution);
        return (value, _mode == SurfaceFringeMode.Irregularity ? 360 * random.NextDouble() : 0);
    }

    internal double ReferenceRadius(Optic optic) => ApertureRadius(Target(optic));

    internal void ApplyFringes(Optic optic, double fringes, double angleDegrees, double? referenceRadius = null)
    {
        var surface = Target(optic);
        if (!Supports(surface) && !(_mode == SurfaceFringeMode.Power && surface.Geometry is IrregularGeometry))
            throw new InvalidOperationException("Fringe tolerances require a Standard refracting or reflecting surface with a circular clear aperture.");

        _original = surface.Geometry;
        if (fringes == 0) return;

        var radius = referenceRadius ?? ApertureRadius(surface);
        if (_mode == SurfaceFringeMode.Power)
        {
            if (!TryPowerCurvature(surface, _testWavelengthMillimeters, fringes, out var curvature, radius))
                throw new InvalidOperationException("Fringe power change places the clear aperture outside the conic domain.");
            surface.Radius = curvature == 0 ? 0 : 1 / curvature;
            return;
        }

        // Half the double-pass sag error goes to each of the spherical and astigmatic terms.
        var coefficient = _testWavelengthMillimeters * fringes / 4;
        surface.Geometry = new IrregularGeometry(surface.Radius, surface.Conic, radius,
            0, 0, 0, 0, coefficient, coefficient, 0, angleDegrees);
    }

    private static bool TryPowerCurvature(OpticalSurface surface, double wavelengthMillimeters, double fringes, out double curvature, double? referenceRadius = null)
    {
        var radius = ApertureRadius(surface);
        var nominalCurvature = double.IsInfinity(surface.Radius) || surface.Radius == 0 ? 0 : 1 / surface.Radius;
        var testRadius = referenceRadius ?? radius;
        curvature = nominalCurvature + wavelengthMillimeters * fringes / (testRadius * testRadius);
        var scaledCurvature = curvature * radius;
        return double.IsFinite(curvature) && 1 - (1 + surface.Conic) * scaledCurvature * scaledCurvature >= 0;
    }

    private OpticalSurface Target(Optic optic) =>
        _surfaceNumber < optic.SurfaceGroup.Items.Count - 1
            ? optic.SurfaceGroup.Items[_surfaceNumber]
            : throw new ArgumentOutOfRangeException(nameof(optic), "A fringe tolerance must target a physical lens surface.");

    public static double ApertureRadius(OpticalSurface surface) =>
        surface.PhysicalAperture is CircularAperture circle ? circle.Radius : surface.SemiDiameter;
}
