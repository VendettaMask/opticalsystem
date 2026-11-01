using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class PreparationModeResumeTests
{
    [Theory]
    [InlineData("Paraxial", "Starts")]
    [InlineData("Paraxial", "StartsAfterRandom")]
    [InlineData("Screening", "Screening")]
    [InlineData("Retrieval", "Retrieval:Transform")]
    [InlineData("Retrieval", "Retrieval:Index")]
    [InlineData("Retrieval", "Retrieval:Validate")]
    [InlineData("Competition", "Competition:Construction")]
    [InlineData("Competition", "Competition:Rounds")]
    [InlineData("Competition", "Competition:Finals")]
    [InlineData("EvolutionDirect", "Evolution:Trials")]
    [InlineData("EvolutionDirect", "Evolution:Parents")]
    [InlineData("EvolutionRecovery", "Evolution:Trials")]
    [InlineData("EvolutionRecovery", "Evolution:Finals")]
    [InlineData("EvolutionCompetition", "Evolution:Trials")]
    [InlineData("EvolutionCompetition", "Evolution:Competition")]
    public async Task EveryPreparationRouteResumesItsPoolStageRandomAndDiscreteWorkExactly(string mode, string stopStage)
    {
        var plan = Plan(mode); var initial = SeedPreparationSearch.Create("mode-resume", plan);
        var baseline = SeedPreparationSearch.Run(initial, _ => { });
        var legacy = ParaxialSeedSearch.Run(plan);
        Same(baseline.Result!, legacy);
        var path = Path.Combine(Path.GetTempPath(), "mode-resume-" + Guid.NewGuid().ToString("N"), "run.preparation.json");
        var store = new SeedPreparationCheckpointStore(); long revision = 0; using var pause = new CancellationTokenSource();
        long first;
        SeedPreparationCheckpoint stopped;
        using (var meter = SequentialTraceMeasurement.Begin()) { stopped = SeedPreparationSearch.Run(initial, Save, pause.Token); first = meter.RayCount; }
        Assert.Equal(SeedPreparationStatus.Suspended, stopped.Status); Assert.NotNull(stopped.EngineState); Assert.Equal(first, stopped.Consumed.TracedRays);
        var loaded = await store.LoadAsync(path); Assert.Equal(ContentFingerprint.Compute(stopped), ContentFingerprint.Compute(loaded));
        using var next = SequentialTraceMeasurement.Begin(); var resumed = SeedPreparationSearch.Run(loaded, Save);
        Assert.Equal(baseline.RandomState, resumed.RandomState); Assert.Equal(baseline.Cursor, resumed.Cursor);
        Assert.Equal(resumed.Consumed.TracedRays, first + next.RayCount); Same(baseline.Result!, resumed.Result!);
        void Save(SeedPreparationCheckpoint value)
        {
            store.SaveAsync(value, path, revision).AsTask().GetAwaiter().GetResult(); revision = value.Revision;
            if ((value.Stage == stopStage || stopStage == "StartsAfterRandom" && value.Stage == "Starts" && value.Cursor >= 2)
                && value.Pending is null) pause.Cancel();
        }
    }

    private static void Same(ParaxialSeedSearchResult a, ParaxialSeedSearchResult b)
    {
        Assert.Equal(a.Algorithm, b.Algorithm); Assert.Equal(a.ProtocolFingerprint, b.ProtocolFingerprint); Assert.Equal(a.Termination, b.Termination);
        Assert.Equal(ContentFingerprint.Compute(a.Seeds), ContentFingerprint.Compute(b.Seeds));
        Assert.Equal(ContentFingerprint.Compute(a.Work with { Elapsed = TimeSpan.Zero }), ContentFingerprint.Compute(b.Work with { Elapsed = TimeSpan.Zero }));
        // Time allowances/timings are observations, not optical outputs or extra budget. The full numeric state otherwise agrees.
        var settings = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var x = JsonSerializer.SerializeToNode(a, settings)!; var y = JsonSerializer.SerializeToNode(b, settings)!; Clean(x); Clean(y);
        if (a.Evolution is not null)
        {
            // The child protocol binds an independently measured remaining TimeLimit; even two uninterrupted runs differ there.
            // Root task identity, full prescriptions, original QA, all choices and discrete work remain exact below.
            x["Competition"]?.AsObject().Remove("ProtocolFingerprint"); y["Competition"]?.AsObject().Remove("ProtocolFingerprint");
        }
        Assert.Equal(x.ToJsonString(), y.ToJsonString());
        static void Clean(System.Text.Json.Nodes.JsonNode node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                foreach (var key in obj.Select(p => p.Key).Where(k => k.Contains("Elapsed", StringComparison.Ordinal) || k.Contains("Time", StringComparison.Ordinal)).ToArray()) obj.Remove(key);
                foreach (var child in obj.Select(p => p.Value).OfType<System.Text.Json.Nodes.JsonNode>().ToArray()) Clean(child);
            }
            else if (node is System.Text.Json.Nodes.JsonArray array) foreach (var child in array.OfType<System.Text.Json.Nodes.JsonNode>()) Clean(child);
        }
    }

    internal static AutonomousSearchPlan Plan(string mode)
    {
        if (mode == "Retrieval")
        {
            var (p, o, _) = LensRetrievalTests.Specification(); return p with { Options = p.Options with { SeedPreparation = o with { TimeLimit = TimeSpan.FromSeconds(600) } } };
        }
        if (mode.StartsWith("Evolution", StringComparison.Ordinal))
        {
            var (p, o) = StructureEvolutionTests.Specification(); var kind = mode == "EvolutionDirect" ? StructureEvolutionMode.DirectCompensation :
                mode == "EvolutionRecovery" ? StructureEvolutionMode.RecoveryAndCompensation : StructureEvolutionMode.RecoveryAndCompetition;
            o = o with
            {
                TimeLimit = TimeSpan.FromSeconds(600),
                Evolution = o.Evolution! with { Mode = kind, MaximumBranches = 3, MaximumPhaseTime = TimeSpan.FromSeconds(30) },
                Competition = o.Competition! with { MaximumStageTimePerCandidate = TimeSpan.FromSeconds(30) }
            };
            return p with { Options = p.Options with { SeedPreparation = o } };
        }
        if (mode == "Competition")
        {
            var (p, o) = StructureCompetitionTests.Specification(); o = o with { TimeLimit = TimeSpan.FromSeconds(120), Competition = o.Competition! with { MaximumStageTimePerCandidate = TimeSpan.FromSeconds(30) } };
            return p with { Options = p.Options with { SeedPreparation = o } };
        }
        var (source, _) = StructureEvolutionTests.Specification(); var options = new ParaxialSeedSearchOptions
        {
            MaximumStarts = 3,
            MaximumFunctionEvaluations = 120,
            MaximumEvaluationsPerStart = 40,
            MaximumTracedRays = 500000,
            TimeLimit = TimeSpan.FromSeconds(120),
            ImageScreening = mode == "Screening" ? new() { MaximumCandidates = 2 } : null
        };
        return source with { Options = source.Options with { SeedPreparation = options } };
    }
}
