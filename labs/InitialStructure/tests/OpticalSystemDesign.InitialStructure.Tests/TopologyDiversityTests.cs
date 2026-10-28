using System.Collections.Immutable;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyDiversityTests
{
    [Fact]
    public void NewPoolKeepsBestAndDifferentPrescriptionWhileNullKeepsExactLegacyRanking()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var topology = plan.Seeds[0];
        var elements = topology.Elements.SetItem(1, topology.Elements[1] with
            { Front = topology.Elements[1].Front with { Profile = topology.Elements[1].Front.Profile with { CurvaturePerMillimeter = .015 } } });
        var near = topology.Elements.SetItem(0, topology.Elements[0] with
            { Front = topology.Elements[0].Front with { Profile = topology.Elements[0].Front.Profile with { CurvaturePerMillimeter = .022223 } } });
        // These synthetic assessments test selection only, never physical acceptance.
        var candidates = ImmutableArray.Create(Candidate("best", topology, 1), Candidate("duplicate", topology with { Name = "copy" }, 1.1),
            Candidate("near", topology with { Elements = near }, 1.2), Candidate("diverse", topology with { Elements = elements }, 1.3));
        var state = AutonomousSearchService.Create("selection", plan with { Options = plan.Options with { CandidatesPerElementCount = 2 } }) with { Candidates = candidates };
        using var trace = SequentialTraceMeasurement.Begin();
        Assert.Equal(new[] { "best", "duplicate" }, TopologyDiversitySelection.Select(state));
        var diverse = state with { Plan = state.Plan with { Options = state.Plan.Options with { PoolDiversity = new() } } };
        Assert.Equal(new[] { "best", "diverse" }, TopologyDiversitySelection.Select(diverse));
        Assert.Equal(TopologyDiversitySelection.Select(diverse).ToArray(), TopologyDiversitySelection.Select(diverse).ToArray());
        var qualified = candidates[3] with { Assessment = candidates[3].Assessment with { MeetsTargets = true }, Score = 10 };
        var qualifiedOnly = diverse with { Candidates = candidates.SetItem(3, qualified),
            Plan = diverse.Plan with { Options = diverse.Plan.Options with { QualifiedReductionOnly = true } } };
        Assert.Equal(new[] { "diverse" }, TopologyDiversitySelection.Select(qualifiedOnly));
        Assert.Equal(0, trace.RayCount);
        Assert.DoesNotContain("poolDiversity", System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        Assert.DoesNotContain("seedPreparation", System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public async Task ActualDiversePoolPersistsCancelsResumesAndRejectsChangedMembershipBeforeTracing()
    {
        var plan = ParaxialSeedSearchTests.Plan();
        var second = plan.Seeds[0] with { Elements = plan.Seeds[0].Elements.SetItem(1, plan.Seeds[0].Elements[1] with
            { ThicknessMillimeters = 2.01 }) };
        plan = plan with { Seeds = [plan.Seeds[0], second], Options = plan.Options with { PoolDiversity = new(),
            MaximumTrials = 1, FixedTopologyOnly = true, MaximumGradientEvaluations = 0,
            Full = plan.Options.Full with { MaximumUpdates = 0, MaximumGradientEvaluations = 0, MaximumFunctionEvaluations = 1 }, TimeLimit = TimeSpan.FromSeconds(30) } };
        var root = Path.Combine(Path.GetTempPath(), "diverse-resume-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "run.autonomous.json"); var store = new AutonomousSearchCheckpointStore();
            using var cancel = new CancellationTokenSource();
            var stopped = AutonomousSearchService.Run(AutonomousSearchService.Create("diverse-resume", plan), state =>
            {
                store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult();
                if (state.Candidates.Length >= 2) cancel.Cancel();
            }, cancel.Token);
            var loaded = await store.LoadAsync(path);
            Assert.Equal(ContentFingerprint.Compute(stopped), ContentFingerprint.Compute(loaded)); Assert.Equal(2, loaded.Pool.Length);
            using (var trace = SequentialTraceMeasurement.Begin())
            {
                Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Validate(loaded with { Pool = loaded.Pool.Reverse().ToImmutableArray() }));
                Assert.Equal(0, trace.RayCount);
            }
            var resumed = AutonomousSearchService.Run(loaded, state => store.SaveAsync(state, path, state.Revision - 1).AsTask().GetAwaiter().GetResult());
            Assert.Equal(ContentFingerprint.Compute(stopped.Events), ContentFingerprint.Compute(resumed.Events.Take(stopped.Events.Length).ToImmutableArray()));
            Assert.True(resumed.Consumed.TracedRays >= stopped.Consumed.TracedRays);
            Assert.Equal(ContentFingerprint.Compute(resumed), ContentFingerprint.Compute(await store.LoadAsync(path)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static AutonomousSearchCandidate Candidate(string id, DesignTopology topology, double score) => new(id, null, topology,
        new() { OpticFingerprint = "selection-only", ProtocolFingerprint = "selection-only", Targets = new(), Completed = true }, score);
}
