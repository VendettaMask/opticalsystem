using System.Numerics;

namespace OpticalSystemDesign.Core.Coatings;

public sealed partial class CoherentThinFilmSolver
{
    public const string DuplexSensitivityVersion = "duplex-incoherent-thickness-adjoint/1";

    /// <summary>Physical-thickness derivatives ordered front exterior-to-substrate, then rear exterior-to-substrate.
    /// The absorbing substrate is propagated incoherently; the same scattering adjoint differentiates both directions.</summary>
    public ThinFilmThicknessSensitivity EvaluateIncoherentSubstrateThicknessSensitivity(CoherentThinFilmSolver rear,
        double substrateThicknessNanometers, double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        // The established power path remains the authority, including its applicability checks and bare-back behavior.
        var power = EvaluateIncoherentSubstrate(rear, substrateThicknessNanometers, wavelengthNanometers, angleDegrees, polarization);
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        var backN = rear._media.Select(m => Index(m, wavelengthNanometers)).ToArray();
        for (var i = 0; i < rear._adjustments.Length; i++) backN[i + 1] += new Complex(rear._adjustments[i].IndexOffset, rear._adjustments[i].ExtinctionOffset);
        var transverse = n[0].Real * Math.Sin(angleDegrees * Math.PI / 180);
        var backQ = backN.Select(z => ForwardRoot(z * z - transverse * transverse)).ToArray();
        var lossless = n.All(z => z.Imaginary == 0) && backN.All(z => z.Imaginary == 0);
        var logP = -4 * Math.PI * q[^1].Imaginary * substrateThicknessNanometers / wavelengthNanometers;
        var frontMultipliers = _adjustments.Select(a => a.Multiplier).ToArray();
        var backMultipliers = rear._adjustments.Select(a => a.Multiplier).Reverse().ToArray();
        ThinFilmThicknessSensitivity Scalar(ThinFilmPolarization pol)
        {
            ThinFilmThicknessSensitivity Direction(Complex[] indices, Complex[] normal, double[] thickness, double[] multipliers)
            {
                var fields = ScatterFields(indices, normal, thickness, wavelengthNanometers, pol);
                Complex Y(int i) => pol == ThinFilmPolarization.S ? normal[i] : indices[i] * indices[i] / normal[i];
                var r = fields.Reflection.Magnitude * fields.Reflection.Magnitude;
                var logT = Y(indices.Length - 1).Real <= 0 ? double.NegativeInfinity
                    : 2 * fields.LogAmplitude + Math.Log(Y(indices.Length - 1).Real / Y(0).Real);
                var t = Math.Exp(logT);
                return DifferentiateScatter(indices, normal, thickness, multipliers, wavelengthNanometers, pol, new(r, t, 1 - r - t, logT));
            }
            var f = Direction(n, q, _thickness, frontMultipliers);
            var rev = Direction(n.Reverse().ToArray(), q.Reverse().ToArray(), _thickness.Reverse().ToArray(), frontMultipliers.Reverse().ToArray());
            var b = Direction(backN.Reverse().ToArray(), backQ.Reverse().ToArray(), rear._thickness.Reverse().ToArray(), backMultipliers);
            var rr = rev.Power.Reflectance; var rb = b.Power.Reflectance;
            var logTr = rev.Power.LogTransmittance; var logTb = b.Power.LogTransmittance;
            var logD = lossless ? LogSumDifference(logTr, logTb) : Math.Log(1 - rr * rb * Math.Exp(2 * logP));
            var returnFactor = Math.Exp(f.Power.LogTransmittance + logTr + 2 * logP - logD);
            var logTransmission = f.Power.LogTransmittance + logP + logTb - logD;
            var t = Math.Exp(logTransmission);
            var derivatives = new ThinFilmThicknessDerivative[_thickness.Length + rear._thickness.Length];
            for (var j = 0; j < derivatives.Length; j++)
            {
                var front = j < _thickness.Length;
                var df = front ? f.Layers[j] : new ThinFilmThicknessDerivative(0, 0, 0, 0);
                var dr = front ? rev.Layers[_thickness.Length - 1 - j] : new ThinFilmThicknessDerivative(0, 0, 0, 0);
                var db = front ? new ThinFilmThicknessDerivative(0, 0, 0, 0) : b.Layers[derivatives.Length - 1 - j];
                // Lossless D=Tr+Tb-Tr*Tb retains derivatives when R rounds to exactly one.
                var dlogD = lossless
                    ? Math.Exp(logTr - logD) * (1 - b.Power.Transmittance) * dr.LogTransmittance
                        + Math.Exp(logTb - logD) * (1 - rev.Power.Transmittance) * db.LogTransmittance
                    : -Math.Exp(2 * logP - logD) * (rb * dr.Reflectance + rr * db.Reflectance);
                var dR = df.Reflectance + returnFactor * (db.Reflectance + rb * (df.LogTransmittance + dr.LogTransmittance - dlogD));
                var dlogT = double.IsNegativeInfinity(logTransmission) ? 0 : df.LogTransmittance + db.LogTransmittance - dlogD;
                var dT = t * dlogT;
                if (!double.IsFinite(dR) || !double.IsFinite(dT) || !double.IsFinite(dlogT)) throw new ArithmeticException("双面膜厚导数非有限。");
                derivatives[j] = new(dR, dT, -dR - dT, dlogT);
            }
            return new(new(f.Power.Reflectance + returnFactor * rb, t, 1 - f.Power.Reflectance - returnFactor * rb - t, logTransmission),
                derivatives, !double.IsNegativeInfinity(logTransmission));
        }
        if (polarization != ThinFilmPolarization.Unpolarized) return Scalar(polarization) with { Power = power };
        var s = Scalar(ThinFilmPolarization.S); var p = Scalar(ThinFilmPolarization.P);
        var maximum = Math.Max(s.Power.LogTransmittance, p.Power.LogTransmittance);
        var hasLog = double.IsFinite(maximum);
        var sw = hasLog ? Math.Exp(s.Power.LogTransmittance - maximum) : 0;
        var pw = hasLog ? Math.Exp(p.Power.LogTransmittance - maximum) : 0;
        return new(power, s.Layers.Zip(p.Layers, (a, b) => new ThinFilmThicknessDerivative((a.Reflectance + b.Reflectance) / 2,
            (a.Transmittance + b.Transmittance) / 2, (a.Absorptance + b.Absorptance) / 2,
            hasLog ? (sw * a.LogTransmittance + pw * b.LogTransmittance) / (sw + pw) : 0)).ToArray(), hasLog);
    }
    private static double LogSumDifference(double a, double b)
    {
        var maximum = Math.Max(a, b);
        return maximum + Math.Log(Math.Exp(a - maximum) + Math.Exp(b - maximum) - Math.Exp(a + b - maximum));
    }
}
