using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record PsfIntensityConvolutionResult(
    double[,] Values,
    int CorrectedKernelCount,
    double MaximumNegativeMass);

public static partial class ImageSimulationEngine
{
    /// <summary>
    /// Reconstructs a local intensity PSF for each contributing source pixel,
    /// takes its nonnegative part and normalizes the complete kernel before
    /// scattering power into the finite image. Boundary losses are retained.
    /// Signed eigenvectors remain valid in the separate linear convolution API.
    /// </summary>
    public static PsfIntensityConvolutionResult ConvolvePsfIntensity(
        double[,] source,
        IReadOnlyList<double[,]> eigenPsfs,
        double[,,] coefficientMaps,
        double[,] meanPsf)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(eigenPsfs);
        ArgumentNullException.ThrowIfNull(coefficientMaps);
        ArgumentNullException.ThrowIfNull(meanPsf);
        var height = source.GetLength(0);
        var width = source.GetLength(1);
        var kernelHeight = meanPsf.GetLength(0);
        var kernelWidth = meanPsf.GetLength(1);
        if (coefficientMaps.GetLength(0) != eigenPsfs.Count
            || coefficientMaps.GetLength(1) != height || coefficientMaps.GetLength(2) != width
            || eigenPsfs.Any(psf => psf.GetLength(0) != kernelHeight || psf.GetLength(1) != kernelWidth))
            throw new ArgumentException("Intensity PSF components and coefficient maps must have matching dimensions.");
        // Component reconstruction plus the mean and normalized scatter passes.
        AnalysisResourceLimits.ValidateConvolutionWork(width, height, kernelWidth, kernelHeight, 2 + eigenPsfs.Count);
        var output = new double[height, width];
        var kernel = new double[kernelHeight, kernelWidth];
        var coefficients = new double[eigenPsfs.Count];
        var anchorY = (kernelHeight - 1) / 2;
        var anchorX = (kernelWidth - 1) / 2;
        var corrected = 0;
        var maximumNegativeMass = 0.0;
        for (var sourceY = 0; sourceY < height; sourceY++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var sourceX = 0; sourceX < width; sourceX++)
            {
                if ((sourceX & 31) == 0) ComputationCancellation.ThrowIfCancellationRequested();
                var sourcePower = source[sourceY, sourceX];
                if (!double.IsFinite(sourcePower) || sourcePower < 0)
                    throw new ArgumentException("Source intensity must be finite and nonnegative.", nameof(source));
                if (sourcePower == 0) continue;
                for (var component = 0; component < coefficients.Length; component++)
                    coefficients[component] = coefficientMaps[component, sourceY, sourceX];
                var positiveMass = 0.0;
                var negativeMass = 0.0;
                for (var y = 0; y < kernelHeight; y++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    for (var x = 0; x < kernelWidth; x++)
                    {
                        var value = meanPsf[y, x];
                        for (var component = 0; component < coefficients.Length; component++)
                            value += coefficients[component] * eigenPsfs[component][y, x];
                        if (!double.IsFinite(value))
                            throw InvalidIntensityPsf();
                        kernel[y, x] = Math.Max(0, value);
                        positiveMass += kernel[y, x];
                        negativeMass += Math.Max(0, -value);
                    }
                }
                if (!double.IsFinite(positiveMass) || positiveMass <= 0 || !double.IsFinite(negativeMass))
                    throw InvalidIntensityPsf();
                // Count material corrections separately from floating-point cancellation.
                if (negativeMass > 1e-12) corrected++;
                maximumNegativeMass = Math.Max(maximumNegativeMass, negativeMass);
                for (var y = 0; y < kernelHeight; y++)
                {
                    ComputationCancellation.ThrowIfCancellationRequested();
                    var targetY = sourceY + y - anchorY;
                    if (targetY < 0 || targetY >= height) continue;
                    for (var x = 0; x < kernelWidth; x++)
                    {
                        var targetX = sourceX + x - anchorX;
                        if (targetX >= 0 && targetX < width)
                            output[targetY, targetX] += sourcePower * (kernel[y, x] / positiveMass);
                    }
                }
            }
        }
        return new PsfIntensityConvolutionResult(output, corrected, maximumNegativeMass);
    }

    private static AnalysisDataUnavailableException InvalidIntensityPsf() => new(
        "Image Simulation PSF", "a finite reconstructed PSF with positive power is required");
}
