using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutoLensManagedTests(ITestOutputHelper output)
{
    internal static InitialStructureSpecification Specification(int budget = 160) => FlatStartRefinementTests.Spec(budget) is { } original
        ? original with
        {
            FlatStart = original.FlatStart! with
            {
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                UsePhysicalStop = true,
                AutomaticLensDiameters = true,
                OptimizationMethod = FlatStartOptimizationMethod.AutoLensManagedV1,
                AutoLens = new() { CurriculumIterations = 2, RefinementIterations = 3, EvenAsphereOrder = 2, FieldRings = 3, PupilRings = 2, PupilArms = 8, TargetPolishEvaluations = 0 }
            },
            Budget = original.Budget with { TimeLimit = TimeSpan.FromMinutes(2) }
        } : throw new InvalidOperationException();

    [Fact]
    public void AdamUsesExactGradientsAndSerializedMomentsResumeIdentically()
    {
        var original = DifferentiableAdam.Initialize([2.0, -1.0]);
        var state = original;
        for (var index = 0; index < 5; index++) state = Step(state);
        var loaded = JsonSerializer.Deserialize<AutoLensAdamState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(Step(state).Coordinates, Step(loaded).Coordinates);
        Assert.Equal(Step(state).FirstMoment, Step(loaded).FirstMoment);
        Assert.Equal(Step(state).SecondMoment, Step(loaded).SecondMoment);
        Assert.Equal(new[] { 2.0, -1.0 }, original.Coordinates);
        Assert.True(state.Coordinates[0] < 2 && state.Coordinates[1] > -1);
        Assert.Throws<ArithmeticException>(() => DifferentiableAdam.Propose(state, double.NaN, [.01, .01]));
        Assert.Throws<InvalidDataException>(() => DifferentiableAdam.Validate(state with { SecondMoment = [-1, 0] }));
        Assert.Throws<InvalidDataException>(() => DifferentiableAdam.Validate(state with { MomentIteration = 0 }));

        static AutoLensAdamState Step(AutoLensAdamState current)
        {
            var x = DifferentialScalar.Variable(current.Coordinates[0], 2, 0);
            var y = DifferentialScalar.Variable(current.Coordinates[1], 2, 1);
            return DifferentiableAdam.Propose(current, x * x + y * y, [.1, .1]);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, .5)]
    [InlineData(2, 1)]
    public void CosineRestartHasCorrectPeriod(int iteration, double expected) =>
        Assert.Equal(expected, DifferentiableAdam.CosineWarmRestart(iteration, 8), 13);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UnsupportedConfigurationDoesNotSilentlyFallBack(int variant)
    {
        var spec = Specification();
        var settings = spec.FlatStart!.AutoLens!;
        var flat = variant switch
        {
            0 => spec.FlatStart with { UsePhysicalStop = false },
            1 => spec.FlatStart with { AutoLens = settings with { EvenAsphereOrder = 1 } },
            2 => spec.FlatStart with { AutoLens = settings with { CurriculumIterations = 0 } },
            3 => spec.FlatStart with { AutoLens = settings with { LearningRateFactor = double.NaN } },
            _ => spec.FlatStart with { SamplingPolicy = FlatStartSamplingPolicy.LegacyEqualRings }
        };
        Assert.Throws<InitialStructureSpecificationException>(() => SpecificationValidator.Validate(spec with { FlatStart = flat }));
    }

    [Fact]
    public async Task RealTwoPhaseSolveUsesCoreAutomaticTangentsAndNeverExportsRelaxedGlass()
    {
        var original = Specification();
        // This is a two-phase contract check, not a training benchmark. Keep
        // both real phases while using a bounded CPU pupil/field sample.
        var spec = original with
        {
            FlatStart = original.FlatStart! with
            {
                AutoLens = original.FlatStart.AutoLens! with { FieldRings = 2, PupilRings = 1, PupilArms = 4, ComputeMode = DesignComputeMode.Cpu }
            },
            Budget = original.Budget with { TimeLimit = TimeSpan.FromMinutes(5) }
        };
        var result = new FlatStartDesignService().Solve(spec, spec.MinimumElementCount, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
        Assert.InRange(result.EvaluationCount, 1, spec.Budget.MaximumEvaluations);
        var state = Assert.IsType<AutoLensManagedState>(result.AutoLensState);
        AutoLensManagedStateValidation.Validate(state);
        Assert.Equal(AutoLensManagedPhase.Completed, state.Phase);
        Assert.Equal(3, state.Adam.Iteration);
        Assert.Equal(spec.MinimumElementCount, state.GlassMatches.Count);
        Assert.True(state.TracedRayCount > 0);
        Assert.NotEmpty(state.TrainingSteps);
        Assert.All(state.TrainingSteps, step => Assert.True(step.AfterLoss < step.BeforeLoss));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "design.autolens-curriculum" && diagnostic.Message.StartsWith("2 finite", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "design.autolens-catalog-refinement" && diagnostic.Message.StartsWith("3 finite", StringComparison.Ordinal));
        var candidate = Assert.IsType<CandidateSnapshot>(result.Candidate);
        var optic = Optic.FromSnapshot(candidate.Optic);
        Assert.All(optic.SurfaceGroup.Items.Skip(1).Take(2 * spec.MinimumElementCount).Where((_, index) => index % 2 == 0),
            surface => Assert.IsType<CatalogGlassMaterial>(surface.MaterialAfter));
        Assert.All(optic.SurfaceGroup.Items.Skip(1).Take(2 * spec.MinimumElementCount), surface => Assert.IsType<EvenAsphereGeometry>(surface.Geometry));
        var fresh = new FlatStartDesignProblem(spec, spec.MinimumElementCount, candidate.Optic)
            .Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Equal(ContentFingerprint.Compute(result.FinalValidation!), ContentFingerprint.Compute(fresh));
        Assert.Equal(fresh.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
        Assert.True(fresh.Objective!.IndependentValidation);
        Record("monochrome-two-phase", result);
        var changed = state with { Adam = state.Adam with { Coordinates = state.Adam.Coordinates.Select(value => value + .01).ToArray() } };
        Assert.Throws<InvalidDataException>(() => new FlatStartDesignService().ResumeAutoLens(spec, result.Bootstrap, changed, ["N-BK7", "N-F2", "N-SF6"]));
        var forgedIncumbent = Optic.FromSnapshot(state.FullTargetIncumbent!);
        forgedIncumbent.SurfaceGroup.Items[1].MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("DesignRelaxed-forged", 1.6, 50);
        Assert.Throws<InvalidDataException>(() => new FlatStartDesignService().ResumeAutoLens(spec, result.Bootstrap,
            state with { FullTargetIncumbent = forgedIncumbent.ToSnapshot() }, ["N-BK7", "N-F2", "N-SF6"]));
        var resumed = new FlatStartDesignService().ResumeAutoLens(spec, result.Bootstrap, state, ["N-BK7", "N-F2", "N-SF6"]);
        Assert.Equal(state.EvaluationCount + 2, resumed.EvaluationCount);
        Assert.Equal(state.Adam.Coordinates, resumed.AutoLensState!.Adam.Coordinates);
        Assert.Equal(state.Adam.FirstMoment, resumed.AutoLensState.Adam.FirstMoment);
        Assert.True(resumed.AutoLensState.ElapsedComputeTicks >= state.ElapsedComputeTicks);
        Assert.Equal(candidate.OpticFingerprint, resumed.Candidate!.OpticFingerprint);
        var lineageResume = new FlatStartDesignService().ResumeAutoLens(spec, result.Bootstrap,
            state with { ParentCandidateId = "original-parent" }, ["N-BK7", "N-F2", "N-SF6"]);
        Assert.Equal("original-parent", lineageResume.Candidate!.Lineage.ParentCandidateId);
        var rootSteps = result.Bootstrap.Steps.ToArray();
        var rootSurfaces = rootSteps[0].Optic.Surfaces.ToArray();
        rootSurfaces[1] = rootSurfaces[1] with { Radius = 100 };
        rootSteps[0] = rootSteps[0] with { Optic = rootSteps[0].Optic with { Surfaces = rootSurfaces.ToList() } };
        Assert.Throws<InvalidDataException>(() => new FlatStartDesignService().ResumeAutoLens(spec,
            result.Bootstrap with { Steps = rootSteps }, state, ["N-BK7", "N-F2", "N-SF6"]));
        var export = Path.Combine(Path.GetTempPath(), "autolens-managed-" + Guid.NewGuid().ToString("N") + ".staropt");
        try
        {
            await new CandidateExportService().ExportStarOptAsync(candidate, export);
            var loaded = await OpticalSystemDesign.Core.Serialization.StarOptProjectStore.LoadAsync(export);
            Assert.Equal(candidate.OpticFingerprint, ContentFingerprint.Compute(loaded.Configurations[0].ToSnapshot()));
        }
        finally { if (File.Exists(export)) File.Delete(export); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void TinyBudgetsStillRequireRealGlassAndPhysicalValidation(int budget)
    {
        var spec = Specification(budget);
        var result = new FlatStartDesignService().Solve(spec, spec.MinimumElementCount);
        Assert.InRange(result.EvaluationCount, 1, budget);
        if (result.Candidate is { } candidate)
        {
            Assert.True(result.FinalValidation!.Objective!.IndependentValidation);
            Assert.DoesNotContain(candidate.Optic.Surfaces, surface => surface.Material.StartsWith("DesignRelaxed-", StringComparison.Ordinal));
        }
        else Assert.Null(result.FinalValidation);
        AutoLensManagedStateValidation.Validate(result.AutoLensState!);
    }

    [Fact]
    public async Task SearchPersistencePreservesStrategyWorkCountersMaterialsAndStrictIdentity()
    {
        var spec = Specification(60);
        var options = FlatStartRefinementTests.Options with { MaximumEvaluationsPerTrial = 60, DesignSearch = new() { Mode = DesignSearchMode.Full } };
        var result = await new FlatStartSearchService().RunAsync(spec, options);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        var completed = result.Checkpoint.Trials.Where(trial => trial.State == FamilyTrialState.Completed).ToArray();
        Assert.NotEmpty(completed);
        Assert.All(completed, trial => Assert.NotNull(trial.AutoLensState));
        var directory = Path.Combine(Path.GetTempPath(), "autolens-managed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new FlatStartRunLibrary(directory);
            await library.SaveAsync(result.Checkpoint);
            var loaded = (await library.LoadLastAsync())!;
            Assert.Equal(ContentFingerprint.Compute(result.Checkpoint), ContentFingerprint.Compute(loaded));
            Assert.Contains("autolens-managed-v1", loaded.Algorithm.Name);
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(loaded with { Algorithm = loaded.Algorithm with { Name = "strict-flat-family-search" } }));
            Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(loaded with { Algorithm = loaded.Algorithm with { Version = "19" } }));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DefaultNativeSpecificationFingerprintDoesNotAcquireAutoLensSettings()
    {
        var native = FlatStartRefinementTests.Spec();
        Assert.DoesNotContain("AutoLens", JsonSerializer.Serialize(native));
        Assert.Equal("strict-flat-design", FlatStartAlgorithm.Name("strict-flat-design", native));
        Assert.Contains("autolens-managed-v1", FlatStartAlgorithm.Name("strict-flat-design", Specification()));
    }

    [Fact]
    public void CementedGroupTiesTheSharedProfileAndMediumAndRejectsAnEditedGap()
    {
        var original = Specification(90);
        var spec = original with { FlatStart = original.FlatStart! with { AutoLens = original.FlatStart.AutoLens! with { CementedGaps = [0, 1], CurriculumIterations = 1, RefinementIterations = 1 } } };
        var family = DesignFormSearch.Roots(spec, new(), ["N-BK7", "N-F2", "N-SF6"])[0];
        Assert.Equal(new[] { 0.0, 0.0 }, family.AirGaps);
        // Validate the production topology and parameter ties directly. A
        // topology test must not run an unconstrained random optimization.
        var optic = AutoLensReferenceStart.Create(spec, 3, family, ["N-BK7", "N-F2", "N-SF6"]);
        var snapshot = optic.ToSnapshot();
        var parameters = AutoLensReferenceStart.Parameters(optic, spec, materials: false);
        Assert.True(AutoLensManagedTopology.IsValid(optic, 0));
        Assert.True(AutoLensManagedTopology.IsValid(optic, 1));
        Assert.Contains(parameters, parameter => parameter.LinkedSurfaceIndex == 4);
        Assert.Contains(parameters, parameter => parameter.LinkedSurfaceIndex == 6);
        Assert.DoesNotContain(parameters, parameter => parameter.SurfaceIndex is 4 or 6);
        optic.SurfaceGroup.Items[3].Thickness = .001;
        optic.SurfaceGroup.Renumber();
        var rejected = new FlatStartDesignProblem(spec, 3, snapshot, family).Evaluate(optic, FlatStartDesignProblem.FullStage, true, default, true);
        Assert.Contains(rejected.Violations, violation => violation.Code == "geometry.cemented-interface");
        Assert.False(rejected.MeetsTargets);
    }

    [Theory]
    [InlineData("03-50mm-monochrome.json")]
    [InlineData("07-24mm-visible.json")]
    [InlineData("09-50mm-visible.json")]
    public void FrozenTargetsAreNotReplacedByTheTrainingLoss(string name)
    {
        var frozen = FlatStartDesignTests.LoadSpecification(name);
        var settings = Specification().FlatStart!;
        var spec = frozen with
        {
            FlatStart = frozen.FlatStart! with
            {
                SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1,
                UsePhysicalStop = true,
                AutomaticLensDiameters = true,
                OptimizationMethod = FlatStartOptimizationMethod.AutoLensManagedV1,
                AutoLens = settings.AutoLens! with { CurriculumIterations = 4, RefinementIterations = 6, EvenAsphereOrder = 4, TargetPolishEvaluations = 120 }
            },
            Budget = frozen.Budget with { MaximumEvaluations = 200, MaximumParallelism = 1, TimeLimit = TimeSpan.FromMinutes(2) }
        };
        var result = new FlatStartDesignService().Solve(spec, spec.MinimumElementCount, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
        Assert.InRange(result.EvaluationCount, 1, 200);
        if (result.Candidate is { } candidate)
        {
            var fresh = new FlatStartDesignProblem(spec, spec.MinimumElementCount, candidate.Optic)
                .Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
            Assert.Equal(ContentFingerprint.Compute(result.FinalValidation!), ContentFingerprint.Compute(fresh));
            Assert.Equal(fresh.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
            var strict = spec with { MaximumRmsSpotRadiusMillimeters = 1e-12, MaximumSpotRadiusMillimeters = 2e-12 };
            Assert.False(new FlatStartDesignProblem(strict, spec.MinimumElementCount, candidate.Optic)
                .Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true).MeetsTargets);
        }
        else Assert.Null(result.FinalValidation);
        Record(name, result);
    }

    private void Record(string name, FlatStartDesignResult result)
    {
        output.WriteLine($"{name}: {result.State}; evaluations {result.EvaluationCount}; rays {result.TracedRayCount}; RMS {result.Candidate?.Evaluation.RmsSpotRadiusMillimeters:R}; max {result.Candidate?.Evaluation.MaximumSpotRadiusMillimeters:R}; violations {result.FinalValidation?.Violations.Count}; phase {result.AutoLensState?.Phase}");
        var directory = Environment.GetEnvironmentVariable("OPTICALSYSTEMDESIGN_AUTOLENS_MANAGED_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions
        { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }

    [Fact]
    public void CementedMinimumTrackDoesNotReserveNonexistentAirLayers()
    {
        var original = Specification(20);
        var compact = original with
        {
            MaximumTrackLengthMillimeters = 11.5,
            FlatStart = original.FlatStart! with { AutoLens = original.FlatStart.AutoLens! with { CementedGaps = [0, 1] } }
        };
        SpecificationValidator.Validate(compact);
        DesignFormSearch.Validate(compact, new());
        var root = new FlatRootFactory().Create(compact, 3);
        Assert.Equal(0, root.Surfaces[2].Thickness);
        Assert.Equal(0, root.Surfaces[4].Thickness);
        Assert.Throws<InitialStructureSpecificationException>(() => SpecificationValidator.Validate(compact with
        { FlatStart = compact.FlatStart! with { AutoLens = compact.FlatStart!.AutoLens! with { CementedGaps = [] } } }));
    }

    [Fact]
    [Trait("Category", "LongRunning")]
    public void ManagedFiftyMillimeterDesignReceivesARealOptimizationBudget()
    {
        var original = Specification(1600);
        var spec = original with
        {
            FlatStart = original.FlatStart! with { AutoLens = new() },
            Budget = original.Budget with { TimeLimit = TimeSpan.FromMinutes(3) }
        };
        var result = new FlatStartDesignService().Solve(spec, 3, allowedGlassNames: ["N-BK7", "N-F2", "N-SF6"]);
        Assert.InRange(result.EvaluationCount, 1, 1600);
        Assert.NotEmpty(result.AutoLensState!.TrainingSteps);
        Assert.All(result.AutoLensState.TrainingSteps, step => Assert.True(step.AfterLoss < step.BeforeLoss));
        if (result.Candidate is { } candidate)
        {
            var replay = new FlatStartDesignProblem(spec, 3, candidate.Optic).Evaluate(Optic.FromSnapshot(candidate.Optic), FlatStartDesignProblem.FullStage, true, default, true);
            Assert.Equal(ContentFingerprint.Compute(result.FinalValidation!), ContentFingerprint.Compute(replay));
            Assert.Equal(replay.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
        }
        Record("03-50mm-managed-1600", result);
    }
}
