namespace OpticalSystemDesign.Core.Analysis;

public static class AnalysisResourceLimits
{
    public const int MaximumFftGridSize = 2_048;
    public const long MaximumImagePixels = 262_144;
    public const int MaximumImageDimension = 4_096;
    public const long MaximumDirectPsfOperations = 100_000_000;
    public const long MaximumDirectFourierOperations = 100_000_000;
    public const long MaximumDirectMtfOperations = 100_000_000;
    public const long MaximumPsfBasisOperations = 200_000_000;
    public const long MaximumImageSimulationPsfOperations = 300_000_000;
    public const long MaximumImageSimulationConvolutionOperations = 300_000_000;
    public const long MaximumPsfDecompositionOperations = 200_000_000;
    public const int MaximumPsfGridPoints = 225;
    public const int MaximumAnalysisGridDimension = 1_024;
    public const long MaximumAnalysisGridCells = 1_048_576;
    public const long MaximumAggregateGridCells = 8_388_608;
    public const int MaximumAnalysisMapCount = 256;
    public const int MaximumWavefrontMapDimension = 512;
    public const int MaximumGridDistortionDimension = 512;
    public const long MaximumAnalysisRayWork = 100_000_000;
    public const long MaximumWavefrontInterpolationWork = 250_000_000;
    public const int MaximumImageDistortionGridSize = 33;
    public const int MaximumImageDistortionDegree = 9;
    public const long MaximumImageDistortionOperations = 100_000_000;

    public static int RoundUpPowerOfTwo(int value, string parameterName)
    {
        if (value <= 0 || value > MaximumFftGridSize)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"FFT grid input must be between 1 and {MaximumFftGridSize}.");
        }

        var result = 1;
        while (result < value)
        {
            result = checked(result << 1);
        }

        return result;
    }

    public static void ValidateFftGrid(int pupilSampling, int gridSize)
    {
        if (pupilSampling < 2 || pupilSampling > MaximumFftGridSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pupilSampling),
                $"Pupil sampling must be between 2 and {MaximumFftGridSize}.");
        }

        if (gridSize < pupilSampling
            || gridSize > MaximumFftGridSize
            || (gridSize & (gridSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gridSize),
                $"FFT grid size must be a power of two between the pupil sampling and {MaximumFftGridSize}.");
        }
    }

    public static void ValidateImageDimensions(int width, int height, string description)
    {
        if (width <= 0 || height <= 0
            || width > MaximumImageDimension
            || height > MaximumImageDimension
            || (long)width * height > MaximumImagePixels)
        {
            throw new ArgumentOutOfRangeException(
                description,
                $"{description} must not exceed {MaximumImageDimension} pixels per side or {MaximumImagePixels:N0} total pixels.");
        }
    }

    public static void ValidateMtfTransformWork(int gridSize)
    {
        if (gridSize < 1 || gridSize > MaximumFftGridSize)
            throw new ArgumentOutOfRangeException(nameof(gridSize),
                $"MTF transform size must be between 1 and {MaximumFftGridSize} samples per side.");
        if ((gridSize & (gridSize - 1)) != 0
            && 2L * gridSize * gridSize * gridSize > MaximumDirectFourierOperations)
            throw new ArgumentOutOfRangeException(nameof(gridSize),
                $"Non-power-of-two MTF transform exceeds the {MaximumDirectFourierOperations:N0}-operation safety budget. Reduce the sampling or choose a power-of-two grid.");
    }

    public static void ValidateDirectMtfWork(int gridSize, int frequencyCount)
    {
        if (gridSize < 1 || gridSize > MaximumFftGridSize || frequencyCount < 0)
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        if (2L * gridSize * gridSize * frequencyCount > MaximumDirectMtfOperations)
            throw new ArgumentOutOfRangeException(nameof(frequencyCount),
                $"Direct MTF work exceeds the {MaximumDirectMtfOperations:N0}-operation safety budget.");
    }

    public static void ValidateAnalysisGrid(int width, int height, string description)
    {
        if (width <= 0 || height <= 0
            || width > MaximumAnalysisGridDimension
            || height > MaximumAnalysisGridDimension
            || checked((long)width * height) > MaximumAnalysisGridCells)
        {
            throw new ArgumentOutOfRangeException(
                description,
                $"{description} must not exceed {MaximumAnalysisGridDimension} cells per side or {MaximumAnalysisGridCells:N0} total cells.");
        }
    }

    public static void ValidateAggregateGridWork(
        int width,
        int height,
        int fieldCount,
        int wavelengthCount,
        int raysPerMap,
        string description)
    {
        ValidateAnalysisGrid(width, height, description);
        if (fieldCount < 1 || wavelengthCount < 1 || raysPerMap < 1)
        {
            throw new ArgumentOutOfRangeException(description);
        }

        var mapCount = checked((long)fieldCount * wavelengthCount);
        if (mapCount > MaximumAnalysisMapCount
            || checked(mapCount * width * height) > MaximumAggregateGridCells
            || checked(mapCount * raysPerMap) > MaximumAnalysisRayWork)
        {
            throw new ArgumentOutOfRangeException(
                description,
                $"{description} exceeds the aggregate map, grid-cell, or ray-work safety budget.");
        }
    }

    public static int ValidateWavefrontMapSize(int mapSize, string parameterName)
    {
        if (mapSize is < 16 or > MaximumWavefrontMapDimension)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Wavefront map size must be between 16 and {MaximumWavefrontMapDimension}.");
        }

        return mapSize;
    }

    public static void ValidateDirectPsfWork(int pupilSampling, int imageSize)
    {
        if (pupilSampling < 2 || imageSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pupilSampling),
                "Pupil sampling must be at least 2 and image size must be positive.");
        }

        var operations = checked((long)pupilSampling * pupilSampling * imageSize * imageSize);
        if (operations > MaximumDirectPsfOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageSize),
                $"Direct PSF work exceeds the {MaximumDirectPsfOperations:N0}-operation safety budget.");
        }
    }

    public static void ValidateImageSimulation(RgbImage source, ImageSimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(config);
        ValidateImageDimensions(source.Width, source.Height, "Source image");

        if (source.Channels is not (1 or 3))
        {
            throw new ArgumentException("Source image must contain one or three channels.", nameof(source));
        }

        var dimensions = ResolveImageSimulationDimensions(source.Width, source.Height, config);
        var preparedWidth = dimensions.PreparedWidth;
        var preparedHeight = dimensions.PreparedHeight;

        if (config.WavelengthsMicrometers.Count is < 1 or > 16
            || config.WavelengthsMicrometers.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Image simulation wavelengths are invalid or exceed the supported count.");
        }

        if (string.Equals(config.AberrationMode, "Partially Coherent", StringComparison.OrdinalIgnoreCase))
        {
            MutualIntensityEngine.ValidateSpatialMethod(config.CoherenceMethod, config.CoherenceFraction, config.CoherenceSampling);
            if (config.CoherenceFunction == SpatialCoherenceFunction.Uniform && config.CoherenceMethod != SpatialCoherenceMethod.ExactSourcePairs)
                throw new ArgumentException("Finite spatial coherence methods require Gaussian or Sinc coherence.", nameof(config));
            if (!double.IsFinite(config.PsfEnergyFraction) || config.PsfEnergyFraction < 0 || config.PsfEnergyFraction >= 1
                || config.PsfEnergyFraction > 0 && config.PsfSize < 2)
                throw new ArgumentOutOfRangeException(nameof(config), "Invalid PSF energy fraction or maximum kernel size.");
            if (config.PsfEnergyFraction > 0 && config.CoherenceFunction == SpatialCoherenceFunction.Uniform)
                throw new ArgumentException("PSF energy selection requires Gaussian/Sinc direct Fourier-conjugate sampling.", nameof(config));
            if (!double.IsFinite(config.SpatialCoherence) || config.SpatialCoherence < 0 || config.SpatialCoherence > 1
                || !Enum.IsDefined(config.CoherenceFunction)
                || config.CoherenceFunction != SpatialCoherenceFunction.Uniform
                    && (!double.IsFinite(config.CoherenceAlphaMillimeters) || config.CoherenceAlphaMillimeters <= 0)
                || !double.IsFinite(config.FileSizeMillimeters) || config.FileSizeMillimeters < 0
                || config.FileSizeMillimeters > 0 && config.PixelSizeMillimeters > 0
                || !double.IsFinite(config.PixelSizeMillimeters) || config.PixelSizeMillimeters < 0
                || !double.IsFinite(config.FieldHeight) || config.FieldHeight < 0
                || config.PsfSize is < 1 or > 256 || config.NumRays is < 2 or > 512
                || !double.IsFinite(config.FieldCenterX) || Math.Abs(config.FieldCenterX) > 1
                || !double.IsFinite(config.FieldCenterY) || Math.Abs(config.FieldCenterY) > 1)
                throw new ArgumentOutOfRangeException(nameof(config), "Invalid spatial coherence, pupil/kernel sampling or reference field.");
            if (config.UsePolarization || !string.Equals(config.Reference, "chief", StringComparison.OrdinalIgnoreCase)
                || config.PsfGridRows != 1 || config.PsfGridColumns != 1)
                throw new AnalysisDataUnavailableException("Partially Coherent Image",
                    "the scalar isoplanatic model requires a 1 x 1 chief-referenced pupil; polarized or field-dependent complex imaging is not implemented");
            if (config.CoherenceFunction != SpatialCoherenceFunction.Uniform
                && config.PsfEnergyFraction == 0
                && RoundUpPowerOfTwo(config.NumRays, nameof(config.NumRays)) < config.PsfSize)
                throw new ArgumentOutOfRangeException(nameof(config.NumRays), "Gaussian/Sinc kernel support must fit inside the Fourier-conjugate pupil sampling grid.");
            if (config.CoherenceFunction != SpatialCoherenceFunction.Uniform)
            {
                var active = new double[source.Channels];
                for (var c = 0; c < source.Channels; c++) for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                    {
                        var value = source[c, y, x];
                        if (!double.IsFinite(value) || value < 0)
                            throw new ArgumentException("Spatial mutual-intensity source must be finite and nonnegative.", nameof(source));
                        if (value > 0) active[c] += (double)config.Oversampling * config.Oversampling;
                    }
                double pairWork = 0;
                for (var c = 0; c < config.WavelengthsMicrometers.Count; c++)
                {
                    var count = active[Math.Min(c, source.Channels - 1)];
                    pairWork += count * (count + 1) / 2 * preparedWidth * preparedHeight;
                }
                if (pairWork > MaximumImageSimulationConvolutionOperations)
                    throw new ArgumentOutOfRangeException(nameof(config), "Exact spatial mutual-intensity work across all wavelengths exceeds the image convolution budget. Reduce source sampling, active area, oversampling or wavelength count.");
            }
            if (config.SourcePhaseRadians is { } phase)
            {
                if (phase.GetLength(1) != source.Height || phase.GetLength(2) != source.Width
                    || phase.GetLength(0) != 1 && phase.GetLength(0) != source.Channels)
                    throw new ArgumentException("Source phase must match the original source dimensions, with one or source-matched channels.");
                foreach (var value in phase)
                    if (!double.IsFinite(value)) throw new ArgumentException("Source phase must be finite.");
            }
            // Uniform uses sampled-field convolutions. Gaussian/Sinc use the separately bounded
            // sparse source-pair sum above, not an image-area times kernel-area convolution.
            if (config.CoherenceFunction == SpatialCoherenceFunction.Uniform)
                ValidateConvolutionWork(preparedWidth, preparedHeight, config.PsfSize, config.PsfSize, 4,
                    config.WavelengthsMicrometers.Count);
            var fourierSize = RoundUpPowerOfTwo(Math.Max(2 * config.PsfSize, 2 * config.NumRays), nameof(config));
            var fourierWork = checked(4L * fourierSize * fourierSize * (long)Math.Log2(fourierSize) * config.WavelengthsMicrometers.Count);
            if (config.CoherenceMethod == SpatialCoherenceMethod.FourierModes)
            {
                var coherenceGrid = MutualIntensityEngine.ResolveSpatialCoherenceSampling(config.CoherenceSampling, config.PsfSize, config.PsfSize);
                fourierWork = checked(fourierWork + 4L * coherenceGrid * coherenceGrid
                    * (long)Math.Log2(coherenceGrid) * config.WavelengthsMicrometers.Count);
            }
            if (fourierWork > MaximumImageSimulationPsfOperations)
                throw new ArgumentOutOfRangeException(nameof(config), "Coherent image Fourier work exceeds the supported budget.");
            return;
        }
        if (config.SourcePhaseRadians is not null)
            throw new ArgumentException("Source phase is supported only by the mutual-intensity image model.", nameof(config));

        ValidatePsfConfiguration(config);
        ValidateImageDistortion(preparedWidth, preparedHeight, config.DistortionGridSize,
            config.DistortionPolynomialDegree, config.WavelengthsMicrometers.Count);
        ValidateConvolutionWork(preparedWidth, preparedHeight, config.PsfSize, config.PsfSize,
            2 + Math.Min(config.Components, config.PsfGridRows * config.PsfGridColumns),
            config.WavelengthsMicrometers.Count);
        if (checked(PsfDecompositionOperations(config) * config.WavelengthsMicrometers.Count)
            > MaximumPsfDecompositionOperations)
            throw new ArgumentOutOfRangeException(nameof(config), "Image simulation PSF decomposition exceeds the supported work budget.");
        var basisOperations = PsfBasisOperations(config);
        if (checked(basisOperations * config.WavelengthsMicrometers.Count)
            > MaximumImageSimulationPsfOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                $"Image simulation PSF work exceeds the {MaximumImageSimulationPsfOperations:N0}-operation safety budget.");
        }
    }

    public static (int PreparedWidth, int PreparedHeight, int OutputWidth, int OutputHeight)
        ResolveImageSimulationDimensions(int sourceWidth, int sourceHeight, ImageSimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateImageDimensions(sourceWidth, sourceHeight, "Source image");
        if (config.Oversampling is < 1 or > 16 || config.Padding is < 0 or > 512)
            throw new ArgumentOutOfRangeException(nameof(config), "Oversampling or padding is outside the supported range.");
        if (NormalizeImageRotation(config.SourceRotationDegrees) is 90 or 270)
            (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);
        var preparedWidth = checked(sourceWidth * config.Oversampling + 2 * config.Padding);
        var preparedHeight = checked(sourceHeight * config.Oversampling + 2 * config.Padding);
        ValidateImageDimensions(preparedWidth, preparedHeight, "Prepared image");
        var outputWidth = config.OutputWidth > 0 ? config.OutputWidth : preparedWidth;
        var outputHeight = config.OutputHeight > 0 ? config.OutputHeight : preparedHeight;
        ValidateImageDimensions(outputWidth, outputHeight, "Output image");
        return (preparedWidth, preparedHeight, outputWidth, outputHeight);
    }

    internal static int NormalizeImageRotation(int degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>Bounds both QR fitting and evaluation before tracing or allocating a distortion grid.</summary>
    public static int ValidateImageDistortion(int width, int height, int numGridPoints, int degree,
        int wavelengthCount = 1)
    {
        ValidateImageDimensions(width, height, "Distortion image");
        if (numGridPoints is < 2 or > MaximumImageDistortionGridSize)
            throw new ArgumentOutOfRangeException(nameof(numGridPoints),
                $"Distortion sampling must be between 2 and {MaximumImageDistortionGridSize} points per side.");
        if (degree is < 1 or > MaximumImageDistortionDegree)
            throw new ArgumentOutOfRangeException(nameof(degree),
                $"Distortion polynomial degree must be between 1 and {MaximumImageDistortionDegree}.");
        if (wavelengthCount is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(wavelengthCount));
        var effectiveGrid = Math.Max(degree + 1, numGridPoints);
        var samples = checked((long)effectiveGrid * effectiveGrid);
        var features = checked((long)(degree + 1) * (degree + 2) / 2);
        // Two reorthogonalized QR fits plus two polynomial evaluations per output pixel.
        var operations = checked(wavelengthCount * (8 * samples * features * features
            + 2L * width * height * features));
        if (operations > MaximumImageDistortionOperations)
            throw new ArgumentOutOfRangeException(nameof(degree),
                $"Image distortion exceeds the {MaximumImageDistortionOperations:N0}-operation safety budget.");
        return effectiveGrid;
    }

    public static void ValidatePsfConfiguration(ImageSimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.PsfGridRows < 1 || config.PsfGridColumns < 1
            || checked((long)config.PsfGridRows * config.PsfGridColumns) > MaximumPsfGridPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(config), $"PSF field grid must contain at most {MaximumPsfGridPoints} points.");
        }

        if (config.PsfSize is < 1 or > 256 || config.NumRays is < 2 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "PSF size or pupil sampling is outside the supported range.");
        }

        ValidateDirectPsfWork(config.NumRays, PsfSamplingSize(config));
        if (PsfBasisOperations(config) > MaximumPsfBasisOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                $"PSF basis work exceeds the {MaximumPsfBasisOperations:N0}-operation safety budget.");
        }
        if (config.Components < 1
            || config.Components > Math.Min(MaximumPsfGridPoints, checked(config.PsfSize * config.PsfSize)))
        {
            throw new ArgumentOutOfRangeException(nameof(config), "EigenPSF component count is outside the supported range.");
        }
        if (PsfDecompositionOperations(config) > MaximumPsfDecompositionOperations)
            throw new ArgumentOutOfRangeException(nameof(config), "PSF decomposition exceeds the supported work budget.");
    }

    private static long PsfDecompositionOperations(ImageSimulationConfig config)
    {
        var fields = checked((long)config.PsfGridRows * config.PsfGridColumns);
        var features = checked((long)config.PsfSize * config.PsfSize);
        var components = Math.Min(config.Components, fields);
        // Gram products, Jacobi pivot scans and reconstruction each have an independent cost.
        return checked(fields * (fields + 1) / 2 * features
            + 100 * fields * fields * (fields * (fields - 1) / 2 + 2 * fields)
            + components * fields * features);
    }

    public static void ValidateConvolutionWork(int width, int height, int kernelWidth, int kernelHeight,
        int kernelCount = 1, int wavelengthCount = 1)
    {
        ValidateImageDimensions(width, height, "Convolution source");
        if (kernelWidth < 1 || kernelHeight < 1 || kernelCount < 1 || wavelengthCount < 1)
            throw new ArgumentOutOfRangeException(nameof(kernelWidth));
        var operations = checked((long)width * height * kernelWidth * kernelHeight * kernelCount * wavelengthCount);
        if (operations > MaximumImageSimulationConvolutionOperations)
            throw new ArgumentOutOfRangeException(nameof(kernelWidth), "Image convolution exceeds the supported work budget.");
    }

    private static long PsfBasisOperations(ImageSimulationConfig config) => checked(
        (long)config.NumRays * config.NumRays
        * PsfSamplingSize(config) * PsfSamplingSize(config)
        * config.PsfGridRows * config.PsfGridColumns);

    private static int PsfSamplingSize(ImageSimulationConfig config) =>
        string.Equals(config.AberrationMode, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.AberrationMode, "Geometric", StringComparison.OrdinalIgnoreCase)
            ? config.PsfSize : config.PsfSize | 1;
}
