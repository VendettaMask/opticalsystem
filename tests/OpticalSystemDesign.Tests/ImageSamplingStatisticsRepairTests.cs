using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Tests;

public sealed class ImageSamplingStatisticsRepairTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteCoherenceCannotBypassThePhysicalCapabilityBoundary(double coherence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartiallyCoherentImageAnalysis(Lens(), coherence: coherence));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(90, 1, 0)]
    [InlineData(270, 1, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(90, 2, 0)]
    [InlineData(270, 2, 0)]
    [InlineData(0, 1, .0057)]
    [InlineData(90, 2, .0057)]
    public void GuardBandPreservesTheActiveSourcePointResponse(int rotation, int oversampling, double pitch)
    {
        var source = Source(32, 24, impulse: true);
        var unpadded = ImageSimulationEngine.Simulate(Lens(), source, Config(0, rotation, oversampling, pitch));
        var padded = ImageSimulationEngine.Simulate(Lens(), source, Config(16, rotation, oversampling, pitch));
        Assert.Equal(unpadded.Simulated.Width + 32, padded.Simulated.Width);
        Assert.Equal(unpadded.Simulated.Height + 32, padded.Simulated.Height);
        for (var y = 0; y < unpadded.Simulated.Height; y++)
            for (var x = 0; x < unpadded.Simulated.Width; x++)
            {
                Assert.Equal(unpadded.Source[0, y, x], padded.Source[0, y + 16, x + 16]);
                Assert.InRange(Math.Abs(unpadded.Simulated[0, y, x] - padded.Simulated[0, y + 16, x + 16]), 0, 1e-10);
            }
    }

    [Fact]
    public void GuardBandPreservesRelativeIlluminationAcrossTheActiveField()
    {
        var source = Source(16, 24);
        var unpadded = ImageSimulationEngine.Simulate(Lens(), source, Config(0, illumination: true, mode: "None"));
        var padded = ImageSimulationEngine.Simulate(Lens(), source, Config(8, illumination: true, mode: "None"));
        // Compare interior points, outside the finite-window interpolation boundary.
        for (var y = 2; y < source.Height - 2; y++)
            for (var x = 2; x < source.Width - 2; x++)
                Assert.InRange(Math.Abs(unpadded.Simulated[0, y, x] - padded.Simulated[0, y + 8, x + 8]), 0, 1e-10);
    }

    [Fact]
    public void GuardBandContainsEscapingPointPowerWithoutRescalingTheKernel()
    {
        var values = new double[1, 32, 32]; values[0, 16, 0] = 1;
        var source = new RgbImage(values);
        var unpadded = ImageSimulationEngine.Simulate(Lens(), source, Config(0));
        var padded = ImageSimulationEngine.Simulate(Lens(), source, Config(16));
        var unpaddedPower = unpadded.Simulated.Values.Cast<double>().Sum();
        var paddedPower = padded.Simulated.Values.Cast<double>().Sum();
        Assert.True(unpaddedPower < .9);
        Assert.InRange(paddedPower, .999, 1.001);
        Assert.True(padded.Simulated[0, 32, 15] > 0);
    }

    [Fact]
    public void CoefficientGridUsesTheActiveRectangleInsideTheGuardBand()
    {
        var grid = new double[2, 3, 4];
        for (var c = 0; c < 2; c++) for (var y = 0; y < 3; y++) for (var x = 0; x < 4; x++)
            grid[c, y, x] = c + .5 * y - .25 * x;
        var unpadded = ImageSimulationEngine.ResizeCoefficientMaps(grid, 12, 16);
        var padded = ImageSimulationEngine.ResizeCoefficientMaps(grid, 28, 32, padding: 8);
        for (var c = 0; c < 2; c++) for (var y = 0; y < 12; y++) for (var x = 0; x < 16; x++)
            Assert.Equal(unpadded[c, y, x], padded[c, y + 8, x + 8]);
        Assert.Equal(0, padded[0, 0, 0]);
    }

    [Fact]
    public void InverseDistortionGridKeepsTheActiveDetectorCoordinates()
    {
        var optic = Lens();
        var wavelength = optic.Wavelengths[0];
        var unpadded = ImageSimulationEngine.GenerateDistortionGrid(optic, wavelength, 12, 16, 5, 3,
            .35, .45, fieldCenterX: .2, fieldCenterY: -.1);
        var padded = ImageSimulationEngine.GenerateDistortionGrid(optic, wavelength, 28, 32, 5, 3,
            .35, .45, fieldCenterX: .2, fieldCenterY: -.1, padding: 8);
        for (var y = 0; y < 12; y++) for (var x = 0; x < 16; x++)
        {
            Assert.Equal(unpadded[y, x].X, padded[y + 8, x + 8].X * 31 / 15, 12);
            Assert.Equal(unpadded[y, x].Y, padded[y + 8, x + 8].Y * 27 / 11, 12);
        }
    }

    [Fact]
    public void DirectCoreUsesTheMutualIntensityPathForPartialCoherence()
    {
        var result = ImageSimulationEngine.Simulate(
            Lens(), Source(16, 16), new ImageSimulationConfig
            {
                Padding = 0,
                WavelengthsMicrometers = [.55],
                PsfSize = 1,
                NumRays = 2,
                PsfGridRows = 1,
                PsfGridColumns = 1,
                Components = 1,
                AberrationMode = "Partially Coherent",
                UseRelativeIllumination = false
            });
        Assert.Contains("Coherent", result.EffectiveAberrationMode);
        Assert.NotNull(result.CoherentImaging);
        Assert.Equal(0, result.GeometricFallbackCount);
    }

    [Theory]
    [InlineData("Horizontal", 0, 0, 1)]
    [InlineData("Vertical", 0, 0, 3)]
    [InlineData("Both", 0, 0, 1)]
    [InlineData("None", 1, 1, 1)]
    [InlineData("Horizontal", 7, 11, 3)]
    [InlineData("Both", 31, 19, 3)]
    public void StatisticsDescribeTheFinalDisplayedPair(string flip, int width, int height, int channels)
    {
        var result = ImageSimulationEngine.Simulate(Lens(), Source(16, 16, channels: channels), new ImageSimulationConfig
        {
            Padding = 3,
            ImageFlip = flip,
            OutputWidth = width,
            OutputHeight = height,
            WavelengthsMicrometers = [.65, .55, .45],
            PsfSize = 1,
            NumRays = 2,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            Components = 1,
            PixelSizeMillimeters = .01,
            FieldHeight = 1,
            AberrationMode = "None",
            UseRelativeIllumination = false,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        });
        double change = 0;
        for (var c = 0; c < result.Simulated.Channels; c++)
            for (var y = 0; y < result.Simulated.Height; y++)
                for (var x = 0; x < result.Simulated.Width; x++)
                    change += Math.Abs(result.Simulated[c, y, x] - result.Source[Math.Min(c, channels - 1), y, x]);
        Assert.Equal(change / result.Simulated.Values.Length, result.MeanAbsoluteChange, 13);
        Assert.Equal(result.Simulated.Values.Cast<double>().Max(), result.MaximumValue);
    }

    [Fact]
    public void DownsamplingDoesNotReportADiscardedPeak()
    {
        var result = ImageSimulationEngine.Simulate(Lens(), Source(16, 16, impulse: true), new ImageSimulationConfig
        {
            Padding = 0,
            OutputWidth = 1,
            OutputHeight = 1,
            WavelengthsMicrometers = [.55],
            PsfSize = 1,
            NumRays = 2,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            Components = 1,
            AberrationMode = "None",
            UseRelativeIllumination = false,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        });
        Assert.Equal(0, result.Simulated[0, 0, 0]);
        Assert.Equal(0, result.MaximumValue);
        Assert.Equal(0, result.MeanAbsoluteChange);
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(.5)]
    [InlineData(1)]
    public void SupportedMutualCoherencePublishesTheComplexPropagationModel(double coherence)
    {
        var data = new PartiallyCoherentImageAnalysis(Lens(), imageSize: 16, pupilSampling: 4,
            coherence: coherence, guardBand: 0, relativeIllumination: false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Contains("W_ij", (string)data.Values["CoherenceModel"]);
        Assert.Contains("Complex pupil", (string)data.Values["Pipeline"]);
        Assert.Equal(2, data.PlotPanes!.Count);
        Assert.Equal(0, data.Values["GeometricFallbackCount"]);
    }

    [Fact]
    public void ZeroCoherencePublishesTheActualIncoherentResultAndStatistics()
    {
        var data = new PartiallyCoherentImageAnalysis(Lens(), imageSize: 16, pupilSampling: 4,
            coherence: 0, guardBand: 0, relativeIllumination: false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(0d, data.Values["Coherence"]);
        var panes = data.PlotPanes!;
        var source = panes[0].Series[0].Points;
        var simulated = panes[1].Series[0].Points;
        var change = source.Zip(simulated, (a, b) =>
            (Math.Abs(a.Red!.Value - b.Red!.Value) + Math.Abs(a.Green!.Value - b.Green!.Value)
                + Math.Abs(a.Blue!.Value - b.Blue!.Value)) / 3).Average();
        Assert.Equal(change, (double)data.Values["MeanAbsoluteChange"], 12);
    }

    private static Optic Lens()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.Angle };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 10.1,
                InteractionModel = new ThinLensInteractionModel(10) },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static RgbImage Source(int width, int height, bool impulse = false, int channels = 1)
    {
        var values = new double[channels, height, width];
        for (var c = 0; c < channels; c++)
            if (impulse) values[c, height / 2, width / 2] = 1;
            else for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                values[c, y, x] = .1 + .3 * x / (width - 1d) + .2 * y / (height - 1d) + .1 * c;
        return new RgbImage(values);
    }

    private static ImageSimulationConfig Config(int padding, int rotation = 0, int oversampling = 1,
        double pitch = 0, bool illumination = false, string mode = "Geometric") => new()
        {
            Padding = padding,
            SourceRotationDegrees = rotation,
            Oversampling = oversampling,
            PixelSizeMillimeters = pitch,
            FieldHeight = 1,
            WavelengthsMicrometers = [.55],
            PsfSize = 17,
            NumRays = 16,
            PsfGridRows = 3,
            PsfGridColumns = 3,
            Components = 3,
            AberrationMode = mode,
            UseRelativeIllumination = illumination,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        };
}
