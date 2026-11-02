using System.Numerics;

namespace OpticalSystemDesign.Core.Coatings;

public sealed partial class CoherentThinFilmSolver
{
    public const string DuplexSubstrateVersion = "duplex-incoherent-substrate-scattering/1";

    /// <summary>Both solvers are ordered from their exterior toward the same substrate.
    /// The plate is illuminated from this solver's exterior; the rear films are traversed in reverse.
    /// The same conserved transverse wavevector is used at every interface, including absorbing substrates.</summary>
    public ThinFilmPower EvaluateIncoherentSubstrate(CoherentThinFilmSolver rear, double substrateThicknessNanometers,
        double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        ArgumentNullException.ThrowIfNull(rear);
        if (!double.IsFinite(substrateThicknessNanometers) || substrateThicknessNanometers <= 0 || !Enum.IsDefined(polarization))
            throw new ArgumentException("非相干基板厚度必须为正有限数值，偏振设置必须有效。");
        if (rear._thickness.Length == 0)
        {
            if (Index(_media[^1], wavelengthNanometers) != Index(rear._media[^1], wavelengthNanometers))
                throw new ArgumentException("正背面必须使用相同基板。");
            return EvaluateIncoherentSubstrate(rear._media[0], substrateThicknessNanometers, wavelengthNanometers, angleDegrees, polarization);
        }
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        var backN = rear._media.Select(m => Index(m, wavelengthNanometers)).ToArray();
        if (backN[^1] != n[^1]) throw new ArgumentException("正背面必须使用相同基板。");
        for (var i = 0; i < rear._adjustments.Length; i++)
        {
            backN[i + 1] += new Complex(rear._adjustments[i].IndexOffset, rear._adjustments[i].ExtinctionOffset);
            if (!double.IsFinite(backN[i + 1].Real) || !double.IsFinite(backN[i + 1].Imaginary) || backN[i + 1].Real <= 0 || backN[i + 1].Imaginary < 0)
                throw new ArgumentException("背面膜层调整后的n/k无效。");
        }
        var transverse = n[0].Real * Math.Sin(angleDegrees * Math.PI / 180);
        var backQ = backN.Select(z => ForwardRoot(z * z - transverse * transverse)).ToArray();
        if (backQ.Any(z => z.Magnitude < 1e-14)) throw new NotSupportedException("当前采样点恰在临界角奇点。");
        var nr = n.Reverse().ToArray(); var qr = q.Reverse().ToArray();
        var bn = backN.Reverse().ToArray(); var bq = backQ.Reverse().ToArray();
        var logPropagation = -4 * Math.PI * q[^1].Imaginary * substrateThicknessNanometers / wavelengthNanometers;
        var lossless = n.All(z => z.Imaginary == 0) && backN.All(z => z.Imaginary == 0);
        ThinFilmPower Scalar(ThinFilmPolarization pol)
        {
            Complex Y(Complex index, Complex normal) => pol == ThinFilmPolarization.S ? normal : index * index / normal;
            var yi = Y(n[0], q[0]); var ys = Y(n[^1], q[^1]); var ye = Y(bn[^1], bq[^1]);
            if (ys.Real <= 0) throw new NotSupportedException("倏逝基板不能作为非相干传播层。");
            var forward = ScatterFields(n, q, _thickness, wavelengthNanometers, pol);
            var reverse = ScatterFields(nr, qr, _thickness.Reverse().ToArray(), wavelengthNanometers, pol);
            var back = ScatterFields(bn, bq, rear._thickness.Reverse().ToArray(), wavelengthNanometers, pol);
            var rf = forward.Reflection.Magnitude * forward.Reflection.Magnitude;
            var rr = reverse.Reflection.Magnitude * reverse.Reflection.Magnitude;
            var rb = back.Reflection.Magnitude * back.Reflection.Magnitude;
            var logTf = 2 * forward.LogAmplitude + Math.Log(ys.Real / yi.Real);
            var logTr = 2 * reverse.LogAmplitude + Math.Log(yi.Real / ys.Real);
            var logTb = ye.Real <= 0 ? double.NegativeInfinity : 2 * back.LogAmplitude + Math.Log(ye.Real / ys.Real);
            double logDenominator;
            if (lossless)
            {
                // Energy conservation gives 1-Rr*Rb = Tr+Tb-Tr*Tb. Retain the tiny transmissions
                // in log space when both reflected powers round to one; never floor T or change n/k.
                var largestLog = Math.Max(logTr, logTb);
                var scaled = Math.Exp(logTr - largestLog) + Math.Exp(logTb - largestLog) - Math.Exp(logTr + logTb - largestLog);
                if (!double.IsFinite(largestLog) || !(scaled > 0)) throw new ArithmeticException("无损双面往返求和无法稳定求解。");
                logDenominator = largestLog + Math.Log(scaled);
            }
            else
            {
                var denominator = 1 - rr * rb * Math.Exp(2 * logPropagation);
                if (!(denominator > 0)) throw new ArithmeticException("非相干基板内部往返求和无法稳定求解。");
                logDenominator = Math.Log(denominator);
            }
            var r = rf + Math.Exp(logTf + logTr + 2 * logPropagation - logDenominator) * rb;
            var logT = logTf + logPropagation + logTb - logDenominator;
            var t = Math.Exp(logT); var a = 1 - r - t;
            if (!double.IsFinite(r) || !double.IsFinite(t) || !double.IsFinite(a) || a < -2e-9 || r > 1 + 2e-9)
                throw new ArithmeticException("双面镀膜未满足被动介质能量条件，结果不能用于验收。");
            return new(r, t, a, logT);
        }
        if (polarization != ThinFilmPolarization.Unpolarized) return Scalar(polarization);
        var s = Scalar(ThinFilmPolarization.S); var p = Scalar(ThinFilmPolarization.P);
        var largest = Math.Max(s.LogTransmittance, p.LogTransmittance);
        var logAverage = double.IsNegativeInfinity(largest) ? largest
            : largest + Math.Log((Math.Exp(s.LogTransmittance - largest) + Math.Exp(p.LogTransmittance - largest)) / 2);
        return new((s.Reflectance + p.Reflectance) / 2, (s.Transmittance + p.Transmittance) / 2,
            (s.Absorptance + p.Absorptance) / 2, logAverage);
    }
}
