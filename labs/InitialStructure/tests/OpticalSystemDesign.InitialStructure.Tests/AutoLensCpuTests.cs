using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutoLensCpuTests(ITestOutputHelper output)
{
    internal static InitialStructureSpecification Specification(int budget = 160)
    {
        var spec = FlatStartRefinementTests.Spec(budget);
        return spec with
        {
            FlatStart = spec.FlatStart! with
            {
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                OptimizationMethod = FlatStartOptimizationMethod.AutoLensCpuExperimentV1,
                AutomaticLensDiameters = true,
                UsePhysicalStop = true
            }
        };
    }

    [Fact]
    public void CurriculumUsesUpstreamHalfRadiusCosineNotQuarterRadiusAndEndsAtFullTarget()
    {
        var stages = AutoLensCpuCurriculum.Stages;
        Assert.Equal(5, stages.Count);
        Assert.Equal(.5, stages[0].PupilFraction);
        Assert.Equal(.75, stages[2].PupilFraction, 14);
        Assert.Equal(FlatStartDesignProblem.FullStage, stages[^1]);
        Assert.All(stages, stage => { Assert.Equal(1, stage.FieldFraction); Assert.True(stage.AllWavelengths); });
        Assert.True(stages.Zip(stages.Skip(1)).All(pair => pair.First.PupilFraction < pair.Second.PupilFraction));
    }

    [Theory]
    [InlineData(-.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidCurriculumProgressIsRejected(double progress) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoLensCpuCurriculum.ApertureFraction(progress));

    [Fact]
    public void LegacySerializationAndDefaultAlgorithmIdentityStayUnchanged()
    {
        var spec = FlatStartRefinementTests.Spec();
        Assert.DoesNotContain("OptimizationMethod", JsonSerializer.Serialize(spec));
        Assert.Equal(ContentFingerprint.Compute(spec), ContentFingerprint.Compute(spec with
        { FlatStart = spec.FlatStart! with { OptimizationMethod = FlatStartOptimizationMethod.TrustRegion } }));
        Assert.Equal("strict-flat-design", FlatStartAlgorithm.Name("strict-flat-design", spec));
        Assert.NotEqual(ContentFingerprint.Compute(spec), ContentFingerprint.Compute(Specification()));
    }

    [Theory]
    [InlineData(-1, FlatStartSamplingPolicy.UniformAreaGaussianV1)]
    [InlineData(2, FlatStartSamplingPolicy.UniformAreaGaussianV1)]
    [InlineData(1, FlatStartSamplingPolicy.LegacyEqualRings)]
    public void InvalidOrLegacyExperimentConfigurationIsRejected(int method, FlatStartSamplingPolicy sampling)
    {
        var spec = Specification();
        Assert.Throws<InitialStructureSpecificationException>(() => SpecificationValidator.Validate(spec with
        { FlatStart = spec.FlatStart! with { OptimizationMethod = (FlatStartOptimizationMethod)method, SamplingPolicy = sampling } }));
    }

    [Fact]
    public void AdamMeasuresDecreaseAndChargesAllCoreCallbackWorkWithoutPublishingProbes()
    {
        var calls = 0;
        var costs = new List<double> { .64 };
        var result = BoundedAdam.Solve([0.0], [0.0], [1.0], point =>
        { calls++; return new([point[0] - .8], WorkUnits: 7); }, 500,
            acceptedStep: point => costs.Add(Math.Pow(point[0] - .8, 2)));
        Assert.Equal(calls, result.EvaluationCount);
        Assert.Equal(7L * calls, result.WorkUnits);
        Assert.Equal(costs.Count - 1, result.AcceptedSteps);
        Assert.True(result.AcceptedSteps > 0);
        Assert.True(result.DifferenceEvaluationCount > 0);
        Assert.True(costs.Zip(costs.Skip(1)).All(pair => pair.Second < pair.First));
        Assert.InRange(result.Variables[0], .7, .9);
        Assert.Equal(costs[^1], Math.Pow(result.Variables[0] - .8, 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void PartialGradientNeverExceedsBudgetOrBecomesPublishedCandidate(int budget)
    {
        var calls = 0;
        var accepted = new List<double>();
        var result = BoundedAdam.Solve([.5], [0.0], [1.0], point =>
        { calls++; return new([point[0] - .8]); }, budget, acceptedStep: point => accepted.Add(point[0]));
        Assert.Equal(calls, result.EvaluationCount);
        Assert.InRange(calls, 1, budget);
        Assert.Equal(accepted.LastOrDefault(.5), result.Variables[0]);
        if (budget <= 3) Assert.Empty(accepted);
    }

    [Fact]
    public void AdamMissingOrNonFiniteDerivativeDoesNotBecomeZeroGradient()
    {
        foreach (var missing in new[] { false, true })
        {
            var result = BoundedAdam.Solve([.5], [0.0], [1.0], point =>
                point[0] == .5 ? new([1.0]) : missing ? new([1.0], false) : new([double.NaN]), 20);
            Assert.Equal(LeastSquaresTermination.DerivativeUnavailable, result.Termination);
            Assert.Equal(.5, result.Variables[0]);
            Assert.Equal(0, result.AcceptedSteps);
        }
    }

    [Fact]
    public void AdamCancellationAndStopPreventFurtherComputation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => BoundedAdam.Solve([.5], [0.0], [1.0],
            _ => { calls++; return new([1.0]); }, 20, cancelled.Token));
        var stopped = BoundedAdam.Solve([.5], [0.0], [1.0],
            _ => { calls++; return new([1.0]); }, 20, stopRequested: () => true);
        Assert.Equal(0, calls);
        Assert.Equal(LeastSquaresTermination.StopRequested, stopped.Termination);
    }

    [Fact]
    public void AdamBoundAndResidualDimensionGuardsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => BoundedAdam.Solve([2.0], [0.0], [1.0], _ => new([1.0]), 20));
        Assert.Throws<ArgumentException>(() => BoundedAdam.Solve([.5], [0.0], [1.0], _ => new([1.0]), 0));
        Assert.Throws<InvalidOperationException>(() => BoundedAdam.Solve([.5], [0.0], [1.0],
            point => point[0] == .5 ? new([1.0]) : new([1.0, 2.0]), 20));
        var fixedPoint = BoundedAdam.Solve([.5], [.5], [.5], _ => new([1.0]), 20);
        Assert.Equal(LeastSquaresTermination.Stationary, fixedPoint.Termination);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(160)]
    public void IntegratedDesignReservesFinalGateAndChargesEveryProbe(int budget)
    {
        var spec = Specification(budget);
        var family = DesignFormSearch.Roots(spec, new(), ["N-BK7", "N-F2", "N-SF6"])[0];
        var result = new FlatStartDesignService().Solve(spec, 3, family: family);
        Assert.Contains("autolens-cpu-experiment-v1", result.Algorithm.Name);
        Assert.InRange(result.EvaluationCount, 1, budget);
        Assert.All(result.Bootstrap.Steps[0].Optic.Surfaces, surface => Assert.Equal(0, surface.Radius));
        if (result.FinalValidation is { } validation)
        {
            Assert.Equal(FlatStartDesignProblem.FullStage, validation.Objective!.Stage);
            Assert.True(validation.Objective.IndependentValidation);
            Assert.Equal(21, validation.Fields.Count);
            Assert.Equal(validation.MeetsTargets, result.Candidate!.Status == CandidateStatus.LabAccepted);
        }
        else Assert.Null(result.Candidate);
    }

    [Fact]
    public async Task ExperimentPersistsResumesRefinesAndCannotMasqueradeAsDefaultOrHistoricalAlgorithm()
    {
        var spec = Specification();
        var options = FlatStartRefinementTests.Options;
        using var cancel = new CancellationTokenSource();
        var stopped = await new FlatStartSearchService().RunAsync(spec, options, cancel.Token,
            checkpointSink: (saved, _) =>
            { if (saved.Trials.Any(trial => trial.State == FamilyTrialState.Completed)) cancel.Cancel(); return ValueTask.CompletedTask; });
        FlatStartCheckpointValidation.Validate(stopped.Checkpoint);
        Assert.Contains("autolens-cpu-experiment-v1", stopped.Checkpoint.Algorithm.Name);
        var directory = Path.Combine(Path.GetTempPath(), "autolens-cpu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var library = new FlatStartRunLibrary(directory);
            await library.SaveAsync(stopped.Checkpoint);
            var loaded = (await library.LoadLastAsync())!;
            Assert.Equal(ContentFingerprint.Compute(stopped.Checkpoint), ContentFingerprint.Compute(loaded));
            var resumed = await new FlatStartSearchService().RunAsync(spec, options, checkpoint: loaded);
            Assert.InRange(resumed.Checkpoint.ChargedEvaluations, stopped.Checkpoint.ChargedEvaluations, spec.Budget.MaximumEvaluations);
            FlatStartCheckpointValidation.Validate(resumed.Checkpoint);
            var candidate = resumed.Checkpoint.Trials.First(trial => trial.Candidate is not null).Candidate!;
            var child = FlatStartSearchService.CreateRefinementCheckpoint(resumed.Checkpoint, candidate.CandidateId, 40, TimeSpan.FromMinutes(1));
            Assert.Equal(FlatStartOptimizationMethod.AutoLensCpuExperimentV1, child.Specification.FlatStart!.OptimizationMethod);
            Assert.Contains("autolens-cpu-experiment-v1", child.Algorithm.Name);
            FlatStartCheckpointValidation.Validate((await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: child)).Checkpoint);
            await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(spec with
            { FlatStart = spec.FlatStart! with { OptimizationMethod = FlatStartOptimizationMethod.TrustRegion } }, options, checkpoint: loaded));
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(loaded with
            { Algorithm = loaded.Algorithm with { Name = "strict-flat-family-search" } }));
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(loaded with
            { Algorithm = loaded.Algorithm with { Version = "19" } }));
            var path = Path.Combine(directory, "chosen.staropt");
            await new CandidateExportService().ExportStarOptAsync(candidate, path);
            var project = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(candidate.OpticFingerprint, ContentFingerprint.Compute(project.Configurations[0].ToSnapshot()));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("03-50mm-monochrome.json")]
    [InlineData("07-24mm-visible.json")]
    [InlineData("09-50mm-visible.json")]
    public async Task SameBudgetComparisonReportsActualGapsAndChecksFreshCoreAcceptance(string name)
    {
        var frozen = FlatStartDesignTests.LoadSpecification(name);
        var common = frozen with
        {
            FlatStart = frozen.FlatStart! with
            { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1, AutomaticLensDiameters = true, UsePhysicalStop = true },
            Budget = frozen.Budget with { MaximumEvaluations = 600, TimeLimit = TimeSpan.FromMinutes(2) }
        };
        var families = DesignFormSearch.Roots(common, new(), ["N-BK7", "N-F2", "N-SF6"]);
        // Freeze one planned form before running either method, never select a winner afterward.
        var family = families[Math.Min(6, families.Count - 1)];
        var native = new FlatStartDesignService().Solve(common, common.MinimumElementCount, family: family);
        var experimentSpec = common with { FlatStart = common.FlatStart with { OptimizationMethod = FlatStartOptimizationMethod.AutoLensCpuExperimentV1 } };
        var experiment = new FlatStartDesignService().Solve(experimentSpec, common.MinimumElementCount, family: family);
        Assert.Equal(ContentFingerprint.Compute(native.Bootstrap.Steps[0].Optic), ContentFingerprint.Compute(experiment.Bootstrap.Steps[0].Optic));
        foreach (var result in new[] { native, experiment })
        {
            Assert.InRange(result.EvaluationCount, 1, 600);
            if (result.FinalValidation is null)
            {
                Assert.Null(result.Candidate);
                Assert.Equal(FlatStartDesignState.TimeLimit, result.State);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "design.validation-not-run");
                output.WriteLine($"{name}; {result.Algorithm.Name}; {result.State}; eval={result.EvaluationCount}; no final validation, no candidate");
                continue;
            }
            var final = Assert.IsType<DesignEvaluation>(result.FinalValidation);
            var candidate = Assert.IsType<CandidateSnapshot>(result.Candidate);
            var fresh = new FlatStartDesignProblem(result.Specification, common.MinimumElementCount, candidate.Optic, family)
                .Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
            Assert.Equal(ContentFingerprint.Compute(final), ContentFingerprint.Compute(fresh));
            Assert.Equal(final.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
            Assert.True(final.Objective!.IndependentValidation);
            output.WriteLine($"{name}; {result.Algorithm.Name}; {result.State}; eval={result.EvaluationCount}; rays={result.TracedRayCount}; RMS={candidate.Evaluation.RmsSpotRadiusMillimeters:R}; violations={final.Violations.Count}; AdamSteps={result.Steps.Count(step => step.Operation == "autolens-cpu-adam-step")}");
        }
        Assert.Contains(experiment.Steps, step => step.Operation == "autolens-cpu-stage-entry");
        // An impossible target must remain rejected even when the search objective is finite.
        if (experiment.Candidate is { } experimentCandidate)
        {
            var tighter = experimentSpec with { MaximumRmsSpotRadiusMillimeters = 1e-12, MaximumSpotRadiusMillimeters = 2e-12 };
            var rejected = new FlatStartDesignProblem(tighter, common.MinimumElementCount, experimentCandidate.Optic, family)
                .Evaluate(Optic.FromSnapshot(experimentCandidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
            Assert.False(rejected.MeetsTargets);
        }
        var evidence = Environment.GetEnvironmentVariable("OPTICALSYSTEMDESIGN_AUTOLENS_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            await using var stream = new FileStream(Path.Combine(evidence, name), FileMode.CreateNew);
            await JsonSerializer.SerializeAsync(stream, new
            {
                UpstreamCommit = "e58db32f3a0b07f25861b5839a45970f77b5c947",
                FrozenSpecificationFingerprint = ContentFingerprint.Compute(frozen),
                Family = family,
                Native = native,
                Experiment = experiment
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            });
        }
    }
}
