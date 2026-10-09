using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

internal sealed record HuygensPsfSynthesisResult(PsfResult Psf, double IdealPeakWeight);

/// <summary>
/// Incoherent spectral synthesis of the formal monochromatic Huygens PSFs.
/// All colors are evaluated at the same physical image coordinates. The
/// monochromatic grids use 100 for their ideal peak; IdealPeakWeight supplies
/// the corresponding ideal polychromatic normalization, exactly once.
/// </summary>
internal static class HuygensPsfSynthesis
{
    internal static HuygensPsfSynthesisResult Compute(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        int pupilSampling,
        int imageSize,
        double pixelPitchMillimeters,
        bool usePolarization = false,
        Wavelength? referenceWavelength = null,
        (double X, double Y)? imageCenterOffset = null)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(wavelengths);
        if (wavelengths.Count == 0)
            throw new ArgumentException("Huygens synthesis requires a wavelength.", nameof(wavelengths));
        AnalysisResourceLimits.ValidateDirectPsfWork(pupilSampling, imageSize);
        referenceWavelength ??= wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelengths[0];
        var shortest = wavelengths.Min(w => w.Nanometers);
        var weightScale = wavelengths.Max(w => w.Weight);
        var values = new double[imageSize, imageSize];
        var idealPeakWeight = 0.0;
        var workingFNumber = 0.0;
        foreach (var wavelength in wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            // A common spectral scale cancels from all normalized observables.
            // Divide it out before summation to avoid overflow. Preserve the
            // analysis-level legacy equal-weight fallback; strict merit-metric
            // selection rejects an all-zero mixture before reaching this layer.
            var spectralWeight = weightScale > 0 ? wavelength.Weight / weightScale : 1;
            var weight = spectralWeight * Math.Pow(shortest / wavelength.Nanometers, 2);
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, wavelength,
                pupilSampling, imageSize, pixelPitchMillimeters, usePolarization,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: referenceWavelength,
                imageCenterOffset: imageCenterOffset);
            workingFNumber += psf.WorkingFNumber / wavelengths.Count;
            idealPeakWeight += weight;
            for (var row = 0; row < imageSize; row++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var column = 0; column < imageSize; column++)
                    values[row, column] += weight * psf.Values[row, column];
            }
        }
        if (!double.IsFinite(idealPeakWeight) || idealPeakWeight <= 0)
            throw new AnalysisDataUnavailableException("Huygens PSF", "no finite positive spectral weight");
        return new(new PsfResult(values, pupilSampling, imageSize, workingFNumber,
            optic.ImageSpaceAfocal ? pixelPitchMillimeters : pixelPitchMillimeters * 1000,
            SampleSpacingUnit: optic.ImageSpaceAfocal
                ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer)
            { UseRayAiming = optic.RayAimingEnabled }, idealPeakWeight);
    }
}
