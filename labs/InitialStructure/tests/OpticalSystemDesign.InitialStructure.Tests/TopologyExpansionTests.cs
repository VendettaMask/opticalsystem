using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyExpansionTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SplitAndMergePreserveOuterFacesFrameAndUntouchedElementsAndCompileRealGlass(bool split)
    {
        var parent = Source();
        var before = ContentFingerprint.Compute(parent);
        var result = split ? TopologyOperators.Split(parent, "lens-1", "split-a", "split-b", .05, .55)
            : TopologyOperators.Merge(parent, "lens-1", "lens-2", "merged", "N-BK7");
        Assert.True(result.Succeeded, result.Failure?.Message);
        var child = result.Topology!;
        Assert.Equal(split ? 4 : 2, child.Elements.Length);
        Assert.Equal(parent.Reference, child.Reference); Assert.Equal(parent.Stop, child.Stop); Assert.Equal(parent.Image, child.Image);
        Assert.Equal(parent.Elements[0].Front, child.Elements[0].Front);
        Assert.Equal(parent.Elements[0].FrontSurfaceId, child.Elements[0].FrontSurfaceId);
        Assert.Equal(parent.Elements[split ? 0 : 1].BackSurfaceId, child.Elements[split ? 1 : 0].BackSurfaceId);
        Assert.Equal(parent.Elements[split ? 0 : 1].BackZMillimeters, child.Elements[split ? 1 : 0].BackZMillimeters, 12);
        Assert.Equal(ContentFingerprint.Compute(parent.Elements[^1]), ContentFingerprint.Compute(child.Elements[^1]));
        Assert.Equal(split ? 2 : 1, result.Change!.CreatedElementIds.Length);
        Assert.Equal(split ? 1 : 2, result.Change.RemovedElementIds.Length);
        var compiled = TopologyCompiler.Compile(child);
        var optic = compiled.CreateOptic();
        Assert.Equal(child.Elements.Length, compiled.Materials.Length);
        Assert.Equal(child.Elements.Length * 2 + 4, optic.SurfaceGroup.Items.Count);
        Assert.NotNull(optic.SequentialRayTracer.TraceGenericFinalSample(0, 0, 0, 0, .5876, aimAtStop: true));
        var matrix = optic.Paraxial.GetTransferMatrix(compiled.SurfaceIndices[child.Reference.Id], compiled.SurfaceIndices[child.Image.Id]);
        Assert.True(double.IsFinite(matrix.A) && double.IsFinite(matrix.C));
        Assert.Equal(before, ContentFingerprint.Compute(parent));
        Evidence(split ? "split-operator" : "merge-operator", new { result, matrix, compiled.Snapshot });
    }

    [Fact]
    public void ExtendedEditsRejectProtectionStopCrossingInsufficientSpaceAndNonCatalogGlass()
    {
        var parent = Source();
        var protectedModel = parent with { Elements = parent.Elements.SetItem(0, parent.Elements[0] with { Protected = true }) };
        Assert.Equal(TopologyFailureCode.ProtectedElement, TopologyOperators.Split(protectedModel, "lens-1", "a", "b", .05).Failure!.Code);
        Assert.Equal(TopologyFailureCode.ProtectedElement, TopologyOperators.ChangeMaterial(protectedModel, "lens-1", "N-F2").Failure!.Code);
        Assert.Equal(TopologyFailureCode.ProtectedElement, TopologyOperators.UpgradeProfile(protectedModel, "lens-1:back", 4).Failure!.Code);
        var stopBetween = parent with { Stop = parent.Stop with { ZMillimeters = 7 } };
        Assert.Equal(TopologyFailureCode.StopInsideElement, TopologyOperators.Merge(stopBetween, "lens-1", "lens-2", "m", "N-BK7").Failure!.Code);
        var thin = parent with { Elements = parent.Elements.SetItem(0, parent.Elements[0] with { ThicknessMillimeters = .1 }) };
        Assert.Equal(TopologyFailureCode.NoSpace, TopologyOperators.Split(thin, "lens-1", "a", "b", .05).Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnsupportedMaterial, TopologyOperators.ChangeMaterial(parent, "lens-1", "Air").Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnsupportedMaterial, TopologyOperators.ChangeMaterial(parent, "lens-1", "missing-catalog-glass").Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnsupportedProfile, TopologyOperators.UpgradeProfile(parent, "lens-1:back", 3).Failure!.Code);
        Assert.Equal(TopologyFailureCode.UnknownElement, TopologyOperators.Merge(parent, "lens-1", "lens-3", "m", "N-BK7").Failure!.Code);
    }

    [Fact]
    public void RealMaterialAndExplicitProfileBranchesRequireFreshBindingsAndAdamState()
    {
        var parent = TopologyCompiler.Compile(Source());
        TopologyVariableRequest[] requests = [new("lens-1:back", OpticalDesignParameterKind.Curvature, -.08, .08, .01)];
        var binding = TopologyParameterBinder.Bind(parent, requests);
        var options = Plan().Options.Full;
        var prior = FixedTopologyOptimizer.Run(parent, binding, new(), options);
        var material = TopologyOperators.ChangeMaterial(parent.Topology, "lens-1", "N-F2");
        var profile = TopologyOperators.UpgradeProfile(parent.Topology, "lens-1:back", 6);
        Assert.True(material.Succeeded); Assert.True(profile.Succeeded);
        foreach (var edit in new[] { material, profile })
        {
            var child = TopologyCompiler.Compile(edit.Topology!);
            Assert.NotEqual(parent.BindingFingerprint, child.BindingFingerprint);
            Assert.Throws<TopologyValidationException>(() => TopologyParameterBinder.Apply(child, binding, binding.InitialCoordinates));
            var nextBinding = TopologyParameterBinder.Bind(child, requests);
            using var measured = SequentialTraceMeasurement.Begin();
            Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(child, nextBinding, new(), options, continuation: prior.Continuation));
            Assert.Equal(0, measured.RayCount);
        }
        var changedProfile = profile.Topology!.Elements[0].Back.Profile;
        Assert.Equal(new[] { 4, 6 }, changedProfile.Terms.Select(term => term.Power));
        Assert.All(changedProfile.Terms, term => Assert.Equal(0, term.Coefficient));
        var a = parent.CreateOptic().Paraxial.GetTransferMatrix(parent.SurfaceIndices[parent.Topology.Reference.Id], parent.SurfaceIndices[parent.Topology.Image.Id]);
        var compiledProfile = TopologyCompiler.Compile(profile.Topology);
        var b = compiledProfile.CreateOptic().Paraxial.GetTransferMatrix(compiledProfile.SurfaceIndices[parent.Topology.Reference.Id], compiledProfile.SurfaceIndices[parent.Topology.Image.Id]);
        Assert.Equal(a.A, b.A, 12); Assert.Equal(a.C, b.C, 12);
        Evidence("material-profile-identities", new { material, profile, OldBinding = binding.Fingerprint, prior.Continuation });
    }

    [Fact]
    public void EveryDeletionScaleUsesTheUnscaledOriginAndOnlyItsTargetGetsThicknessExceptions()
    {
        var parent = Source();
        var constraints = new TopologyConstraints(); var targets = LooseTargets();
        var original = parent.Elements[0];
        var origin = TopologyOperators.CreateGradualDeletion(parent, original.Id, new TopologyExpansionOptions().DeletionScales);
        var current = parent;
        for (var index = 0; index < origin.Scales.Length; index++)
        {
            var stage = origin with { StageIndex = index };
            var edit = TopologyOperators.ApplyDeletionStage(stage, current, constraints, targets);
            Assert.True(edit.Succeeded, edit.Failure?.Message);
            current = edit.Topology!;
            AssertPrescribed(stage, current);
            var scoped = TopologyOperators.DeletionConstraints(stage, constraints, targets);
            var compiled = TopologyCompiler.Compile(current, scoped);
            var rows = TopologyEvaluationPlan.Geometry(compiled, targets, 8);
            Assert.Equal(constraints.MinimumCenterThicknessMillimeters * stage.Scales[index], rows.Single(row => row.Id == "lens-1:center").Limit);
            Assert.Equal(constraints.MinimumCenterThicknessMillimeters, rows.Single(row => row.Id == "lens-2:center").Limit);
            Assert.Equal(targets.MinimumEdgeThicknessMillimeters, rows.First(row => row.Id.StartsWith("lens-2:edge:", StringComparison.Ordinal)).Limit);
            Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Compile(current, constraints));
            var otherThin = current with { Elements = current.Elements.SetItem(1, current.Elements[1] with { ThicknessMillimeters = .01 }) };
            Assert.Equal(TopologyFailureCode.CenterThickness, Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Compile(otherThin, scoped)).Code);
            var otherTransition = current with { Elements = current.Elements.SetItem(1, current.Elements[1] with { Transition = true }) };
            Assert.Equal(TopologyFailureCode.TransitionNotAllowed, Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Compile(otherTransition, scoped)).Code);
        }
        var last = origin with { StageIndex = origin.Scales.Length - 1 };
        var transitional = TopologyValidator.Validate(TopologyCompiler.Compile(current, TopologyOperators.DeletionConstraints(last, constraints, targets)), targets);
        Assert.True(transitional.Completed); Assert.False(transitional.MeetsTargets);
        Assert.Contains(transitional.Violations, violation => violation.Code == "transition-element");
        var removed = TopologyOperators.ApplyDeletionStage(origin with { StageIndex = origin.Scales.Length }, current, constraints, targets);
        Assert.True(removed.Succeeded, removed.Failure?.Message);
        Assert.False(removed.Topology!.HasTransitionElements);
        Assert.Equal(constraints, TopologyOperators.DeletionConstraints(origin with { StageIndex = origin.Scales.Length }, constraints, targets));
        _ = TopologyCompiler.Compile(removed.Topology, constraints);
        var tampered = origin with { OriginalElement = original with { ThicknessMillimeters = 3 } };
        Assert.Throws<TopologyValidationException>(() => TopologyOperators.ValidateGradualDeletion(tampered));
        Evidence("original-relative-scales", new { origin, current, transitional, removed });
    }

    [Theory]
    [InlineData("split")]
    [InlineData("merge")]
    [InlineData("material")]
    [InlineData("profile")]
    public void ExpandedSchedulerDispatchesRealOperatorsAndRetainsCompensationOrFailure(string operation)
    {
        var expansion = Only(operation);
        var plan = Plan() with { Targets = operation == "merge" ? LooseTargets() : new() { MaximumRmsRadiusMillimeters = 1e-9 } };
        plan = plan with { Options = plan.Options with { Expansion = expansion, MaximumTrials = operation == "merge" ? 1 : 2 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("m5-" + operation, plan));
        Evidence("automatic-" + operation, result);
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        var recovery = Assert.Single(result.Events.Where(item => item.Operation == operation && item.Recovery is not null));
        Assert.NotNull(recovery.Change);
        Assert.True(recovery.Work.FunctionEvaluations > 0);
        Assert.DoesNotContain(recovery.Outcome, new[] { "Unsupported", "PreflightRejected" });
        Assert.Equal(operation switch { "split" => 4, "merge" => 2, _ => 3 }, recovery.Endpoint!.Elements.Length);
        if (recovery.Outcome == "Recovered")
        {
            Assert.Contains(result.Events, item => item.Operation == operation && item.Phase == AutonomousSearchPhase.Full);
            Assert.Contains(result.Events, item => item.Operation == operation && item.Phase == AutonomousSearchPhase.Validation);
        }
        AssertLedger(result);
        output.WriteLine($"{operation}: recovery={recovery.Outcome}; functions={result.Consumed.FunctionEvaluations}; rays={result.Consumed.TracedRays}");
    }

    [Fact]
    public void FailedDirectDeleteRunsAllOriginalRelativeStagesThenPhysicallyDeletesAndUsesOriginalAcceptance()
    {
        var plan = GradualPlan();
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("m5-gradual", plan));
        Evidence("gradual-complete", result);
        AssertCompleteGradual(result);
        AssertLedger(result);
        output.WriteLine($"gradual deletion: functions={result.Consumed.FunctionEvaluations}; gradients={result.Consumed.GradientEvaluations}; rays={result.Consumed.TracedRays}; accepted={result.AcceptedArchive.Length}");
    }

    [Fact]
    public async Task MidTransitionSaveResumePreservesOriginAccountingAndArchiveAndUnknownWorkCannotPublishTransitions()
    {
        var initial = AutonomousSearchService.Create("m5-resume", GradualPlan());
        var full = AutonomousSearchService.Run(initial);
        var directory = Path.Combine(Path.GetTempPath(), "m5-resume-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "transition-checkpoint.json");
        var store = new AutonomousSearchCheckpointStore();
        using var stop = new CancellationTokenSource();
        var part = AutonomousSearchService.Run(initial, state =>
        {
            store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
            if (state.Pending?.GradualDeletion?.StageIndex == 2 && state.Reservation is null) stop.Cancel();
        }, stop.Token);
        Evidence("gradual-partial", part);
        Assert.Equal(AutonomousSearchStatus.Cancelled, part.Status);
        Assert.Equal(2, part.Pending!.GradualDeletion!.StageIndex);
        var loaded = await store.LoadAsync(path);
        CopyCheckpoint(path, "transition-checkpoint.json");
        var resumed = AutonomousSearchService.Run(loaded);
        AssertCompleteGradual(resumed); AssertLedger(resumed);
        Assert.Equal(ContentFingerprint.Compute(full.Candidates[^1].Topology), ContentFingerprint.Compute(resumed.Candidates[^1].Topology));
        Assert.Equal(full.Consumed.FunctionEvaluations, resumed.Consumed.FunctionEvaluations);
        Assert.Equal(full.Consumed.TracedRays, resumed.Consumed.TracedRays);
        Assert.Equal(ContentFingerprint.Compute(loaded.AcceptedArchive[0]), ContentFingerprint.Compute(resumed.AcceptedArchive[0]));
        var forged = loaded with { Pending = loaded.Pending! with { GradualDeletion = loaded.Pending.GradualDeletion! with { StageIndex = 1 } } };
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Run(forged));
        Assert.Throws<SimulatedCrash>(() => AutonomousSearchService.Run(loaded, state =>
        {
            store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
            if (state.Reservation is not null) throw new SimulatedCrash();
        }));
        var inFlight = await store.LoadAsync(path);
        CopyCheckpoint(path, "transition-inflight-checkpoint.json");
        var recovered = AutonomousSearchService.Run(inFlight);
        Assert.Single(recovered.Events.Where(item => item.Outcome == "InterruptedUnknownCharged"));
        Assert.Equal(inFlight.Consumed.FunctionEvaluations + inFlight.Reservation!.Maximum.FunctionEvaluations, recovered.Consumed.FunctionEvaluations);
        Assert.Equal(ContentFingerprint.Compute(part.AcceptedArchive), ContentFingerprint.Compute(recovered.AcceptedArchive));
        Assert.DoesNotContain(recovered.Candidates, candidate => candidate.Topology.HasTransitionElements);
        Assert.Null(recovered.Pending);
        AssertLedger(recovered);
        Evidence("gradual-resume", new { full, resumed, inFlight, recovered });
    }

    [Fact]
    public void AcceptedParentGetsExactlyItsConfiguredBranchLimitWithoutAnExtraZeroIndexedBranch()
    {
        var plan = Plan() with { Targets = LooseTargets() };
        plan = plan with { Options = plan.Options with { Expansion = Only("none"), MaximumTrials = 10, BranchesPerParent = 3 } };
        var result = AutonomousSearchService.Run(AutonomousSearchService.Create("m5-branch-limit", plan));
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Equal(3, result.TrialCursor);
        Assert.Equal(3, result.Events.Count(item => item.Outcome == "EditRejected"));
        Assert.Equal(1, result.Consumed.FunctionEvaluations);
        Assert.Single(result.AcceptedArchive);
        AssertLedger(result);
        Evidence("parent-branch-limit", result);
    }

    [Fact]
    public async Task ExistingM4FilesRetainTheirChecksumProtocolAndReadWithoutExecuting()
    {
        var root = Root();
        using var measurement = SequentialTraceMeasurement.Begin();
        foreach (var name in new[] { "checkpoint-archive.json", "checkpoint-mid-phase.json", "checkpoint-interrupted.json" })
        {
            var loaded = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root, "validation/rechecks/2026-10-26/autonomous-search-m4", name));
            Assert.Null(loaded.Plan.Options.Expansion);
            Assert.Null(loaded.Pending?.GradualDeletion);
            Assert.Equal(1, loaded.SchemaVersion);
        }
        Assert.Equal(0, measurement.RayCount);
    }

    private static void AssertCompleteGradual(AutonomousSearchCheckpoint result)
    {
        Assert.Equal(AutonomousSearchStatus.Completed, result.Status);
        Assert.Contains(result.Events, item => item.Operation == "delete" && item.Outcome == "BudgetExhausted");
        var transitions = result.Events.Where(item => item.Phase == AutonomousSearchPhase.TransitionValidation).ToArray();
        Assert.Equal(5, transitions.Length);
        Assert.All(transitions, item => { Assert.Equal("TransitionFeasible", item.Outcome); AssertPrescribed(item.GradualDeletion!, item.Endpoint!); });
        foreach (var item in result.Events.Where(item => item.GradualDeletion is not null && item.Optimization is not null))
            Assert.DoesNotContain(item.Optimization!.Variables, variable => variable.SurfaceId is "lens-1:front" or "lens-1:back");
        Assert.DoesNotContain(result.Candidates, candidate => candidate.Topology.HasTransitionElements);
        var final = Assert.Single(result.Candidates.Where(candidate => candidate.ParentId is not null));
        Assert.Equal(2, final.Topology.Elements.Length);
        Assert.True(final.Assessment.Completed);
        Assert.Equal(ContentFingerprint.Compute(result.Plan.Constraints), ContentFingerprint.Compute(final.Assessment.StructuralConstraints));
        Assert.Null(final.Assessment.StructuralConstraints.TransitionAllowance);
        Assert.Equal(result.Plan.Targets, final.Assessment.Targets);
        Assert.Contains(result.Events, item => item.GradualDeletion?.StageIndex == 5 && item.Phase == AutonomousSearchPhase.Recovery && item.Outcome == "Recovered");
    }
    private static void AssertPrescribed(GradualDeletionState stage, DesignTopology topology)
    {
        var actual = topology.Elements.Single(element => element.Id == stage.OriginalElement.Id);
        var scale = stage.Scales[stage.StageIndex];
        Assert.Equal(stage.OriginalElement.ThicknessMillimeters * scale, actual.ThicknessMillimeters);
        Assert.Equal(stage.OriginalElement.Front.Profile.CurvaturePerMillimeter * scale, actual.Front.Profile.CurvaturePerMillimeter);
        foreach (var term in stage.OriginalElement.Front.Profile.Terms)
            Assert.Equal(term.Coefficient * scale, actual.Front.Profile.Terms.Single(item => item.Power == term.Power).Coefficient);
        Assert.Equal(ContentFingerprint.Compute(TopologyOperators.PrescribedDeletionElement(stage)), ContentFingerprint.Compute(actual));
    }
    private static AutonomousSearchPlan GradualPlan()
    {
        var plan = Plan();
        var stage = plan.Options.Full with { MaximumUpdates = 1, MaximumFunctionEvaluations = 2, MaximumGradientEvaluations = 2, LearningRate = .001 };
        return plan with
        {
            Targets = LooseTargets(),
            Options = plan.Options with
            {
                MaximumTrials = 1,
                Local = stage,
                Full = stage,
                Expansion = Only("delete") with
                { EnableGradualDeletion = true, DirectDeletionRecoveryMaximumFunctionEvaluations = 2 }
            }
        };
    }
    private static TopologyExpansionOptions Only(string operation) => new()
    {
        EnableInsertion = false,
        EnableDeletion = operation == "delete",
        EnableGradualDeletion = false,
        EnableSplit = operation == "split",
        EnableMerge = operation == "merge",
        EnableMaterial = operation == "material",
        EnableProfile = operation == "profile"
    };
    private static AutonomousSearchPlan Plan()
    {
        var stage = new FixedTopologyOptions
        {
            ComputeMode = DesignComputeMode.Cpu,
            PerformFinalAssessment = false,
            MaximumUpdates = 0,
            MaximumFunctionEvaluations = 1,
            MaximumGradientEvaluations = 1,
            Sampling = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 }
        };
        return new()
        {
            Seeds = [Source()],
            Options = new()
            {
                MaximumTrials = 2,
                MaximumFunctionEvaluations = 1500,
                MaximumGradientEvaluations = 100,
                MaximumTracedRays = 1_000_000,
                MaximumStageTracedRays = 100_000,
                FinalValidationRayReserve = 50_000,
                TimeLimit = TimeSpan.FromMinutes(3),
                Recovery = new() { ProfileRadialSamples = 8, MaximumFunctionEvaluations = 300 },
                Local = stage,
                Full = stage
            }
        };
    }
    private static TopologyTargets LooseTargets() => new()
    {
        FocalLengthRelativeTolerance = 1,
        FNumberRelativeTolerance = 1,
        MaximumRmsRadiusMillimeters = 5,
        MaximumSpotRadiusMillimeters = 10,
        MaximumDistortionFraction = 1,
        MinimumValidRayFraction = .1
    };
    private static DesignTopology Source() => JsonSerializer.Deserialize<DesignTopology>(File.ReadAllText(Path.Combine(Root(),
        "labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json")), Json)! with
    { Fields = [new("Axis", 0, 0)] };
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory!.FullName;
    }
    private static void AssertLedger(AutonomousSearchCheckpoint result)
    {
        Assert.Equal(result.Consumed.FunctionEvaluations, result.Events.Sum(item => item.Work.FunctionEvaluations));
        Assert.Equal(result.Consumed.GradientEvaluations, result.Events.Sum(item => item.Work.GradientEvaluations));
        Assert.Equal(result.Consumed.TracedRays, result.Events.Sum(item => item.Work.TracedRays));
        Assert.InRange(result.Consumed.FunctionEvaluations, 0, result.Plan.Options.MaximumFunctionEvaluations);
        Assert.InRange(result.Consumed.TracedRays, 0, result.Plan.Options.MaximumTracedRays);
        AutonomousSearchService.Validate(result);
    }
    private static string? EvidenceDirectory() => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M5_EVIDENCE");
    private static void CopyCheckpoint(string path, string name)
    {
        var directory = EvidenceDirectory(); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); File.Copy(path, Path.Combine(directory, name), true);
    }
    private static void Evidence(string name, object value)
    {
        var directory = EvidenceDirectory(); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, Json));
    }
    private sealed class SimulatedCrash : Exception;
}
