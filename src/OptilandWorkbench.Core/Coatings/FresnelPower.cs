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
