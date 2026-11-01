using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousCementedTests
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    [Theory]
    [InlineData("N-BK7")]
    [InlineData("N-F2")]
    public void GrowthUsesOneRealSharedFaceAndCopiesEveryOriginalAperture(string material)
    {
        var plan = Plan(); var original = plan.Seeds[0]; var hash = ContentFingerprint.Compute(original);
        var edit = TopologyOperators.CementedInsert(original, "crown", "added", material, 1, plan.Constraints);
        Assert.True(edit.Succeeded, edit.Failure?.Message); var model = edit.Topology!;
        Assert.Equal(original.Elements[0], model.Elements[0]); Assert.Equal(original.Stop, model.Stop); Assert.Equal(original.Image, model.Image);
        Assert.Equal(original.Elements[0].Back, model.Elements[1].Front); Assert.Equal(original.Elements[0].Back, model.Elements[1].Back);
        var source = TopologyCompiler.Compile(model, plan.Constraints); var optic = source.CreateOptic();
        Assert.Equal(source.Elements[0].BackIndex, source.Elements[1].FrontIndex);
        Assert.Equal(material, optic.SurfaceGroup.Items[source.Elements[0].BackIndex].MaterialAfter.Name);
        Assert.Equal(3, TopologyPrescriptionFaces.Enumerate(model).Count());
        Assert.Equal(hash, ContentFingerprint.Compute(original));
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("slots")]
    [InlineData("space")]
    [InlineData("thickness")]
    [InlineData("occupied")]
    public void InvalidAttachmentsCannotChangeProtectedGeometryOrManufacturingLimits(string defect)
    {
        var plan = Plan(); var model = plan.Seeds[0]; var limits = plan.Constraints; double thickness = 1;
        if (defect == "protected") model = model with { Elements = [model.Elements[0] with { Protected = true }] };
        if (defect == "slots") limits = limits with { MaximumElementCount = 1 };
        if (defect == "space") thickness = 60;
        if (defect == "thickness") thickness = 0;
        if (defect == "occupied") model = CementedTopologyTests.Example();
        using var rays = SequentialTraceMeasurement.Begin();
        var result = TopologyOperators.CementedInsert(model, "crown", "illegal", "N-F2", thickness, limits);
        Assert.False(result.Succeeded); Assert.Equal(0, rays.RayCount);
    }

    [Fact]
    public async Task SafeStopAcrossDispatchBothCompensationPhasesAndCandidatePoolResumesExactly()
    {
        var plan = Plan(); var full = AutonomousSearchService.Run(AutonomousSearchService.Create("same-run", plan));
        Assert.True(full.Events.Any(e => e.Operation == "cemented-insert" && e.Phase == AutonomousSearchPhase.Validation),
            string.Join("; ", full.Events.Select(e => e.Operation + "/" + e.Phase + ":" + e.Outcome + " " + string.Join(",", e.Recovery?.Diagnostics ?? []))));
        var root = Path.Combine(Path.GetTempPath(), "cemented-resume-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "task.autonomous.json"); var store = new AutonomousSearchCheckpointStore();
            var state = AutonomousSearchService.Create("same-run", plan) with { Revision = 1 }; await store.SaveAsync(state, path, 0);
            var targets = new Queue<string>(["dispatch", "local", "full", "pool"]);
            while (state.Status != AutonomousSearchStatus.Completed)
            {
                using var pause = new CancellationTokenSource(); var revision = state.Revision;
                void Stop(string point) { if (targets.TryPeek(out var expected) && expected == point) { targets.Dequeue(); pause.Cancel(); } }
                state = AutonomousSearchService.Run(state, next =>
                {
                    store.SaveAsync(next, path, revision).AsTask().GetAwaiter().GetResult(); revision = next.Revision;
                    if (next.Pending is { Operation: "cemented-insert", Phase: AutonomousSearchPhase.Recovery } && next.Reservation is not null) Stop("dispatch");
                    if (next.Reservation is null && next.Candidates.Any(c => c.Topology.CementedInterfaces is not null)) Stop("pool");
                }, pause.Token, (pending, _) =>
                {
                    if (pending.Operation == "cemented-insert") Stop(pending.Phase == AutonomousSearchPhase.Local ? "local" : "full");
                });
                store = new(); using var reads = SequentialTraceMeasurement.Begin(); state = await store.LoadAsync(path); Assert.Equal(0, reads.RayCount);
            }
            Assert.True(targets.Count == 0, string.Join(",", targets) + " | " + string.Join("; ", full.Events.Select(e => e.Operation + "/" + e.Phase + ":" + e.Outcome + " steps=" + e.Optimization?.Steps.Length + " " + string.Join(",", (e.Optimization?.Diagnostics ?? []).Take(2))))); AssertEquivalent(full, state);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedVariablesAreUniqueAndAnyProtectedOwnerFreezesTheCommonFace()
    {
        var plan = Plan(); var seed = CementedTopologyTests.Example();
        plan = plan with { Seeds = [seed with { Elements = seed.Elements.SetItem(1, seed.Elements[1] with { Protected = true }) }],
            Options = plan.Options with { FixedTopologyOnly = true, AxialCompensation = new(), CompensateConic = true, MaximumTrials = 1 } };
        var done = AutonomousSearchService.Run(AutonomousSearchService.Create("protected-shared", plan));
        var stages = done.Events.Where(e => e.Optimization is not null).ToArray(); Assert.NotEmpty(stages);
        foreach (var stage in stages)
        {
            Assert.DoesNotContain(stage.Optimization!.Variables, r => r.SurfaceId == "common");
            var vars = stage.Optimization.Variables;
            Assert.Equal(vars.Length, vars.DistinctBy(v => (v.SurfaceId, v.Kind, v.Power)).Count());
        }
    }

    [Fact]
    public void F4ConflictRemainsZeroWorkAndForeignKernelsRemainReadOnly()
    {
        var plan = Plan(); var f4 = plan with { Targets = plan.Targets with { FNumber = 4 },
            Seeds = [plan.Seeds[0] with { Stop = new("stop", 0, 6.25) }] };
        using var rays = SequentialTraceMeasurement.Begin(); var done = AutonomousSearchService.Run(AutonomousSearchService.Create("f4", f4));
        Assert.Equal(0, rays.RayCount); Assert.Equal(0, done.Consumed.FunctionEvaluations); Assert.Empty(done.AcceptedArchive);
        Assert.Contains(done.Events, e => e.Outcome == "OriginalExternalStopApertureConflict");
        var foreign = AutonomousSearchService.Create("foreign", plan) with { CementedKernelFingerprint = new('0', 64) };
        AutonomousSearchService.Validate(foreign); Assert.False(AutonomousSearchService.CanExecute(foreign));
        Assert.Throws<NotSupportedException>(() => AutonomousSearchService.Run(foreign)); Assert.Equal(0, rays.RayCount);
        Assert.DoesNotContain("cemented", JsonSerializer.Serialize(new TopologyExpansionOptions(), Json));
        Assert.DoesNotContain("cementedKernelFingerprint", JsonSerializer.Serialize(AutonomousSearchService.Create("old", plan with
        { Options = plan.Options with { Expansion = plan.Options.Expansion! with { Cemented = null } } }), Json));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void OriginalInsertionPermissionAndElementCountStillGovernScheduling(bool allowed, int count)
    {
        var plan = Plan(); plan = plan with { Constraints = plan.Constraints with { MaximumElementCount = count },
            Options = plan.Options with { MaximumTrials = 2, Expansion = plan.Options.Expansion! with { EnableInsertion = allowed } } };
        var done = AutonomousSearchService.Run(AutonomousSearchService.Create("permissions", plan));
        Assert.DoesNotContain(done.Events, e => e.Operation == "cemented-insert");
        Assert.All(done.Candidates, c => Assert.Null(c.Topology.CementedInterfaces));
    }

    [Fact]
    public void LegacyCementedSchedulingStillRequiresTheExplicitVersionedPolicy()
    {
        var plan = Plan(); plan = plan with { Seeds = [CementedTopologyTests.Example()],
            Options = plan.Options with { Expansion = plan.Options.Expansion! with { Cemented = null } } };
        Assert.Throws<NotSupportedException>(() => AutonomousSearchService.Create("no-opt-in", plan));
    }

    [Fact]
    public async Task CheckpointWriterCannotChangeNumericIdentityAndTinyBudgetCannotQualifyPartialWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "cemented-policy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = new AutonomousSearchCheckpointStore(); var path = Path.Combine(root, "state.autonomous.json");
            var state = AutonomousSearchService.Create("immutable", Plan()) with { Revision = 1 }; await store.SaveAsync(state, path, 0);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(state with
            { Revision = 2, CementedKernelFingerprint = new('0', 64) }, path, 1).AsTask());
            var plan = Plan(); plan = plan with { Options = plan.Options with { MaximumTracedRays = 100, FinalValidationRayReserve = 100, MaximumStageTracedRays = 100 } };
            var done = AutonomousSearchService.Run(AutonomousSearchService.Create("limited", plan));
            Assert.InRange(done.Consumed.TracedRays, 0, 100); Assert.Empty(done.AcceptedArchive);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReusedCementedGroupsGrowWithoutCollidingWithPriorTrialIdentities()
    {
        var model = CementedTopologyTests.Example();
        model = model with { Elements = [model.Elements[0] with { Id = "trial-0-cemented" }, model.Elements[1] with { Id = "trial-1-cemented" }],
            CementedInterfaces = new() { Interfaces = [new("trial-0-cemented", "trial-1-cemented", "common")] } };
        var plan = Plan(); plan = plan with { Seeds = [model], Options = plan.Options with { MaximumTrials = 2,
            Expansion = plan.Options.Expansion! with { Cemented = new() { MaximumGroupElements = 3, ThicknessMultipliers = [1] } } } };
        var state = AutonomousSearchService.Run(AutonomousSearchService.Create("reused-group", plan));
        Assert.Contains(state.Events, e => e.Operation == "cemented-insert" && e.Endpoint?.Elements.Length == 3);
        Assert.DoesNotContain(state.Events, e => e.Operation == "cemented-insert" && e.Outcome == "EditRejected");
    }

    internal static AutonomousSearchPlan Plan(long seed = 42)
    {
        var plan = SpcBranchTests.Plan(); var local = new AutonomousSearchOptions().Local with
        { Sampling = plan.Options.Local.Sampling, MaximumUpdates = 3, MaximumFunctionEvaluations = 20, MaximumGradientEvaluations = 20 };
        return plan with { Options = plan.Options with { Seed = seed, SeedPreparation = null,
            Expansion = new() { Cemented = new() { ThicknessMultipliers = [1] }, EnableInsertion = true, EnableMaterial = true,
                EnableDeletion = false, EnableGradualDeletion = false, EnableSplit = false, EnableMerge = false, EnableProfile = false },
            InsertionMaterials = ["N-F2"], MaximumTrials = 3, BranchesPerParent = 4,
            MaximumFunctionEvaluations = 300, MaximumGradientEvaluations = 200, MaximumTracedRays = 3_000_000,
            MaximumStageTracedRays = 500_000, FinalValidationRayReserve = 100_000, TimeLimit = TimeSpan.FromSeconds(120),
            Recovery = plan.Options.Recovery with { MaximumFunctionEvaluations = 24, TimeLimit = TimeSpan.FromSeconds(10) },
            Local = local, Full = local with { MaximumUpdates = 4 }, AxialCompensation = new() } };
    }

    internal static void AssertEquivalent(AutonomousSearchCheckpoint a, AutonomousSearchCheckpoint b)
    {
        Assert.Equal(a.RandomState, b.RandomState); Assert.Equal(a.TrialCursor, b.TrialCursor); Assert.Equal(a.SeedCursor, b.SeedCursor);
        Assert.Equal(a.Consumed with { Elapsed = TimeSpan.Zero }, b.Consumed with { Elapsed = TimeSpan.Zero });
        Assert.Equal(ContentFingerprint.Compute(a.Candidates), ContentFingerprint.Compute(b.Candidates));
        Assert.Equal(ContentFingerprint.Compute(a.AcceptedArchive), ContentFingerprint.Compute(b.AcceptedArchive)); Assert.Equal(a.Pool.ToArray(), b.Pool.ToArray());
        Assert.Null(b.Pending); Assert.Null(b.Reservation);
    }
}
