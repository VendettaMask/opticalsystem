using System.Numerics;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class PartiallyCoherentImagingTests
{
    [Theory]
    [InlineData(0, 0, .5)]
    [InlineData(.5, 0, .75)]
    [InlineData(1, 0, 1)]
    [InlineData(0, Math.PI, .5)]
    [InlineData(.5, Math.PI, .25)]
    [InlineData(1, Math.PI, 0)]
    [InlineData(.7, Math.PI / 2, .5)]
    public void TwoPointsProduceTheMutualCoherenceInterferenceTerm(double gamma, double relativePhase, double expected)
    {
        var source = new double[,] { { 0, 1, 0, 1, 0 } };
        var phase = new double[,] { { 0, 0, 0, relativePhase, 0 } };
        var h = new Complex[,] { { .5, .5, .5 } };
        var actual = MutualIntensityEngine.PropagateUniformCoherence(source, h, gamma, phase);
        Assert.Equal(expected, actual[0, 2], 13);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.2)]
    [InlineData(.7)]
    [InlineData(1)]
    public void PropagationMatchesAnIndependentFullMutualIntensityDoubleSum(double gamma)
    {
        var intensity = new double[,] { { .2, .9, 0 }, { .7, .3, .4 } };
        var phase = new double[,] { { .2, 1.1, -.4 }, { 2, -.8, .3 } };
        var kernel = new Complex[,] { { new(.1, .2), new(-.3, .2) }, { new(.4, -.2), new(.1, .3) } };
        var actual = MutualIntensityEngine.PropagateUniformCoherence(intensity, kernel, gamma, phase);
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
        {
            Complex expected = 0;
            for (var iy = 0; iy < 2; iy++) for (var ix = 0; ix < 3; ix++)
                for (var jy = 0; jy < 2; jy++) for (var jx = 0; jx < 3; jx++)
                {
                    var i = Complex.FromPolarCoordinates(Math.Sqrt(intensity[iy, ix]), phase[iy, ix]);
                    var j = Complex.FromPolarCoordinates(Math.Sqrt(intensity[jy, jx]), phase[jy, jx]);
                    var w = gamma * i * Complex.Conjugate(j);
                    if (ix == jx && iy == jy) w += (1 - gamma) * intensity[iy, ix];
                    expected += w * Response(y - iy, x - ix) * Complex.Conjugate(Response(y - jy, x - jx));
                }
            Assert.InRange(Math.Abs(expected.Imaginary), 0, 1e-14);
            Assert.Equal(expected.Real, actual[y, x], 13);
        }
        Complex Response(int y, int x) => y is >= 0 and < 2 && x is >= 0 and < 2 ? kernel[y, x] : Complex.Zero;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void PropagationPreservesKernelGainAndFiniteWindowLoss(double gamma)
    {
        var source = new double[3, 5]; source[1, 4] = 2;
        var kernel = new Complex[,] { { .2, new(.3, .4), .1 } };
        var image = MutualIntensityEngine.PropagateUniformCoherence(source, kernel, gamma);
        Assert.Equal(2 * (.04 + .25), image.Cast<double>().Sum(), 13);
        var attenuated = new Complex[,] { { .1, new(.15, .2), .05 } };
        var lower = MutualIntensityEngine.PropagateUniformCoherence(source, attenuated, gamma);
        Assert.Equal(image.Cast<double>().Sum() / 4, lower.Cast<double>().Sum(), 13);
    }

    [Fact]
    public void ComplexPupilTiltRetainsPhysicalImageAxesAndPhase()
    {
        const int sampling = 64;
        var pupil = new Complex[sampling, sampling];
        for (var y = 0; y < sampling; y++) for (var x = 0; x < sampling; x++)
        {
            var px = -1 + (2d * x + 1) / sampling;
            var py = -1 + (2d * y + 1) / sampling;
            pupil[y, x] = Complex.FromPolarCoordinates(1, Math.PI * (3 * px + 2 * py) + .7);
        }
        var response = DiffractionEngine.CreateCoherentImageKernel(pupil, 8, 8, .5, .004, 33);
        var peak = response.Values.Cast<Complex>().Select((value, index) => (Power: value.Magnitude, Index: index)).MaxBy(item => item.Power);
        Assert.Equal(16 - 2, peak.Index / 33);
        Assert.Equal(16 + 3, peak.Index % 33);
        Assert.InRange(Math.Abs(response.Values[14, 19].Imaginary), .2, 1);
        Assert.InRange(response.CapturedPowerFraction, .95, 1);
    }

    [Fact]
    public void ConstantTransferPassesARepresentableObjectWithoutIntensityRescaling()
    {
        var pupil = new Complex[8, 8];
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) pupil[y, x] = Complex.FromPolarCoordinates(2, .6);
        var response = DiffractionEngine.CreateCoherentImageKernel(pupil, 2, 2, .5, .1, 17);
        Assert.Equal(1, response.KernelPower, 12);
        Assert.Equal(1, response.CapturedPowerFraction, 12);
        var source = new double[,] { { .2, .8, .3 }, { .5, .1, .7 } };
        var image = MutualIntensityEngine.PropagateUniformCoherence(source, response.Values, .6);
        Assert.All(source.Cast<double>().Zip(image.Cast<double>()), pair => Assert.Equal(pair.First, pair.Second, 12));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void OriginalPrimaryLensUsesComplexPropagationAndReportsTheModel(double gamma)
    {
        var optic = Primary();
        var source = PairSource();
        var result = ImageSimulationEngine.Simulate(optic, source, Config(gamma));
        Assert.Contains("Coherent", result.EffectiveAberrationMode);
        Assert.Equal(0, result.GeometricFallbackCount);
        Assert.Equal(0, result.PsfIntensityCorrectionCount);
        Assert.NotNull(result.CoherentImaging);
        Assert.Equal(gamma, result.CoherentImaging.SpatialCoherence);
        Assert.InRange(result.CoherentImaging.MinimumKernelCapturedFraction, 0, 1);
        Assert.All(result.Simulated.Values.Cast<double>(), value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.True(result.Simulated.Values.Cast<double>().Zip(result.Source.Values.Cast<double>()).Max(pair => Math.Abs(pair.First - pair.Second)) > .01);
    }

    [Fact]
    public void OriginalPrimaryLensPhaseChangesCoherentImageButNotIncoherentImage()
    {
        var optic = Primary();
        var source = PairSource();
        var zero = new double[1, 24, 24];
        var shifted = new double[1, 24, 24]; shifted[0, 12, 13] = Math.PI;
        var c0 = ImageSimulationEngine.Simulate(optic, source, Config(1, zero));
        var c1 = ImageSimulationEngine.Simulate(optic, source, Config(1, shifted));
        Assert.True(Difference(c0, c1) > 1e-5);
        var i0 = ImageSimulationEngine.Simulate(optic, source, Config(0, zero));
        var i1 = ImageSimulationEngine.Simulate(optic, source, Config(0, shifted));
        Assert.Equal(0, Difference(i0, i1));
        var half = ImageSimulationEngine.Simulate(optic, source, Config(.5, shifted));
        Assert.All(half.Simulated.Values.Cast<double>().Zip(c1.Simulated.Values.Cast<double>().Zip(i1.Simulated.Values.Cast<double>())),
            item => Assert.Equal((item.Second.First + item.Second.Second) / 2, item.First, 12));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void ExtendedAnalysisPublishesSuccessAndActualFinalStatistics(double gamma)
    {
        var data = new PartiallyCoherentImageAnalysis(Primary(), imageSize: 16, pupilSampling: 8,
            coherence: gamma, fieldHeight: .01, guardBand: 4, relativeIllumination: false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(gamma, data.Values["Coherence"]);
        Assert.Equal("1 x 1 (isoplanatic)", data.Values["FieldGrid"]);
        Assert.Contains("W_ij", (string)data.Values["CoherenceModel"]);
        var source = data.PlotPanes![0].Series[0].Points;
        var output = data.PlotPanes[1].Series[0].Points;
        var difference = source.Zip(output, (a, b) => (Math.Abs(a.Red!.Value - b.Red!.Value)
            + Math.Abs(a.Green!.Value - b.Green!.Value) + Math.Abs(a.Blue!.Value - b.Blue!.Value)) / 3).Average();
        Assert.Equal(difference, (double)data.Values["MeanAbsoluteChange"], 12);
    }

    [Theory]
    [InlineData(-.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidSourceCoherenceIsRejected(double gamma) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        MutualIntensityEngine.PropagateUniformCoherence(new double[,] { { 1 } }, new Complex[,] { { 1 } }, gamma));

    [Fact]
    public void MissingVectorCoatingPhaseRemainsExplicitlyUnavailable()
    {
        var optic = Primary();
        optic.SurfaceGroup.Items[1].CoatingModel = new ApproximateTransmissionRippleCoating([new ThinFilmLayer("MgF2", 120)]);
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.Simulate(optic, PairSource(), Config(.5)));
        Assert.Contains("phase-bearing coatings", exception.Reason);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(250, false)]
    public void FinitePupilPassesAndRejectsCoherentObjectSpatialFrequencies(double frequency, bool transmitted)
    {
        var pupil = new Complex[64, 64];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) pupil[y, x] = 1;
        // lambda=.5 um, F/8: coherent cutoff 125 cycles/mm; sampled Nyquist 500 cycles/mm.
        var kernel = DiffractionEngine.CreateCoherentImageKernel(pupil, 8, 8, .5, .001, 33);
        var source = new double[64, 64];
        var phase = new double[64, 64];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
        { source[y, x] = 1; phase[y, x] = 2 * Math.PI * frequency * .001 * x; }
        var image = MutualIntensityEngine.PropagateUniformCoherence(source, kernel.Values, 1, phase);
        if (transmitted) Assert.InRange(image[32, 32], .8, 1.2);
        else Assert.InRange(image[32, 32], 0, .01);
    }

    [Fact]
    public void UniformSourcePhaseCannotChangeDetectedIntensity()
    {
        var source = new double[,] { { .2, .8, .4 } };
        var phase = new double[,] { { 1.7, 1.7, 1.7 } };
        var kernel = new Complex[,] { { new(.1, -.3), .5, new(.2, .1) } };
        var zero = MutualIntensityEngine.PropagateUniformCoherence(source, kernel, .6);
        var shifted = MutualIntensityEngine.PropagateUniformCoherence(source, kernel, .6, phase);
        Assert.All(zero.Cast<double>().Zip(shifted.Cast<double>()), pair => Assert.Equal(pair.First, pair.Second, 13));
    }

    [Fact]
    public void PrimaryLensGuardBandOnlyExtendsTheSameComplexImageWindow()
    {
        var optic = Primary();
        var source = PairSource();
        var a = ImageSimulationEngine.Simulate(optic, source, Config(.6, padding: 0));
        var b = ImageSimulationEngine.Simulate(optic, source, Config(.6, padding: 8));
        for (var y = 0; y < 24; y++) for (var x = 0; x < 24; x++)
            Assert.Equal(a.Simulated[0, y, x], b.Simulated[0, y + 8, x + 8]);
        Assert.Equal(a.CoherentImaging!.DetectorPixelPitchMillimeters, b.CoherentImaging!.DetectorPixelPitchMillimeters);
    }

    [Fact]
    public void SourcePhaseRotatesAndOversamplesWithItsIntensity()
    {
        var source = PairSource();
        var phase = new double[1, 24, 24]; phase[0, 12, 13] = 1.7;
        var actual = ImageSimulationEngine.Simulate(Primary(), source, Config(.7, phase, rotation: 90, oversampling: 2));
        double[,,] Prepare(double[,,] values)
        {
            var result = new double[1, 48, 48];
            for (var y = 0; y < 24; y++) for (var x = 0; x < 24; x++)
                for (var iy = 0; iy < 2; iy++) for (var ix = 0; ix < 2; ix++)
                    result[0, 2 * x + iy, 2 * (23 - y) + ix] = values[0, y, x];
            return result;
        }
        var expected = ImageSimulationEngine.Simulate(Primary(), new RgbImage(Prepare(source.Values)), Config(.7, Prepare(phase)));
        Assert.Equal(0, Difference(expected, actual));
    }

    [Fact]
    public void SpectralChannelsPropagateIndependentlyWithTheirOwnComplexPupil()
    {
        var source = PairSource();
        var waves = new[] { .65, .55, .45 };
        var spectrum = ImageSimulationEngine.Simulate(Primary(), source, Config(.4, waves: waves));
        for (var c = 0; c < 3; c++)
        {
            var single = ImageSimulationEngine.Simulate(Primary(), source, Config(.4, waves: [waves[c]]));
            for (var y = 0; y < spectrum.Simulated.Height; y++) for (var x = 0; x < spectrum.Simulated.Width; x++)
                Assert.Equal(single.Simulated[0, y, x], spectrum.Simulated[c, y, x]);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UnsupportedVectorOrFieldVaryingSettingsAreNotSilentlyIgnored(bool polarization, bool varying)
    {
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.Simulate(
            Primary(), PairSource(), Config(.5, polarization: polarization, fieldGrid: varying ? 3 : 1)));
        Assert.Contains("scalar isoplanatic", exception.Reason);
    }

    [Fact]
    public void AfocalComplexImagingRemainsAnExplicitCapabilityBoundary()
    {
        var optic = Primary(); optic.ImageSpaceAfocal = true;
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.Simulate(optic, PairSource(), Config(.5)));
        Assert.Contains("afocal", exception.Reason);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void NonphysicalSourceIntensityIsNeverConvertedIntoAValidImage(double value) => Assert.Throws<ArgumentException>(() =>
        MutualIntensityEngine.PropagateUniformCoherence(new double[,] { { value } }, new Complex[,] { { 1 } }, .5));

    [Fact]
    public void NonfinitePhaseAtAnUnlitPixelIsStillRejected()
    {
        Assert.Throws<ArgumentException>(() => MutualIntensityEngine.PropagateUniformCoherence(
            new double[,] { { 0, 1 } }, new Complex[,] { { 1 } }, 0, new double[,] { { double.NaN, 0 } }));
    }

    [Fact]
    public void FourierWorkIsBoundedAcrossTheEntireSpectrumBeforeTracing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResourceLimits.ValidateImageSimulation(PairSource(),
            new ImageSimulationConfig
            {
                AberrationMode = "Partially Coherent",
                PsfGridRows = 1,
                PsfGridColumns = 1,
                NumRays = 512,
                PsfSize = 1,
                Padding = 0,
                WavelengthsMicrometers = Enumerable.Repeat(.55, 16).ToArray()
            }));
    }

    [Fact]
    public void ComplexPropagationHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => MutualIntensityEngine.PropagateUniformCoherence(
            new double[,] { { 1 } }, new Complex[,] { { 1 } }, .5));
    }

    [Fact]
    public void LargerKernelWindowRetainsMorePowerWithoutChangingItsSamples()
    {
        var pupil = new Complex[32, 32];
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) pupil[y, x] = 1;
        var small = DiffractionEngine.CreateCoherentImageKernel(pupil, 8, 8, .5, .001, 17, 256);
        var large = DiffractionEngine.CreateCoherentImageKernel(pupil, 8, 8, .5, .001, 65, 256);
        Assert.Equal(small.FullKernelPower, large.FullKernelPower);
        Assert.True(large.KernelPower > small.KernelPower);
        Assert.True(large.CapturedPowerFraction > small.CapturedPowerFraction);
        for (var y = 0; y < 17; y++) for (var x = 0; x < 17; x++)
            Assert.Equal(small.Values[y, x], large.Values[y + 24, x + 24]);
    }

    [Fact]
    public void WorkbenchForwardsKernelAndPhysicalPixelSettingsToTheComplexModel()
    {
        var runtime = new WorkbenchRuntime(Primary());
        var parameters = runtime.GetAnalysisParameters("Partially Coherent Image Analysis");
        Assert.Contains(parameters, parameter => parameter.Key == "PsfSize");
        Assert.Contains(parameters, parameter => parameter.Key == "PixelSize");
        var data = runtime.BuildAnalysisData("Partially Coherent Image Analysis", new Dictionary<string, string>
        {
            ["ImageSize"] = "16",
            ["PupilSampling"] = "8 x 8",
            ["Coherence"] = "0.7",
            ["PsfSize"] = "16",
            ["PixelSize"] = "0.001",
            ["GuardBand"] = "4",
            ["FieldHeight"] = "0.01",
            ["RelativeIllumination"] = "false"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(16, data.Values["PsfSize"]);
        Assert.Equal(.001, data.Values["DetectorPixelPitchMillimeters"]);
        Assert.Equal(.7, data.Values["Coherence"]);
    }

    [Theory]
    [InlineData(-.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPhysicalPixelPitchCannotSilentlyBecomeAutomatic(double pitch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.Simulate(Primary(), PairSource(),
            new ImageSimulationConfig
            {
                AberrationMode = "Partially Coherent",
                PsfGridRows = 1,
                PsfGridColumns = 1,
                PixelSizeMillimeters = pitch,
                UseRelativeIllumination = false
            }));
    }

    private static Optic Primary() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");

    private static RgbImage PairSource()
    {
        var values = new double[1, 24, 24]; values[0, 12, 11] = 1; values[0, 12, 13] = 1;
        return new RgbImage(values);
    }

    private static ImageSimulationConfig Config(double coherence, double[,,]? phase = null, int padding = 8,
        int rotation = 0, int oversampling = 1, IReadOnlyList<double>? waves = null, bool polarization = false, int fieldGrid = 1) => new()
        {
            ImageWidth = 24,
            ImageHeight = 24,
            PsfGridRows = fieldGrid,
            PsfGridColumns = fieldGrid,
            Components = 1,
            PsfSize = 33,
            NumRays = 16,
            Padding = padding,
            WavelengthsMicrometers = waves ?? [.55],
            FieldHeight = .01,
            SourceRotationDegrees = rotation,
            Oversampling = oversampling,
            UsePolarization = polarization,
            PixelSizeMillimeters = .001,
            AberrationMode = "Partially Coherent",
            SpatialCoherence = coherence,
            SourcePhaseRadians = phase,
            UseRelativeIllumination = false
        };

    private static double Difference(ImageSimulationResult a, ImageSimulationResult b) =>
        a.Simulated.Values.Cast<double>().Zip(b.Simulated.Values.Cast<double>(), (x, y) => Math.Abs(x - y)).Max();
}
