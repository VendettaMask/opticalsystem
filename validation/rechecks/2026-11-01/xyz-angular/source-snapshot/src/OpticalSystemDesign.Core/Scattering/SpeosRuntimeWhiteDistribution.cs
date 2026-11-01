using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Runtime White power uses shared upper-cell reference scales. Conditional angular
/// distributions retain the measured power table and declared slope transport; no wavelength
/// colour is applied a second time by the detector. Grid/5 remains a separate frozen model.</summary>
internal sealed class SpeosRuntimeWhiteDistribution : IMeasuredBsdfDistribution
{
    private readonly SpeosThetaDistribution _power, _shape;
    public bool HasTransmission => _shape.HasTransmission;
    public bool HasBothSides => _shape.HasBothSides;

    internal SpeosRuntimeWhiteDistribution(IReadOnlyList<MeasuredBsdfPoint> points, BsdfCoordinates coordinates,
        BsdfStoredQuantity quantity, SpeosSpectralDefinition spectrum, SpeosPeakDefinition peak)
    {
        _power = new(points, coordinates, quantity, spectrum, peak);
        // A nonzero reference shape is needed even where the measured spectrum is zero but
        // White Specular supplies power from its declared base wavelength.
        var flat = spectrum with { RuntimeWhite = false, Lobes = spectrum.Lobes.Select(l => l with
            { Fractions = Array.AsReadOnly(l.Fractions.Select(_ => l.StoredFraction).ToArray()) }).ToArray() };
        _shape = new(points, coordinates, quantity, flat, peak with { WhiteReferenceNanometers = null });
    }
    public BsdfEnergy Integrate(BsdfContext c) => _power.Integrate(c);
    public double Evaluate(BsdfContext c, Vector3D direction)
    {
        var t = BsdfContext.Dot(c.SurfaceNormal, BsdfContext.Unit(direction)) < 0;
        var e = Integrate(c); var s = _shape.Integrate(c);
        var denominator = t ? s.Transmission : s.Reflection;
        return denominator > 0 ? _shape.Evaluate(c, direction) * (t ? e.Transmission : e.Reflection) / denominator : 0;
    }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var energy = Integrate(c);
        if (energy.Total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = u1 * energy.Total; var transmission = energy.Reflection == 0 || target >= energy.Reflection;
        var probability = transmission ? energy.Transmission : energy.Reflection;
        var conditional = Math.Clamp((target - (transmission ? energy.Reflection : 0)) / probability, 0, Math.BitDecrement(1d));
        return _shape.SampleLobe(model, c, conditional, u2, transmission);
    }
}
