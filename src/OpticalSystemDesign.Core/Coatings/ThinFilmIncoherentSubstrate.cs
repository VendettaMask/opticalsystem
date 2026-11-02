using System.Numerics;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Coatings;

public sealed partial class CoherentThinFilmSolver
{
    public const string IncoherentSubstrateVersion = "incoherent-substrate-scattering/1";

    /// <summary>Coherent front films, a finite incoherent substrate, and a bare rear interface.
    /// T enters the exit medium; A includes finite film and substrate absorption. Thickness is in nm.</summary>
    public ThinFilmPower EvaluateIncoherentSubstrate(IMaterial exit, double substrateThicknessNanometers,
        double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        ArgumentNullException.ThrowIfNull(exit);
        if (!double.IsFinite(substrateThicknessNanometers) || substrateThicknessNanometers <= 0 || !Enum.IsDefined(polarization))
            throw new ArgumentException("非相干基板厚度必须为正有限数值，偏振设置必须有效。");
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        var ne = Index(exit, wavelengthNanometers);
        var transverse = n[0].Real * Math.Sin(angleDegrees * Math.PI / 180);
        var qe = ForwardRoot(ne * ne - transverse * transverse);
        if (qe.Magnitude < 1e-14) throw new NotSupportedException("出射介质恰在临界角奇点，请调整角度后重算。");
        var nr = n.Reverse().ToArray(); var qr = q.Reverse().ToArray(); var reverseThickness = _thickness.Reverse().ToArray();
        var logPropagation = -4 * Math.PI * q[^1].Imaginary * substrateThicknessNanometers / wavelengthNanometers;
        ThinFilmPower Scalar(ThinFilmPolarization pol)
        {
            Complex Admittance(Complex index, Complex normal) => pol == ThinFilmPolarization.S ? normal : index * index / normal;
            var ys = Admittance(n[^1], q[^1]); var yi = Admittance(n[0], q[0]); var ye = Admittance(ne, qe);
            if (ys.Real <= 0) throw new NotSupportedException("倏逝基板不能作为非相干传播层。");
            var forward = ScatterFields(n, q, _thickness, wavelengthNanometers, pol);
            var reverse = ScatterFields(nr, qr, reverseThickness, wavelengthNanometers, pol);
            var rbAmplitude = (ys - ye) / (ys + ye); var tbAmplitude = 2 * ys / (ys + ye);
            var rf = forward.Reflection.Magnitude * forward.Reflection.Magnitude;
            var rr = reverse.Reflection.Magnitude * reverse.Reflection.Magnitude;
            var rb = rbAmplitude.Magnitude * rbAmplitude.Magnitude;
            var logTf = 2 * forward.LogAmplitude + Math.Log(ys.Real / yi.Real);
            var logTr = 2 * reverse.LogAmplitude + Math.Log(yi.Real / ys.Real);
            var denominator = 1 - rr * rb * Math.Exp(2 * logPropagation);
            if (!(denominator > 0)) throw new ArithmeticException("非相干基板的内部往返求和无法稳定求解。");
            var r = rf + Math.Exp(logTf + logTr + 2 * logPropagation) * rb / denominator;
            var logTb = ye.Real <= 0 ? double.NegativeInfinity : 2 * Math.Log(tbAmplitude.Magnitude) + Math.Log(ye.Real / ys.Real);
            var logT = logTf + logPropagation + logTb - Math.Log(denominator);
            var t = Math.Exp(logT); var a = 1 - r - t;
            if (!double.IsFinite(r) || !double.IsFinite(t) || !double.IsFinite(a) || a < -2e-9 || r > 1 + 2e-9)
                throw new ArithmeticException("整块基板计算未满足被动介质能量条件，结果不能使用。");
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
