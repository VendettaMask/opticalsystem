using System.Numerics;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Coatings;

public sealed record ThinFilmThicknessDerivative(double Reflectance, double Transmittance, double Absorptance, double LogTransmittance);
public sealed record ThinFilmThicknessSensitivity(ThinFilmPower Power, ThinFilmThicknessDerivative[] Layers, bool HasLogTransmissionDerivative);

public sealed partial class CoherentThinFilmSolver
{
    /// <summary>Positive thickness scale for optimization, including evanescent films; not a quarter-wave seed.</summary>
    public static double CharacteristicThickness(IMaterial material, IMaterial incident, double wavelengthNanometers, double angleDegrees)
    {
        var n = Index(material, wavelengthNanometers); var n0 = Index(incident, wavelengthNanometers);
        if (!double.IsFinite(angleDegrees) || angleDegrees < 0 || angleDegrees >= 90 || n0.Imaginary != 0)
            throw new ArgumentException("膜厚尺度的入射条件无效。");
        var transverse = n0.Real * Math.Sin(angleDegrees * Math.PI / 180);
        var q = ForwardRoot(n * n - transverse * transverse);
        if (q.Magnitude < 1e-14) throw new NotSupportedException("当前采样点恰在临界角奇点，请调整角度后重算。");
        return wavelengthNanometers / (4 * q.Magnitude);
    }

    public const string SensitivityVersion = "stable-film-thickness-adjoint/1";

    /// <summary>Derivatives per nm of the input physical film thickness, including its adjustment multiplier.</summary>
    public ThinFilmThicknessSensitivity EvaluateThicknessSensitivity(double wavelengthNanometers, double angleDegrees, ThinFilmPolarization polarization)
    {
        if (!Enum.IsDefined(polarization)) throw new ArgumentException("偏振设置无效。");
        var (n, q) = Prepare(wavelengthNanometers, angleDegrees);
        if (polarization != ThinFilmPolarization.Unpolarized) return Sensitivity(n, q, wavelengthNanometers, polarization);
        var s = Sensitivity(n, q, wavelengthNanometers, ThinFilmPolarization.S);
        var p = Sensitivity(n, q, wavelengthNanometers, ThinFilmPolarization.P);
        var power = Evaluate(wavelengthNanometers, angleDegrees, polarization);
        var hasLog = s.HasLogTransmissionDerivative || p.HasLogTransmissionDerivative;
        var maximum = Math.Max(s.Power.LogTransmittance, p.Power.LogTransmittance);
        var sw = hasLog ? Math.Exp(s.Power.LogTransmittance - maximum) : 0;
        var pw = hasLog ? Math.Exp(p.Power.LogTransmittance - maximum) : 0;
        return new(power, s.Layers.Zip(p.Layers, (a, b) => new ThinFilmThicknessDerivative(
            (a.Reflectance + b.Reflectance) / 2, (a.Transmittance + b.Transmittance) / 2,
            (a.Absorptance + b.Absorptance) / 2, hasLog ? (sw * a.LogTransmittance + pw * b.LogTransmittance) / (sw + pw) : 0)).ToArray(), hasLog);
    }

    private ThinFilmThicknessSensitivity Sensitivity(Complex[] n, Complex[] q, double wavelength, ThinFilmPolarization pol)
    {
        // Reuse the authoritative forward response. No transfer matrix or growing exponential is introduced.
        var response = Solve(n, q, wavelength, pol);
        var y = q.Select((z, i) => pol == ThinFilmPolarization.S ? z : n[i] * n[i] / z).ToArray();
        var count = _thickness.Length;
        var reflected = Complex.Zero;
        var drNext = new Complex[count]; var drLocal = new Complex[count];
        var dlNext = new Complex[count]; var dlLocal = new Complex[count];
        for (var j = count; j >= 0; j--)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var beta = j < count ? 2 * Math.PI * q[j + 1] / wavelength : Complex.Zero;
            var phase = j < count ? beta * _thickness[j] : Complex.Zero;
            var propagationSquared = Complex.FromPolarCoordinates(Math.Exp(-2 * phase.Imaginary), 2 * phase.Real);
            var a = (y[j] - y[j + 1]) / (y[j] + y[j + 1]);
            var wave = reflected * propagationSquared;
            var denominator = 1 + a * wave;
            if (j < count)
            {
                var waveDerivative = 2 * Complex.ImaginaryOne * beta * wave * _adjustments[j].Multiplier;
                var factor = (1 - a * a) / (denominator * denominator);
                drLocal[j] = factor * waveDerivative;
                drNext[j] = factor * propagationSquared;
                dlLocal[j] = Complex.ImaginaryOne * beta * _adjustments[j].Multiplier - a * waveDerivative / denominator;
                dlNext[j] = -a * propagationSquared / denominator;
            }
            reflected = (a + wave) / denominator;
        }
        var derivatives = new ThinFilmThicknessDerivative[count];
        var adjointReflection = Complex.One; var adjointLogTransmission = Complex.Zero;
        var hasLog = !double.IsNegativeInfinity(response.Power.LogTransmittance);
        for (var j = 0; j < count; j++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var dr = 2 * (Complex.Conjugate(reflected) * adjointReflection * drLocal[j]).Real;
            var dlog = hasLog ? 2 * (dlLocal[j] + adjointLogTransmission * drLocal[j]).Real : 0;
            var dt = response.Power.Transmittance * dlog;
            if (!double.IsFinite(dr) || !double.IsFinite(dt) || !double.IsFinite(dlog))
                throw new ArithmeticException("薄膜膜厚导数非有限，不能用于优化。");
            derivatives[j] = new(dr, dt, -dr - dt, dlog);
            adjointReflection *= drNext[j];
            adjointLogTransmission = dlNext[j] + adjointLogTransmission * drNext[j];
        }
        return new(response.Power, derivatives, hasLog);
    }
}
