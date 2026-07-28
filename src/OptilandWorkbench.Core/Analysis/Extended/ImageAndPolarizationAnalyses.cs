using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class ImageSimulationAnalysis : BaseAnalysis
{
    private readonly ImageSimulationConfig _config;

    public ImageSimulationAnalysis(Optic optic, ImageSimulationConfig? config = null) : base(optic)
    {
        _config = config ?? new ImageSimulationConfig
        {
            PsfGridRows = 3,
            PsfGridColumns = 3,
            PsfSize = 32,
            NumRays = 16,
            Components = 3,
            Padding = 16,
            DistortionGridSize = 9,
            DistortionPolynomialDegree = 5,
            ImageWidth = 64,
            ImageHeight = 48
        };
    }

    public override string Name => "Image Simulation";

    public override AnalysisData GenerateData()
    {
        var oversampling = Math.Clamp(_config.Oversampling, 1, 16);
        var source = ImageSimulationEngine.CreateSourceImage(
            _config.SourcePattern,
            Math.Max(16, _config.ImageWidth * oversampling),
            Math.Max(16, _config.ImageHeight * oversampling));
        var result = ImageSimulationEngine.Simulate(Optic, source, _config);
        var original = RasterSeries(result.Source);
        var simulated = RasterSeries(result.Simulated);
        var panes = new[]
        {
            RasterPane("Original Image [0]", original, result.Source),
            RasterPane("Simulated Image [0]", simulated, result.Simulated)
        };
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Pipeline"] = "EigenPSF spatially variable convolution + geometric distortion + lateral color",
            ["ZemaxImageSimulationSettings"] = "Source, FieldHeight, Oversampling, GuardBand, RelativeIllumination, PSF grid, and AberrationMode",
            ["SourceMode"] = _config.SourceMode,
            ["SourcePattern"] = _config.SourcePattern.ToString(),
            ["FieldHeight"] = _config.FieldHeight,
            ["Oversampling"] = oversampling,
            ["GuardBand"] = _config.Padding,
            ["RelativeIllumination"] = _config.UseRelativeIllumination,
            ["AberrationMode"] = _config.AberrationMode,
            ["OutputShape"] = $"(1, {result.Simulated.Channels}, {result.Simulated.Height}, {result.Simulated.Width})",
            ["WavelengthsMicrometers"] = string.Join(", ", _config.WavelengthsMicrometers.Select(value => value.ToString("0.00"))),
            ["PsfGridShape"] = $"({_config.PsfGridRows}, {_config.PsfGridColumns})",
            ["PsfSize"] = _config.PsfSize,
            ["NumRays"] = _config.NumRays,
            ["EigenPsfComponents"] = _config.Components,
            ["DistortionGridSize"] = _config.DistortionGridSize,
            ["DistortionPolynomialDegree"] = _config.DistortionPolynomialDegree,
            ["MeanAbsoluteChange"] = result.MeanAbsoluteChange,
            ["MaximumOutputValue"] = result.MaximumValue
        }, original, new[] { original }, PlotPanes: panes, PlotPaneColumns: 2);
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
                    Red: image.Values[0, row, column],
                    Green: image.Values[Math.Min(1, image.Channels - 1), row, column],
                    Blue: image.Values[Math.Min(2, image.Channels - 1), row, column]));
            }
        }

        return new AnalysisSeries("", "", points, AnalysisSeriesKind.Raster);
    }

    private static AnalysisPlotPane RasterPane(string title, AnalysisSeries series, RgbImage image)
    {
        return new AnalysisPlotPane(title, new[] { series }, new AnalysisPlotOptions(
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

public sealed class JonesPupilAnalysis : BaseAnalysis
{
    private readonly int _gridSize;

    public JonesPupilAnalysis(Optic optic, int gridSize = 65) : base(optic)
    {
        _gridSize = Math.Max(3, gridSize);
    }

    public override string Name => "Jones Pupil";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var result = JonesPupilEngine.Generate(Optic, (0, 0), wavelength, _gridSize, useFresnelCoatings: true);
        var elements = new (string Name, Func<JonesPupilSample, System.Numerics.Complex> Select)[]
        {
            ("Jxx", sample => sample.Jxx),
            ("Jxy", sample => sample.Jxy),
            ("Jyx", sample => sample.Jyx),
            ("Jyy", sample => sample.Jyy)
        };
        var panes = new List<AnalysisPlotPane>(8);
        foreach (var component in new[] { "Re", "Im" })
        {
            foreach (var element in elements)
            {
                var series = new AnalysisSeries(
                    "Px",
                    "Py",
                    result.Samples.Select(sample => new AnalysisPoint(
                        sample.Px,
                        sample.Py,
                        Value: sample.IsValid
                            ? (component == "Re" ? element.Select(sample).Real : element.Select(sample).Imaginary)
                            : double.NaN)).ToArray(),
                    AnalysisSeriesKind.Heatmap,
                    ValueLabel: $"{component}({element.Name})");
                panes.Add(new AnalysisPlotPane(
                    $"{component}({element.Name})",
                    new[] { series },
                    new AnalysisPlotOptions(
                        Title: $"{component}({element.Name})",
                        EqualAspect: true,
                        XMinimum: -1,
                        XMaximum: 1,
                        YMinimum: -1,
                        YMaximum: 1,
                        HideTopAndRightAxes: true,
                        GridOpacity: 0)));
            }
        }

        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Field"] = "(0, 0)",
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["GridSize"] = _gridSize,
            ["ValidRayCount"] = result.Samples.Count(sample => sample.IsValid),
            ["CoatingMode"] = "Fresnel",
            ["Layout"] = "2 rows (real, imaginary) x 4 columns (Jxx, Jxy, Jyx, Jyy)"
        }, PlotPanes: panes, PlotPaneColumns: 4);
    }
}

public sealed class PrescriptionReportAnalysis : BaseAnalysis
{
    public PrescriptionReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Prescription Report";

    public override AnalysisData GenerateData()
    {
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Name"] = Optic.Name,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count,
            ["FieldCount"] = Optic.Fields.Count,
            ["WavelengthCount"] = Optic.Wavelengths.Count,
            ["EFL"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}
