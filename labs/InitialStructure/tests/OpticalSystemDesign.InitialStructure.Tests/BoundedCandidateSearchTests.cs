using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class BoundedCandidateSearchTests
{
    [Fact]
    public async Task OriginalWideDescriptorPreparationKeepsItsBudgetAndNeverPassesAnUnrecoveredFNumber()
    {
        var root = Root();
        var plan = await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(root,
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json"));
        using var protocol = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "validation/rechecks/2026-10-29/happylens-wide21/protocol.json")));
        var options = protocol.RootElement.GetProperty("Preparation").Deserialize<ParaxialSeedSearchOptions>(Json)! with
        { FixedStopRecoveryVersion = 2 };
        var result = ParaxialSeedSearch.Run(plan, options);
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUNDED_EVIDENCE") is { } evidence)
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(evidence, "wide-preparation-regression.json"));
        Assert.InRange(result.Work.FunctionEvaluations, 1, 120); Assert.Equal(0, result.Work.TracedRays);
        Assert.Equal(result.Work.FunctionEvaluations, result.Attempts.Sum(a => a.FunctionEvaluations));
        foreach (var seed in result.Seeds)
        {
            var source = TopologyCompiler.Compile(seed, plan.Constraints); var optic = source.CreateOptic();
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateFNumber() / plan.Targets.FNumber - 1), 0, .02);
            Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, .02);
            Assert.Equal(plan.Seeds[0].Stop, seed.Stop); Assert.Equal(plan.Seeds[0].Image, seed.Image);
        }
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUNDED_EVIDENCE") is { } path)
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(path, "wide-preparation-regression.seed-search.json"));
    }

    [Fact]
    public async Task IndependentPupilRecoveryRepairsAPerturbedOriginalWidePrescriptionWithTheStopFixed()
    {
        var plan = await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json"));
        var seed = plan.Seeds[0]; var element = seed.Elements[0];
        var changed = element with
        {
            Front = element.Front with
            {
                Profile = element.Front.Profile with
                { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter * .98 }
            }
        };
        plan = plan with { Seeds = [seed with { Elements = seed.Elements.SetItem(0, changed) }] };
        var before = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints).CreateOptic().Paraxial.EstimateFNumber();
        var result = ParaxialSeedSearch.Run(plan, new()
        {
            FixedStopRecoveryVersion = 2,
            MaximumStarts = 1,
            MaximumFunctionEvaluations = 60,
            MaximumEvaluationsPerStart = 60,
            TimeLimit = TimeSpan.FromSeconds(15)
        });
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOUNDED_EVIDENCE") is { } path)
            await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(path, "fixed-stop-dense-recovery.json"));
        Assert.True(result.Seeds.Length > 0, string.Join(";", result.Attempts.Where(a => a.Endpoint is not null).SelectMany(a =>
        {
            var source = TopologyCompiler.Compile(a.Endpoint!, plan.Constraints); var optic = source.CreateOptic();
            return TopologyEvaluationPlan.Geometry(source, plan.Targets, 64).Where(row =>
            {
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                return !double.IsFinite(value) || (row.Upper ? value > row.Limit : value < row.Limit);
            }).Select(row => row.Id);
        })));
        Assert.Equal(0, result.Work.TracedRays);
        var optic = TopologyCompiler.Compile(result.Seeds[0], plan.Constraints).CreateOptic();
        var after = optic.Paraxial.EstimateFNumber();
        Assert.True(Math.Abs(after / plan.Targets.FNumber - 1) < Math.Abs(before / plan.Targets.FNumber - 1));
        Assert.InRange(Math.Abs(after / plan.Targets.FNumber - 1), 0, .02);
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() / plan.Targets.EffectiveFocalLengthMillimeters - 1), 0, .02);
        Assert.Equal(seed.Stop, result.Seeds[0].Stop); Assert.Equal(seed.Image, result.Seeds[0].Image);
    }

    [Fact]
    public async Task CorePupilTangentsMatchFixedStopFiniteDifferencesWithoutRays()
    {
        var plan = await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(Root(),
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json"));
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, ParaxialSeedSearch.Variables(source.Topology, plan.Options).Take(4));
        using var rays = SequentialTraceMeasurement.Begin();
        var result = DifferentiableParaxialMetrics.TransferMatrix(source.CreateOptic(), binding.CoreParameters,
            source.SurfaceIndices[source.Topology.Reference.Id], source.SurfaceIndices[source.Topology.Image.Id], includeInverseFNumber: true);
        Assert.Equal(source.CreateOptic().Paraxial.EstimateEntrancePupilDiameter(), result.EntrancePupilDiameterMillimeters!.Value.Value, 10);
        for (var i = 0; i < binding.Slots.Length; i++)
        {
            var plus = binding.InitialCoordinates.ToArray(); var minus = plus.ToArray(); plus[i] += 1e-5; minus[i] -= 1e-5;
            var difference = (TopologyParameterBinder.Apply(source, binding, plus).CreateOptic().Paraxial.EstimateEntrancePupilDiameter()
                - TopologyParameterBinder.Apply(source, binding, minus).CreateOptic().Paraxial.EstimateEntrancePupilDiameter()) / 2e-5;
            Assert.InRange(Math.Abs(difference - result.EntrancePupilDiameterMillimeters.Value.Derivative(i)), 0, 1e-7);
        }
        Assert.Equal(0, rays.RayCount);
    }

    [Fact]
    public void GeometryBacktrackingRejectsTheInvalidSurfaceBeforeAnyPhysicalRayAndKeepsAllBounds()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, ParaxialSeedSearch.Variables(source.Topology, plan.Options));
        var proposal = binding.InitialCoordinates.ToArray(); proposal[0] = binding.Slots[0].UpperCoordinate;
        using var rays = SequentialTraceMeasurement.Begin();
        var screened = BoundedGeometryScreen.Run(source, binding, proposal, plan.Targets, 8, default);
        Assert.NotNull(screened.Topology); Assert.InRange(screened.Work.FunctionEvaluations, 2, 8);
        Assert.Equal("ScreenPassed", screened.Outcome); Assert.Equal(0, rays.RayCount);
        var optic = TopologyCompiler.Compile(screened.Topology, plan.Constraints).CreateOptic();
        Assert.All(TopologyEvaluationPlan.Geometry(TopologyCompiler.Compile(screened.Topology, plan.Constraints), plan.Targets, 64), row =>
        {
            var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
            Assert.True(double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit));
        });
    }

    [Fact]
    public void BoundPolicyStopsWithoutARequestedElementCountAndClosedResumeDoesNoWork()
    {
        var plan = SmallPlan() with
        {
            Options = SmallPlan().Options with
            { BoundedSearch = new() { MaximumAttemptsPerElementCount = 1, MaximumUnsuccessfulTrials = 1 } }
        };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("bounded-stop", plan));
        Assert.NotNull(result.StopReason); Assert.NotEmpty(result.AcceptedArchive);
        Assert.InRange(result.TrialCursor, 0, 1);
        using var rays = SequentialTraceMeasurement.Begin();
        Assert.Same(result, AutonomousSearchService.Run(result)); Assert.Equal(0, rays.RayCount);
        var summary = AutonomousSearchService.BoundedSummary(result);
        Assert.Equal(3, summary.Complexity!.Elements); Assert.NotNull(summary.BestQualifiedId);
    }

    [Fact]
    public void LayerLimitStopsSameCountRefinementEvenWhenTheCandidateStaysQualified()
    {
        var plan = SmallPlan() with { Options = SmallPlan().Options with { FixedTopologyOnly = true,
            BoundedSearch = new() { MaximumAttemptsPerElementCount = 1, MaximumUnsuccessfulTrials = 128 } } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("layer-limit", plan));
        Assert.Equal(BoundedSearchStopReason.LayerLimit, result.StopReason);
        Assert.Equal(1, result.TrialCursor); Assert.NotEmpty(result.AcceptedArchive);
    }

    [Fact]
    public void StagnationStopsAfterTheConfiguredNumberOfEffectiveTrials()
    {
        var plan = SmallPlan() with { Options = SmallPlan().Options with { FixedTopologyOnly = true,
            Full = SmallPlan().Options.Full with { LearningRate = 1e-16 },
            BoundedSearch = new() { MaximumAttemptsPerElementCount = 8, MaximumUnsuccessfulTrials = 1, MinimumRelativeQualityGain = .1 } } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("no-improvement", plan));
        Assert.Equal(BoundedSearchStopReason.NoImprovement, result.StopReason);
        Assert.Equal(1, result.TrialCursor); Assert.NotEmpty(result.AcceptedArchive);
    }

    [Fact]
    public void InsufficientCompleteTrialBudgetStopsBeforeAProposalAndRetainsTheQualifiedArchive()
    {
        var plan = SmallPlan() with
        {
            Options = SmallPlan().Options with
            { BoundedSearch = new() { MinimumCompensationRays = 490_000 } }
        };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("complete-budget", plan));
        Assert.Equal(BoundedSearchStopReason.InsufficientCompleteTrialBudget, result.StopReason);
        Assert.Equal(0, result.TrialCursor); Assert.Single(result.AcceptedArchive);
        Assert.All(result.Events, e => Assert.Null(e.ParentId));
    }

    [Fact]
    public void FailedGeometricProposalStopsAtItsFiniteAttemptLimitWithoutTracing()
    {
        var plan = ParaxialSeedSearchTests.Plan(); var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, ParaxialSeedSearch.Variables(source.Topology, plan.Options));
        var proposal = binding.InitialCoordinates.ToArray(); proposal[0] = binding.Slots[0].UpperCoordinate;
        using var rays = SequentialTraceMeasurement.Begin();
        var result = BoundedGeometryScreen.Run(source, binding, proposal, plan.Targets, 1, default);
        Assert.Null(result.Topology); Assert.Equal("ScreenRejected", result.Outcome);
        Assert.Equal(1, result.Work.FunctionEvaluations); Assert.Equal(0, rays.RayCount);
    }

    [Fact]
    public async Task ScreenReservationSurvivesStopSaveAndResumeWithOriginalRandomStateAndCharges()
    {
        var plan = SmallPlan() with
        {
            Options = SmallPlan().Options with
            {
                FixedTopologyOnly = true,
                PopulationExploration = new() { Geometry = new() },
                BoundedSearch = new()
            }
        };
        using var stop = new CancellationTokenSource(); var directory = Path.Combine(Path.GetTempPath(), "bounded-screen-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "run.json"); var store = new AutonomousSearchCheckpointStore();
        try
        {
            var partial = AutonomousSearchService.Run(AutonomousSearchService.Create("screen-resume", plan), state =>
            {
                store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (state.Pending?.Phase == AutonomousSearchPhase.Screening && state.Reservation is not null) stop.Cancel();
            }, stop.Token);
            Assert.Equal(AutonomousSearchStatus.Cancelled, partial.Status);
            var loaded = await store.LoadAsync(path); Assert.Equal(partial.RandomState, loaded.RandomState);
            var resumed = AutonomousSearchService.Run(loaded);
            var full = AutonomousSearchService.Run(AutonomousSearchService.Create("screen-control", plan));
            Assert.Equal(full.Consumed with { Elapsed = TimeSpan.Zero }, resumed.Consumed with { Elapsed = TimeSpan.Zero });
            Assert.Equal(full.RandomState, resumed.RandomState);
            Assert.Equal(full.Candidates.Select(c => ContentFingerprint.Compute(c.Topology)), resumed.Candidates.Select(c => ContentFingerprint.Compute(c.Topology)));
            Assert.Contains(resumed.Events, e => e.Phase == AutonomousSearchPhase.Screening && e.Work.FunctionEvaluations > 0);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    internal static AutonomousSearchPlan SmallPlan()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var stage = new FixedTopologyOptions
        {
            ComputeMode = DesignComputeMode.Cpu,
            PerformFinalAssessment = false,
            RequireDescent = true,
            MaximumUpdates = 1,
            MaximumFunctionEvaluations = 3,
            MaximumGradientEvaluations = 2,
            Sampling = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 }
        };
        return plan with
        {
            Targets = plan.Targets with
            {
                FocalLengthRelativeTolerance = 1,
                FNumberRelativeTolerance = 1,
                MaximumRmsRadiusMillimeters = 5,
                MaximumSpotRadiusMillimeters = 10,
                MaximumDistortionFraction = 1
            },
            Options = plan.Options with
            {
                AxialCompensation = null,
                Expansion = null,
                CheckStageFeasibility = null,
                MaximumTrials = 4,
                MaximumFunctionEvaluations = 150,
                MaximumGradientEvaluations = 20,
                MaximumTracedRays = 500_000,
                MaximumStageTracedRays = 50_000,
                FinalValidationRayReserve = 50_000,
                TimeLimit = TimeSpan.FromSeconds(30),
                Local = stage,
                Full = stage
            }
        };
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };
}
