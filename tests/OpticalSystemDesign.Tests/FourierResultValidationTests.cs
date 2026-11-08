using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FourierResultValidationTests
{
    [Fact]
    public void FixedModeKeepsTheExistingRejectionAndDoesNotRefine()
    {
        var calls = new List<int>(); var optic = Optic.CreateBlank();
        var analysis = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { calls.Add(n); Risk(false).RequireResolved("test"); return Data(1); }), new());
        Assert.Throws<FftPupilSamplingException>(() => analysis.GenerateData());
        Assert.Equal(new[] { 8 }, calls);
    }

    [Theory]
    [InlineData(FourierSamplingMode.Adaptive, 32, 2)]
    [InlineData(FourierSamplingMode.ValidateRequested, 8, 1)]
    public void TwoResolvedReferencesAreRequiredAndOnlyTheSelectedCandidateIsReturned(FourierSamplingMode mode, int returned, double value)
    {
        var optic = Optic.CreateBlank();
        var analysis = new FourierResultValidationAnalysis(optic, "test", 8, n => new Probe(optic, () =>
        { Risk(n > 8).RequireResolved("test"); return Data(n == 8 ? 1 : 2); }), new(mode, 32, .001));
        var result = analysis.GenerateData();
        if (mode == FourierSamplingMode.ValidateRequested)
        { Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome); Assert.Empty(result.PlotSeries); }
        else
        {
            Assert.Equal(value, result.PlotSeries[0].Points[0].Y);
            var report = Report(result); Assert.True(report.Validated); Assert.Equal(returned, report.ReturnedPupilSampling);
            Assert.Equal(new[] { 8, 16, 32 }, report.Steps.Select(s => s.PupilSampling));
        }
    }

    [Fact]
    public void AVerifiedOriginalKeepsItsExactValuesAndDisclosesExtraWork()
    {
        var optic = Optic.CreateBlank(); var original = Data(1.0002);
        var result = new FourierResultValidationAnalysis(optic, "test", 8, n => new Probe(optic, () =>
        { Risk(n > 8).RequireResolved("test"); return n == 8 ? original : Data(1); }), new(FourierSamplingMode.ValidateRequested, 32, .001)).GenerateData();
        Assert.Equal(original.PlotSeries[0].Points, result.PlotSeries[0].Points);
        Assert.Equal(8, Report(result).ReturnedPupilSampling); Assert.Equal(32, Report(result).Steps[^1].PupilSampling);
    }

    [Fact]
    public void EqualAliasedFiniteSumsDoNotBypassThePhaseCheck()
    {
        var optic = Optic.CreateBlank(); var result = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { Risk(false).RequireResolved("test"); return Data(1); }), new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome); Assert.Empty(result.PlotSeries); Assert.False(Report(result).Validated);
    }

    [Fact]
    public void ABudgetWithOnlyOneResolvedReferenceCannotClaimConvergence()
    {
        var optic = Optic.CreateBlank(); var result = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { Risk(n >= 32).RequireResolved("test"); return Data(1); }), new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome); Assert.Equal(3, Report(result).Steps.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdaptiveSamplingCanRecoverAnUnusableCoarseGridWithoutCertifyingTheOriginal(bool insufficientPupilNodes)
    {
        var optic = Optic.CreateBlank();
        BaseAnalysis Create(int sampling) => new Probe(optic, () =>
        {
            Risk(true).RequireResolved("test");
            if (sampling == 8)
            {
                if (insufficientPupilNodes)
                {
                    var failure = new InvalidOperationException("the pupil contains too few nodes");
                    failure.Data[DiffractionEngine.InsufficientFftPupilNodesKey] = true;
                    throw failure;
                }
                throw new AnalysisDataUnavailableException("test", "requested range is outside the Fourier interval")
                { CanRetryAtHigherSampling = true };
            }
            return Data(1);
        });

        var result = new FourierResultValidationAnalysis(optic, "test", 8, Create,
            new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        var report = Report(result);
        Assert.Equal(AnalysisOutcome.Success, result.Outcome);
        Assert.True(report.Validated);
        Assert.Equal(32, report.ReturnedPupilSampling);
        Assert.Equal(new[] { 8, 16, 32 }, report.Steps.Select(step => step.PupilSampling));
        Assert.False(report.Steps[0].PhaseResolved);
        Assert.NotNull(report.Steps[0].UnavailableReason);
        Assert.All(report.Steps.Skip(1), step => Assert.True(step.PhaseResolved));
        Assert.Null(report.RequestedRelativeRmsDeviation);
        Assert.Null(report.RequestedMaximumAbsoluteDeviation);

        var single = new FourierResultValidationAnalysis(optic, "test", 8, Create,
            new(FourierSamplingMode.Adaptive, 16)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, single.Outcome);
        Assert.False(Report(single).Validated);
        var original = new FourierResultValidationAnalysis(optic, "test", 8, Create,
            new(FourierSamplingMode.ValidateRequested, 32));
        if (insufficientPupilNodes)
            Assert.Throws<InvalidOperationException>(() => original.GenerateData());
        else
            Assert.Throws<AnalysisDataUnavailableException>(() => original.GenerateData());
    }

    [Theory]
    [InlineData(FftPhaseSamplingStatus.NoConnectedSamples)] [InlineData(FftPhaseSamplingStatus.UnverifiedNodeOrder)]
    public void UnknownPupilGeometryCannotBeCertified(FftPhaseSamplingStatus status)
    {
        var optic = Optic.CreateBlank(); var result = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { new FftPupilSampling(status, 0, 0).RequireResolved("test"); return Data(1); }), new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
    }

    [Theory]
    [InlineData("Adaptive", 128, AnalysisOutcome.Unavailable)]
    [InlineData("Adaptive", 256, AnalysisOutcome.Success)]
    [InlineData("ValidateRequested", 256, AnalysisOutcome.Unavailable)]
    public async Task FocalPsfRetriesADisconnectedPupilOnlyInAdaptiveMode(
        string mode, int maximumSampling, AnalysisOutcome expected)
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src", "OpticalSystemDesign.App",
            "Assets", "SequentialExamples", "Telescopes", "Hubble.zmx");
        var optic = await new ZemaxZmxImporter().ImportFileAsync(source);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("PSF", new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["Display"] = "128", ["ImageDeltaMicrometers"] = "0.25",
            ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["FourierSamplingMode"] = mode,
            ["FourierMaximumSampling"] = maximumSampling.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FourierRelativeAccuracy"] = "0.01"
        });

        Assert.Equal(expected, data.Outcome);
        var report = Report(data);
        Assert.Null(report.RequestedRelativeRmsDeviation);
        Assert.Null(report.RequestedMaximumAbsoluteDeviation);
        if (mode == "ValidateRequested")
        {
            Assert.Empty(report.Steps);
            return;
        }
        var coarse = report.Steps[0];
        Assert.Equal(64, coarse.PupilSampling);
        Assert.False(coarse.PhaseResolved);
        Assert.NotNull(coarse.UnavailableReason);
        Assert.Null(report.Steps[1].RelativeRmsChange);
        Assert.True(report.Steps[1].PhaseResolved);
        if (expected == AnalysisOutcome.Unavailable)
        {
            Assert.False(report.Validated);
            Assert.Empty(data.PlotSeries);
            return;
        }
        Assert.True(report.Validated);
        Assert.Equal(256, report.ReturnedPupilSampling);
        var pixels = Assert.Single(data.PlotSeries).Points;
        Assert.Equal(16384, pixels.Count);
        Assert.Equal((-15.75, -15.75), (pixels[0].X, pixels[0].Y));
        Assert.Equal((16.0, 16.0), (pixels[^1].X, pixels[^1].Y));
    }

    [Theory]
    [InlineData("Image Simulation", "Example 1, A singlet eyepiece.ZMX", "2", true)]
    [InlineData("Telescopes", "Unobscured Gregorian.zmx", "1", false)]
    public async Task FixedPsfEvaluatesTheFineImageWindowAtTheRequestedPupilDensity(
        string category, string lens, string wavelength, bool aimAtStop)
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src", "OpticalSystemDesign.App",
            "Assets", "SequentialExamples", category, lens);
        var optic = await new ZemaxZmxImporter().ImportFileAsync(source);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("PSF", new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["Display"] = "128", ["ImageDeltaMicrometers"] = "0.25",
            ["WavelengthNumber"] = wavelength, ["FieldNumber"] = "1", ["UseRayAiming"] = aimAtStop.ToString(),
            ["FourierSamplingMode"] = "Fixed"
        });

        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(64, data.Values["PupilSampling"]);
        Assert.Equal(true, data.Values["PhysicalFourierWindow"]);
        Assert.Equal(new[] { aimAtStop }, Assert.IsType<bool[]>(data.Values["UseRayAiming"]));
        Assert.False(data.Values.ContainsKey("FourierSamplingValidation"));
        var pixels = Assert.Single(data.PlotSeries).Points;
        Assert.Equal(128 * 128, pixels.Count);
        for (var index = 0; index < pixels.Count; index++)
        {
            var pixel = pixels[index];
            Assert.Equal(((index % 128 - 63) * .25, (index / 128 - 63) * .25), (pixel.X, pixel.Y));
            Assert.True(pixel.Value is >= 0 and <= 1.000000000001);
        }
        Assert.True(data.Values["StrehlRatio"] is double and > 0);
    }

    [Fact]
    public async Task AimedSingletConvergesOnTheOriginalFinePsfWindow()
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src", "OpticalSystemDesign.App",
            "Assets", "SequentialExamples", "Image Simulation", "Example 1, A singlet eyepiece.ZMX");
        var optic = await new ZemaxZmxImporter().ImportFileAsync(source);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("PSF", new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["Display"] = "128", ["ImageDeltaMicrometers"] = "0.25",
            ["WavelengthNumber"] = "2", ["FieldNumber"] = "1", ["UseRayAiming"] = "True",
            ["FourierSamplingMode"] = "Adaptive", ["FourierMaximumSampling"] = "2048"
        });

        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        var report = Report(data);
        Assert.True(report.Validated);
        var physicalSteps = report.Steps.Where(step => step.UsesPhysicalPsfWindow).ToArray();
        Assert.Equal(new[] { 512, 1024 }, physicalSteps.Select(step => step.PupilSampling));
        Assert.All(physicalSteps, step => Assert.True(step.PhaseResolved));
        Assert.Null(report.RequestedRelativeRmsDeviation);
        var pixels = Assert.Single(data.PlotSeries).Points;
        Assert.Equal(16384, pixels.Count);
        Assert.Equal((-15.75, -15.75), (pixels[0].X, pixels[0].Y));
        Assert.Equal((16.0, 16.0), (pixels[^1].X, pixels[^1].Y));
    }

    [Fact]
    public async Task DefaultWorkbenchMtfRecoversMeasuredPhaseRiskWithinTheSelectedBudget()
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src", "OpticalSystemDesign.App",
            "Assets", "SequentialExamples", "Tilted systems & prisms", "Lens pivoted about a point.zmx");
        var runtime = new WorkbenchRuntime(await new ZemaxZmxImporter().ImportFileAsync(source));
        var settings = runtime.MergeAnalysisSettings("MTF", new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["MaximumFrequency"] = "50", ["WavelengthNumber"] = "2", ["FieldNumber"] = "1",
            ["SurfaceNumber"] = "-1", ["UseRayAiming"] = "False"
        });
        Assert.Equal("按需加密", settings["FourierSamplingMode"]);
        var data = runtime.BuildAnalysisData("MTF", settings);
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        var report = Report(data);
        Assert.True(report.Validated);
        Assert.Equal(64, report.RequestedPupilSampling);
        Assert.True(report.ReturnedPupilSampling > 64);
        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase,
            Assert.IsType<FftPupilSampling>(data.Values["RequestedPupilPhaseSampling"]).Status);
        Assert.Equal(2, data.PlotSeries.Count);
        Assert.All(data.PlotSeries, series =>
        {
            Assert.Equal(300, series.Points.Count);
            Assert.Equal(0, series.Points[0].X);
            Assert.Equal(50, series.Points[^1].X);
        });

        settings["FourierMaximumSampling"] = "64";
        var insufficient = runtime.BuildAnalysisData("MTF", settings);
        Assert.Equal(AnalysisOutcome.Unavailable, insufficient.Outcome);
        Assert.Empty(insufficient.PlotSeries);
        Assert.False(insufficient.Values.ContainsKey("FourierSamplingValidation"));
    }

    [Fact]
    public void AConvergedPlotCannotHideAnUnconvergedReportedStrehl()
    {
        var optic = Optic.CreateBlank(); var result = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { Risk(true).RequireResolved("test"); return Data(1) with { Values = new Dictionary<string, object> { ["StrehlRatio"] = n } }; }),
            new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public void PhysicalCurveProjectionCoversEveryOriginalNodeAndNeverExtrapolates()
    {
        var domain = Data(0); var source = Data(0) with { SeriesList = [Data(0).PlotSeries[0] with { Points = [new(-1, -2), new(0, 0), new(2, 4)] }] };
        var actual = FourierResultValidationAnalysis.Project(source, domain);
        Assert.Equal(new[] { 0d, 2d }, actual.PlotSeries[0].Points.Select(p => p.Y));
        Assert.Equal(domain.PlotSeries[0].Points.Select(p => p.X), actual.PlotSeries[0].Points.Select(p => p.X));
        Assert.Throws<InvalidOperationException>(() => FourierResultValidationAnalysis.Project(Data(1), source));
    }

    [Fact]
    public void PhysicalGridChangesAndUnitChangesAreRejected()
    {
        var image = Data(1) with { SeriesList = [new("x", "y", [new(0, 0, Value: 1), new(1, 0, Value: 1)], AnalysisSeriesKind.Heatmap,
            XQuantity: AnalysisAxisQuantity.ImageHeight, XUnit: AnalysisAxisUnit.Micrometer, YQuantity: AnalysisAxisQuantity.ImageHeight,
            YUnit: AnalysisAxisUnit.Micrometer, ValueQuantity: AnalysisAxisQuantity.Irradiance, ValueUnit: AnalysisAxisUnit.Dimensionless)] };
        var shifted = image with { SeriesList = [image.PlotSeries[0] with { Points = [new(.01, 0, Value: 1), new(1.01, 0, Value: 1)] }] };
        Assert.Throws<InvalidOperationException>(() => FourierResultValidationAnalysis.Project(shifted, image));
        Assert.Throws<InvalidOperationException>(() => FourierResultValidationAnalysis.Project(Data(1) with { SeriesList = [Data(1).PlotSeries[0] with { XUnit = AnalysisAxisUnit.Millimeter }] }, Data(1)));
    }

    [Fact]
    public void ObservationScopesRestoreStrictRejectionAfterExceptions()
    {
        var observed = new List<FftPupilSampling>();
        Assert.Throws<InvalidOperationException>((Action)(() => { using var scope = FftPupilSampling.ObserveForValidation(observed); Risk(false).RequireResolved("test"); throw new InvalidOperationException(); }));
        Assert.Single(observed); Assert.Throws<FftPupilSamplingException>(() => Risk(false).RequireResolved("test"));
    }

    [Fact]
    public void CancellationIsPreservedAndDoesNotLeakObservationScopes()
    {
        var optic = Optic.CreateBlank(); var source = new CancellationTokenSource(); source.Cancel();
        var analysis = new FourierResultValidationAnalysis(optic, "test", 8, _ => new Probe(optic, () => Data(1)), new(FourierSamplingMode.Adaptive, 32));
        Assert.ThrowsAny<OperationCanceledException>(() => analysis.GenerateData(source.Token));
        Assert.Throws<FftPupilSamplingException>(() => Risk(false).RequireResolved("test"));
    }

    [Fact]
    public void AggregateWorkIsBoundedBeforeStartingAnotherGrid()
    {
        var optic = Optic.CreateBlank(); var calls = 0;
        var result = new FourierResultValidationAnalysis(optic, "test", 8, _ => new Probe(optic, () =>
        { calls++; Risk(true).RequireResolved("test"); return Data(1); }), new(FourierSamplingMode.Adaptive, 32, MaximumNominalPupilCells: 200)).GenerateData();
        Assert.Equal(1, calls); Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Equal(128, Report(result).EvaluatedNominalPupilCells);
    }

    [Fact]
    public void ASourceMutationCannotReceiveAConvergenceCertificate()
    {
        var optic = Optic.CreateBlank();
        var result = new FourierResultValidationAnalysis(optic, "test", 8, n => new Probe(optic, () =>
        { Risk(true).RequireResolved("test"); optic.Aperture.Value += 1; return Data(1); }), new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome); Assert.Contains("source changed", result.OutcomeReason);
    }

    [Fact]
    public async Task ParallelPupilObservationsKeepEveryRejectionAndRestoreTheParentScope()
    {
        var observed = new List<FftPupilSampling>();
        using (FftPupilSampling.ObserveForValidation(observed))
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            { for (var i = 0; i < 100; i++) Risk(false).RequireResolved("parallel"); })));
        Assert.Equal(800, observed.Count);
        Assert.All(observed, p => Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase, p.Status));
        Assert.Throws<FftPupilSamplingException>(() => Risk(false).RequireResolved("test"));
    }

    static FourierValidationReport Report(AnalysisData data) => Assert.IsType<FourierValidationReport>(data.Values["FourierSamplingValidation"]);
    static FftPupilSampling Risk(bool resolved) => new(resolved ? FftPhaseSamplingStatus.ResolvedAtNodes : FftPhaseSamplingStatus.UndersampledPhase, 8, resolved ? .1 : .75);
    static AnalysisData Data(double y) => new("test", new Dictionary<string, object>(), SeriesList: [new("x", "y", [new(0, y), new(1, y)],
        XQuantity: AnalysisAxisQuantity.ImageHeight, XUnit: AnalysisAxisUnit.Micrometer, YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless)]);
    sealed class Probe(Optic optic, Func<AnalysisData> execute) : BaseAnalysis(optic) { public override string Name => "test"; public override AnalysisData GenerateData() => execute(); }
}
