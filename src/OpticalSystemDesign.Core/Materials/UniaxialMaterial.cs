using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Propagation;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Homogeneous uniaxial crystal. Axis is local to its entrance surface.</summary>
public sealed class UniaxialMaterial : IMaterial
{
    public UniaxialMaterial(IMaterial ordinary, IMaterial extraordinary, Vector3D axis, bool traceExtraordinary, bool paraxialIgnore = false)
    {
        if (ordinary is UniaxialMaterial or GradientIndexMaterial or UnresolvedMaterial || extraordinary is UniaxialMaterial or GradientIndexMaterial or UnresolvedMaterial
            || ordinary.PropagationModel is not HomogeneousPropagationModel || extraordinary.PropagationModel is not HomogeneousPropagationModel)
            throw new NotSupportedException("Crystal dispersion curves must be isotropic homogeneous materials.");
        if (!double.IsFinite(axis.Length) || axis.Length <= 0) throw new ArgumentException("A crystal needs a finite nonzero axis.");
        Ordinary = ordinary.Clone(); Extraordinary = extraordinary.Clone(); Axis = axis / axis.Length;
        TraceExtraordinary = traceExtraordinary; ParaxialIgnore = paraxialIgnore;
    }
    public IMaterial Ordinary { get; }
    public IMaterial Extraordinary { get; }
    public Vector3D Axis { get; }
    public bool TraceExtraordinary { get; }
    public bool ParaxialIgnore { get; }
    public string Name => Ordinary.Name;
    public IPropagationModel PropagationModel { get; } = new HomogeneousPropagationModel();
    // Catalog/first-order scalar quantities retain the ordinary index. Real
    // propagation uses the located phase index, not this scalar approximation.
    public double RefractiveIndex(double wavelengthNanometers) => Ordinary.RefractiveIndex(wavelengthNanometers);
    public double ExtinctionCoefficient(double wavelengthNanometers) => Ordinary.ExtinctionCoefficient(wavelengthNanometers);
    public IMaterial Clone() => new UniaxialMaterial(Ordinary, Extraordinary, Axis, TraceExtraordinary, ParaxialIgnore);
    internal OpticalIndexEllipsoid Ellipsoid(double wavelength, Vector3D axis) => new(axis, Ordinary.RefractiveIndex(wavelength),
        Extraordinary.RefractiveIndex(wavelength), TraceExtraordinary);
    internal LocatedUniaxialMaterial At(CoordinateSystem entrance, Vector3D globalDirection) =>
        new(this, entrance.ToGlobalDirection(Axis), globalDirection);
}

/// <summary>Momentum ellipsoid k^T A k = 1; the ray follows normalize(A k).</summary>
public readonly record struct OpticalIndexEllipsoid(Vector3D Axis, double OrdinaryIndex, double ExtraordinaryIndex, bool IsExtraordinary)
{
    internal Vector3D Apply(Vector3D momentum)
    {
        var ordinary = 1 / (OrdinaryIndex * OrdinaryIndex);
        var extra = IsExtraordinary ? 1 / (ExtraordinaryIndex * ExtraordinaryIndex) : ordinary;
        return extra * momentum + (ordinary - extra) * Dot(momentum, Axis) * Axis;
    }
    internal Vector3D Momentum(Vector3D energyDirection)
    {
        var ordinary = OrdinaryIndex * OrdinaryIndex;
        var extra = IsExtraordinary ? ExtraordinaryIndex * ExtraordinaryIndex : ordinary;
        var inverse = extra * energyDirection + (ordinary - extra) * Dot(energyDirection, Axis) * Axis;
        var normalization = Math.Sqrt(Dot(energyDirection, inverse));
        if (!double.IsFinite(normalization) || normalization <= 0) throw new ArithmeticException("Invalid crystal index ellipsoid.");
        return inverse / normalization;
    }
    internal static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

internal sealed class LocatedUniaxialMaterial(UniaxialMaterial material, Vector3D globalAxis, Vector3D energyDirection) : IMaterial
{
    internal UniaxialMaterial Material { get; } = material;
    internal Vector3D GlobalAxis { get; } = globalAxis;
    internal Vector3D EnergyDirection { get; } = energyDirection;
    public string Name => Material.Name;
    public IPropagationModel PropagationModel => Material.PropagationModel;
    public double RefractiveIndex(double wavelengthNanometers) => OpticalIndexEllipsoid.Dot(
        Material.Ellipsoid(wavelengthNanometers, GlobalAxis).Momentum(EnergyDirection), EnergyDirection);
    public double ExtinctionCoefficient(double wavelengthNanometers) => Material.ExtinctionCoefficient(wavelengthNanometers);
    public IMaterial Clone() => new LocatedUniaxialMaterial((UniaxialMaterial)Material.Clone(), GlobalAxis, EnergyDirection);
}
