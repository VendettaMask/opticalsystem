using System.Numerics;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record CoherentImageKernel(Complex[,] Values, double PixelPitchMillimeters,
    int FourierGridSize, double CapturedPowerFraction, double KernelPower, double FullKernelPower)
{
    public double EnergyWindowFraction { get; init; }
    public double SelectionCapturedPowerFraction { get; init; }
    public double MaximumIntensityPixelPitchMillimeters { get; init; }
}

public static partial class DiffractionEngine
{
    /// <summary>Scalar isoplanatic imaging of a band-limited sampled object. Retains the traced pupil OPD phase.
    /// The transfer pupil has unit maximum amplitude; neither the kernel nor output is renormalized.</summary>
    public static CoherentImageKernel ComputeCoherentImageKernel(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int kernelSize, double pixelPitchMillimeters,
        bool fourierConjugateSampling = false, double energyWindowFraction = 0)
    {
        if (!double.IsFinite(energyWindowFraction) || energyWindowFraction < 0 || energyWindowFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(energyWindowFraction));
        if (energyWindowFraction > 0 && !fourierConjugateSampling)
            throw new ArgumentException("Energy-selected kernel support requires direct Fourier-conjugate pupil sampling.", nameof(energyWindowFraction));
        if (optic.ImageSpaceAfocal)
            throw new AnalysisDataUnavailableException("Partially Coherent Image", "finite image-space scalar imaging is required; afocal complex imaging is not implemented");
        if (optic.SurfaceGroup.Items.Any(surface => surface.IsReflective
            || surface.CoatingModel is not (NoneCoatingModel or SimpleCoatingModel)))
            throw new AnalysisDataUnavailableException("Partially Coherent Image",
                "the scalar model requires a transmitted refractive pupil and no phase-bearing coatings; reflected or thin-film complex fields require vector propagation");
        var gridSize = AnalysisResourceLimits.RoundUpPowerOfTwo(pupilSampling, nameof(pupilSampling));
        if (fourierConjugateSampling)
            return ComputeConjugateCoherentKernel(optic, field, wavelength, gridSize, kernelSize, pixelPitchMillimeters, energyWindowFraction);
        var psf = ComputeFftPsfCore(optic, field, wavelength, pupilSampling, gridSize,
            usePolarization: false, cellCenteredPupil: true, defocusMillimeters: 0,
            preparedWavefront: null, preparedPolarization: null, zemaxFftSampling: false,
            ignoreOpd: false, aimAtStop: optic.RayAimingEnabled, referenceWavelength: null,
            imageDelta: 0, ignoreScalarTransmission: false, out var pupil);
        return CreateCoherentImageKernel(pupil, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber,
            wavelength.Micrometers, pixelPitchMillimeters, kernelSize);
    }

    private static CoherentImageKernel ComputeConjugateCoherentKernel(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int size, int kernelSize, double pitch, double energyWindowFraction)
    {
        if (kernelSize < 1 || energyWindowFraction == 0 && kernelSize > size || kernelSize > 256 || !double.IsFinite(pitch) || pitch <= 0)
            throw new ArgumentOutOfRangeException(nameof(kernelSize), "The finite kernel must fit inside the Fourier-conjugate pupil grid.");
        // Trace at the actual Fourier-conjugate nodes. Interpolating a separately traced cell-centered
        // complex pupil changes edge amplitudes and phase, even if both nominal grids have the same size.
        var psf = ComputeFftPsfCore(optic, field, wavelength, size, size,
            usePolarization: false, cellCenteredPupil: true, defocusMillimeters: 0,
            preparedWavefront: null, preparedPolarization: null, zemaxFftSampling: true,
            ignoreOpd: false, aimAtStop: optic.RayAimingEnabled, referenceWavelength: null,
            imageDelta: pitch * 1000, ignoreScalarTransmission: true, out var pupil);
        // A complex field can fit while its intensity autocorrelation aliases. Keep one
        // sample outside each end of that doubled band on this centered discrete grid.
        var intensityPitchLimit = wavelength.Micrometers * .001 * psf.WorkingFNumber * (size - 4) / (2 * size);
        if (energyWindowFraction > 0 && (size <= 4 || !double.IsFinite(intensityPitchLimit) || intensityPitchLimit <= 0 || pitch > intensityPitchLimit))
            throw new AnalysisDataUnavailableException("Partially Coherent Image",
                $"the requested intensity sampling is too low for the full pupil band (pixel pitch {pitch:R} mm, limit {Math.Max(0, intensityPitchLimit):R} mm); reduce the pixel pitch or increase source oversampling");
        var peak = pupil.Cast<Complex>().Max(value => value.Magnitude);
        var fullPower = pupil.Cast<Complex>().Sum(Power) / ((double)size * size * peak * peak);
        var fieldValues = new Complex[size, size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            fieldValues[(y + size / 2) % size, (x + size / 2) % size] = pupil[y, x] / peak;
        FourierTransform2D(fieldValues);
        if (energyWindowFraction > 0)
        {
            var full = new Complex[size, size];
            var fullAnchor = (size - 1) / 2;
            for (var y = 0; y < size; y++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < size; x++)
                    full[y, x] = fieldValues[(fullAnchor - y + size) % size, (x - fullAnchor + size) % size] / ((double)size * size);
            }
            return CropCoherentImageKernelByEnergy(full, pitch, energyWindowFraction, kernelSize) with
            {
                MaximumIntensityPixelPitchMillimeters = intensityPitchLimit
            };
        }
        var kernel = new Complex[kernelSize, kernelSize];
        var anchor = (kernelSize - 1) / 2;
        double power = 0;
        for (var y = 0; y < kernelSize; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < kernelSize; x++)
            {
                var value = fieldValues[(anchor - y + size) % size, (x - anchor + size) % size] / ((double)size * size);
                kernel[y, x] = value;
                power += Power(value);
            }
        }
        return new CoherentImageKernel(kernel, pitch, size, Math.Min(1, power / fullPower), power, fullPower);

        static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
    }

    /// <summary>Selects a finite even square from a full Fourier-periodic complex response without
    /// renormalizing amplitudes. The selection square includes +/-r; the extracted 2r square has
    /// displacements [1-r,r] about its lower-center anchor. Both energy fractions are reported.</summary>
    public static CoherentImageKernel CropCoherentImageKernelByEnergy(Complex[,] fullKernel, double pixelPitchMillimeters,
        double fraction, int maximumKernelSize = 256)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(fullKernel);
        var size = fullKernel.GetLength(0);
        if (size is < 2 or > 512 || size != fullKernel.GetLength(1) || (size & (size - 1)) != 0
            || !double.IsFinite(pixelPitchMillimeters) || pixelPitchMillimeters <= 0
            || !double.IsFinite(fraction) || fraction <= 0 || fraction >= 1
            || maximumKernelSize is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(fraction), "A complete square Fourier response, finite physical pitch, fraction in (0,1), and support cap of 2..256 are required.");
        var anchor = (size - 1) / 2;
        var shells = new double[size / 2 + 1];
        double total = 0;
        for (var y = 0; y < size; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var value = fullKernel[y, x];
                var power = value.Real * value.Real + value.Imaginary * value.Imaginary;
                if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary) || !double.IsFinite(power))
                    throw new ArgumentException("The full complex response must have finite power.", nameof(fullKernel));
                shells[Math.Max(Math.Abs(y - anchor), Math.Abs(x - anchor))] += power;
                total += power;
            }
        }
        if (!double.IsFinite(total) || total <= 0)
            throw new AnalysisDataUnavailableException("Coherent Image Kernel", "the complete sampled PSF must have finite nonzero power");
        var radius = 0;
        var selected = shells[0];
        while (radius < size / 2 && selected < fraction * total) selected += shells[++radius];
        if (radius == 0) { radius = 1; selected += shells[1]; }
        var width = 2 * radius;
        if (width > maximumKernelSize)
            throw new AnalysisDataUnavailableException("Coherent Image Kernel",
                $"the energy-selected PSF needs {width} pixels but the configured support cap is {maximumKernelSize}; increase the complex kernel size or reduce the PSF energy fraction");
        var kernel = new Complex[width, width];
        double retained = 0;
        for (var y = 0; y < width; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var value = fullKernel[anchor - radius + 1 + y, anchor - radius + 1 + x];
                kernel[y, x] = value;
                retained += value.Real * value.Real + value.Imaginary * value.Imaginary;
            }
        }
        return new CoherentImageKernel(kernel, pixelPitchMillimeters, size, Math.Min(1, retained / total), retained, total)
        {
            EnergyWindowFraction = fraction,
            SelectionCapturedPowerFraction = Math.Min(1, selected / total)
        };
    }

    /// <summary>Samples a cell-centered complex pupil on the object's discrete spatial-frequency lattice.
    /// Frequencies outside the circular pupil are zero; object frequencies above Nyquist are absent.
    /// Image rows run downward, while pupil Y runs upward.</summary>
    public static CoherentImageKernel CreateCoherentImageKernel(Complex[,] pupil, double tangentialFNumber,
        double sagittalFNumber, double wavelengthMicrometers, double pixelPitchMillimeters, int kernelSize,
        int fourierGridSize = 0)
    {
        ArgumentNullException.ThrowIfNull(pupil);
        var sampling = pupil.GetLength(0);
        if (sampling < 2 || sampling != pupil.GetLength(1)) throw new ArgumentException("A square complex pupil is required.", nameof(pupil));
        if (!double.IsFinite(tangentialFNumber) || tangentialFNumber <= 0 || !double.IsFinite(sagittalFNumber) || sagittalFNumber <= 0
            || !double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0
            || !double.IsFinite(pixelPitchMillimeters) || pixelPitchMillimeters <= 0 || kernelSize is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(pixelPitchMillimeters), "Finite positive optical scales and a kernel of 1..256 pixels are required.");
        var minimumGrid = AnalysisResourceLimits.RoundUpPowerOfTwo(Math.Max(2 * kernelSize, 2 * sampling), nameof(kernelSize));
        var size = fourierGridSize == 0 ? minimumGrid : fourierGridSize;
        AnalysisResourceLimits.ValidateFftGrid(minimumGrid, size);
        double peak = 0;
        foreach (var value in pupil)
        {
            if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)) throw new ArgumentException("Pupil amplitudes must be finite.", nameof(pupil));
            peak = Math.Max(peak, value.Magnitude);
        }
        if (!double.IsFinite(peak) || peak <= 0)
            throw new AnalysisDataUnavailableException("Coherent Image Pupil", "no complex pupil power reached the image surface");
        var transfer = new Complex[size, size];
        double spectralPower = 0;
        for (var row = 0; row < size; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var fy = -(row < size / 2 ? row : row - size) / (size * pixelPitchMillimeters);
            for (var column = 0; column < size; column++)
            {
                var fx = (column < size / 2 ? column : column - size) / (size * pixelPitchMillimeters);
                // The shared OPD pupil is Fourier-transformed with a negative sign.
                // Reverse pupil frequency for the inverse transform, retaining that image-axis convention.
                var px = -2 * wavelengthMicrometers * .001 * sagittalFNumber * fx;
                var py = -2 * wavelengthMicrometers * .001 * tangentialFNumber * fy;
                var value = px * px + py * py <= 1 ? SampleCellCenteredPupil(pupil, px, py) / peak : Complex.Zero;
                transfer[row, column] = value;
                spectralPower += value.Real * value.Real + value.Imaginary * value.Imaginary;
            }
        }
        var fullPower = spectralPower / ((double)size * size);
        if (!double.IsFinite(fullPower) || fullPower <= 0)
            throw new AnalysisDataUnavailableException("Coherent Image Pupil", "the sampled object frequency band has no transmitted complex power");
        FourierTransform2D(transfer, inverse: true);
        var kernel = new Complex[kernelSize, kernelSize];
        var anchor = (kernelSize - 1) / 2;
        double captured = 0;
        for (var row = 0; row < kernelSize; row++)
            for (var column = 0; column < kernelSize; column++)
            {
                var value = transfer[(row - anchor + size) % size, (column - anchor + size) % size];
                kernel[row, column] = value;
                captured += value.Real * value.Real + value.Imaginary * value.Imaginary;
            }
        if (!double.IsFinite(captured) || captured <= 0)
            throw new AnalysisDataUnavailableException("Coherent Image Kernel", "the finite kernel window contains no complex power");
        return new CoherentImageKernel(kernel, pixelPitchMillimeters, size,
            Math.Min(1, captured / fullPower), captured, fullPower);
    }

    private static Complex SampleCellCenteredPupil(Complex[,] pupil, double x, double y)
    {
        var sampling = pupil.GetLength(0);
        var column = ((x + 1) * sampling - 1) / 2;
        var row = ((y + 1) * sampling - 1) / 2;
        var left = (int)Math.Floor(column);
        var top = (int)Math.Floor(row);
        var fx = column - left;
        var fy = row - top;
        Complex At(int r, int c) => r >= 0 && r < sampling && c >= 0 && c < sampling ? pupil[r, c] : Complex.Zero;
        return (1 - fy) * ((1 - fx) * At(top, left) + fx * At(top, left + 1))
            + fy * ((1 - fx) * At(top + 1, left) + fx * At(top + 1, left + 1));
    }
}
