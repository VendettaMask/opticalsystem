using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Scattering;

/// <summary>Explicit calibrated linear mapping from normalized XYZ channel BSDFs to radiometric BSDF.
/// Coefficients are supplied by measurement/calibration, never inferred from tristimulus colour.</summary>
public sealed record XyzRadiometricBand(double WavelengthNanometers, double X, double Y, double Z);
public sealed record XyzRadiometricCalibration(string Name, string Observer, string Illuminant, string Units,
    IReadOnlyList<XyzRadiometricBand> Bands, string SourceSha256 = "")
{
    public const string UnitsContract = "radiometric-bsdf-per-normalized-XYZ";
    public static XyzRadiometricCalibration Read(string path)
    {
        using var file = File.OpenRead(path); if (file.Length is < 1 or > 65536) throw new InvalidDataException("XYZ 校准文件须小于 64 KiB。");
        var raw = new byte[(int)file.Length]; file.ReadExactly(raw);
        var value = JsonSerializer.Deserialize<XyzRadiometricCalibration>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("XYZ 校准文件为空。");
        return (value with { SourceSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() }).Freeze();
    }
    internal XyzRadiometricCalibration Freeze()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 2048 || string.IsNullOrWhiteSpace(Observer) || Observer.Length > 2048 || string.IsNullOrWhiteSpace(Illuminant) || Illuminant.Length > 2048
            || Units != UnitsContract || SourceSha256.Length != 64 || SourceSha256.Any(c => !Uri.IsHexDigit(c)) || Bands is null || Bands.Count is < 1 or > 32 || Bands.Any(b => b is null
                || !double.IsFinite(b.WavelengthNanometers) || b.WavelengthNanometers is < 200 or > 20000 || !double.IsFinite(b.X) || b.X < 0 || !double.IsFinite(b.Y) || b.Y < 0 || !double.IsFinite(b.Z) || b.Z < 0))
            throw new InvalidDataException("XYZ 需要有来源、观察者、照明条件、单位声明和非负光谱映射的校准文件。");
        var bands = Bands.OrderBy(b => b.WavelengthNanometers).ToArray(); if (bands.Select(b => b.WavelengthNanometers).Distinct().Count() != bands.Length) throw new InvalidDataException("XYZ 校准波长重复。");
        return this with { Bands = Array.AsReadOnly(bands) };
    }
}

public sealed record ZemaxXyzDefinition(XyzRadiometricCalibration Calibration, IReadOnlyList<MeasuredBsdfPoint> XPoints,
    IReadOnlyList<MeasuredBsdfPoint> ZPoints, IReadOnlyList<ZemaxBsdfTis> XIntegrals, IReadOnlyList<ZemaxBsdfTis> ZIntegrals)
{
    internal ZemaxXyzDefinition Freeze()
    {
        if (Calibration is null || XPoints is null || ZPoints is null || XIntegrals is null || ZIntegrals is null || XPoints.Count > MeasuredBsdfData.MaximumPoints || ZPoints.Count > MeasuredBsdfData.MaximumPoints
            || XPoints.Any(p => p is null) || ZPoints.Any(p => p is null) || XIntegrals.Count > 6400 || ZIntegrals.Count > 6400 || XIntegrals.Any(p => p is null) || ZIntegrals.Any(p => p is null)) throw new InvalidDataException("XYZ 原始通道载荷无效。");
        return this with { Calibration = Calibration.Freeze(), XPoints = Array.AsReadOnly(XPoints.ToArray()), ZPoints = Array.AsReadOnly(ZPoints.ToArray()), XIntegrals = Array.AsReadOnly(XIntegrals.ToArray()), ZIntegrals = Array.AsReadOnly(ZIntegrals.ToArray()) };
    }
}

internal sealed class ZemaxXyzDistribution : IMeasuredBsdfDistribution
{
    private readonly ZemaxBsdfDistribution[] _channels;
    private readonly ZemaxXyzDefinition _definition;
    private readonly double _channelWave;
    private readonly double[] _waves;
    public bool HasTransmission => _channels.Any(c => c.HasTransmission);
    public bool HasBothSides => _channels.All(c => c.HasBothSides);
    internal ZemaxXyzDistribution(IReadOnlyList<MeasuredBsdfPoint> y, BsdfCoordinates coordinates, ZemaxBsdfDefinition definition, ZemaxXyzDefinition xyz)
    {
        _definition = xyz; _channelWave = y.First().WavelengthNanometers; _waves = xyz.Calibration.Bands.Select(b => b.WavelengthNanometers).ToArray();
        _channels = [new(xyz.XPoints, coordinates, definition with { Integrals = xyz.XIntegrals }), new(y, coordinates, definition), new(xyz.ZPoints, coordinates, definition with { Integrals = xyz.ZIntegrals })];
        static object Key(MeasuredBsdfPoint p) => (p.Side, p.WavelengthNanometers, p.IncidentThetaDegrees, p.IncidentPhiDegrees, p.OutgoingThetaDegrees, p.OutgoingPhiDegrees);
        if (!y.Select(Key).ToHashSet().SetEquals(xyz.XPoints.Select(Key)) || !y.Select(Key).ToHashSet().SetEquals(xyz.ZPoints.Select(Key))) throw new InvalidDataException("XYZ 通道角度轴不一致。");
        var xTis = xyz.XIntegrals.ToDictionary(t => (t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees)); var zTis = xyz.ZIntegrals.ToDictionary(t => (t.Side, t.SpecularThetaDegrees, t.SampleRotationDegrees));
        foreach (var tis in definition.Integrals) foreach (var b in xyz.Calibration.Bands)
        {
            var key = (tis.Side, tis.SpecularThetaDegrees, tis.SampleRotationDegrees);
            var total = b.X * xTis[key].Fraction + b.Y * tis.Fraction + b.Z * zTis[key].Fraction;
            if (!double.IsFinite(total) || total > 1 + 32 * 2.2204460492503131e-16) throw new InvalidDataException("XYZ 校准产生 R+T>1；不能归一化掩盖错误校准。");
        }
    }
    private BsdfContext ChannelContext(BsdfContext c) => new(c.IncomingDirection, c.SurfaceNormal, _channelWave, c.Side, c.MaterialTangent, c.IncidentRefractiveIndex, c.TransmittedRefractiveIndex);
    private double[] Weights(BsdfContext c)
    {
        var b = MeasuredBsdfGrid.Bracket(_waves, c.WavelengthNanometers); var lo = _definition.Calibration.Bands[b.Low]; var hi = _definition.Calibration.Bands[b.High];
        return [lo.X * (1 - b.T) + hi.X * b.T, lo.Y * (1 - b.T) + hi.Y * b.T, lo.Z * (1 - b.T) + hi.Z * b.T];
    }
    public BsdfEnergy Integrate(BsdfContext c)
    { var weights = Weights(c); var cc = ChannelContext(c); var e = _channels.Select(x => x.Integrate(cc)).ToArray(); return new(Enumerable.Range(0, 3).Sum(i => weights[i] * e[i].Reflection), Enumerable.Range(0, 3).Sum(i => weights[i] * e[i].Transmission)); }
    public double Evaluate(BsdfContext c, Vector3D d)
    { var weights = Weights(c); var cc = ChannelContext(c); return Enumerable.Range(0, 3).Sum(i => weights[i] * _channels[i].Evaluate(cc, d)); }
    public BsdfSample Sample(MeasuredBsdf model, BsdfContext c, double u1, double u2)
    {
        if (!double.IsFinite(u1) || !double.IsFinite(u2) || u1 is < 0 or >= 1 || u2 is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(u1));
        var weights = Weights(c); var cc = ChannelContext(c); var masses = Enumerable.Range(0, 3).Select(i => weights[i] * _channels[i].Integrate(cc).Total).ToArray(); var total = masses.Sum();
        if (total == 0) return new LambertianReflectionBsdf(0).Sample(c, u1, u2);
        var target = Math.Clamp(u1, 5.551115123125783e-17, Math.BitDecrement(1d)) * total; int index = Array.FindLastIndex(masses, m => m > 0);
        for (var i = 0; i < 3; i++) { if (masses[i] > 0 && target < masses[i]) { index = i; break; } target -= masses[i]; }
        var s = _channels[index].Sample(model, cc, Math.Clamp(target / masses[index], 0, Math.BitDecrement(1d)), u2);
        return new(s.Direction, model.Evaluate(c, s.Direction), model.Pdf(c, s.Direction), s.Lobe);
    }
}
