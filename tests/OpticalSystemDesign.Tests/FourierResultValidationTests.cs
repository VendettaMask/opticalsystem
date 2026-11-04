using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

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
    [InlineData(FftPhaseSamplingStatus.NoConnectedSamples)] [InlineData(FftPhaseSamplingStatus.UnverifiedNodeOrder)]
    public void UnknownPupilGeometryCannotBeCertified(FftPhaseSamplingStatus status)
    {
        var optic = Optic.CreateBlank(); var result = new FourierResultValidationAnalysis(optic, "test", 8,
            n => new Probe(optic, () => { new FftPupilSampling(status, 0, 0).RequireResolved("test"); return Data(1); }), new(FourierSamplingMode.Adaptive, 32)).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
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
