using System.Security.Cryptography;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public sealed record XyzObserverSample(double WavelengthNanometers, double X, double Y, double Z);
public sealed record XyzSpectralSample(double WavelengthNanometers, double Value);
public sealed record XyzColorimetricConditions(string Observer, string Illuminant, string ObserverSourceSha256, string IlluminantSourceSha256,
    string ProjectionTablesSha256, double MinimumWavelengthNanometers, double MaximumWavelengthNanometers, string Method)
{
    public const string ExactMethod = "PiecewiseLinearProductGauss2WhiteY/1";
    internal XyzColorimetricConditions Freeze()
    {
        static bool Sha(string s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Observer) || Observer.Length > 2048 || string.IsNullOrWhiteSpace(Illuminant) || Illuminant.Length > 2048
            || !Sha(ObserverSourceSha256) || !Sha(IlluminantSourceSha256) || !Sha(ProjectionTablesSha256) || Method != ExactMethod
            || !double.IsFinite(MinimumWavelengthNanometers) || !double.IsFinite(MaximumWavelengthNanometers)
            || MinimumWavelengthNanometers < 200 || MaximumWavelengthNanometers > 20000 || MinimumWavelengthNanometers >= MaximumWavelengthNanometers)
            throw new InvalidDataException("XYZ 观察者、照明、波段和积分来源声明无效。");
        return this;
    }
}

/// <summary>Exact integration of the declared piecewise-linear spectrum, observer and illuminant.
/// Two-point Gauss integrates their cubic product on every merged interval; no unknown spectrum
/// is reconstructed and no extrapolation beyond the declared measured band is performed.</summary>
public sealed class XyzSpectralProjector
{
    private readonly XyzObserverSample[] _observer;
    private readonly XyzSpectralSample[] _illuminant;
    private readonly double[] _observerAxis, _illuminantAxis, _breaks;
    private readonly double _whiteY;
    public XyzColorimetricConditions Conditions { get; }
    public XyzSpectralProjector(string observerName, string illuminantName, string observerSourceSha256, string illuminantSourceSha256,
        IReadOnlyList<XyzObserverSample> observer, IReadOnlyList<XyzSpectralSample> illuminant, double minimumNm, double maximumNm)
    {
        if (observer is null || observer.Count is < 2 or > 4096 || observer.Any(p => p is null || new[] { p.X, p.Y, p.Z }.Any(v => !double.IsFinite(v) || v < 0 || v > 1e12))
            || illuminant is null || illuminant.Count is < 2 or > 4096 || illuminant.Any(p => p is null || !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1e12)) throw new InvalidDataException("XYZ 响应和照明表必须为有界非负数据。");
        _observer = observer.ToArray(); _illuminant = illuminant.ToArray();
        _observerAxis = Axis(_observer.Select(p => p.WavelengthNanometers)); _illuminantAxis = Axis(_illuminant.Select(p => p.WavelengthNanometers));
        if (_observerAxis[0] > minimumNm || _observerAxis[^1] < maximumNm || _illuminantAxis[0] > minimumNm || _illuminantAxis[^1] < maximumNm) throw new InvalidDataException("XYZ 响应/照明未覆盖声明波段；不能外推。");
        using var bytes = new MemoryStream(); using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            writer.Write(XyzColorimetricConditions.ExactMethod); writer.Write(_observer.Length);
            foreach (var p in _observer) { writer.Write(p.WavelengthNanometers); writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
            writer.Write(_illuminant.Length); foreach (var p in _illuminant) { writer.Write(p.WavelengthNanometers); writer.Write(p.Value); }
        }
        Conditions = new XyzColorimetricConditions(observerName, illuminantName, observerSourceSha256, illuminantSourceSha256,
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), minimumNm, maximumNm, XyzColorimetricConditions.ExactMethod).Freeze();
        _breaks = _observerAxis.Concat(_illuminantAxis).Where(w => w > minimumNm && w < maximumNm).Append(minimumNm).Append(maximumNm).Distinct().Order().ToArray();
        _whiteY = Integrate(_breaks, (_, y, p) => y * p, 1, null);
        if (!double.IsFinite(_whiteY) || _whiteY <= 0) throw new InvalidDataException("声明波段的照明白色 Y 响应为零或无效。");
    }
    private static double[] Axis(IEnumerable<double> source)
    {
        var a = source.ToArray();
        if (a.Any(w => !double.IsFinite(w) || w is < 200 or > 20000) || a.Zip(a.Skip(1)).Any(p => p.First >= p.Second)) throw new InvalidDataException("XYZ 光谱轴须严格递增且在有效波长内。");
        return a;
    }
    public XyzCalibrationColor Project(IReadOnlyList<XyzSpectralSample> measuredSpectrum, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); using var scope = ComputationCancellation.Push(cancellationToken);
        if (measuredSpectrum is null || measuredSpectrum.Count is < 2 or > 4096 || measuredSpectrum.Any(p => p is null || !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 1e12)) throw new InvalidDataException("实测光谱必须为有限非负数据。");
        var values = measuredSpectrum.ToArray(); var axis = Axis(values.Select(p => p.WavelengthNanometers));
        if (axis[0] > Conditions.MinimumWavelengthNanometers || axis[^1] < Conditions.MaximumWavelengthNanometers) throw new InvalidDataException("实测光谱未覆盖声明 XYZ 波段；不能外推。");
        var knots = _breaks.Concat(axis.Where(w => w > _breaks[0] && w < _breaks[^1])).Distinct().Order().ToArray();
        double F(double w) { var b = MeasuredBsdfGrid.Bracket(axis, w); return values[b.Low].Value * (1 - b.T) + values[b.High].Value * b.T; }
        var xyz = Enumerable.Range(0, 3).Select(c => Integrate(knots, (w, response, power) => F(w) * response * power, c, cancellationToken) / _whiteY).ToArray();
        if (xyz.Any(v => !double.IsFinite(v) || v < 0)) throw new InvalidDataException("XYZ 光谱积分溢出。");
        return new(xyz[0], xyz[1], xyz[2]);
    }
    private double Integrate(double[] knots, Func<double, double, double, double> integrand, int channel, CancellationToken? cancellationToken)
    {
        double sum = 0, correction = 0;
        for (var i = 0; i < knots.Length - 1; i++)
        {
            cancellationToken?.ThrowIfCancellationRequested(); var midpoint = (knots[i] + knots[i + 1]) / 2; var half = (knots[i + 1] - knots[i]) / 2;
            foreach (var sign in new[] { -1, 1 })
            {
                var w = midpoint + sign * half / Math.Sqrt(3); var o = MeasuredBsdfGrid.Bracket(_observerAxis, w); var s = MeasuredBsdfGrid.Bracket(_illuminantAxis, w);
                double Value(XyzObserverSample p) => channel == 0 ? p.X : channel == 1 ? p.Y : p.Z;
                var response = Value(_observer[o.Low]) * (1 - o.T) + Value(_observer[o.High]) * o.T;
                var power = _illuminant[s.Low].Value * (1 - s.T) + _illuminant[s.High].Value * s.T;
                var term = half * integrand(w, response, power) - correction; var next = sum + term; correction = (next - sum) - term; sum = next;
            }
        }
        return sum;
    }
}
