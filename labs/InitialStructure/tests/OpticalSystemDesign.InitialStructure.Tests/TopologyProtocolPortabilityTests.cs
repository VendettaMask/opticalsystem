using System.Collections.Immutable;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class TopologyProtocolPortabilityTests
{
    [Fact]
    public async Task RetainedPlatformEncodingsLoadWithoutRewritingPhysicsOrSpendingRays()
    {
        using var trace = SequentialTraceMeasurement.Begin();
        var state = await Fixture();
        var before = ContentFingerprint.Compute(state);
        var root = Path.Combine(Path.GetTempPath(), "topology-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var encoding in Encodings)
            {
                var alternate = state with { Candidates = state.Candidates.Select(candidate => candidate with
                    { Assessment = candidate.Assessment with { ProtocolFingerprint = encoding } }).ToImmutableArray(), Revision = 1 };
                AutonomousSearchService.Validate(alternate);
                var path = Path.Combine(root, encoding + ".autonomous.json");
                var store = new AutonomousSearchCheckpointStore();
                await store.SaveAsync(alternate, path, expectedRevision: 0);
                Assert.Equal(ContentFingerprint.Compute(alternate), ContentFingerprint.Compute(await store.LoadAsync(path)));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
        Assert.Equal(before, ContentFingerprint.Compute(state));
        Assert.Equal(0, trace.RayCount);
    }

    [Fact]
    public async Task HistoricalAliasesBindAllTargetsConstraintsAndFieldCoordinatesExactly()
    {
        var state = await Fixture();
        var candidate = state.Candidates[0];
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        Assert.Equal(HistoricalTopologyProtocols.ThreeToTwoProtocol, TopologyValidator.GetProtocolFingerprint(source, state.Plan.Targets));
        foreach (var encoding in Encodings)
        {
            Assert.True(TopologyValidator.MatchesProtocol(source, state.Plan.Targets, encoding));
            Assert.False(TopologyValidator.MatchesProtocol(source, state.Plan.Targets with
                { MaximumRmsRadiusMillimeters = Math.BitIncrement(state.Plan.Targets.MaximumRmsRadiusMillimeters) }, encoding));
            Assert.False(TopologyValidator.MatchesProtocol(TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints with
                { MinimumAirGapMillimeters = Math.BitIncrement(state.Plan.Constraints.MinimumAirGapMillimeters) }), state.Plan.Targets, encoding));
            var fields = candidate.Topology.Fields.SetItem(1, candidate.Topology.Fields[1] with { YAngleDegrees = 13 });
            Assert.False(TopologyValidator.MatchesProtocol(TopologyCompiler.Compile(candidate.Topology with { Fields = fields },
                state.Plan.Constraints), state.Plan.Targets, encoding));
        }
        Assert.False(TopologyValidator.MatchesProtocol(source, state.Plan.Targets, new string('0', 64)));
        var tampered = state with { Candidates = state.Candidates.SetItem(0, candidate with
            { Assessment = candidate.Assessment with { ProtocolFingerprint = new string('0', 64) } }) };
        using var trace = SequentialTraceMeasurement.Begin();
        Assert.Throws<InvalidDataException>(() => AutonomousSearchService.Validate(tampered));
        Assert.Equal(0, trace.RayCount);
    }

    private static readonly string[] Encodings = [
        "7fc8e0607c4f59d029dfe5693362e2672c54a39ddddf9b284863852c5a92e964",
        "8aa5e4e99da9c1ae72ce1609da55d28b05896c2c1c42a35a5f7242cb826245f4"];

    private static Task<AutonomousSearchCheckpoint> Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/run.autonomous.json"));
    }
}
