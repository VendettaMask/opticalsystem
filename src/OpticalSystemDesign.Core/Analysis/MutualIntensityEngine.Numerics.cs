using System.Numerics;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class MutualIntensityEngine
{
    private static void ValidateImpulseResponse(Complex[,] impulseResponse)
    {
        var nonzero = false;
        foreach (var value in impulseResponse)
        {
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
                throw new ArgumentException("The complex impulse response must be finite.", nameof(impulseResponse));
            nonzero |= value.Real != 0 || value.Imaginary != 0;
        }
        if (!nonzero)
            throw new AnalysisDataUnavailableException("Mutual Intensity", "a finite nonzero complex impulse response is required");
    }

    private static double StablePairPower(Complex a, Complex b, double correlation, double decorrelationAmplitude)
    {
        // Positive sums of the symmetric/antisymmetric coherent modes avoid
        // subtracting nearly equal O(|a|²) diagonal and interference terms.
        var sameSign = correlation >= 0;
        var residualScale = sameSign ? decorrelationAmplitude : Math.Sqrt(Math.Max(0, 1 + correlation));
        var coherentScale = Math.Sqrt(Math.Abs(correlation));
        var coherent = coherentScale * a + (sameSign ? coherentScale * b : -coherentScale * b);
        return Power(residualScale * a) + Power(residualScale * b) + Power(coherent);
    }

    private static double SpatialDecorrelationAmplitude(SpatialCoherenceFunction function, double dx, double dy, double alpha)
    {
        var x = dx / alpha;
        var y = dy / alpha;
        if (function == SpatialCoherenceFunction.Gaussian)
        {
            var radius = double.Hypot(x, y);
            var square = radius * radius;
            // Keep sqrt(1-exp(-q)) in amplitude form: q itself may underflow
            // while its product with a bright source remains representable.
            return radius < .1 ? radius * Math.Sqrt(1 - square * (1d / 2 - square * (1d / 6
                - square * (1d / 24 - square * (1d / 120 - square * (1d / 720 - square / 5040))))))
                : Math.Sqrt(1 - Math.Exp(-square));
        }
        if (Math.Abs(x) >= .01 || Math.Abs(y) >= .01)
            return Math.Sqrt(Math.Max(0, 1 - Sinc(x) * Sinc(y)));
        var a = SincDecorrelationAmplitude(x);
        var b = SincDecorrelationAmplitude(y);
        var length = double.Hypot(a, b);
        return length == 0 ? 0 : length * Math.Sqrt(1 - Math.Pow(a / length * b, 2));
    }

    private static double SincDecorrelationAmplitude(double x)
    {
        var z = Math.PI * x;
        var square = z * z;
        return Math.Abs(z) * Math.Sqrt(1d / 6 - square * (1d / 120 - square * (1d / 5040 - square / 362880)));
    }
}
