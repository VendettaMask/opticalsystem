namespace OptilandWorkbench.Core.Analysis;

public sealed class GeometricImageAnalysis : BaseAnalysis
{
    private readonly ImageSimulationSourcePattern _sourcePattern;
    private readonly int _imageSize;
    private readonly int _numRays;

    public GeometricImageAnalysis(
        Optic optic,
        ImageSimulationSourcePattern sourcePattern = ImageSimulationSourcePattern.ResolutionTarget,
        int imageSize = 64,
        int numRays = 8) : base(optic)
    {
        _sourcePattern = sourcePattern;
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _numRays = Math.Clamp(numRays, 2, 128);
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
            pipeline: "Geometric image approximation from field-dependent ray samples");
    }
}

public sealed class GeometricBitmapImageAnalysis : BaseAnalysis
{
    private readonly int _imageSize;
    private readonly int _raysPerPixel;

    public GeometricBitmapImageAnalysis(
        Optic optic,
        int imageSize = 64,
        int raysPerPixel = 8) : base(optic)
    {
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _raysPerPixel = Math.Clamp(raysPerPixel, 2, 128);
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
            pipeline: "RGB bitmap geometric ray image");
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

    public PartiallyCoherentImageAnalysis(
        Optic optic,
        int imageSize = 64,
        int pupilSampling = 16,
        double coherence = 0.5) : base(optic)
    {
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _pupilSampling = Math.Clamp(pupilSampling, 4, 128);
        _coherence = Math.Clamp(coherence, 0, 1);
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
            psfSize: 32,
            psfGridSize: 3,
            pipeline: "Partially coherent diffraction image",
            sourceBlend: _coherence,
            additionalValues: new Dictionary<string, object>
            {
                ["Coherence"] = _coherence
            });
    }
}

public sealed class ExtendedDiffractionImageAnalysis : BaseAnalysis
{
    private readonly ImageSimulationSourcePattern _sourcePattern;
    private readonly int _imageSize;
    private readonly int _pupilSampling;
    private readonly int _fieldGrid;

    public ExtendedDiffractionImageAnalysis(
        Optic optic,
        ImageSimulationSourcePattern sourcePattern = ImageSimulationSourcePattern.ResolutionTarget,
        int imageSize = 64,
        int pupilSampling = 16,
        int fieldGrid = 5) : base(optic)
    {
        _sourcePattern = sourcePattern;
        _imageSize = Math.Clamp(imageSize, 16, 256);
        _pupilSampling = Math.Clamp(pupilSampling, 4, 128);
        _fieldGrid = Math.Clamp(fieldGrid, 2, 9);
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
            pipeline: "Field-dependent extended diffraction image");
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
        double sourceBlend = 0,
        IReadOnlyDictionary<string, object>? additionalValues = null)
    {
        var source = ImageSimulationEngine.CreateSourceImage(sourcePattern, imageSize, imageSize);
        var result = ImageSimulationEngine.Simulate(optic, source, new ImageSimulationConfig
        {
            SourcePattern = sourcePattern,
            PsfGridRows = psfGridSize,
            PsfGridColumns = psfGridSize,
            PsfSize = psfSize,
            NumRays = numRays,
            Components = Math.Min(3, psfGridSize),
            Padding = Math.Max(4, psfSize / 2),
            DistortionGridSize = Math.Max(5, psfGridSize * 2 + 1),
            DistortionPolynomialDegree = 5
        });
        var simulated = sourceBlend > 0
            ? Blend(result.Source, result.Simulated, sourceBlend)
            : result.Simulated;
        var originalSeries = RasterSeries(result.Source);
        var simulatedSeries = RasterSeries(simulated);
        var values = new Dictionary<string, object>
        {
            ["Pipeline"] = pipeline,
            ["SourcePattern"] = sourcePattern.ToString(),
            ["ImageSize"] = imageSize,
            ["PupilRaySampling"] = numRays,
            ["PsfSize"] = psfSize,
            ["FieldGrid"] = $"{psfGridSize} x {psfGridSize}",
            ["MeanAbsoluteChange"] = result.MeanAbsoluteChange
        };
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

    private static RgbImage Blend(RgbImage source, RgbImage simulated, double sourceFraction)
    {
        var values = new double[
            simulated.Channels,
            simulated.Height,
            simulated.Width];
        for (var channel = 0; channel < simulated.Channels; channel++)
        {
            for (var row = 0; row < simulated.Height; row++)
            {
                for (var column = 0; column < simulated.Width; column++)
                {
                    values[channel, row, column] =
                        (sourceFraction * source[Math.Min(channel, source.Channels - 1), row, column])
                        + ((1 - sourceFraction) * simulated[channel, row, column]);
                }
            }
        }

        return new RgbImage(values);
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
