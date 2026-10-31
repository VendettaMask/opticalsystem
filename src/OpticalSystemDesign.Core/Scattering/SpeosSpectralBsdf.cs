using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record SpeosSpectralLobe(bool Transmission, IReadOnlyList<double> Wavelengths, IReadOnlyList<double> Fractions, double StoredFraction);
public sealed record SpeosSpectralDefinition(IReadOnlyList<SpeosSpectralLobe> Lobes)
{
    internal SpeosSpectralDefinition Freeze()
    {
        if (Lobes is null || Lobes.Count is < 1 or > 2 || Lobes.Any(l => l is null) || Lobes.Select(l => l.Transmission).Distinct().Count() != Lobes.Count) throw new InvalidDataException("Speos 光谱载荷无效。");
        var result = new List<SpeosSpectralLobe>();
        foreach (var l in Lobes)
        {
            if (l.Wavelengths is null || l.Fractions is null || l.Wavelengths.Count is < 1 or > 256 || l.Wavelengths.Count != l.Fractions.Count
                || l.Wavelengths.Any(w => !double.IsFinite(w) || w is < 200 or > 20000) || l.Fractions.Any(f => !double.IsFinite(f) || f is < 0 or > 1)
                || l.Wavelengths.Zip(l.Wavelengths.Skip(1)).Any(p => p.First >= p.Second) || !double.IsFinite(l.StoredFraction) || l.StoredFraction <= 0 || l.StoredFraction > 1 || l.Fractions.Any(f => f > l.StoredFraction)) throw new InvalidDataException("Speos 光谱必须递增、有限且有界。");
            result.Add(l with { Wavelengths = Array.AsReadOnly(l.Wavelengths.ToArray()), Fractions = Array.AsReadOnly(l.Fractions.ToArray()) });
        }
        return new(Array.AsReadOnly(result.OrderBy(l => l.Transmission).ToArray()));
    }
    internal double Scale(bool transmission, double wave)
    {
        var l = Lobes.SingleOrDefault(l => l.Transmission == transmission); if (l is null) return 0;
        var waves = l.Wavelengths; var tolerance = 32 * 2.2204460492503131e-16 * Math.Max(1, waves[^1]);
        if (wave < waves[0] - tolerance || wave > waves[^1] + tolerance) throw new InvalidDataException("Speos 波长超出测量范围；不外推。");
        wave = Math.Clamp(wave, waves[0], waves[^1]); int low = 0, high = waves.Count - 1;
        while (high - low > 1) { var mid = low + (high - low) / 2; if (waves[mid] <= wave) low = mid; else high = mid; }
        var weight = low == high ? 0 : (wave - waves[low]) / (waves[high] - waves[low]);
        return (l.Fractions[low] * (1 - weight) + l.Fractions[high] * weight) / l.StoredFraction;
    }
}

/// <summary>Compact angle grid times declared spectrum. Each lobe has its own normalization;
/// peak and background can use different spectra without multiplying detector power twice.</summary>
internal sealed class SpeosSpectralDistribution : IMeasuredBsdfDistribution
{
    private readonly record struct Channel(MeasuredBsdf Model, bool Transmission, bool Peak);
    private readonly Channel[] _channels;
    private readonly SpeosSpectralDefinition _spectral;
    private readonly double? _white;
    private readonly double _storedWave;
    public bool HasTransmission => _channels.Any(c => c.Transmission && c.Model.Data.HasTransmission);
    public bool HasBothSides => _channels.All(c => c.Model.Data.HasBothSides);
    private static BsdfContext Probe(MeasuredBsdfPoint p) => new(new(Math.Sin(p.IncidentThetaDegrees * Math.PI / 180) * Math.Cos(p.IncidentPhiDegrees * Math.PI / 180), Math.Sin(p.IncidentThetaDegrees * Math.PI / 180) * Math.Sin(p.IncidentPhiDegrees * Math.PI / 180), MeasuredBsdfGrid.Cos(p.IncidentThetaDegrees)), new(0, 0, 1), p.WavelengthNanometers, p.Side, new(1, 0, 0));
    internal SpeosSpectralDistribution(IReadOnlyList<MeasuredBsdfPoint> points, BsdfCoordinates coordinates, BsdfStoredQuantity quantity, SpeosSpectralDefinition spectral, SpeosPeakDefinition? peak)
    {
        _spectral = spectral; _white = peak?.WhiteReferenceNanometers; _storedWave = points.First().WavelengthNanometers;
        if (points.Any(p => p.WavelengthNanometers != _storedWave)) throw new InvalidDataException("可分 Speos 只保存一个角分布波长标签。");
        var channels = new List<Channel>();
        foreach (var l in spectral.Lobes)
        {
            MeasuredBsdfPoint Filter(MeasuredBsdfPoint p) => p with { BrdfPerSteradian = l.Transmission ? 0 : p.BrdfPerSteradian, BtdfPerSteradian = l.Transmission ? p.BtdfPerSteradian : 0 };
            var diffuse = points.Select(Filter).ToArray();
            channels.Add(new(new(new("Speos background", "embedded", new string('0', 64), diffuse, coordinates: coordinates, sourceFormat: "SpeosSeparableLobe/1", storedQuantity: quantity)), l.Transmission, false));
            if (peak is not null)
            {
                var zero = diffuse.Select(p => p with { BrdfPerSteradian = 0, BtdfPerSteradian = 0 }).ToArray();
                var def = peak with { WhiteReferenceNanometers = null, Points = peak.Points.Select(Filter).ToArray() };
                channels.Add(new(new(new("Speos peak", "embedded", new string('0', 64), zero, coordinates: coordinates, sourceFormat: "SpeosSeparablePeak/1", storedQuantity: quantity, speosPeak: def)), l.Transmission, true));
            }
        }
        _channels = channels.ToArray();
        // The sum is piecewise linear in wavelength and in input cos(theta)/rotation. Check every
        // spectral breakpoint and every angular input. This bounds the full interpolated domain.
        var low = spectral.Lobes.Max(l => l.Wavelengths[0]); var high = spectral.Lobes.Min(l => l.Wavelengths[^1]);
        if (low > high) throw new InvalidDataException("Speos R/T 光谱没有公共波长范围。");
        var waves = spectral.Lobes.SelectMany(l => l.Wavelengths).Append(low).Append(high).Where(w => w >= low && w <= high).Distinct().Order().ToArray();
        foreach (var p in points.GroupBy(p => (p.Side, p.IncidentThetaDegrees, p.IncidentPhiDegrees)).Select(g => g.First())) foreach (var wave in waves)
        {
            Services.ComputationCancellation.ThrowIfCancellationRequested(); var c0 = Probe(p); var c = new BsdfContext(c0.IncomingDirection, c0.SurfaceNormal, wave, c0.Side, c0.MaterialTangent);
            var e = Integrate(c); if (!double.IsFinite(e.Total) || e.Total > 1 + 32 * 2.2204460492503131e-16) throw new InvalidDataException("Speos 光谱/White Specular 的 R+T 超过 1。");
        }
    }
    private BsdfContext Stored(BsdfContext c) => new(c.IncomingDirection, c.SurfaceNormal, _storedWave, c.Side, c.MaterialTangent, c.IncidentRefractiveIndex, c.TransmittedRefractiveIndex);
    private double Scale(Channel c, double wavelength) => _spectral.Scale(c.Transmission, c.Peak && _white is { } w ? w : wavelength);
    public BsdfEnergy Integrate(BsdfContext c)
    {
        var cc = Stored(c); double r = 0, t = 0;
        foreach (var channel in _channels) { var e = channel.Model.Integrate(cc); var scale = Scale(channel, c.WavelengthNanometers); r += e.Reflection * scale; t += e.Transmission * scale; }
        return new(r, t);
    }
    public double Evaluate(BsdfContext c, Vector3D direction)
    { var cc = Stored(c); return _channels.Sum(channel => Scale(channel, c.WavelengthNanometers) * channel.Model.Evaluate(cc, direction)); }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var cc = Stored(c); var masses = _channels.Select(channel => Scale(channel, c.WavelengthNanometers) * channel.Model.Integrate(cc).Total).ToArray(); var total = masses.Sum(); if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = Math.Clamp(u1, 5.551115123125783e-17, Math.BitDecrement(1d)) * total; var index = Array.FindLastIndex(masses, m => m > 0);
        for (var i = 0; i < masses.Length; i++) { if (masses[i] > 0 && target < masses[i]) { index = i; break; } target -= masses[i]; }
        var s = _channels[index].Model.Sample(cc, Math.Clamp(target / masses[index], 0, Math.BitDecrement(1d)), u2);
        return new(s.Direction, model.Evaluate(c, s.Direction), model.Pdf(c, s.Direction), s.Lobe);
    }
}
