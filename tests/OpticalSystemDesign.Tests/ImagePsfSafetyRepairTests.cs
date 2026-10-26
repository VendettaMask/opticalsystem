using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ImagePsfSafetyRepairTests
{
    [Fact]
    public void SimulationNormalizesTheNonnegativePointResponseBeforeImageWarping()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        var config = new ImageSimulationConfig
        {
            ImageWidth = 65,
            ImageHeight = 65,
            Padding = 0,
            WavelengthsMicrometers = [.55],
            PsfGridRows = 3,
            PsfGridColumns = 3,
            PsfSize = 33,
            NumRays = 12,
            Components = 3,
            PixelSizeMillimeters = .01,
            AberrationMode = "Geometric",
            UseRelativeIllumination = false,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        };
        var wavelength = new Wavelength { Nanometers = 550 };
        var basis = ImageSimulationEngine.GenerateBasis(optic, wavelength, config);
        var kernel = (double[,])basis.MeanPsf.Clone();
        for (var y = 0; y < 33; y++)
            for (var x = 0; x < 33; x++)
                for (var component = 0; component < basis.EigenPsfs.Length; component++)
                    kernel[y, x] += basis.EigenPsfs[component][y, x] * basis.CoefficientGrid[component, 2, 2];
        Assert.True(kernel.Cast<double>().Min() < -1e-4);
        var positivePower = kernel.Cast<double>().Where(value => value > 0).Sum();
        var expectedResponse = new double[65, 65];
        for (var y = 0; y < 33; y++)
            for (var x = 0; x < 33; x++)
            {
                var targetY = 64 + y - 16;
                var targetX = 64 + x - 16;
                if (targetY < 65 && targetX < 65)
                    expectedResponse[targetY, targetX] = Math.Max(0, kernel[y, x]) / positivePower;
            }
        var grid = ImageSimulationEngine.GenerateDistortionGrid(optic, wavelength, 65, 65, 5, 1,
            referenceWavelength: optic.Wavelengths.First(item => item.IsPrimary));
        var expected = ImageSimulationEngine.WarpImage(expectedResponse, grid);
        var values = new double[1, 65, 65]; values[0, 64, 64] = 1;
        var actual = ImageSimulationEngine.Simulate(optic, new RgbImage(values), config);
        Assert.All(actual.Simulated.Values.Cast<double>(), value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.InRange(expected.Cast<double>().Zip(actual.Simulated.Values.Cast<double>(),
            (a, b) => Math.Abs(a - b)).Max(), 0, 1e-12);
        Assert.Equal(1, actual.PsfIntensityCorrectionCount);
        Assert.True(actual.MaximumPsfNegativeMass > 1e-4);
    }

    [Fact]
    public void IntensityPsfConservesPointPowerWithoutNegativePixels()
    {
        var source = new double[1, 5]; source[0, 2] = 1;
        var result = ImageSimulationEngine.ConvolvePsfIntensity(source,
            [new double[,] { { -.2, .2, 0 } }], Coefficients(5), new double[,] { { 0, .8, .2 } });
        EqualPixels(new double[,] { { 0, 0, 5d / 6, 1d / 6, 0 } }, result.Values);
        Assert.Equal(1, result.Values.Cast<double>().Sum(), 12);
        Assert.Equal(1, result.CorrectedKernelCount);
        Assert.Equal(.2, result.MaximumNegativeMass, 12);
    }

    [Fact]
    public void IntensityPsfRetainsPowerLostOutsideTheFiniteWindow()
    {
        var source = new double[1, 5]; source[0, 4] = 1;
        var result = ImageSimulationEngine.ConvolvePsfIntensity(source,
            [new double[,] { { -.2, .2, 0 } }], Coefficients(5), new double[,] { { 0, .8, .2 } });
        Assert.Equal(5d / 6, result.Values.Cast<double>().Sum(), 12);
        EqualPixels(new double[,] { { 0, 0, 0, 0, 5d / 6 } }, result.Values);
    }

    [Fact]
    public void DifferentSourcePositionsUseTheirOwnUnitPowerPsf()
    {
        var source = new double[1, 7]; source[0, 1] = 2; source[0, 5] = 3;
        var maps = Coefficients(7); maps[0, 0, 1] = 0;
        var result = ImageSimulationEngine.ConvolvePsfIntensity(source,
            [new double[,] { { -.2, .2, 0 } }], maps, new double[,] { { 0, .8, .2 } });
        EqualPixels(new double[,] { { 0, 1.6, .4, 0, 0, 2.5, .5 } }, result.Values);
        Assert.Equal(5, result.Values.Cast<double>().Sum(), 12);
        Assert.Equal(1, result.CorrectedKernelCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AlreadyPhysicalPsfsMatchTheExistingSignedConvolution(int size)
    {
        var source = new double[5, 6];
        var maps = new double[1, 5, 6];
        var mean = new double[size, size];
        var component = new double[size, size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) mean[y, x] = 1d / (size * size);
        component[0, 0] = .02; component[size - 1, size - 1] = -.02;
        for (var y = 0; y < 5; y++) for (var x = 0; x < 6; x++)
        { source[y, x] = .1 + .1 * y + .05 * x; maps[0, y, x] = Math.Sin(x + y); }
        var expected = ImageSimulationEngine.SpatiallyVariableConvolution(source, [component], maps, mean);
        var result = ImageSimulationEngine.ConvolvePsfIntensity(source, [component], maps, mean);
        EqualPixels(expected, result.Values);
        Assert.Equal(0, result.CorrectedKernelCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidReconstructedPsfCannotBecomeAnIdealPoint(double value)
    {
        Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.ConvolvePsfIntensity(
            new double[,] { { 1 } }, [], new double[0, 1, 1], new double[,] { { value } }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSourceIntensityIsExplicitlyRejected(double value)
    {
        Assert.Throws<ArgumentException>(() => ImageSimulationEngine.ConvolvePsfIntensity(
            new double[,] { { value } }, [], new double[0, 1, 1], new double[,] { { 1 } }));
    }

    [Fact]
    public void IntensityConvolutionHonorsCancellationAndTheAdditionalWorkBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.ConvolvePsfIntensity(
            new double[512, 512], [], new double[0, 512, 512], new double[32, 32]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => ImageSimulationEngine.ConvolvePsfIntensity(
            new double[,] { { 1 } }, [], new double[0, 1, 1], new double[,] { { 1 } }));
    }

    [Fact]
    public async Task IntensityConvolutionObservesCancellationDuringKernelReconstruction()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCheckpoint = new ManualResetEventSlim();
        var pending = Task.Factory.StartNew(() =>
        {
            using var scope = ComputationCancellation.Push(cancellation.Token);
            var source = new double[256, 256];
            var kernel = new double[32, 32];
            for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++) source[y, x] = 1;
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) kernel[y, x] = 1d / 1024;
            using var checkpoint = ComputationCancellation.ObserveCheckpoints(() =>
            {
                started.TrySetResult();
                if (!releaseCheckpoint.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            });
            return ImageSimulationEngine.ConvolvePsfIntensity(source, [], new double[0, 256, 256], kernel);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var cancelDuringWork = Task.Factory.StartNew(() =>
        {
            started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            var wasComputing = !pending.IsCompleted;
            cancellation.Cancel();
            releaseCheckpoint.Set();
            return wasComputing;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(await cancelDuringWork);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ExtendedDiffractionReportsTheActualModeAndEveryGeometricFallback()
    {
        var data = new ExtendedDiffractionImageAnalysis(ThinLens(12), imageSize: 16,
            pupilSampling: 8, fieldGrid: 3, guardBand: 0, relativeIllumination: false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("Geometric", data.Values["AberrationMode"]);
        Assert.Equal("Diffraction", data.Values["RequestedAberrationMode"]);
        Assert.Equal("Geometric", data.Values["EffectiveAberrationMode"]);
        Assert.Equal(27, data.Values["GeometricFallbackCount"]);
        Assert.Contains("Geometric", (string)data.Values["Pipeline"]);
    }

    [Fact]
    public void DiffractionWithoutFallbackRetainsItsActualMode()
    {
        var data = new ExtendedDiffractionImageAnalysis(ThinLens(), imageSize: 16,
            pupilSampling: 8, fieldGrid: 2, guardBand: 0, relativeIllumination: false).GenerateData();
        Assert.Equal("Diffraction", data.Values["RequestedAberrationMode"]);
        Assert.Equal("Diffraction", data.Values["EffectiveAberrationMode"]);
        Assert.Equal(0, data.Values["GeometricFallbackCount"]);
    }

    [Fact]
    public void OrdinaryImageSimulationAlsoPublishesRequestedAndActualMode()
    {
        var data = new ImageSimulationAnalysis(ThinLens(12), new ImageSimulationConfig
        {
            ImageWidth = 16,
            ImageHeight = 16,
            Padding = 0,
            WavelengthsMicrometers = [.55],
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 32,
            NumRays = 8,
            Components = 1,
            PixelSizeMillimeters = .02,
            FieldHeight = 1,
            AberrationMode = "Diffraction",
            UseRelativeIllumination = false,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        }).GenerateData();
        Assert.Equal("Diffraction", data.Values["RequestedAberrationMode"]);
        Assert.Equal("Geometric", data.Values["AberrationMode"]);
        Assert.Equal("Geometric", data.Values["EffectiveAberrationMode"]);
        Assert.Equal(1, data.Values["GeometricFallbackCount"]);
        Assert.Contains("PsfIntensityCorrectionCount", data.Values.Keys);
        Assert.Contains("MaximumPsfNegativeMass", data.Values.Keys);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    [InlineData(-90)]
    [InlineData(450)]
    public void OversizedRotatedDetectorIsRejectedBeforeTracing(int rotation)
    {
        var source = new RgbImage(new double[1, 256, 16]);
        var config = SmallConfig(rotation, outputHeight: 2048);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResourceLimits.ValidateImageSimulation(source, config));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    [InlineData(-90)]
    [InlineData(450)]
    public void LegalRotatedDetectorUsesTheRotatedAutomaticWidth(int rotation)
    {
        var source = new RgbImage(new double[1, 16, 256]);
        var config = SmallConfig(rotation, outputHeight: 2048);
        AnalysisResourceLimits.ValidateImageSimulation(source, config);
        var result = ImageSimulationEngine.Simulate(ThinLens(), source, config);
        Assert.Equal(16, result.Simulated.Width);
        Assert.Equal(2048, result.Simulated.Height);
        Assert.True((long)result.Simulated.Width * result.Simulated.Height <= AnalysisResourceLimits.MaximumImagePixels);
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(90, false)]
    [InlineData(270, true)]
    [InlineData(270, false)]
    public void RotatedAutomaticHeightIsAlsoChecked(int rotation, bool oversized)
    {
        var source = oversized ? new RgbImage(new double[1, 16, 256]) : new RgbImage(new double[1, 256, 16]);
        var config = SmallConfig(rotation, outputWidth: 2048);
        if (oversized)
            Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResourceLimits.ValidateImageSimulation(source, config));
        else
        {
            AnalysisResourceLimits.ValidateImageSimulation(source, config);
            var result = ImageSimulationEngine.Simulate(ThinLens(), source, config);
            Assert.Equal(2048, result.Simulated.Width);
            Assert.Equal(16, result.Simulated.Height);
        }
    }

    [Theory]
    [InlineData(0, 72, 40)]
    [InlineData(90, 40, 72)]
    [InlineData(180, 72, 40)]
    [InlineData(270, 40, 72)]
    public void PreparedAndOutputDimensionsIncludeRotationSamplingAndPadding(int rotation, int width, int height)
    {
        var source = new RgbImage(new double[1, 16, 32]);
        var config = new ImageSimulationConfig
        {
            SourceRotationDegrees = rotation,
            Oversampling = 2,
            Padding = 4,
            WavelengthsMicrometers = [.55],
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 1,
            NumRays = 2,
            Components = 1,
            PixelSizeMillimeters = .01,
            AberrationMode = "None",
            UseRelativeIllumination = false,
            DistortionGridSize = 5,
            DistortionPolynomialDegree = 1
        };
        Assert.Equal((width, height, width, height),
            AnalysisResourceLimits.ResolveImageSimulationDimensions(source.Width, source.Height, config));
        var result = ImageSimulationEngine.Simulate(ThinLens(), source, config);
        Assert.Equal(width, result.Simulated.Width);
        Assert.Equal(height, result.Simulated.Height);
    }

    private static double[,,] Coefficients(int width)
    {
        var result = new double[1, 1, width];
        for (var x = 0; x < width; x++) result[0, 0, x] = 1;
        return result;
    }

    private static void EqualPixels(double[,] expected, double[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        Assert.InRange(expected.Cast<double>().Zip(actual.Cast<double>(), (a, b) => Math.Abs(a - b)).Max(), 0, 1e-12);
    }

    private static Optic ThinLens(double imageDistance = 10)
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.Angle };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = imageDistance,
                InteractionModel = new ThinLensInteractionModel(10) },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static ImageSimulationConfig SmallConfig(int rotation = 0, int outputWidth = 0, int outputHeight = 0) => new()
    {
        Padding = 0,
        SourceRotationDegrees = rotation,
        WavelengthsMicrometers = [.55],
        PsfGridRows = 1,
        PsfGridColumns = 1,
        PsfSize = 1,
        NumRays = 2,
        Components = 1,
        PixelSizeMillimeters = .01,
        FieldHeight = 1,
        AberrationMode = "None",
        UseRelativeIllumination = false,
        DistortionGridSize = 5,
        DistortionPolynomialDegree = 1,
        OutputWidth = outputWidth,
        OutputHeight = outputHeight
    };
}
