using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    /// <summary>Finite local incoherent intensity kernel from the shared traced complex OPD pupil.
    /// Separably evaluates its Fourier integral on a physical image lattice. Integral is relative to
    /// full pupil power, per square micrometer; the finite window is NOT normalized to unit power.</summary>
    public static PsfResult ComputeLocalIncoherentPsf(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int imageSize, double spacingMicrometers, Wavelength? referenceWavelength = null)
        => ComputeLocalIncoherentPsf(optic, field, wavelength, pupilSampling, imageSize, spacingMicrometers, referenceWavelength, out _);

    internal static PsfResult ComputeLocalIncoherentPsf(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int imageSize, double spacingMicrometers, Wavelength? referenceWavelength,
        out Complex[,] sampledPupil)
    {
        if (optic.ImageSpaceAfocal || optic.SurfaceGroup.Items.Any(s => s.IsReflective
            || s.CoatingModel is not (Coatings.NoneCoatingModel or Coatings.SimpleCoatingModel)))
            throw new NotSupportedException("Local scalar SFR requires a focal transmitted lens without phase-bearing coatings.");
        AnalysisResourceLimits.ValidateFftGrid(pupilSampling,
            AnalysisResourceLimits.RoundUpPowerOfTwo(pupilSampling, nameof(pupilSampling)));
        if (imageSize < 3 || imageSize % 2 == 0 || imageSize > 1025
            || !double.IsFinite(spacingMicrometers) || spacingMicrometers <= 0)
            throw new ArgumentException("Local physical PSF exceeds its separable Fourier work bound.");
        var (pupil, psf) = PrepareLocalScalarPupil(optic, field, wavelength, pupilSampling, referenceWavelength);
        sampledPupil = pupil;
        var n = pupil.GetLength(0);
        var dx = 1 / (n * wavelength.Micrometers * psf.SagittalWorkingFNumber);
        var dy = 1 / (n * wavelength.Micrometers * psf.TangentialWorkingFNumber);
        var radius = imageSize / 2 * spacingMicrometers;
        if (radius >= .5 / Math.Max(dx, dy))
            throw new ArgumentException("PSF window reaches the sampled pupil's periodic replica; increase pupil sampling or reduce physical window.");
        if (spacingMicrometers > wavelength.Micrometers * Math.Min(psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber) / 2)
            throw new ArgumentException("PSF intensity is undersampled; increase image samples per pixel.");
        double power = 0;
        foreach (var z in pupil) power += z.Magnitude * z.Magnitude;
        if (!(power > 0)) throw new InvalidOperationException("No transmitted pupil power.");
        var useChirp = 2L * imageSize * n * (imageSize + n) > AnalysisResourceLimits.MaximumDirectFourierOperations;
        var amplitudes = useChirp ? PhysicalFourierWindow(pupil, imageSize, dx * spacingMicrometers, dy * spacingMicrometers) : null;
        var phaseX = new Complex[useChirp ? 0 : imageSize, n]; var phaseY = new Complex[useChirp ? 0 : imageSize, n];
        for (var k = 0; !useChirp && k < imageSize; k++) for (var j = 0; j < n; j++)
        {
            var u = j + .5 - n / 2.0;
            var x = (k - imageSize / 2) * spacingMicrometers;
            phaseX[k, j] = Complex.FromPolarCoordinates(1, -2 * Math.PI * u * dx * x);
            phaseY[k, j] = Complex.FromPolarCoordinates(1, -2 * Math.PI * u * dy * x);
        }
        var intermediate = new Complex[n, useChirp ? 0 : imageSize];
        for (var row = 0; !useChirp && row < n; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < imageSize; x++)
                for (var j = 0; j < n; j++) intermediate[row, x] += pupil[row, j] * phaseX[x, j];
        }
        var intensity = new double[imageSize, imageSize];
        for (var y = 0; y < imageSize; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < imageSize; x++)
            {
                Complex sum = amplitudes is null ? 0 : amplitudes[y, x];
                if (amplitudes is null) for (var j = 0; j < n; j++) sum += intermediate[j, x] * phaseY[y, j];
                // Each traced sample represents a rectangular pupil cell. Integrating that
                // cell gives its Fourier sinc footprint; treating cells as point emitters
                // instead creates periodic PSF replicas and a different full-image OTF.
                var cell = LocalCellFootprint((x - imageSize / 2) * spacingMicrometers * dx)
                    * LocalCellFootprint((y - imageSize / 2) * spacingMicrometers * dy);
                intensity[y, x] = sum.Magnitude * sum.Magnitude * cell * cell * dx * dy / power;
            }
        }
        return new(intensity, n, imageSize, psf.WorkingFNumber, spacingMicrometers,
            psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber)
        { CroppedImageCenterIndex = imageSize / 2, UseRayAiming = psf.UseRayAiming,
            StopAimingFallbackUsed = psf.StopAimingFallbackUsed };
    }

    private static double LocalCellFootprint(double x) => Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static (Complex[,] Pupil, PsfResult Parameters) PrepareLocalScalarPupil(Optic optic,
        (double Hx, double Hy) field, Wavelength wavelength, int pupilSampling, Wavelength? referenceWavelength = null)
    {
        if (optic.ImageSpaceAfocal || optic.SurfaceGroup.Items.Any(s => s.IsReflective
            || s.CoatingModel is not (Coatings.NoneCoatingModel or Coatings.SimpleCoatingModel)))
            throw new NotSupportedException("Local scalar imaging requires a focal transmitted lens without phase-bearing coatings.");
        WavefrontResult? sampledWavefront = null;
        var parameters = ComputeFftPsfCore(optic, field, wavelength, pupilSampling,
            AnalysisResourceLimits.RoundUpPowerOfTwo(pupilSampling, nameof(pupilSampling)),
            usePolarization: false, cellCenteredPupil: true, defocusMillimeters: 0,
            preparedWavefront: null, preparedPolarization: null, zemaxFftSampling: false,
            ignoreOpd: false, aimAtStop: optic.RayAimingEnabled, referenceWavelength: referenceWavelength,
            imageDelta: 0, ignoreScalarTransmission: false, out var pupil,
            observeWavefront: wavefront => sampledWavefront = wavefront, croppedOutputSize: 2);
        ValidateLocalPupilSampling(sampledWavefront!);
        return (pupil, parameters);
    }

    // Inspect unwrapped traced OPD, not the already wrapped complex phase: a 13-wave
    // step can look arbitrarily smooth after wrapping and yield a misleading local PSF.
    internal static void ValidateLocalPupilSampling(WavefrontResult wavefront)
    {
        var grid = wavefront.PupilGrid ?? throw new ArgumentException("Local PSF requires an explicit sampled pupil.");
        var values = new double[grid.SampleCount, grid.SampleCount];
        for (var y = 0; y < grid.SampleCount; y++) for (var x = 0; x < grid.SampleCount; x++) values[y, x] = double.NaN;
        foreach (var sample in wavefront.Samples.Where(s => s.Intensity > 0))
        {
            if (!double.IsFinite(sample.OpdWaves)) throw new InvalidOperationException("Non-finite local pupil OPD.");
            values[grid.RowIndex(sample.NormalizedPupilY), grid.ColumnIndex(sample.NormalizedPupilX)] = sample.OpdWaves;
        }
        double maximum = 0;
        for (var y = 0; y < grid.SampleCount; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < grid.SampleCount; x++)
            {
                var current = values[y, x]; if (!double.IsFinite(current)) continue;
                if (x > 0 && double.IsFinite(values[y, x - 1])) maximum = Math.Max(maximum, Math.Abs(current - values[y, x - 1]));
                if (y > 0 && double.IsFinite(values[y - 1, x])) maximum = Math.Max(maximum, Math.Abs(current - values[y - 1, x]));
            }
        }
        if (maximum > .5)
            throw new AnalysisDataUnavailableException("Local PSF",
                $"瞳面相位欠采样：当前 {grid.SampleCount} 档相邻 OPD 最大差 {maximum:G6} 波，超过 0.5 波；请增加瞳面采样，并检查物距、像距和离焦。强离焦时还需扩大图像及 PSF 的物理范围；不能将混叠后的局部 PSF 用于 SFR");
    }
}
