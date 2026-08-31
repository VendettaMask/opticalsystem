namespace OptilandWorkbench.Core.Analysis;

public static class AnalysisResourceLimits
{
    public const int MaximumFftGridSize = 2_048;
    public const long MaximumImagePixels = 262_144;
    public const int MaximumImageDimension = 4_096;
    public const long MaximumDirectPsfOperations = 100_000_000;
    public const long MaximumPsfBasisOperations = 200_000_000;
    public const long MaximumImageSimulationPsfOperations = 300_000_000;
    public const int MaximumPsfGridPoints = 225;
    public const int MaximumAnalysisGridDimension = 1_024;
    public const long MaximumAnalysisGridCells = 1_048_576;
    public const long MaximumAggregateGridCells = 8_388_608;
    public const int MaximumAnalysisMapCount = 256;
    public const int MaximumWavefrontMapDimension = 512;
    public const int MaximumGridDistortionDimension = 512;
    public const long MaximumAnalysisRayWork = 100_000_000;
    public const long MaximumWavefrontInterpolationWork = 250_000_000;

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

        if (config.Oversampling is < 1 or > 16 || config.Padding is < 0 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Oversampling or padding is outside the supported range.");
        }

        var preparedWidth = checked((source.Width * config.Oversampling) + (2 * config.Padding));
        var preparedHeight = checked((source.Height * config.Oversampling) + (2 * config.Padding));
        ValidateImageDimensions(preparedWidth, preparedHeight, "Prepared image");

        var outputWidth = config.OutputWidth > 0 ? config.OutputWidth : preparedWidth;
        var outputHeight = config.OutputHeight > 0 ? config.OutputHeight : preparedHeight;
        ValidateImageDimensions(outputWidth, outputHeight, "Output image");

        if (config.WavelengthsMicrometers.Count is < 1 or > 16
            || config.WavelengthsMicrometers.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Image simulation wavelengths are invalid or exceed the supported count.");
        }

        ValidatePsfConfiguration(config);
        var basisOperations = PsfBasisOperations(config);
        if (checked(basisOperations * config.WavelengthsMicrometers.Count)
            > MaximumImageSimulationPsfOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                $"Image simulation PSF work exceeds the {MaximumImageSimulationPsfOperations:N0}-operation safety budget.");
        }
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

        ValidateDirectPsfWork(config.NumRays, config.PsfSize);
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
    }

    private static long PsfBasisOperations(ImageSimulationConfig config) => checked(
        (long)config.NumRays * config.NumRays
        * config.PsfSize * config.PsfSize
        * config.PsfGridRows * config.PsfGridColumns);
}
