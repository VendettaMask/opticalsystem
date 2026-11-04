using System.Numerics;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.NonSequential;

public sealed record NonSequentialSourcePhysics(SystemPolarization? Polarization = null,
    string? CoherenceGroup = null, double PhaseDegrees = 0);

/// <summary>Explicit real/imaginary storage; does not depend on a framework Complex JSON converter.</summary>
public sealed record NonSequentialComplexVector(Vector3D Real, Vector3D Imaginary)
{
    [JsonIgnore] public ComplexElectricField Field => new(new(Real.X, Imaginary.X), new(Real.Y, Imaginary.Y), new(Real.Z, Imaginary.Z));
    public static NonSequentialComplexVector From(ComplexElectricField field) => new(
        new(field.X.Real, field.Y.Real, field.Z.Real), new(field.X.Imaginary, field.Y.Imaginary, field.Z.Imaginary));
}

/// <summary>Two mutually incoherent normalized Jones modes in world coordinates. Modes in the same
/// declared coherence group and wavelength interfere with their matching mode, never with each other.</summary>
public sealed record NonSequentialFieldState(NonSequentialComplexVector First, NonSequentialComplexVector Second, string CoherenceGroup)
{
    [JsonIgnore] public double Power => First.Field.SquaredNorm + Second.Field.SquaredNorm;
    internal static NonSequentialFieldState Create(Vector3D direction, NonSequentialSourcePhysics? source, string defaultGroup,
        Func<Vector3D, Vector3D>? toLocal = null, Func<Vector3D, Vector3D>? toWorld = null)
    {
        var polarization = source?.Polarization ?? new SystemPolarization(); polarization.Validate();
        if (source is not null && (!double.IsFinite(source.PhaseDegrees) || source.CoherenceGroup is { Length: > 128 }
            || source.CoherenceGroup?.Any(char.IsControl) == true)) throw new ArgumentException("Invalid source phase/coherence identity.");
        var localDirection = toLocal?.Invoke(direction) ?? direction;
        var input = polarization.Input;
        // An unpolarized field has no preferred transverse axis. Select a nonsingular local basis;
        // a polarized source keeps its declared axis and reports a parallel-axis error.
        (Vector3D X, Vector3D Y, Complex Jx, Complex Jy) basis;
        try { basis = input.Resolve(localDirection); }
        catch (NotSupportedException) when (polarization.Unpolarized)
        {
            var axis = Math.Abs(localDirection.X) <= Math.Abs(localDirection.Y) && Math.Abs(localDirection.X) <= Math.Abs(localDirection.Z)
                ? PolarizationReferenceAxis.X : Math.Abs(localDirection.Y) <= Math.Abs(localDirection.Z) ? PolarizationReferenceAxis.Y : PolarizationReferenceAxis.Z;
            input = input with { ReferenceAxis = axis };
            basis = input.Resolve(localDirection);
        }
        var x = toWorld?.Invoke(basis.X) ?? basis.X; var y = toWorld?.Invoke(basis.Y) ?? basis.Y;
        var phase = Complex.FromPolarCoordinates(1, Math.IEEERemainder(source?.PhaseDegrees ?? 0, 360) * Math.PI / 180);
        var first = polarization.Unpolarized ? ComplexElectricField.FromReal(x) * (1 / Math.Sqrt(2))
            : ComplexElectricField.FromReal(x) * basis.Jx + ComplexElectricField.FromReal(y) * basis.Jy;
        var second = polarization.Unpolarized ? ComplexElectricField.FromReal(y) * (1 / Math.Sqrt(2)) : default;
        return new(NonSequentialComplexVector.From(first * phase), NonSequentialComplexVector.From(second * phase), source?.CoherenceGroup ?? defaultGroup);
    }

    internal NonSequentialFieldState Propagate(double opticalDistanceMillimeters, double wavelengthNanometers)
    {
        var wavelength = wavelengthNanometers * 1e-6;
        // Same negative-time convention as the shared n+ik coating solver: exp(+i k.OPL).
        var phase = Complex.FromPolarCoordinates(1, 2 * Math.PI * Math.IEEERemainder(opticalDistanceMillimeters, wavelength) / wavelength);
        return this with { First = NonSequentialComplexVector.From(First.Field * phase), Second = NonSequentialComplexVector.From(Second.Field * phase) };
    }

    internal (NonSequentialFieldState State, double Fraction) Apply(Vector3D incoming, Vector3D outgoing, Vector3D normal, Complex s, Complex p)
    {
        var transport = new UnpolarizedPowerTransport(incoming);
        // Apply uses the common physical s/p basis and handles normal-incidence degeneracy in shared Core.
        transport.Apply(incoming, outgoing, normal, s, p);
        // Resolve an arbitrary input through the transported real basis; no second Fresnel implementation.
        var input = new UnpolarizedPowerTransport(incoming);
        var u = input.Combine(Complex.One, Complex.Zero); var v = input.Combine(Complex.Zero, Complex.One);
        ComplexElectricField Transform(ComplexElectricField field) => transport.Combine(
            field.X * u.X + field.Y * u.Y + field.Z * u.Z,
            field.X * v.X + field.Y * v.Y + field.Z * v.Z);
        var a = Transform(First.Field); var b = Transform(Second.Field); var fraction = a.SquaredNorm + b.SquaredNorm;
        if (!double.IsFinite(fraction) || fraction < 0) throw new ArithmeticException("Invalid polarized interface power.");
        var norm = fraction > 0 ? 1 / Math.Sqrt(fraction) : 0;
        return (this with { First = NonSequentialComplexVector.From(a * norm), Second = NonSequentialComplexVector.From(b * norm) }, fraction);
    }
}

/// <summary>Incident field at an interaction, including propagation phase and actual W flux scale.
/// It is deliberately separate from a split segment's outgoing scalar intensity.</summary>
public sealed record NonSequentialIncidentField(NonSequentialFieldState State, double PowerWatts, double RefractiveIndex);
public sealed record NonSequentialStokesFrame(IReadOnlyList<double> I, IReadOnlyList<double> Q, IReadOnlyList<double> U, IReadOnlyList<double> V,
    IReadOnlyList<double> LongitudinalPowerWatts);
public sealed record NonSequentialCoherentFrame(IReadOnlyList<double> RawPowerWatts, IReadOnlyList<double> PowerWatts,
    bool NormalizeTotalPower, string Protocol = "NscJonesCoherence/1; mode sums; wavelength/group separation; pixel-centre phase")
{
    public IReadOnlyList<NonSequentialCoherentModeFrame> Modes { get; init; } = [];
}
public sealed record NonSequentialCoherentModeFrame(string CoherenceGroup,
    IReadOnlyList<NonSequentialComplexVector> First, IReadOnlyList<NonSequentialComplexVector> Second)
{
    public IReadOnlyList<double>? SummedRayAmplitudes { get; init; }
    public IReadOnlyList<long>? HitCounts { get; init; }
}
