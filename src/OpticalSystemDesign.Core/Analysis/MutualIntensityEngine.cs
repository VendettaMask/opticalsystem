using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Propagation of W[i,j] = (1-gamma) I[i] delta[i,j] + gamma a[i] conjugate(a[j]),
/// where a[i] = sqrt(I[i]) exp(i phase[i]). The source matrix is positive semidefinite for 0..1 gamma.</summary>
public static partial class MutualIntensityEngine
{
    public static double[,] PropagateUniformCoherence(double[,] sourceIntensity, Complex[,] impulseResponse,
        double coherence, double[,]? sourcePhaseRadians = null)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(sourceIntensity);
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (!double.IsFinite(coherence) || coherence < 0 || coherence > 1)
            throw new ArgumentOutOfRangeException(nameof(coherence));
        var height = sourceIntensity.GetLength(0);
        var width = sourceIntensity.GetLength(1);
        var kernelHeight = impulseResponse.GetLength(0);
        var kernelWidth = impulseResponse.GetLength(1);
        AnalysisResourceLimits.ValidateConvolutionWork(width, height, kernelWidth, kernelHeight, 4);
        if (sourcePhaseRadians is not null && (sourcePhaseRadians.GetLength(0) != height || sourcePhaseRadians.GetLength(1) != width))
            throw new ArgumentException("Source phase must match source intensity dimensions.", nameof(sourcePhaseRadians));
        ValidateImpulseResponse(impulseResponse);
        var incoherent = new double[height, width];
        var coherent = coherence > 0 ? new Complex[height, width] : null;
        var anchorY = (kernelHeight - 1) / 2;
        var anchorX = (kernelWidth - 1) / 2;
        var incoherentAmplitudeScale = Math.Sqrt(1 - coherence);
        var coherentAmplitudeScale = Math.Sqrt(coherence);
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if ((x & 31) == 0) ComputationCancellation.ThrowIfCancellationRequested();
                var intensity = sourceIntensity[y, x];
                var phase = sourcePhaseRadians?[y, x] ?? 0;
                if (!double.IsFinite(intensity) || intensity < 0 || !double.IsFinite(phase))
                    throw new ArgumentException("Source intensity must be finite and nonnegative, and source phase finite.");
                if (intensity == 0) continue;
                var amplitude = Complex.FromPolarCoordinates(Math.Sqrt(intensity), phase);
                var incoherentAmplitude = incoherentAmplitudeScale * Math.Sqrt(intensity);
                for (var ky = 0; ky < kernelHeight; ky++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var targetY = y + ky - anchorY;
                    if (targetY < 0 || targetY >= height) continue;
                    for (var kx = 0; kx < kernelWidth; kx++)
                    {
                        var targetX = x + kx - anchorX;
                        if (targetX < 0 || targetX >= width) continue;
                        var h = impulseResponse[ky, kx];
                        // Balance intensity and response in the field domain;
                        // squaring h first can overflow or underflow even when
                        // the final propagated power is representable.
                        if (coherence < 1) incoherent[targetY, targetX] += Power(incoherentAmplitude * h);
                        if (coherent is not null) coherent[targetY, targetX] += (coherentAmplitudeScale * amplitude) * h;
                    }
                }
            }
        }
        for (var y = 0; y < height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var value = incoherent[y, x] + (coherent is null ? 0 : Power(coherent[y, x]));
                if (!double.IsFinite(value) || value < 0)
                    throw new AnalysisDataUnavailableException("Mutual Intensity", "propagation exceeded finite nonnegative intensity range");
                incoherent[y, x] = value;
            }
        }
        return incoherent;
    }

    private static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
}
