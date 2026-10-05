using System.Numerics;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Coatings;

public enum ThinFilmPolarization { S, P, Unpolarized }

public sealed record CoherentFilm(IMaterial Material, double ThicknessNanometers, CoatingLayerParameters? Parameters = null)
{
    public CoatingLayerParameters Adjustment => Parameters ?? new();
}

/// <summary>Power entering the semi-infinite substrate is T; A is absorption in finite layers only.</summary>
public sealed record ThinFilmPower(double Reflectance, double Transmittance, double Absorptance,
    double LogTransmittance)
{
    // Display saturation is deliberately independent of the stored, untruncated T/log(T).
    public double OpticalDensity => -LogTransmittance / Math.Log(10);
}

/// <summary>
/// Reflection uses the orthonormal ray basis p = s cross propagation direction.
/// PowerTransmission has squared magnitude T. It is absent for an absorbing
/// substrate, whose inhomogeneous transmitted wave cannot use this real-ray basis.
/// Reflection and Power remain available for an absorbing substrate.
/// </summary>
public sealed record ThinFilmAmplitude(Complex Reflection, Complex? PowerTransmission,
    ThinFilmPower Power, double TransmissionPhaseRadians);

/// <summary>
/// Passive isotropic coherent planar films. n+ik, exp(-i omega t), nm, degrees.
/// Backward scattering recursion uses only decaying propagation factors and tracks log transmission.
/// Layer order: transparent incident medium -> finite films -> semi-infinite substrate.
/// No connection to the legacy empirical ray coating model.
/// </summary>
public sealed class CoherentThinFilmSolver
{
    public const string Version = "coherent-scattering/2";
    public const int MaximumLayers = 4096;
    private readonly IMaterial[] _media;
    private readonly double[] _thickness;
    private readonly CoatingLayerParameters[] _adjustments;

    public CoherentThinFilmSolver(IMaterial incident, IMaterial substrate, IEnumerable<CoherentFilm> films)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(substrate);
        var layers = films.ToArray();
        if (layers.Length > MaximumLayers) throw new ArgumentException("膜层数量超过计算上限。");
        if (layers.Any(x => !double.IsFinite(x.ThicknessNanometers) || x.ThicknessNanometers < 0))
            throw new ArgumentException("膜层厚度必须是非负有限数值（nm）。");
        foreach (var layer in layers) layer.Adjustment.Validate();
        _media = new[] { incident.Clone() }.Concat(layers.Select(x => x.Material.Clone()))
            .Append(substrate.Clone()).ToArray();
        _thickness = layers.Select(x => x.ThicknessNanometers * x.Adjustment.Multiplier).ToArray();
        if (_thickness.Any(x => !double.IsFinite(x))) throw new ArgumentException("调整后的膜层厚度溢出。");
        _adjustments = layers.Select(x => x.Adjustment).ToArray();
    }

    public ThinFilmPower Evaluate(double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        if (!Enum.IsDefined(polarization)) throw new ArgumentException("偏振设置无效。");
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        if (polarization != ThinFilmPolarization.Unpolarized) return Solve(n, q, wavelengthNanometers, polarization).Power;
        var s = Solve(n, q, wavelengthNanometers, ThinFilmPolarization.S).Power;
        var p = Solve(n, q, wavelengthNanometers, ThinFilmPolarization.P).Power;
        var logMax = Math.Max(s.LogTransmittance, p.LogTransmittance);
        var logAverage = double.IsNegativeInfinity(logMax) ? logMax
            : logMax + Math.Log((Math.Exp(s.LogTransmittance - logMax) + Math.Exp(p.LogTransmittance - logMax)) / 2);
        return new((s.Reflectance + p.Reflectance) / 2, (s.Transmittance + p.Transmittance) / 2,
            (s.Absorptance + p.Absorptance) / 2, logAverage);
    }

    public ThinFilmAmplitude EvaluateAmplitude(double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        if (polarization is not (ThinFilmPolarization.S or ThinFilmPolarization.P))
            throw new ArgumentException("复振幅必须指定 S 或 P；非偏振光没有单一相干振幅。");
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        return Solve(n, q, wavelengthNanometers, polarization);
    }

    private (Complex[] N, Complex[] Q) Prepare(double wavelengthNanometers, double angleDegrees)
    {
        if (!double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0)
            throw new ArgumentException("波长必须大于零（nm）。");
        if (!double.IsFinite(angleDegrees) || angleDegrees < 0 || angleDegrees >= 90)
            throw new ArgumentException("入射角必须在 0°（含）至 90°（不含）之间。");
        ComputationCancellation.ThrowIfCancellationRequested();
        var n = _media.Select(m => Index(m, wavelengthNanometers)).ToArray();
        for (var layer = 0; layer < _adjustments.Length; layer++)
        {
            var adjustment = _adjustments[layer];
            n[layer + 1] += new Complex(adjustment.IndexOffset, adjustment.ExtinctionOffset);
            if (!double.IsFinite(n[layer + 1].Real) || !double.IsFinite(n[layer + 1].Imaginary)
                || n[layer + 1].Real <= 0 || n[layer + 1].Imaginary < 0)
                throw new ArgumentException($"第 {layer + 1} 膜层在 {wavelengthNanometers:G8} nm 的调整后 n/k 无效；要求 n > 0、k ≥ 0。");
        }
        if (n[0].Imaginary != 0) throw new NotSupportedException("首版要求入射介质无吸收（k = 0）。");
        var transverse = n[0].Real * Math.Sin(angleDegrees * Math.PI / 180);
        var q = n.Select(z => ForwardRoot(z * z - transverse * transverse)).ToArray();
        if (q.Any(z => z.Magnitude < 1e-14))
            throw new NotSupportedException("当前采样点恰在临界角奇点，请调整角度后重算。");
        return (n, q);
    }

    public static double QuarterWaveThickness(IMaterial material, IMaterial incident, double wavelengthNanometers,
        double angleDegrees)
    {
        var n = Index(material, wavelengthNanometers);
        var n0 = Index(incident, wavelengthNanometers);
        if (!double.IsFinite(angleDegrees) || angleDegrees < 0 || angleDegrees >= 90 || n0.Imaginary != 0)
            throw new ArgumentException("初始结构的入射条件无效。");
        var transverse = n0.Real * Math.Sin(angleDegrees * Math.PI / 180);
        var q = ForwardRoot(n * n - transverse * transverse);
        if (q.Real <= 1e-12) throw new ArgumentException("所选材料在该入射角没有可用的传播相位厚度。");
        return wavelengthNanometers / (4 * q.Real);
    }

    private ThinFilmAmplitude Solve(Complex[] n, Complex[] q, double wavelength, ThinFilmPolarization polarization)
    {
        var y = q.Select((z, i) => polarization == ThinFilmPolarization.S ? z : n[i] * n[i] / z).ToArray();
        var reflection = Complex.Zero;
        var logAmplitude = 0.0;
        var transmissionPhase = 0.0;
        for (var j = _media.Length - 2; j >= 0; j--)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var propagation = Complex.One;
            var attenuation = 0.0;
            var propagationPhase = 0.0;
            if (j < _thickness.Length)
            {
                var phase = 2 * Math.PI * q[j + 1] * _thickness[j] / wavelength;
                attenuation = -phase.Imaginary;
                propagationPhase = phase.Real;
                propagation = Complex.FromPolarCoordinates(Math.Exp(attenuation), phase.Real);
            }
            var interfaceReflection = (y[j] - y[j + 1]) / (y[j] + y[j + 1]);
            var interfaceTransmission = 2 * y[j] / (y[j] + y[j + 1]);
            var reflectedWave = reflection * propagation * propagation;
            var denominator = 1 + interfaceReflection * reflectedWave;
            logAmplitude += Math.Log(interfaceTransmission.Magnitude) + attenuation - Math.Log(denominator.Magnitude);
            transmissionPhase = Math.IEEERemainder(transmissionPhase + interfaceTransmission.Phase
                + propagationPhase - denominator.Phase, 2 * Math.PI);
            reflection = (interfaceReflection + reflectedWave) / denominator;
        }
        var r = reflection.Magnitude * reflection.Magnitude;
        var flux = y[^1].Real / y[0].Real;
        var logT = flux <= 0 ? double.NegativeInfinity : 2 * logAmplitude + Math.Log(flux);
        var t = Math.Exp(logT);
        var a = 1 - r - t;
        if (!double.IsFinite(r) || !double.IsFinite(t) || !double.IsFinite(a) || a < -2e-9 || r > 1 + 2e-9)
            throw new ArithmeticException("薄膜计算未满足被动介质能量条件，结果不能用于设计验收。");
        var power = new ThinFilmPower(r, t, a, logT);
        // The admittance recursion uses tangential electric-field components.
        // Reflection changes the local p direction, hence its sign conversion.
        // Transparent exterior media need no additional transmission phase change.
        return new(polarization == ThinFilmPolarization.P ? -reflection : reflection,
            n[^1].Imaginary == 0 ? Complex.FromPolarCoordinates(Math.Exp(logT / 2), transmissionPhase) : null,
            power, transmissionPhase);
    }

    private static Complex ForwardRoot(Complex z)
    {
        var q = Complex.Sqrt(z);
        return q.Imaginary < 0 || (q.Imaginary == 0 && q.Real < 0) ? -q : q;
    }

    private static Complex Index(IMaterial material, double wavelength)
    {
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentException("波长无效。");
        if (material is CatalogGlassMaterial catalog &&
            (wavelength < catalog.MinimumWavelengthNanometers || wavelength > catalog.MaximumWavelengthNanometers))
            throw new ArgumentException($"材料 {material.Name} 的有效波段不包含 {wavelength:G8} nm。");
        var n = material.RefractiveIndex(wavelength);
        var k = material.ExtinctionCoefficient(wavelength);
        if (!double.IsFinite(n) || !double.IsFinite(k) || n <= 0 || k < 0)
            throw new ArgumentException($"材料 {material.Name} 的 n/k 无效；要求 n > 0、k ≥ 0。");
        return new Complex(n, k);
    }
}
