namespace OpticalSystemDesign.Core.Analysis;

public sealed class GeometricImageAnalysis : BaseAnalysis
{
    private readonly ImageSimulationSourcePattern _sourcePattern;
    private readonly int _imageSize;
    private readonly int _numRays;
    private readonly double _fieldHeight;
    private readonly int _oversampling;
    private readonly int _guardBand;
    private readonly bool _relativeIllumination;
    private readonly string _aberrationMode;

    public GeometricImageAnalysis(
        Optic optic,
        ImageSimulationSourcePattern sourcePattern = ImageSimulationSourcePattern.ResolutionTarget,
        int imageSize = 64,
        int numRays = 8,
        double fieldHeight = 0,
        int oversampling = 1,
        int guardBand = 4,
        bool relativeIllumination = true,
        string aberrationMode = "Geometric") : base(optic)
    {
        _sourcePattern = sourcePattern;
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _numRays = Math.Clamp(numRays, 2, 128);
        _fieldHeight = Math.Max(0, fieldHeight);
        _oversampling = Math.Clamp(oversampling, 1, 16);
        _guardBand = Math.Clamp(guardBand, 0, 512);
        _relativeIllumination = relativeIllumination;
        _aberrationMode = aberrationMode;
    }

    public override string Name => "Geometric Image Analysis";

    public override AnalysisData GenerateData()
    {
        return ExtendedSceneImageSupport.Simulate(
            Optic,
            Name,
            _sourcePattern,
            _imageSize,
            _numRays,
            psfSize: 8,
            psfGridSize: 3,
            pipeline: "Geometric image approximation from field-dependent ray samples",
            fieldHeight: _fieldHeight,
            oversampling: _oversampling,
            guardBand: _guardBand,
            relativeIllumination: _relativeIllumination,
            aberrationMode: _aberrationMode);
    }
}

public sealed class GeometricBitmapImageAnalysis : BaseAnalysis
{
    private readonly int _imageSize;
    private readonly int _raysPerPixel;
    private readonly double _fieldHeight;
    private readonly int _oversampling;
    private readonly int _guardBand;
    private readonly bool _relativeIllumination;
    private readonly string _aberrationMode;

    public GeometricBitmapImageAnalysis(
        Optic optic,
        int imageSize = 64,
        int raysPerPixel = 8,
        double fieldHeight = 0,
        int oversampling = 1,
        int guardBand = 4,
        bool relativeIllumination = true,
        string aberrationMode = "Geometric") : base(optic)
    {
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _raysPerPixel = Math.Clamp(raysPerPixel, 2, 128);
        _fieldHeight = Math.Max(0, fieldHeight);
        _oversampling = Math.Clamp(oversampling, 1, 16);
        _guardBand = Math.Clamp(guardBand, 0, 512);
        _relativeIllumination = relativeIllumination;
        _aberrationMode = aberrationMode;
    }

    public override string Name => "Geometric Bitmap Image Analysis";

    public override AnalysisData GenerateData()
    {
        return ExtendedSceneImageSupport.Simulate(
            Optic,
            Name,
            ImageSimulationSourcePattern.ColorChart,
            _imageSize,
            _raysPerPixel,
            psfSize: 8,
            psfGridSize: 3,
            pipeline: "RGB bitmap geometric ray image",
            fieldHeight: _fieldHeight,
            oversampling: _oversampling,
            guardBand: _guardBand,
            relativeIllumination: _relativeIllumination,
            aberrationMode: _aberrationMode,
            sourceMode: "Built-in bitmap surrogate");
    }
}

public sealed class LightSourceAnalysis : BaseAnalysis
{
    private readonly int _resolution;
    private readonly int _numRays;

    public LightSourceAnalysis(Optic optic, int resolution = 65, int numRays = 2048) : base(optic)
    {
        _resolution = Math.Clamp(resolution, 9, 257);
        _numRays = Math.Clamp(numRays, 32, 200_000);
    }

    public override string Name => "Light Source Analysis";

    public override AnalysisData GenerateData()
    {
        var source = new RadiantIntensityAnalysis(
            Optic,
            _resolution,
            _resolution,
            useAbsoluteUnits: false,
            numRays: _numRays,
            distribution: "sobol",
            normalize: true).GenerateData();
        var values = source.Values.ToDictionary(item => item.Key, item => item.Value);
        values["Method"] = "Sequential source-ray angular intensity";
        return source with { Name = Name, Values = values };
    }
}

public sealed class PartiallyCoherentImageAnalysis : BaseAnalysis
{
    private readonly int _imageSize;
    private readonly int _pupilSampling;
    private readonly double _coherence;
    private readonly int _psfSize;
    private readonly double _pixelSizeMillimeters;
    private readonly SpatialCoherenceFunction _coherenceFunction;
    private readonly double _alphaMillimeters;
    private readonly double _fileSizeMillimeters;
    private readonly bool _normalizeImagePeak;
    private readonly IReadOnlyList<double>? _wavelengthsMicrometers;
    private readonly SpatialCoherenceMethod _coherenceMethod;
    private readonly double _coherenceFraction;
    private readonly int _coherenceSampling;
    private readonly double _psfEnergyFraction;
    private readonly double _fieldHeight;
    private readonly int _oversampling;
    private readonly int _guardBand;
    private readonly bool _relativeIllumination;

    public PartiallyCoherentImageAnalysis(
        Optic optic,
        int imageSize = 64,
        int pupilSampling = 16,
        double coherence = 0.5,
        double fieldHeight = 0,
        int oversampling = 1,
        int guardBand = 16,
        bool relativeIllumination = true,
        int psfSize = 32,
        double pixelSizeMillimeters = 0,
        SpatialCoherenceFunction coherenceFunction = SpatialCoherenceFunction.Uniform,
        double alphaMillimeters = .001,
        double fileSizeMillimeters = 0,
        bool normalizeImagePeak = false,
        IReadOnlyList<double>? wavelengthsMicrometers = null,
        SpatialCoherenceMethod coherenceMethod = SpatialCoherenceMethod.ExactSourcePairs,
        double coherenceFraction = .98, int coherenceSampling = 64, double psfEnergyFraction = 0) : base(optic)
    {
        if (!double.IsFinite(coherence))
            throw new ArgumentOutOfRangeException(nameof(coherence), "Coherence must be finite.");
        if (!double.IsFinite(pixelSizeMillimeters) || pixelSizeMillimeters < 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSizeMillimeters));
        _psfSize = Math.Clamp(psfSize, 1, 256);
        _pixelSizeMillimeters = pixelSizeMillimeters;
        _coherenceFunction = coherenceFunction;
        _alphaMillimeters = alphaMillimeters;
        _fileSizeMillimeters = fileSizeMillimeters;
        _normalizeImagePeak = normalizeImagePeak;
        _wavelengthsMicrometers = wavelengthsMicrometers;
        _coherenceMethod = coherenceMethod;
        _coherenceFraction = coherenceFraction;
        _coherenceSampling = coherenceSampling;
        _psfEnergyFraction = psfEnergyFraction;
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _pupilSampling = Math.Clamp(pupilSampling, 4, 512);
        _coherence = Math.Clamp(coherence, 0, 1);
        _fieldHeight = Math.Max(0, fieldHeight);
        _oversampling = Math.Clamp(oversampling, 1, 16);
        _guardBand = Math.Clamp(guardBand, 0, 512);
        _relativeIllumination = relativeIllumination;
    }

    public override string Name => "Partially Coherent Image Analysis";

    public override AnalysisData GenerateData()
    {
        return ExtendedSceneImageSupport.Simulate(
            Optic,
            Name,
            ImageSimulationSourcePattern.ResolutionTarget,
            _imageSize,
            _pupilSampling,
            psfSize: _psfSize,
            psfGridSize: 1,
            pipeline: CoherentImagingDiagnostics.Pipeline,
            fieldHeight: _fieldHeight,
            oversampling: _oversampling,
            guardBand: _guardBand,
            relativeIllumination: _relativeIllumination,
            aberrationMode: "Partially Coherent",
            spatialCoherence: _coherence,
            pixelSizeMillimeters: _pixelSizeMillimeters,
            coherenceFunction: _coherenceFunction,
            alphaMillimeters: _alphaMillimeters,
            fileSizeMillimeters: _fileSizeMillimeters,
            normalizeImagePeak: _normalizeImagePeak,
            coherenceMethod: _coherenceMethod,
            coherenceFraction: _coherenceFraction,
            coherenceSampling: _coherenceSampling,
            psfEnergyFraction: _psfEnergyFraction,
            wavelengthsMicrometers: _wavelengthsMicrometers
                ?? [(Optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? Optic.Wavelengths.First()).Micrometers],
            additionalValues: _coherenceFunction == SpatialCoherenceFunction.Uniform
                ? new Dictionary<string, object> { ["Coherence"] = _coherence } : null);
    }
}

public sealed class ExtendedDiffractionImageAnalysis : BaseAnalysis
{
    private readonly ImageSimulationSourcePattern _sourcePattern;
    private readonly int _imageSize;
    private readonly int _pupilSampling;
    private readonly int _fieldGrid;
    private readonly double _fieldHeight;
    private readonly int _oversampling;
    private readonly int _guardBand;
    private readonly bool _relativeIllumination;
    private readonly string _aberrationMode;

    public ExtendedDiffractionImageAnalysis(
        Optic optic,
        ImageSimulationSourcePattern sourcePattern = ImageSimulationSourcePattern.ResolutionTarget,
        int imageSize = 64,
        int pupilSampling = 16,
        int fieldGrid = 5,
        double fieldHeight = 0,
        int oversampling = 1,
        int guardBand = 16,
        bool relativeIllumination = true,
        string aberrationMode = "Diffraction") : base(optic)
    {
        _sourcePattern = sourcePattern;
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _pupilSampling = Math.Clamp(pupilSampling, 4, 128);
        _fieldGrid = Math.Clamp(fieldGrid, 2, 9);
        _fieldHeight = Math.Max(0, fieldHeight);
        _oversampling = Math.Clamp(oversampling, 1, 16);
        _guardBand = Math.Clamp(guardBand, 0, 512);
        _relativeIllumination = relativeIllumination;
        _aberrationMode = aberrationMode;
    }

    public override string Name => "Extended Diffraction Image Analysis";

    public override AnalysisData GenerateData()
    {
        return ExtendedSceneImageSupport.Simulate(
            Optic,
            Name,
            _sourcePattern,
            _imageSize,
            _pupilSampling,
            psfSize: 32,
            psfGridSize: _fieldGrid,
            pipeline: "Field-dependent extended diffraction image",
            fieldHeight: _fieldHeight,
            oversampling: _oversampling,
            guardBand: _guardBand,
            relativeIllumination: _relativeIllumination,
            aberrationMode: _aberrationMode);
    }
}

internal static class ExtendedSceneImageSupport
{
    public static AnalysisData Simulate(
        Optic optic,
        string name,
        ImageSimulationSourcePattern sourcePattern,
        int imageSize,
        int numRays,
        int psfSize,
        int psfGridSize,
        string pipeline,
        double fieldHeight = 0,
        int oversampling = 1,
        int guardBand = -1,
        bool relativeIllumination = true,
        string aberrationMode = "Diffraction",
        string sourceMode = "Built-in",
        double spatialCoherence = 0.5,
        double pixelSizeMillimeters = 0,
        SpatialCoherenceFunction coherenceFunction = SpatialCoherenceFunction.Uniform,
        double alphaMillimeters = .001,
        double fileSizeMillimeters = 0,
        bool normalizeImagePeak = false,
        IReadOnlyList<double>? wavelengthsMicrometers = null,
        SpatialCoherenceMethod coherenceMethod = SpatialCoherenceMethod.ExactSourcePairs,
        double coherenceFraction = .98, int coherenceSampling = 64,
        double psfEnergyFraction = 0,
        IReadOnlyDictionary<string, object>? additionalValues = null)
    {
        oversampling = Math.Clamp(oversampling, 1, 16);
        var source = ImageSimulationEngine.CreateSourceImage(
            sourcePattern,
            imageSize,
            imageSize);
        var padding = guardBand >= 0 ? guardBand : Math.Max(4, psfSize / 2);
        var config = new ImageSimulationConfig
        {
            SourcePattern = sourcePattern,
            SourceMode = sourceMode,
            FieldHeight = Math.Max(0, fieldHeight),
            Oversampling = oversampling,
            UseRelativeIllumination = relativeIllumination,
            AberrationMode = aberrationMode,
            SpatialCoherence = spatialCoherence,
            CoherenceFunction = coherenceFunction,
            CoherenceAlphaMillimeters = alphaMillimeters,
            CoherenceMethod = coherenceMethod,
            CoherenceFraction = coherenceFraction,
            CoherenceSampling = coherenceSampling,
            PsfEnergyFraction = psfEnergyFraction,
            FileSizeMillimeters = fileSizeMillimeters,
            NormalizeImagePeak = normalizeImagePeak,
            WavelengthsMicrometers = wavelengthsMicrometers ?? [.65, .55, .45],
            PixelSizeMillimeters = pixelSizeMillimeters,
            ImageWidth = imageSize,
            ImageHeight = imageSize,
            PsfGridRows = psfGridSize,
            PsfGridColumns = psfGridSize,
            PsfSize = psfSize,
            NumRays = numRays,
            Components = Math.Min(3, psfGridSize),
            Padding = padding,
            DistortionGridSize = Math.Max(5, psfGridSize * 2 + 1),
            DistortionPolynomialDegree = 5
        };
        ImageSimulationResult result;
        try { result = ImageSimulationEngine.Simulate(optic, source, config); }
        catch (AnalysisDataUnavailableException exception)
        {
            return ImageSimulationAnalysis.UnavailableSimulation(name, exception);
        }
        var simulated = result.Simulated;
        var originalSeries = RasterSeries(result.Source);
        var simulatedSeries = RasterSeries(simulated);
        var values = new Dictionary<string, object>
        {
            ["Pipeline"] = $"{pipeline}; actual PSF: {result.EffectiveAberrationMode}",
            ["SourceMode"] = sourceMode,
            ["SourcePattern"] = sourcePattern.ToString(),
            ["ImageSize"] = imageSize,
            ["FieldHeight"] = Math.Max(0, fieldHeight),
            ["Oversampling"] = oversampling,
            ["GuardBand"] = padding,
            ["RelativeIllumination"] = relativeIllumination,
            ["AberrationMode"] = result.EffectiveAberrationMode,
            ["RequestedAberrationMode"] = aberrationMode,
            ["EffectiveAberrationMode"] = result.EffectiveAberrationMode,
            ["GeometricFallbackCount"] = result.GeometricFallbackCount,
            ["PsfIntensityCorrectionCount"] = result.PsfIntensityCorrectionCount,
            ["MaximumPsfNegativeMass"] = result.MaximumPsfNegativeMass,
            ["PupilRaySampling"] = numRays,
            ["WavelengthsMicrometers"] = config.WavelengthsMicrometers.ToArray(),
            ["PsfSize"] = psfSize,
            ["FieldGrid"] = $"{psfGridSize} x {psfGridSize}",
            ["MeanAbsoluteChange"] = result.MeanAbsoluteChange
        };
        result.CoherentImaging?.AddValues(values);
        if (additionalValues is not null)
        {
            foreach (var item in additionalValues)
            {
                values[item.Key] = item.Value;
            }
        }

        return new AnalysisData(
            name,
            values,
            originalSeries,
            new[] { originalSeries },
            PlotPanes: new[]
            {
                RasterPane("Source", originalSeries, result.Source),
                RasterPane("Image", simulatedSeries, simulated)
            },
            PlotPaneColumns: 2);
    }

    private static AnalysisSeries RasterSeries(RgbImage image)
    {
        var points = new List<AnalysisPoint>(image.Width * image.Height);
        for (var row = 0; row < image.Height; row++)
        {
            for (var column = 0; column < image.Width; column++)
            {
                points.Add(new AnalysisPoint(
                    column,
                    image.Height - 1 - row,
                    Red: image[0, row, column],
                    Green: image[Math.Min(1, image.Channels - 1), row, column],
                    Blue: image[Math.Min(2, image.Channels - 1), row, column]));
            }
        }

        return new AnalysisSeries("", "", points, AnalysisSeriesKind.Raster);
    }

    private static AnalysisPlotPane RasterPane(string title, AnalysisSeries series, RgbImage image)
    {
        return new AnalysisPlotPane(
            title,
            new[] { series },
            new AnalysisPlotOptions(
                Title: title,
                EqualAspect: true,
                XMinimum: -0.5,
                XMaximum: image.Width - 0.5,
                YMinimum: -0.5,
                YMaximum: image.Height - 0.5,
                GridOpacity: 0,
                HideAxes: true));
    }
}
