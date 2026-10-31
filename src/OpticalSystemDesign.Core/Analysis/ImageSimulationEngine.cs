using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public enum ImageSimulationSourcePattern
{
    ColorChart,
    ResolutionTarget,
    DistortionGrid,
    SiemensStar
}

public sealed class ImageSimulationConfig
{
    public ImageSimulationSourcePattern SourcePattern { get; init; } = ImageSimulationSourcePattern.ColorChart;
    public string SourceFile { get; init; } = string.Empty;

    public RgbImage? SourceImage { get; init; }


    public string SourceMode { get; init; } = "Built-in";

    public double FieldHeight { get; init; }

    public int Oversampling { get; init; } = 1;

    public string SourceFlip { get; init; } = "None";

    public int SourceRotationDegrees { get; init; }

    public string ImageFlip { get; init; } = "None";

    public bool UseRelativeIllumination { get; init; } = true;

    public string AberrationMode { get; init; } = "Diffraction";

    public double SpatialCoherence { get; init; } = 0.5;

    public SpatialCoherenceFunction CoherenceFunction { get; init; } = SpatialCoherenceFunction.Uniform;

    public double CoherenceAlphaMillimeters { get; init; } = .001;

    public SpatialCoherenceMethod CoherenceMethod { get; init; } = SpatialCoherenceMethod.ExactSourcePairs;

    public double CoherenceFraction { get; init; } = .98;

    public int CoherenceSampling { get; init; } = 64;

    /// <summary>Zero keeps explicit support. Positive values select an even finite PSF window from
    /// the complete Fourier-conjugate intensity; PsfSize remains its maximum allowed size.</summary>
    public double PsfEnergyFraction { get; init; }

    /// <summary>Full active source width in image space, before oversampling and guard pixels; zero selects the existing pitch/field controls.</summary>
    public double FileSizeMillimeters { get; init; }

    public bool NormalizeImagePeak { get; init; }

    /// <summary>Optional object phase in radians, in the original source orientation; one or source-matched channels.</summary>
    public double[,,]? SourcePhaseRadians { get; init; }

    public string Reference { get; init; } = "chief";

    public string DisplayAs { get; init; } = "Both";

    public double FieldCenterX { get; init; }

    public double FieldCenterY { get; init; }

    public int ImageWidth { get; init; } = 64;

    public int ImageHeight { get; init; } = 48;

    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    public double PixelSizeMillimeters { get; init; }

    public bool UsePolarization { get; init; }

    public bool ApplyFixedApertures { get; init; } = true;

    public bool CompressFrame { get; init; }

    public string OutputFile { get; init; } = string.Empty;

    public IReadOnlyList<double> WavelengthsMicrometers { get; init; } = new[] { 0.65, 0.55, 0.45 };

    public int PsfGridRows { get; init; } = 3;

    public int PsfGridColumns { get; init; } = 3;

    public int PsfSize { get; init; } = 32;

    public int NumRays { get; init; } = 16;

    public int Components { get; init; } = 3;

    public int Padding { get; init; } = 16;

    public int DistortionGridSize { get; init; } = 9;

    public int DistortionPolynomialDegree { get; init; } = 5;
}

public sealed record RgbImage(double[,,] Values)
{
    public int Channels => Values.GetLength(0);

    public int Height => Values.GetLength(1);

    public int Width => Values.GetLength(2);

    public double this[int channel, int row, int column] => Values[channel, row, column];
}

public sealed record PsfBasisResult(
    double[][,] EigenPsfs,
    double[,,] CoefficientGrid,
    double[,] MeanPsf,
    int GridRows,
    int GridColumns,
    IReadOnlyList<string>? EffectiveModes = null,
    int GeometricFallbackCount = 0);

public sealed record ImageSimulationResult(
    RgbImage Source,
    RgbImage Simulated,
    ImageSimulationConfig Config,
    double MeanAbsoluteChange,
    double MaximumValue,
    string EffectiveAberrationMode = "Diffraction",
    int GeometricFallbackCount = 0,
    int PsfIntensityCorrectionCount = 0,
    double MaximumPsfNegativeMass = 0,
    CoherentImagingDiagnostics? CoherentImaging = null);

public static partial class ImageSimulationEngine
{
    public static RgbImage CreateSourceImage(
        ImageSimulationSourcePattern pattern,
        int width = 96,
        int height = 64)
    {
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Built-in source image");
        return pattern switch
        {
            ImageSimulationSourcePattern.ResolutionTarget => CreateResolutionTarget(width, height),
            ImageSimulationSourcePattern.DistortionGrid => CreateDistortionGrid(width, height),
            ImageSimulationSourcePattern.SiemensStar => CreateSiemensStar(width, height),
            _ => CreateTestChart(width, height)
        };
    }

    public static ImageSimulationResult Simulate(Optic optic, RgbImage source, ImageSimulationConfig? config = null)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        config ??= new ImageSimulationConfig();
        AnalysisResourceLimits.ValidateImageSimulation(source, config);
        if (config.UseRelativeIllumination) IlluminationMetrics.RequireRelativeIllumination(optic);
        var partialCoherence = NormalizeAberrationMode(config.AberrationMode) == "Partially Coherent";
        var dimensions = AnalysisResourceLimits.ResolveImageSimulationDimensions(source.Width, source.Height, config);

        if (config.WavelengthsMicrometers.Count == 0)
        {
            throw new ArgumentException("At least one wavelength is required.", nameof(config));
        }

        var workingOptic = PrepareOptic(optic, config);
        var orientedSource = TransformImage(
            source.Values,
            config.SourceFlip,
            config.SourceRotationDegrees);
        var oversampled = NearestNeighborOversample(orientedSource, config.Oversampling);
        var prepared = ZeroPad(oversampled, config.Padding);
        var sourcePhase = partialCoherence && config.SourcePhaseRadians is not null
            ? NearestNeighborOversample(TransformImage(config.SourcePhaseRadians,
                config.SourceFlip, config.SourceRotationDegrees), config.Oversampling)
            : null;
        var (halfFieldX, halfFieldY) = ResolveNormalizedFieldExtent(
            workingOptic,
            config.FieldHeight,
            oversampled.GetLength(2),
            oversampled.GetLength(1));
        var referenceWavelength = ResolveReferenceWavelength(workingOptic, config);
        var pixelPitch = partialCoherence && config.FileSizeMillimeters > 0
            ? config.FileSizeMillimeters / oversampled.GetLength(2)
            : config.PixelSizeMillimeters > 0
            ? config.PixelSizeMillimeters
            : EstimateDetectorPixelPitch(
                workingOptic,
                referenceWavelength,
                halfFieldX,
                halfFieldY,
                oversampled.GetLength(2),
                oversampled.GetLength(1),
                config.FieldCenterX,
                config.FieldCenterY);
        var output = new double[config.WavelengthsMicrometers.Count, prepared.GetLength(1), prepared.GetLength(2)];
        var effectiveModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackCount = 0;
        var correctionCount = 0;
        var maximumNegativeMass = 0.0;
        var minimumCapturedFraction = 1.0;
        var coherentFourierGrid = 0;
        var coherentWindows = new List<CoherentKernelWindow>();
        for (var channel = 0; channel < config.WavelengthsMicrometers.Count; channel++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var wavelength = ResolveWavelength(workingOptic, config.WavelengthsMicrometers[channel]);
            if (partialCoherence)
            {
                var kernel = DiffractionEngine.ComputeCoherentImageKernel(workingOptic,
                    (config.FieldCenterX, config.FieldCenterY), wavelength, config.NumRays, config.PsfSize, pixelPitch,
                    fourierConjugateSampling: config.CoherenceFunction != SpatialCoherenceFunction.Uniform,
                    energyWindowFraction: config.PsfEnergyFraction);
                var coherenceSampling = config.CoherenceMethod == SpatialCoherenceMethod.FourierModes
                    ? MutualIntensityEngine.ResolveSpatialCoherenceSampling(config.CoherenceSampling, kernel.Values.GetLength(1), kernel.Values.GetLength(0)) : config.CoherenceSampling;
                coherentWindows.Add(new CoherentKernelWindow(wavelength.Micrometers, kernel.Values.GetLength(0),
                    kernel.EnergyWindowFraction, kernel.SelectionCapturedPowerFraction, kernel.CapturedPowerFraction,
                    config.CoherenceMethod == SpatialCoherenceMethod.FourierModes ? coherenceSampling : 0,
                    kernel.MaximumIntensityPixelPitchMillimeters));
                var intensity = SliceChannel(oversampled, Math.Min(channel, source.Channels - 1));
                if (config.UseRelativeIllumination)
                    ApplyRelativeIllumination(intensity, workingOptic, wavelength, halfFieldX, halfFieldY,
                        Math.Max(5, Math.Min(33, config.NumRays)), config.FieldCenterX, config.FieldCenterY, false);
                var phase = sourcePhase is null ? null
                    : PadChannel(SliceChannel(sourcePhase, Math.Min(channel, sourcePhase.GetLength(0) - 1)), config.Padding);
                var paddedIntensity = PadChannel(intensity, config.Padding);
                var image = config.CoherenceFunction == SpatialCoherenceFunction.Uniform
                    ? MutualIntensityEngine.PropagateUniformCoherence(paddedIntensity, kernel.Values, config.SpatialCoherence, phase)
                    : MutualIntensityEngine.PropagateSpatialCoherence(paddedIntensity, kernel.Values,
                        config.CoherenceFunction, config.CoherenceAlphaMillimeters, pixelPitch, phase,
                        config.CoherenceMethod, config.CoherenceFraction, coherenceSampling);
                if (config.NormalizeImagePeak)
                {
                    var peak = image.Cast<double>().Max();
                    if (peak > 0) for (var y = 0; y < image.GetLength(0); y++)
                        for (var x = 0; x < image.GetLength(1); x++) image[y, x] /= peak;
                }
                CopyChannel(output, channel, image);
                effectiveModes.Add("Partially Coherent (Scalar Isoplanatic)");
                minimumCapturedFraction = Math.Min(minimumCapturedFraction, kernel.CapturedPowerFraction);
                coherentFourierGrid = Math.Max(coherentFourierGrid, kernel.FourierGridSize);
                continue;
            }
            var basis = GenerateBasis(
                workingOptic,
                wavelength,
                config,
                halfFieldX,
                halfFieldY,
                pixelPitch);
            foreach (var mode in basis.EffectiveModes ?? Array.Empty<string>())
            {
                effectiveModes.Add(mode);
            }

            fallbackCount += basis.GeometricFallbackCount;
            var coefficientMaps = ResizeCoefficientMaps(
                basis.CoefficientGrid,
                prepared.GetLength(1),
                prepared.GetLength(2),
                config.Padding);
            var sourceChannel = SliceChannel(oversampled, Math.Min(channel, source.Channels - 1));
            if (config.UseRelativeIllumination)
            {
                ApplyRelativeIllumination(
                    sourceChannel,
                    workingOptic,
                    wavelength,
                    halfFieldX,
                    halfFieldY,
                    Math.Max(5, Math.Min(33, config.NumRays)),
                    config.FieldCenterX,
                    config.FieldCenterY,
                    config.UsePolarization);
            }

            var convolution = ConvolvePsfIntensity(
                PadChannel(sourceChannel, config.Padding),
                basis.EigenPsfs,
                coefficientMaps,
                basis.MeanPsf);
            var blurred = convolution.Values;
            correctionCount += convolution.CorrectedKernelCount;
            maximumNegativeMass = Math.Max(maximumNegativeMass, convolution.MaximumNegativeMass);
            var distortionGrid = GenerateDistortionGrid(
                workingOptic,
                wavelength,
                blurred.GetLength(0),
                blurred.GetLength(1),
                config.DistortionGridSize,
                config.DistortionPolynomialDegree,
                halfFieldX,
                halfFieldY,
                referenceWavelength,
                config.FieldCenterX,
                config.FieldCenterY,
                config.Padding);
            var warped = WarpImage(blurred, distortionGrid);
            CopyChannel(output, channel, warped);
        }

        for (var channel = 0; channel < output.GetLength(0); channel++)
        {
            for (var row = 0; row < output.GetLength(1); row++)
            {
                for (var column = 0; column < output.GetLength(2); column++)
                {
                    if (!double.IsFinite(output[channel, row, column]) || output[channel, row, column] < 0)
                        throw new AnalysisDataUnavailableException("Image Simulation",
                            "the intensity image must remain finite and nonnegative");
                }
            }
        }

        var effectiveMode = effectiveModes.Count == 0
            ? NormalizeAberrationMode(config.AberrationMode)
            : string.Join(" + ", effectiveModes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        var orientedOutput = TransformImage(output, config.ImageFlip, 0);
        var outputWidth = dimensions.OutputWidth;
        var outputHeight = dimensions.OutputHeight;
        var displayedSource = ResizeRgb(prepared, outputHeight, outputWidth);
        var displayedOutput = ResizeRgb(orientedOutput, outputHeight, outputWidth);
        var maximum = 0.0;
        var totalChange = 0.0;
        for (var channel = 0; channel < displayedOutput.GetLength(0); channel++)
            for (var row = 0; row < outputHeight; row++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var column = 0; column < outputWidth; column++)
                {
                    var value = displayedOutput[channel, row, column];
                    maximum = Math.Max(maximum, value);
                    totalChange += Math.Abs(value - displayedSource[Math.Min(channel, displayedSource.GetLength(0) - 1), row, column]);
                }
            }
        return new ImageSimulationResult(
            new RgbImage(displayedSource),
            new RgbImage(displayedOutput),
            config,
            totalChange / displayedOutput.Length,
            maximum,
            effectiveMode,
            fallbackCount,
            correctionCount,
            maximumNegativeMass,
            partialCoherence ? new CoherentImagingDiagnostics(config.SpatialCoherence, coherentFourierGrid,
                pixelPitch, minimumCapturedFraction, sourcePhase is null ? "Zero object phase" : "Supplied object phase in radians")
            {
                Function = config.CoherenceFunction,
                AlphaMillimeters = config.CoherenceAlphaMillimeters,
                Method = config.CoherenceMethod,
                Fraction = config.CoherenceFraction,
                CoherenceSampling = config.CoherenceSampling,
                PsfEnergyFraction = config.PsfEnergyFraction,
                KernelWindows = coherentWindows.ToArray(),
                FileSizeMillimeters = config.FileSizeMillimeters,
                NormalizedImagePeak = config.NormalizeImagePeak
            } : null);
    }

    public static RgbImage CreateTestChart(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Test chart");
        var values = new double[3, height, width];
        var patches = new[]
        {
            (0.85, 0.12, 0.10), (0.12, 0.62, 0.22), (0.10, 0.30, 0.88),
            (0.95, 0.72, 0.10), (0.72, 0.16, 0.78), (0.08, 0.72, 0.78)
        };
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var checker = ((row / 4) + (column / 4)) % 2 == 0 ? 0.08 : 0.92;
                var color = row < height / 2
                    ? patches[Math.Min(patches.Length - 1, column * patches.Length / width)]
                    : (checker, checker, checker);
                values[0, row, column] = color.Item1;
                values[1, row, column] = color.Item2;
                values[2, row, column] = color.Item3;
            }
        }

        var centerX = width / 2;
        var centerY = height * 3 / 4;
        var radius = Math.Min(width, height) / 7.0;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var distance = Math.Sqrt(((column - centerX) * (column - centerX)) + ((row - centerY) * (row - centerY)));
                if (Math.Abs(distance - radius) <= 1.2 || Math.Abs(column - centerX) <= 1 || Math.Abs(row - centerY) <= 1)
                {
                    values[0, row, column] = 1;
                    values[1, row, column] = 1;
                    values[2, row, column] = 1;
                }
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateResolutionTarget(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Resolution target");
        var values = new double[3, height, width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var band = Math.Min(3, column * 4 / width);
                var period = Math.Max(2, 8 - (band * 2));
                var verticalBars = ((column / period) % 2) == 0;
                var horizontalBars = ((row / period) % 2) == 0;
                var value = row < height / 2
                    ? verticalBars ? 0.06 : 0.94
                    : horizontalBars ? 0.06 : 0.94;
                if (Math.Abs(column - (width / 2)) <= 1 || Math.Abs(row - (height / 2)) <= 1)
                {
                    value = 0.5;
                }

                SetRgb(values, row, column, value, value, value);
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateDistortionGrid(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Distortion grid");
        var values = new double[3, height, width];
        var spacing = Math.Max(4, Math.Min(width, height) / 8);
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var onGrid = row % spacing <= 1 || column % spacing <= 1;
                var value = onGrid ? 0.05 : 0.96;
                var centerLine = Math.Abs(column - (width / 2)) <= 1 || Math.Abs(row - (height / 2)) <= 1;
                SetRgb(
                    values,
                    row,
                    column,
                    centerLine ? 0.82 : value,
                    centerLine ? 0.12 : value,
                    centerLine ? 0.10 : value);
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateSiemensStar(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Siemens star");
        var values = new double[3, height, width];
        var centerX = (width - 1) / 2.0;
        var centerY = (height - 1) / 2.0;
        var radius = Math.Min(width, height) * 0.44;
        const int sectorPairs = 18;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var dx = column - centerX;
                var dy = row - centerY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var angle = Math.Atan2(dy, dx);
                var value = distance <= radius
                    ? Math.Sin(angle * sectorPairs) >= 0 ? 0.04 : 0.96
                    : 0.82;
                if (Math.Abs(distance - radius) <= 1)
                {
                    value = 0.35;
                }

                SetRgb(values, row, column, value, value, value);
            }
        }

        return new RgbImage(values);
    }

    private static void SetRgb(double[,,] values, int row, int column, double red, double green, double blue)
    {
        values[0, row, column] = red;
        values[1, row, column] = green;
        values[2, row, column] = blue;
    }

    public static PsfBasisResult GenerateBasis(Optic optic, Wavelength wavelength, ImageSimulationConfig config)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        AnalysisResourceLimits.ValidatePsfConfiguration(config);
        optic = PrepareOptic(optic, config, convertRealImageFields: false);
        AnalysisResourceLimits.ValidateImageDimensions(
            Math.Max(1, config.ImageWidth),
            Math.Max(1, config.ImageHeight),
            "PSF basis image field");
        var (halfFieldX, halfFieldY) = ResolveNormalizedFieldExtent(
            optic,
            config.FieldHeight,
            Math.Max(1, config.ImageWidth),
            Math.Max(1, config.ImageHeight));
        var pixelPitch = config.PixelSizeMillimeters > 0
            ? config.PixelSizeMillimeters
            : EstimateDetectorPixelPitch(
                optic,
                wavelength,
                halfFieldX,
                halfFieldY,
                Math.Max(1, config.ImageWidth),
                Math.Max(1, config.ImageHeight),
                config.FieldCenterX,
                config.FieldCenterY);
        return GenerateBasis(optic, wavelength, config, halfFieldX, halfFieldY, pixelPitch);
    }

    private static Optic PrepareOptic(Optic optic, ImageSimulationConfig config, bool convertRealImageFields = true)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (config.ApplyFixedApertures && (!convertRealImageFields || optic.FieldDefinition != FieldDefinitionKind.RealImageHeight))
            return optic;

        // Keep runtime catalogs/backends, but never share trace cache entries with a changed analysis optic.
        var working = optic.CreateMeritEvaluationCopy();
        if (convertRealImageFields && working.FieldDefinition == FieldDefinitionKind.RealImageHeight)
            working.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        if (!config.ApplyFixedApertures)
        {
            foreach (var surface in working.SurfaceGroup.Items.Where(surface => !surface.IsStop))
                surface.PhysicalAperture = null;
        }
        return working;
    }

    private static PsfBasisResult GenerateBasis(
        Optic optic,
        Wavelength wavelength,
        ImageSimulationConfig config,
        double halfFieldX,
        double halfFieldY,
        double pixelPitchMillimeters)
    {
        var rows = config.PsfGridRows;
        var columns = config.PsfGridColumns;
        var psfCount = rows * columns;
        var featureCount = config.PsfSize * config.PsfSize;
        var psfs = new double[psfCount, featureCount];
        var effectiveModes = new string[psfCount];
        var fallbackCount = 0;
        var sample = 0;
        for (var row = 0; row < rows; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var minimumY = Math.Max(-1, config.FieldCenterY - halfFieldY);
            var maximumY = Math.Min(1, config.FieldCenterY + halfFieldY);
            var hy = rows == 1
                ? (minimumY + maximumY) / 2
                : maximumY + ((minimumY - maximumY) * row / (rows - 1));
            for (var column = 0; column < columns; column++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var minimumX = Math.Max(-1, config.FieldCenterX - halfFieldX);
                var maximumX = Math.Min(1, config.FieldCenterX + halfFieldX);
                var hx = columns == 1
                    ? (minimumX + maximumX) / 2
                    : minimumX + ((maximumX - minimumX) * column / (columns - 1));
                var requestedMode = NormalizeAberrationMode(config.AberrationMode);
                if (requestedMode == "Partially Coherent")
                    throw new AnalysisDataUnavailableException("EigenPSF Basis", "an intensity-only PCA basis cannot represent a complex coherent response; use image simulation's mutual-intensity path");
                (double[,] Psf, double RmsRadiusMillimeters)? geometric = requestedMode is "Geometric" or "Diffraction"
                    ? ComputeGeometricPsf(
                        optic,
                        (hx, hy),
                        wavelength,
                        config.NumRays,
                        config.PsfSize,
                        pixelPitchMillimeters,
                        config.Reference,
                        config.UsePolarization)
                    : null;
                var actualMode = requestedMode;
                double[,] values;
                if (requestedMode == "None")
                {
                    values = DeltaPsf(config.PsfSize);
                }
                else if (requestedMode == "Geometric")
                {
                    values = geometric!.Value.Psf;
                }
                else if (ShouldFallbackToGeometric(
                    geometric!.Value.RmsRadiusMillimeters,
                    1.22 * wavelength.Micrometers * 1e-3
                        * DiffractionEngine.WorkingFNumber(optic, (hx, hy), wavelength, aimAtStop: optic.RayAimingEnabled)))
                {
                    values = geometric.Value.Psf;
                    actualMode = "Geometric";
                    fallbackCount++;
                }
                else
                {
                    try
                    {
                        (double X, double Y)? imageCenterOffset = string.Equals(config.Reference, "centroid", StringComparison.OrdinalIgnoreCase)
                            ? DiffractionEngine.HuygensGeometricCenterOffset(optic, (hx, hy), [wavelength],
                                wavelength, Math.Max(2, config.NumRays), optic.RayAimingEnabled)
                            : null;
                        // The physical grid has increasing +Y and zero at size/2.
                        // An odd grid includes both edges of an even detector
                        // window without changing the physical footprint/reference.
                        var physicalSize = config.PsfSize | 1;
                        var physical = DiffractionEngine.ComputeHuygensPsf(
                            optic,
                            (hx, hy),
                            wavelength,
                            Math.Max(2, config.NumRays),
                            physicalSize,
                            pixelPitchMillimeters, usePolarization: config.UsePolarization,
                            aimAtStop: optic.RayAimingEnabled, imageCenterOffset: imageCenterOffset).Values;
                        values = new double[config.PsfSize, config.PsfSize];
                        var physicalCenter = physicalSize / 2;
                        var anchor = (config.PsfSize - 1) / 2;
                        for (var y = 0; y < config.PsfSize; y++)
                            for (var x = 0; x < config.PsfSize; x++)
                                values[y, x] = physical[physicalCenter + anchor - y, physicalCenter + x - anchor];
                    }
                    catch (Exception exception) when (!config.UsePolarization && (exception is InvalidOperationException
                        or ArgumentException
                        or ArithmeticException))
                    {
                        values = geometric.Value.Psf;
                        actualMode = "Geometric";
                        fallbackCount++;
                    }
                }

                effectiveModes[sample] = actualMode;
                var sum = values.Cast<double>().Where(value => double.IsFinite(value) && value > 0).Sum();
                if (!double.IsFinite(sum) || sum <= 0)
                    throw new AnalysisDataUnavailableException("Image Simulation PSF",
                        FormattableString.Invariant($"no positive PSF power was sampled; enlarge the PSF window or increase pupil sampling (normalized field X={hx:G17}, Y={hy:G17}; wavelength={wavelength.Micrometers:G17} µm)"));
                for (var y = 0; y < config.PsfSize; y++)
                {
                    for (var x = 0; x < config.PsfSize; x++)
                    {
                        var value = double.IsFinite(values[y, x]) ? Math.Max(0, values[y, x]) : 0;
                        psfs[sample, (y * config.PsfSize) + x] = value / sum;
                    }
                }

                sample++;
            }
        }

        var mean = new double[featureCount];
        for (var feature = 0; feature < featureCount; feature++)
        {
            if ((feature & 255) == 0) ComputationCancellation.ThrowIfCancellationRequested();
            for (sample = 0; sample < psfCount; sample++)
            {
                mean[feature] += psfs[sample, feature] / psfCount;
            }

            for (sample = 0; sample < psfCount; sample++)
            {
                psfs[sample, feature] -= mean[feature];
            }
        }

        var gram = new double[psfCount, psfCount];
        for (var left = 0; left < psfCount; left++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var right = left; right < psfCount; right++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var value = 0.0;
                for (var feature = 0; feature < featureCount; feature++)
                {
                    value += psfs[left, feature] * psfs[right, feature];
                }

                gram[left, right] = value;
                gram[right, left] = value;
            }
        }

        var eigen = SymmetricEigen(gram);
        var componentCount = Math.Min(Math.Max(1, config.Components), psfCount);
        var eigenPsfs = new double[componentCount][,];
        var coefficients = new double[componentCount, rows, columns];
        for (var component = 0; component < componentCount; component++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var singular = Math.Sqrt(Math.Max(0, eigen.Values[component]));
            var basis = new double[config.PsfSize, config.PsfSize];
            if (singular > 1e-15)
            {
                for (var feature = 0; feature < featureCount; feature++)
                {
                    var value = 0.0;
                    for (sample = 0; sample < psfCount; sample++)
                    {
                        value += psfs[sample, feature] * eigen.Vectors[sample, component];
                    }

                    basis[feature / config.PsfSize, feature % config.PsfSize] = value / singular;
                }
            }

            for (sample = 0; sample < psfCount; sample++)
            {
                coefficients[component, sample / columns, sample % columns] = eigen.Vectors[sample, component] * singular;
            }

            eigenPsfs[component] = basis;
        }

        var meanPsf = new double[config.PsfSize, config.PsfSize];
        for (var feature = 0; feature < featureCount; feature++)
        {
            meanPsf[feature / config.PsfSize, feature % config.PsfSize] = mean[feature];
        }

        return new PsfBasisResult(
            eigenPsfs,
            coefficients,
            meanPsf,
            rows,
            columns,
            effectiveModes,
            fallbackCount);
    }

    internal static bool ShouldFallbackToGeometric(
        double geometricRmsRadiusMillimeters,
        double airyRadiusMillimeters)
    {
        return !double.IsFinite(geometricRmsRadiusMillimeters)
            || !double.IsFinite(airyRadiusMillimeters)
            || airyRadiusMillimeters <= 0
            || geometricRmsRadiusMillimeters > 20 * airyRadiusMillimeters;
    }

    private static string NormalizeAberrationMode(string mode)
    {
        if (string.Equals(mode, "Partially Coherent", StringComparison.OrdinalIgnoreCase))
            return "Partially Coherent";

        if (string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        if (string.Equals(mode, "Geometric", StringComparison.OrdinalIgnoreCase))
        {
            return "Geometric";
        }

        return "Diffraction";
    }

    private static (double[,] Psf, double RmsRadiusMillimeters) ComputeGeometricPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int sampleParameter,
        int size,
        double pixelPitchMillimeters,
        string reference,
        bool usePolarization)
    {
        var rays = GeometricPsfRays(optic, field, wavelength, sampleParameter, reference, usePolarization);
        var psf = new double[size, size];
        // Match the discrete delta and same-convolution anchor, including even-sized kernels.
        var center = (size - 1) / 2;
        var totalWeight = 0.0;
        var secondMoment = 0.0;
        foreach (var ray in rays)
        {
            var weight = Math.Max(0, ray.Intensity);
            if (weight <= 0 || !double.IsFinite(ray.X) || !double.IsFinite(ray.Y))
            {
                continue;
            }

            var x = (int)Math.Round(center + (ray.X / pixelPitchMillimeters));
            var y = (int)Math.Round(center - (ray.Y / pixelPitchMillimeters));
            if (x >= 0 && x < size && y >= 0 && y < size)
            {
                psf[y, x] += weight;
            }

            totalWeight += weight;
            secondMoment += weight * ((ray.X * ray.X) + (ray.Y * ray.Y));
        }

        var rms = totalWeight <= 0
            ? double.PositiveInfinity
            : Math.Sqrt(secondMoment / totalWeight);
        return (psf, rms);
    }

    private static IReadOnlyList<SpotRayData> GeometricPsfRays(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int sampleParameter, string reference, bool usePolarization)
    {
        if (usePolarization)
        {
            var image = optic.SurfaceGroup.Items.Last();
            var descriptor = ImageSpaceAnalysisSupport.CoordinateDescriptor(optic);
            SpotRayData? Trace(RealRay ray)
            {
                var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(ray,
                    stopAtIncidentImage: true, usePolarization: true);
                if (!diagnostic.InsideAllApertures || diagnostic.UnclippedImage is not { } hit)
                    return null;
                var power = diagnostic.PolarizationWeightedIntensity
                    ?? throw new InvalidOperationException("Missing polarized image power.");
                return ImageSpaceAnalysisSupport.ToImageSpaceRayData(optic, hit, image, descriptor)
                    with
                { Intensity = power };
            }

            var pupils = SpotAnalysisEngine.CreatePupilSamples(Math.Max(2, sampleParameter), "uniform");
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                field.Hx, field.Hy, wavelength.Micrometers, pupils, aimAtStop: optic.RayAimingEnabled);
            var rays = new List<SpotRayData>();
            foreach (var ray in bundle.Rays)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                if (Trace(ray) is { Intensity: > 0 } sample && double.IsFinite(sample.Intensity))
                    rays.Add(sample);
            }
            var center = SpotAnalysisEngine.Centroid(rays);
            if (!string.Equals(reference, "centroid", StringComparison.OrdinalIgnoreCase))
            {
                var chief = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy,
                    0, 0, wavelength.Micrometers, optic.RayAimingEnabled).Rays.Single();
                if (Trace(chief) is { Intensity: > 0 } point) center = (point.X, point.Y);
            }
            return rays.Select(ray => ray with { X = ray.X - center.X, Y = ray.Y - center.Y }).ToArray();
        }

        var spot = SpotAnalysisEngine.Generate(
            optic,
            new[] { field },
            new[] { wavelength },
            Math.Max(2, sampleParameter),
            "uniform",
            reference: string.Equals(reference, "centroid", StringComparison.OrdinalIgnoreCase)
                ? "centroid"
                : "chief",
            aimAtStop: optic.RayAimingEnabled);
        return spot.Fields.FirstOrDefault()?.Wavelengths.FirstOrDefault()?.Rays
            ?? Array.Empty<SpotRayData>();
    }

    private static double[,] DeltaPsf(int size)
    {
        var psf = new double[size, size];
        psf[(size - 1) / 2, (size - 1) / 2] = 1;
        return psf;
    }

    private static (double HalfX, double HalfY) ResolveNormalizedFieldExtent(
        Optic optic,
        double fieldHeight,
        int width,
        int height)
    {
        var maximumField = FieldCoordinates.MaximumRadius(optic.Fields);
        var halfY = fieldHeight > 0 && maximumField > 1e-12
            ? Math.Clamp(fieldHeight / (2 * maximumField), 1e-9, 1)
            : 1;
        var halfX = Math.Clamp(halfY * width / Math.Max(1.0, height), 1e-9, 1);
        return (halfX, halfY);
    }

    private static Wavelength ResolveReferenceWavelength(Optic optic, ImageSimulationConfig config)
    {
        var primary = optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? optic.Wavelengths.FirstOrDefault();
        var micrometers = primary?.Micrometers
            ?? config.WavelengthsMicrometers[config.WavelengthsMicrometers.Count / 2];
        return ResolveWavelength(optic, micrometers);
    }

    private static double EstimateDetectorPixelPitch(
        Optic optic,
        Wavelength wavelength,
        double halfFieldX,
        double halfFieldY,
        int width,
        int height,
        double fieldCenterX,
        double fieldCenterY)
    {
        try
        {
            var imageCoordinates = optic.SurfaceGroup.Items[^1].CoordinateSystem;
            (double X, double Y)? LocalHit(double hx, double hy)
            {
                var hit = optic.TraceGenericFinalSample(hx, hy, 0, 0, wavelength.Micrometers);
                if (hit is null) return null;
                var local = imageCoordinates.ToLocalPoint(hit.Position);
                return (local.X, local.Y);
            }
            var left = LocalHit(Math.Max(-1, fieldCenterX - halfFieldX), fieldCenterY);
            var right = LocalHit(Math.Min(1, fieldCenterX + halfFieldX), fieldCenterY);
            var bottom = LocalHit(fieldCenterX, Math.Max(-1, fieldCenterY - halfFieldY));
            var top = LocalHit(fieldCenterX, Math.Min(1, fieldCenterY + halfFieldY));
            var pitchX = left.HasValue && right.HasValue
                ? Math.Abs(right.Value.X - left.Value.X) / Math.Max(1, width - 1)
                : 0;
            var pitchY = bottom.HasValue && top.HasValue
                ? Math.Abs(top.Value.Y - bottom.Value.Y) / Math.Max(1, height - 1)
                : 0;
            var pitch = new[] { pitchX, pitchY }
                .Where(value => double.IsFinite(value) && value > 1e-12)
                .DefaultIfEmpty(0)
                .Average();
            if (pitch > 0)
            {
                return pitch;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or ArithmeticException)
        {
        }

        var fNumber = Math.Max(1e-6, DiffractionEngine.WorkingFNumber(optic, (0, 0), wavelength, aimAtStop: optic.RayAimingEnabled));
        return Math.Max(1e-6, wavelength.Micrometers * 1e-3 * fNumber / 2);
    }

    internal static void ApplyRelativeIllumination(
        double[,] source,
        Optic optic,
        Wavelength wavelength,
        double halfFieldX,
        double halfFieldY,
        int rayDensity,
        double fieldCenterX,
        double fieldCenterY,
        bool usePolarization = false)
    {
        IlluminationMetrics.RequireRelativeIllumination(optic);
        const int gridSize = 9;
        var fieldIndices = new int[gridSize, gridSize];
        var fields = new Dictionary<(double X, double Y), int>();
        for (var row = 0; row < gridSize; row++)
        {
            var maximumY = Math.Min(1, fieldCenterY + halfFieldY);
            var minimumY = Math.Max(-1, fieldCenterY - halfFieldY);
            var hy = maximumY + ((minimumY - maximumY) * row / (gridSize - 1.0));
            for (var column = 0; column < gridSize; column++)
            {
                var minimumX = Math.Max(-1, fieldCenterX - halfFieldX);
                var maximumX = Math.Min(1, fieldCenterX + halfFieldX);
                var hx = minimumX + ((maximumX - minimumX) * column / (gridSize - 1.0));
                if (!fields.TryGetValue((hx, hy), out var index))
                {
                    index = fields.Count;
                    fields.Add((hx, hy), index);
                }
                fieldIndices[row, column] = index;
            }
        }

        var samples = IlluminationMetrics.EvaluateFields(optic,
            fields.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToArray(), wavelength.Micrometers, rayDensity,
            usePolarization: usePolarization);
        var maximum = samples.Max(sample => sample.ProjectedCosineArea);

        if (maximum <= 0)
        {
            Array.Clear(source);
            return;
        }

        var illumination = new double[gridSize, gridSize];
        for (var row = 0; row < gridSize; row++)
            for (var column = 0; column < gridSize; column++)
                illumination[row, column] = samples[fieldIndices[row, column]].ProjectedCosineArea;

        for (var row = 0; row < source.GetLength(0); row++)
        {
            var y = row * (gridSize - 1.0) / Math.Max(1, source.GetLength(0) - 1);
            for (var column = 0; column < source.GetLength(1); column++)
            {
                var x = column * (gridSize - 1.0) / Math.Max(1, source.GetLength(1) - 1);
                source[row, column] *= Bilinear(illumination, y, x) / maximum;
            }
        }
    }

    public static double[,] SpatiallyVariableConvolution(
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
        if (coefficientMaps.GetLength(0) != eigenPsfs.Count
            || coefficientMaps.GetLength(1) != source.GetLength(0)
            || coefficientMaps.GetLength(2) != source.GetLength(1))
            throw new ArgumentException("Coefficient maps must match the source and EigenPSF count.", nameof(coefficientMaps));
        var largestHeight = Math.Max(meanPsf.GetLength(0), eigenPsfs.Count == 0 ? 0 : eigenPsfs.Max(psf => psf.GetLength(0)));
        var largestWidth = Math.Max(meanPsf.GetLength(1), eigenPsfs.Count == 0 ? 0 : eigenPsfs.Max(psf => psf.GetLength(1)));
        AnalysisResourceLimits.ValidateConvolutionWork(source.GetLength(1), source.GetLength(0),
            largestWidth, largestHeight, 1 + eigenPsfs.Count);
        var output = ConvolveSame(source, meanPsf);
        for (var component = 0; component < eigenPsfs.Count; component++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var weighted = new double[source.GetLength(0), source.GetLength(1)];
            for (var row = 0; row < source.GetLength(0); row++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var column = 0; column < source.GetLength(1); column++)
                {
                    weighted[row, column] = source[row, column] * coefficientMaps[component, row, column];
                }
            }

            var convolved = ConvolveSame(weighted, eigenPsfs[component]);
            for (var row = 0; row < source.GetLength(0); row++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var column = 0; column < source.GetLength(1); column++)
                {
                    output[row, column] += convolved[row, column];
                }
            }
        }

        return output;
    }

    public static (double X, double Y)[,] GenerateDistortionGrid(
        Optic optic,
        Wavelength wavelength,
        int height,
        int width,
        int numGridPoints = 25,
        int degree = 5,
        double halfFieldX = 1,
        double halfFieldY = 1,
        Wavelength? referenceWavelength = null,
        double fieldCenterX = 0,
        double fieldCenterY = 0,
        int padding = 0)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        numGridPoints = AnalysisResourceLimits.ValidateImageDistortion(width, height, numGridPoints, degree);
        if (padding < 0 || 2L * padding >= Math.Min(width, height))
            throw new ArgumentOutOfRangeException(nameof(padding), "The guard band must leave a nonempty active image.");
        var activeWidth = width - 2 * padding;
        var activeHeight = height - 2 * padding;
        optic = RealImageFieldConversion.ForImageSimulation(optic);
        halfFieldX = Math.Clamp(Math.Abs(halfFieldX), 1e-9, 1);
        halfFieldY = Math.Clamp(Math.Abs(halfFieldY), 1e-9, 1);
        referenceWavelength ??= wavelength;
        var imageCoordinates = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var count = checked(numGridPoints * numGridPoints);
        var realX = new double[count];
        var realY = new double[count];
        var referenceX = new double[count];
        var referenceY = new double[count];
        var fieldX = new double[count];
        var fieldY = new double[count];
        var minimumFieldX = Math.Max(-1, fieldCenterX - halfFieldX);
        var maximumFieldX = Math.Min(1, fieldCenterX + halfFieldX);
        var minimumFieldY = Math.Max(-1, fieldCenterY - halfFieldY);
        var maximumFieldY = Math.Min(1, fieldCenterY + halfFieldY);
        var index = 0;
        for (var row = 0; row < numGridPoints; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var normalizedY = -1.0 + (2.0 * row / (numGridPoints - 1));
            var hy = minimumFieldY + ((maximumFieldY - minimumFieldY) * row / (numGridPoints - 1));
            for (var column = 0; column < numGridPoints; column++)
            {
                var normalizedX = -1.0 + (2.0 * column / (numGridPoints - 1));
                var hx = minimumFieldX + ((maximumFieldX - minimumFieldX) * column / (numGridPoints - 1));
                var final = optic.TraceGenericFinalSample(hx, hy, 0, 0, wavelength.Micrometers)
                    ?? throw new InvalidOperationException("Distortion-grid ray did not reach the image surface.");
                var reference = optic.TraceGenericFinalSample(
                    hx,
                    hy,
                    0,
                    0,
                    referenceWavelength.Micrometers)
                    ?? throw new InvalidOperationException("Reference distortion-grid ray did not reach the image surface.");
                // The detector grid and source field rotate with their frames, not global X/Y.
                var local = imageCoordinates.ToLocalPoint(final.Position);
                var localReference = imageCoordinates.ToLocalPoint(reference.Position);
                realX[index] = local.X;
                realY[index] = local.Y;
                referenceX[index] = localReference.X;
                referenceY[index] = localReference.Y;
                fieldX[index] = normalizedX;
                fieldY[index] = normalizedY;
                index++;
            }
        }

        // Fit in a centered unit box: absolute world coordinates make high-order
        // monomials nearly parallel even after max-absolute-value scaling.
        var centerX = realX.Min() + (realX.Max() - realX.Min()) / 2;
        var centerY = realY.Min() + (realY.Max() - realY.Min()) / 2;
        var scaleX = (realX.Max() - realX.Min()) / 2;
        var scaleY = (realY.Max() - realY.Min()) / 2;
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY)
            || !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            throw new AnalysisDataUnavailableException("Image Simulation Distortion",
                "a finite, nondegenerate two-dimensional chief-ray mapping is required");
        var featureCount = checked((degree + 1) * (degree + 2) / 2);
        var design = new double[count, featureCount];
        for (index = 0; index < count; index++)
        {
            FillPolynomialFeatures(design, index, (realX[index] - centerX) / scaleX, (realY[index] - centerY) / scaleY, degree);
        }

        double[] coefficientX;
        double[] coefficientY;
        try
        {
            coefficientX = QrLeastSquares.Solve(design, fieldX, requireFullRank: true);
            coefficientY = QrLeastSquares.Solve(design, fieldY, requireFullRank: true);
        }
        catch (InvalidOperationException exception)
        {
            throw new AnalysisDataUnavailableException("Image Simulation Distortion",
                $"the inverse distortion fit is not uniquely resolved: {exception.Message}");
        }
        var minX = referenceX.Min();
        var maxX = referenceX.Max();
        var minY = referenceY.Min();
        var maxY = referenceY.Max();
        var grid = new (double X, double Y)[height, width];
        var features = new double[featureCount];
        for (var row = 0; row < height; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            // Fit only traced active fields. The guard extends the same detector lattice;
            // it contains PSF spillover, without adding object fields or rescaling them.
            var y = activeHeight == 1
                ? (minY + maxY) / 2 + (minY - maxY) * (row - padding)
                : maxY + ((minY - maxY) * (row - padding) / (activeHeight - 1.0));
            for (var column = 0; column < width; column++)
            {
                var x = activeWidth == 1
                    ? (minX + maxX) / 2 + (maxX - minX) * (column - padding)
                    : minX + ((maxX - minX) * (column - padding) / (activeWidth - 1.0));
                FillPolynomialFeatures(features, (x - centerX) / scaleX, (y - centerY) / scaleY, degree);
                var sourceX = Dot(features, coefficientX);
                var sourceY = -Dot(features, coefficientY);
                grid[row, column] = padding == 0 ? (sourceX, sourceY)
                    : (sourceX * Math.Max(1, activeWidth - 1) / (width - 1.0),
                        sourceY * Math.Max(1, activeHeight - 1) / (height - 1.0));
            }
        }

        return grid;
    }

    public static double[,] WarpImage(double[,] source, (double X, double Y)[,] grid)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(grid);
        AnalysisResourceLimits.ValidateImageDimensions(source.GetLength(1), source.GetLength(0), "Warp source");
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Warp output");
        var output = new double[height, width];
        for (var row = 0; row < height; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < width; column++)
            {
                // Distortion grids sample the endpoint pixel centers, not the outer pixel edges.
                var point = grid[row, column];
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)
                    || source.GetLength(1) == 1 && Math.Abs(point.X) > 1
                    || source.GetLength(0) == 1 && Math.Abs(point.Y) > 1)
                    continue;
                var sourceX = (point.X + 1) * (source.GetLength(1) - 1) / 2;
                var sourceY = (point.Y + 1) * (source.GetLength(0) - 1) / 2;
                if (sourceX <= -1 || sourceX >= source.GetLength(1)
                    || sourceY <= -1 || sourceY >= source.GetLength(0))
                    continue;
                output[row, column] = BilinearWithZeroPadding(source, sourceY, sourceX);
            }
        }

        return output;
    }

    private static Wavelength ResolveWavelength(Optic optic, double micrometers)
    {
        return new Wavelength
        {
            Label = $"{micrometers:0.0000} um",
            Nanometers = micrometers * 1000,
            Weight = 1
        };
    }

    public static double[,,] ResizeCoefficientMaps(double[,,] source, int height, int width, int padding = 0)
    {
        if (padding < 0 || 2L * padding >= Math.Min(width, height))
            throw new ArgumentOutOfRangeException(nameof(padding), "The guard band must leave a nonempty active image.");
        var activeHeight = height - 2 * padding;
        var activeWidth = width - 2 * padding;
        var output = new double[source.GetLength(0), height, width];
        for (var component = 0; component < source.GetLength(0); component++)
        {
            for (var row = 0; row < activeHeight; row++)
            {
                var y = activeHeight == 1 ? 0 : row * (source.GetLength(1) - 1.0) / (activeHeight - 1.0);
                for (var column = 0; column < activeWidth; column++)
                {
                    var x = activeWidth == 1 ? 0 : column * (source.GetLength(2) - 1.0) / (activeWidth - 1.0);
                    output[component, row + padding, column + padding] = Bilinear(source, component, y, x);
                }
            }
        }

        return output;
    }

    private static double[,] ConvolveSame(double[,] source, double[,] kernel)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var output = new double[source.GetLength(0), source.GetLength(1)];
        var startY = (kernel.GetLength(0) - 1) / 2;
        var startX = (kernel.GetLength(1) - 1) / 2;
        for (var row = 0; row < output.GetLength(0); row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < output.GetLength(1); column++)
            {
                if ((column & 31) == 0) ComputationCancellation.ThrowIfCancellationRequested();
                var sum = 0.0;
                for (var kernelY = 0; kernelY < kernel.GetLength(0); kernelY++)
                {
                    var sourceY = row + startY - kernelY;
                    if (sourceY < 0 || sourceY >= source.GetLength(0))
                    {
                        continue;
                    }

                    for (var kernelX = 0; kernelX < kernel.GetLength(1); kernelX++)
                    {
                        var sourceX = column + startX - kernelX;
                        if (sourceX >= 0 && sourceX < source.GetLength(1))
                        {
                            sum += source[sourceY, sourceX] * kernel[kernelY, kernelX];
                        }
                    }
                }

                output[row, column] = sum;
            }
        }

        return output;
    }

    public static double[,,] ZeroPad(double[,,] source, int padding)
    {
        padding = Math.Max(0, padding);
        var output = new double[
            source.GetLength(0),
            source.GetLength(1) + (2 * padding),
            source.GetLength(2) + (2 * padding)];
        for (var channel = 0; channel < source.GetLength(0); channel++)
        {
            for (var row = 0; row < source.GetLength(1); row++)
            {
                for (var column = 0; column < source.GetLength(2); column++)
                {
                    output[channel, row + padding, column + padding] = source[channel, row, column];
                }
            }
        }

        return output;
    }

    public static double[,,] NearestNeighborOversample(double[,,] source, int factor)
    {
        factor = Math.Clamp(factor, 1, 16);
        if (factor == 1)
        {
            return (double[,,])source.Clone();
        }

        var output = new double[
            source.GetLength(0),
            source.GetLength(1) * factor,
            source.GetLength(2) * factor];
        for (var channel = 0; channel < output.GetLength(0); channel++)
        {
            for (var row = 0; row < output.GetLength(1); row++)
            {
                for (var column = 0; column < output.GetLength(2); column++)
                {
                    output[channel, row, column] = source[channel, row / factor, column / factor];
                }
            }
        }

        return output;
    }

    internal static double[,,] TransformImage(double[,,] source, string flip, int rotationDegrees)
    {
        var horizontal = flip.Contains("Horizontal", StringComparison.OrdinalIgnoreCase)
            || flip.Contains("水平", StringComparison.Ordinal);
        var vertical = flip.Contains("Vertical", StringComparison.OrdinalIgnoreCase)
            || flip.Contains("垂直", StringComparison.Ordinal);
        var rotation = AnalysisResourceLimits.NormalizeImageRotation(rotationDegrees);
        var sourceHeight = source.GetLength(1);
        var sourceWidth = source.GetLength(2);
        var outputHeight = rotation is 90 or 270 ? sourceWidth : sourceHeight;
        var outputWidth = rotation is 90 or 270 ? sourceHeight : sourceWidth;
        var output = new double[source.GetLength(0), outputHeight, outputWidth];
        for (var channel = 0; channel < source.GetLength(0); channel++)
        {
            for (var row = 0; row < sourceHeight; row++)
            {
                for (var column = 0; column < sourceWidth; column++)
                {
                    var sourceRow = vertical ? sourceHeight - 1 - row : row;
                    var sourceColumn = horizontal ? sourceWidth - 1 - column : column;
                    var (targetRow, targetColumn) = rotation switch
                    {
                        90 => (sourceColumn, sourceHeight - 1 - sourceRow),
                        180 => (sourceHeight - 1 - sourceRow, sourceWidth - 1 - sourceColumn),
                        270 => (sourceWidth - 1 - sourceColumn, sourceRow),
                        _ => (sourceRow, sourceColumn)
                    };
                    output[channel, targetRow, targetColumn] = source[channel, row, column];
                }
            }
        }

        return output;
    }

    private static double[,,] ResizeRgb(double[,,] source, int height, int width)
    {
        height = Math.Max(1, height);
        width = Math.Max(1, width);
        if (height == source.GetLength(1) && width == source.GetLength(2))
        {
            return (double[,,])source.Clone();
        }

        var output = new double[source.GetLength(0), height, width];
        for (var channel = 0; channel < source.GetLength(0); channel++)
        {
            for (var row = 0; row < height; row++)
            {
                var sourceRow = height == 1
                    ? 0
                    : (int)Math.Round(row * (source.GetLength(1) - 1.0) / (height - 1.0));
                for (var column = 0; column < width; column++)
                {
                    var sourceColumn = width == 1
                        ? 0
                        : (int)Math.Round(column * (source.GetLength(2) - 1.0) / (width - 1.0));
                    output[channel, row, column] = source[channel, sourceRow, sourceColumn];
                }
            }
        }

        return output;
    }

    public static double[,,] ReflectPad(double[,,] source, int padding)
    {
        var output = new double[source.GetLength(0), source.GetLength(1) + (2 * padding), source.GetLength(2) + (2 * padding)];
        for (var channel = 0; channel < output.GetLength(0); channel++)
        {
            for (var row = 0; row < output.GetLength(1); row++)
            {
                var sourceY = ReflectIndex(row - padding, source.GetLength(1));
                for (var column = 0; column < output.GetLength(2); column++)
                {
                    var sourceX = ReflectIndex(column - padding, source.GetLength(2));
                    output[channel, row, column] = source[channel, sourceY, sourceX];
                }
            }
        }

        return output;
    }

    private static int ReflectIndex(int index, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        var period = (2 * size) - 2;
        index %= period;
        if (index < 0)
        {
            index += period;
        }

        return index < size ? index : period - index;
    }

    private static double[,] SliceChannel(double[,,] source, int channel)
    {
        var output = new double[source.GetLength(1), source.GetLength(2)];
        for (var row = 0; row < output.GetLength(0); row++)
        {
            for (var column = 0; column < output.GetLength(1); column++)
            {
                output[row, column] = source[channel, row, column];
            }
        }

        return output;
    }

    private static void CopyChannel(double[,,] destination, int channel, double[,] source)
    {
        for (var row = 0; row < source.GetLength(0); row++)
        {
            for (var column = 0; column < source.GetLength(1); column++)
            {
                destination[channel, row, column] = source[row, column];
            }
        }
    }

    private static double Bilinear(double[,] source, double y, double x)
    {
        var y0 = Math.Clamp((int)Math.Floor(y), 0, source.GetLength(0) - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, source.GetLength(1) - 1);
        var y1 = Math.Min(y0 + 1, source.GetLength(0) - 1);
        var x1 = Math.Min(x0 + 1, source.GetLength(1) - 1);
        var fy = y - y0;
        var fx = x - x0;
        return ((1 - fy) * (((1 - fx) * source[y0, x0]) + (fx * source[y0, x1])))
            + (fy * (((1 - fx) * source[y1, x0]) + (fx * source[y1, x1])));
    }

    private static double Bilinear(double[,,] source, int component, double y, double x)
    {
        var y0 = Math.Clamp((int)Math.Floor(y), 0, source.GetLength(1) - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, source.GetLength(2) - 1);
        var y1 = Math.Min(y0 + 1, source.GetLength(1) - 1);
        var x1 = Math.Min(x0 + 1, source.GetLength(2) - 1);
        var fy = y - y0;
        var fx = x - x0;
        return ((1 - fy) * (((1 - fx) * source[component, y0, x0]) + (fx * source[component, y0, x1])))
            + (fy * (((1 - fx) * source[component, y1, x0]) + (fx * source[component, y1, x1])));
    }

    private static double BilinearWithZeroPadding(double[,] source, double y, double x)
    {
        var y0 = (int)Math.Floor(y);
        var x0 = (int)Math.Floor(x);
        var fy = y - y0;
        var fx = x - x0;
        double Value(int row, int column) => row >= 0 && row < source.GetLength(0) && column >= 0 && column < source.GetLength(1)
            ? source[row, column]
            : 0;
        return ((1 - fy) * (((1 - fx) * Value(y0, x0)) + (fx * Value(y0, x0 + 1))))
            + (fy * (((1 - fx) * Value(y0 + 1, x0)) + (fx * Value(y0 + 1, x0 + 1))));
    }

    private static void FillPolynomialFeatures(double[,] destination, int row, double x, double y, int degree)
    {
        var feature = 0;
        for (var order = 0; order <= degree; order++)
        {
            for (var xPower = 0; xPower <= order; xPower++)
            {
                destination[row, feature++] = Math.Pow(x, xPower) * Math.Pow(y, order - xPower);
            }
        }
    }

    private static void FillPolynomialFeatures(double[] destination, double x, double y, int degree)
    {
        var feature = 0;
        for (var order = 0; order <= degree; order++)
        {
            for (var xPower = 0; xPower <= order; xPower++)
            {
                destination[feature++] = Math.Pow(x, xPower) * Math.Pow(y, order - xPower);
            }
        }
    }

    private static (double[] Values, double[,] Vectors) SymmetricEigen(double[,] source)
    {
        var size = source.GetLength(0);
        var matrix = (double[,])source.Clone();
        var vectors = new double[size, size];
        for (var index = 0; index < size; index++)
        {
            vectors[index, index] = 1;
        }

        for (var iteration = 0; iteration < 100 * size * size; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var p = 0;
            var q = 1;
            var maximum = 0.0;
            for (var row = 0; row < size; row++)
            {
                for (var column = row + 1; column < size; column++)
                {
                    if (Math.Abs(matrix[row, column]) > maximum)
                    {
                        maximum = Math.Abs(matrix[row, column]);
                        p = row;
                        q = column;
                    }
                }
            }

            if (maximum <= 1e-14)
            {
                break;
            }

            var angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
            var sine = Math.Sin(angle);
            var cosine = Math.Cos(angle);
            for (var index = 0; index < size; index++)
            {
                if (index != p && index != q)
                {
                    var left = matrix[index, p];
                    var right = matrix[index, q];
                    matrix[index, p] = matrix[p, index] = (cosine * left) - (sine * right);
                    matrix[index, q] = matrix[q, index] = (sine * left) + (cosine * right);
                }

                var vectorP = vectors[index, p];
                var vectorQ = vectors[index, q];
                vectors[index, p] = (cosine * vectorP) - (sine * vectorQ);
                vectors[index, q] = (sine * vectorP) + (cosine * vectorQ);
            }

            var app = matrix[p, p];
            var aqq = matrix[q, q];
            var apq = matrix[p, q];
            matrix[p, p] = (cosine * cosine * app) - (2 * sine * cosine * apq) + (sine * sine * aqq);
            matrix[q, q] = (sine * sine * app) + (2 * sine * cosine * apq) + (cosine * cosine * aqq);
            matrix[p, q] = matrix[q, p] = 0;
        }

        var order = Enumerable.Range(0, size).OrderByDescending(index => matrix[index, index]).ToArray();
        var values = order.Select(index => matrix[index, index]).ToArray();
        var sortedVectors = new double[size, size];
        for (var column = 0; column < size; column++)
        {
            for (var row = 0; row < size; row++)
            {
                sortedVectors[row, column] = vectors[row, order[column]];
            }
        }

        return (values, sortedVectors);
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var value = 0.0;
        for (var index = 0; index < left.Count; index++)
        {
            value += left[index] * right[index];
        }

        return value;
    }
}
