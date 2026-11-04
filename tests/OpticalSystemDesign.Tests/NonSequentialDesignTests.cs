using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialDesignTests
{
    private static (NonSequentialDocument Doc, Guid Source, Guid Detector) Scene(double x = .3)
    {
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with { LocalCoordinateSystem = new(new(x, 0, 0)) };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, 10)), Parameters = new DetectorRectangleParameters(4, 4, 4, 4) };
        return (new("design control", [new("green", 550, 1, true)], [source, detector]), source.Id, detector.Id);
    }
    private static NonSequentialDesignVariable X(Guid id) => new(id, NonSequentialVariableDomain.Position, "X", -1, 1, .01);
    private static NonSequentialDesignTarget Centre(Guid id, double tolerance = 1e-7) => new(id, NonSequentialMetric.CentroidX, 0, tolerance);

    [Theory]
    [InlineData("Damped Least Squares")] [InlineData("Coordinate Pattern Search")] [InlineData("Nelder-Mead")]
    public void SharedOptimizersCentreTheActualRayInsideOneCoarsePixelAndLeaveSourceUntouched(string optimizer)
    {
        var (doc, source, detector) = Scene(); var materials = Optic.CreateBlank().Materials; var hash = NonSequentialSceneHasher.Compute(doc, materials);
        var result = NonSequentialDesignEngine.Optimize(doc, materials, [X(source)], [Centre(detector, 1e-4)],
            new(Optimizer: optimizer, MaximumIterations: 100, MaximumTraceEvaluations: 200));
        Assert.True(result.Passed, result.Optimizer.Message); Assert.Equal(hash, result.SourceSceneSha256);
        Assert.InRange(Math.Abs(result.BestValues[0]), 0, 1e-4); Assert.Equal(.3, NonSequentialDesignParameters.Read(doc, X(source)));
        Assert.Equal(hash, NonSequentialSceneHasher.Compute(doc, materials)); Assert.Equal("mm", result.ValidationMetrics.Single().Unit);
        Assert.Equal(.3, result.InitialMetrics.Single().Value, 12);
        Assert.Equal(result.Cost.TraceEvaluations, result.Cost.LaunchedSourceRays); Assert.InRange(result.Cost.TraceEvaluations, 2, 200);
    }

    [Fact]
    public void WholeJobBudgetRetainsOnlyMeasuredCandidateAndReservesIndependentValidation()
    {
        var (doc, source, detector) = Scene();
        var result = NonSequentialDesignEngine.Optimize(doc, Optic.CreateBlank().Materials, [X(source)], [Centre(detector)],
            new(MaximumTraceEvaluations: 2, MaximumLaunchedRays: 2));
        Assert.False(result.Passed); Assert.Equal(2, result.Cost.TraceEvaluations); Assert.Equal(2, result.Cost.LaunchedSourceRays);
        Assert.Equal(.3, result.BestValues.Single()); Assert.Equal("LaunchedRayOrTraceBudget", result.Optimizer.StopReason);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => NonSequentialDesignEngine.Optimize(doc, Optic.CreateBlank().Materials,
            [X(source)], [Centre(detector)], cancellationToken: cts.Token));
    }

    [Fact]
    public void UniformManufacturingDistributionGivesAnalyticSensitivityAndDeterministicYieldWithoutClipping()
    {
        var (doc, source, detector) = Scene(0); var materials = Optic.CreateBlank().Materials;
        var tolerance = new NonSequentialToleranceVariable(X(source), .1, NonSequentialToleranceDistribution.Uniform);
        var settings = new NonSequentialDesignSettings(MaximumTraceEvaluations: 1000);
        var first = NonSequentialToleranceEngine.Run(doc, materials, [tolerance], [Centre(detector, .05)], 500, 42, settings);
        var second = NonSequentialToleranceEngine.Run(doc, materials, [tolerance], [Centre(detector, .05)], 500, 42, settings);
        Assert.Equal(1, first.Sensitivities.Single().Derivatives!.Single(), 12); Assert.InRange(first.Yield!.Value, .43, .57);
        Assert.Equal(first.Samples.Select(s => s.Values.Single()), second.Samples.Select(s => s.Values.Single()));
        Assert.Equal(first.Yield, second.Yield); Assert.Equal(503, first.Cost.TraceEvaluations); Assert.Equal(503, first.Cost.LaunchedSourceRays);
        Assert.All(first.Samples, s => { Assert.Null(s.Failure); Assert.Equal(s.Values.Single(), s.Metrics!.Single().Value, 12); });
        Assert.Throws<InvalidOperationException>(() => NonSequentialToleranceEngine.Run(doc, materials, [tolerance], [Centre(detector)],
            500, settings: settings with { MaximumLaunchedRays = 502 }));
    }

    [Fact]
    public void GaussianPhysicallyInvalidSamplesRemainInYieldDenominatorAndSourcePowerIsNotMutated()
    {
        var (doc, source, detector) = Scene(0); var variable = new NonSequentialDesignVariable(source, NonSequentialVariableDomain.ObjectParameter, "PowerWatts", .01, 10, .1);
        var result = NonSequentialToleranceEngine.Run(doc, Optic.CreateBlank().Materials, [new(variable, 2)],
            [new(detector, NonSequentialMetric.TotalPower, 1, 100)], 100, 14, new(MaximumTraceEvaluations: 500));
        var failed = result.Samples.Where(s => s.Failure is not null).ToArray(); Assert.NotEmpty(failed);
        Assert.All(failed, s => { Assert.True(s.Values[0] <= 0); Assert.False(s.Passed); Assert.Null(s.Metrics); });
        Assert.Equal(result.Samples.Count(s => s.Passed) / 100d, result.Yield); Assert.Equal(1, NonSequentialDesignParameters.Read(doc, variable));
        Assert.Contains(result.Samples, s => s.Values[0] > variable.UpperBound || s.Values[0] < variable.LowerBound);
    }

    [Fact]
    public void CompensatorUsesSharedDesignWithOnePreflightedAggregateBudget()
    {
        var (doc, source, detector) = Scene(0); var variable = X(source);
        var result = NonSequentialToleranceEngine.Run(doc, Optic.CreateBlank().Materials,
            [new(variable, .1, NonSequentialToleranceDistribution.Uniform)], [Centre(detector, 1e-5)], 8, 7,
            new(MaximumTraceEvaluations: 100), [variable], compensationTraceEvaluations: 8);
        Assert.Equal(1, result.Yield); Assert.All(result.Samples, s => { Assert.True(s.Passed, s.Failure); Assert.NotNull(s.CompensatedValues); });
        Assert.InRange(result.Cost.TraceEvaluations, 3 + 8, 3 + 8 * 9); Assert.Equal(result.Cost.TraceEvaluations, result.Cost.LaunchedSourceRays);
        Assert.Throws<InvalidOperationException>(() => NonSequentialToleranceEngine.Run(doc, Optic.CreateBlank().Materials,
            [new(variable, .1)], [Centre(detector)], 8, settings: new(MaximumTraceEvaluations: 100, MaximumLaunchedRays: 74),
            compensators: [variable], compensationTraceEvaluations: 8));
    }

    [Fact]
    public void SourceAndPathTargetsUseTheActualFilteredHitStream()
    {
        var (doc, source, detector) = Scene(0);
        var other = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
        { LocalCoordinateSystem = new(new(.7, 0, 0)), Parameters = new SourceRayParameters(2) };
        doc.Insert(1, other);
        var result = NonSequentialDesignEngine.Optimize(doc, Optic.CreateBlank().Materials, [X(source)],
            [new(detector, NonSequentialMetric.TotalPower, 1, 1e-12, PathFilter: "Q1 & D3"), Centre(detector) with { SourceObjectId = source }],
            new(MaximumTraceEvaluations: 2, MaximumLaunchedRays: 4));
        Assert.True(result.Passed); Assert.Equal(1, result.ValidationMetrics[0].Value); Assert.Equal(0, result.ValidationMetrics[1].Value);
        Assert.Equal(4, result.Cost.LaunchedSourceRays);
    }
}
