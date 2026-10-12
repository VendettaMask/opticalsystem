using System.Numerics;

namespace OptilandWorkbench.Core.Coatings;

/// <summary>
/// Jones amplitudes normalized to power flux for a lossless, isotropic interface.
/// The s basis is common to both rays; p = s cross propagation direction.
/// Squared magnitudes are power R/T, not electric-field transmittances.
/// </summary>
public readonly record struct FresnelPowerCoefficients(
    Complex ReflectionS, Complex ReflectionP, Complex TransmissionS, Complex TransmissionP,
    bool TotalInternalReflection);

public static class FresnelPower
{
    /// <summary>
    /// Bare isotropic interface power coefficients for a homogeneous incident
    /// plane wave. The complex Snell angle and the s/p Poynting-flux factors are
    /// retained. These power-equivalent amplitudes are not a transmitted XYZ
    /// electric field in an absorbing medium, nor a general inhomogeneous-wave
    /// transport model. Bulk attenuation belongs to the ray propagation engine.
    /// n+ik and exp(-i omega t) are used, as in the coherent coating solver.
    /// </summary>
    internal static FresnelPowerCoefficients EvaluateHomogeneousPower(
        Complex incidentIndex, Complex transmittedIndex, double incidentCosine)
    {
        static void Validate(Complex index)
        {
            if (!double.IsFinite(index.Real) || !double.IsFinite(index.Imaginary)
                || index.Real <= 0 || index.Imaginary < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Passive indices require finite n > 0 and k >= 0.");
        }
        Validate(incidentIndex); Validate(transmittedIndex);
        if (!double.IsFinite(incidentCosine) || incidentCosine <= 0 || incidentCosine > 1)
            throw new ArgumentOutOfRangeException(nameof(incidentCosine));
        if (incidentIndex.Imaginary == 0 && transmittedIndex.Imaginary == 0)
            return Evaluate(incidentIndex.Real, transmittedIndex.Real, incidentCosine);
        var transverseSquared = incidentIndex * incidentIndex * (1 - incidentCosine * incidentCosine);
        var normal = Complex.Sqrt(transmittedIndex * transmittedIndex - transverseSquared);
        // Absorbing incidence carries a complex tangential wavevector. Its
        // transmitted normal imaginary part can be negative in a transparent
        // exit medium; choosing decay alone would reverse the forward flux.
        if (normal.Real < 0 || (normal.Real == 0 && normal.Imaginary < 0)) normal = -normal;
        var transmittedCosine = normal / transmittedIndex;
        var sDenominator = incidentIndex * incidentCosine + normal;
        var pDenominator = transmittedIndex * incidentCosine + incidentIndex * transmittedCosine;
        var rs = (incidentIndex * incidentCosine - normal) / sDenominator;
        var rp = (transmittedIndex * incidentCosine - incidentIndex * transmittedCosine) / pDenominator;
        var incidentFlux = (incidentIndex * incidentCosine).Real;
        var sFlux = normal.Real / incidentFlux;
        var pFlux = (transmittedIndex * Complex.Conjugate(transmittedCosine)).Real / incidentFlux;
        if (!double.IsFinite(sFlux) || !double.IsFinite(pFlux) || sFlux < 0 || pFlux < 0)
            throw new NotSupportedException("This absorbing interface has no supported forward homogeneous power flux.");
        var ts = 2 * incidentIndex * incidentCosine / sDenominator * Math.Sqrt(sFlux);
        var tp = 2 * incidentIndex * incidentCosine / pDenominator * Math.Sqrt(pFlux);
        return new(rs, rp, ts, tp, false);
    }

    public static FresnelPowerCoefficients Evaluate(double incidentIndex, double transmittedIndex,
        double incidentCosine)
    {
        if (!double.IsFinite(incidentIndex) || incidentIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(incidentIndex));
        if (!double.IsFinite(transmittedIndex) || transmittedIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(transmittedIndex));
        if (!double.IsFinite(incidentCosine) || incidentCosine <= 0 || incidentCosine > 1)
            throw new ArgumentOutOfRangeException(nameof(incidentCosine));

        var ratio = incidentIndex / transmittedIndex;
        var transmittedCosineSquared = 1 - ratio * ratio * (1 - incidentCosine * incidentCosine);
        var cosine = Complex.Sqrt(new Complex(transmittedCosineSquared, 0));
        var sDenominator = incidentIndex * incidentCosine + transmittedIndex * cosine;
        var pDenominator = transmittedIndex * incidentCosine + incidentIndex * cosine;
        var rs = (incidentIndex * incidentCosine - transmittedIndex * cosine) / sDenominator;
        var rp = (transmittedIndex * incidentCosine - incidentIndex * cosine) / pDenominator;
        // The flux factor is essential for air/glass and immersion interfaces.
        // TIR has no propagating transmitted power, but retains complex reflection phase.
        var fluxAmplitude = Math.Sqrt(Math.Max(0, transmittedIndex * cosine.Real / (incidentIndex * incidentCosine)));
        var ts = 2 * incidentIndex * incidentCosine / sDenominator * fluxAmplitude;
        var tp = 2 * incidentIndex * incidentCosine / pDenominator * fluxAmplitude;
        return new(rs, rp, ts, tp, transmittedCosineSquared < 0);
    }
}
