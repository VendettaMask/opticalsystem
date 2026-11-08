using System.Numerics;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal const int MaximumPhysicalPsfWindowSampling = 1024;

    internal static (Wavelength Wavelength, PsfResult Result)[] ComputePhysicalPsfWindowSpectrum(
        Optic optic, (double Hx, double Hy) field, IReadOnlyList<Wavelength> wavelengths,
        int sampling, int displaySize, double imagePitch, bool interpolateNativePupil = true)
    {
        var reference = wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? wavelengths[0];
        return wavelengths.Select(wave => (wave, Compute(wave))).ToArray();

        PsfResult Compute(Wavelength wave)
        {
            // Sample the actual normalized pupil. Resolve image coordinates with
            // exact physical Fourier frequencies independently of that lattice.
            var metadata = ComputeFftPsfCore(optic, field, wave, sampling, sampling,
                false, true, 0, null, null, true, false, optic.RayAimingEnabled, reference,
                0, true, out var pupil, explicitPupilGrid: new(sampling, true, true, 1),
                interpolateScalarPupilAmplitude: true, croppedOutputSize: 2, interpolateNativePupil: interpolateNativePupil);
            if (!TryWorkingFNumbersAtPupilZone(optic, field, wave, metadata.UseRayAiming,
                    WorkingApertureReduction.RootMeanSquare, 1, out var axes))
                throw new AnalysisDataUnavailableException("FFT PSF", "the physical Fourier window requires a finite marginal cone on both axes");
            var conjugateStep = imagePitch / (wave.Micrometers * (sampling - 2));
            var spectrum = PhysicalFourierWindow(pupil, displaySize, conjugateStep / axes.Sagittal,
                conjugateStep / axes.Tangential, nativeCenteredOutput: true);
            var illuminated = (double)pupil.Cast<Complex>().Count(value => value != Complex.Zero);
            var intensity = new double[displaySize, displaySize];
            for (var row = 0; row < displaySize; row++)
                for (var column = 0; column < displaySize; column++)
                {
                    var amplitude = spectrum[row, column].Magnitude / illuminated;
                    intensity[row, column] = 100 * amplitude * amplitude;
                }
            return metadata with
            {
                Values = intensity, GridSize = displaySize, SampleSpacingMicrometers = imagePitch,
                CroppedImageCenterIndex = (displaySize - 1) / 2
            };
        }
    }
}
