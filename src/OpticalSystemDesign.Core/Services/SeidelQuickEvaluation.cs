using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Services;

public sealed record SeidelQuickEvaluation(bool Supported, string Outcome, string Units,
    double WavelengthMicrometers, double[]? CoefficientsInWaves, double? Score);

public static class SeidelQuickEvaluator
{
    /// <summary>Third-order wave coefficients, not a spot radius or acceptance metric. Unsupported systems
    /// return no score. Every conversion uses the explicitly supplied wavelength in micrometers.</summary>
    public static SeidelQuickEvaluation Evaluate(Optic optic, double wavelengthMicrometers)
    {
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers));
        try
        {
            var waves = SeidelMetrics.Calculate(optic, wavelengthMicrometers).Total.InWaves(wavelengthMicrometers / 1000);
            var score = waves.Max(v => Math.Abs(v));
            return new(true, "SupportedSphericalThirdOrder", "waves", wavelengthMicrometers, waves, score);
        }
        catch (NotSupportedException error) { return new(false, "Unsupported:" + error.Message, "waves", wavelengthMicrometers, null, null); }
        catch (Exception error) when (error is ArithmeticException or InvalidOperationException)
        { return new(false, "Unavailable:" + error.Message, "waves", wavelengthMicrometers, null, null); }
    }
}
