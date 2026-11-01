using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SeedPreparationCheckpointTests
{
    [Fact]
    public async Task StreamingEnvelopeKeepsLegacyReadbackAndDetectsChangedFilesEvenWithCachedMetadata()
    {
        var state=SeedPreparationSearch.Create("codec-guard",Plan()) with{Revision=1};
        var path=Path.Combine(Path.GetTempPath(),"preparation-codec-"+Guid.NewGuid().ToString("N"),"saved.preparation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters={new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{checkpoint=state,sha256=ContentFingerprint.Compute(state)},settings));
        var store=new SeedPreparationCheckpointStore();Assert.Equal(ContentFingerprint.Compute(state),ContentFingerprint.Compute(await store.LoadAsync(path)));
        var second=state with{Revision=2,Consumed=new(){FunctionEvaluations=1}};await store.SaveAsync(second,path,1);
        Assert.Contains("\"format\":2",await File.ReadAllTextAsync(path));
        var modified=await File.ReadAllTextAsync(path);var timestamp=File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path,modified.Replace("\"cursor\":0","\"cursor\":1"));File.SetLastWriteTimeUtc(path,timestamp);
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync(second with{Revision=3},path,2).AsTask());
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ConstructionStopRestoresRandomPoolAndBudgetWithoutAnyReplayedTrace(int stopAfter)
    {
        var plan = Plan(); var initial = SeedPreparationSearch.Create("cold-restart", plan);
        var baseline = SeedPreparationSearch.Run(initial, _ => { });
        using var pause = new CancellationTokenSource(); var store = new SeedPreparationCheckpointStore();
        var path = Path.Combine(Path.GetTempPath(), "preparation-test-" + Guid.NewGuid().ToString("N"), "saved.preparation.json");
        long revision = 0;
        SeedPreparationCheckpoint paused;
        long firstRays;
        using (var measurement = SequentialTraceMeasurement.Begin())
        {
            paused = SeedPreparationSearch.Run(initial, Save, pause.Token);
            firstRays = measurement.RayCount;
        }
        Assert.Equal(SeedPreparationStatus.Suspended, paused.Status); Assert.Equal(stopAfter, paused.Candidates.Length);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(ContentFingerprint.Compute(paused), ContentFingerprint.Compute(loaded));
        Assert.Equal(paused.RandomState, loaded.RandomState); Assert.Equal(paused.Consumed.TracedRays, firstRays);
        using var second = SequentialTraceMeasurement.Begin();
        var resumed = SeedPreparationSearch.Run(loaded, Save);
        Equivalent(baseline, resumed);
        Assert.Equal(resumed.Consumed.TracedRays, firstRays + second.RayCount);
        Assert.Equal(SeedPreparationStatus.Completed, resumed.Status);
        var final = await store.LoadAsync(path); Assert.Equal(ContentFingerprint.Compute(resumed), ContentFingerprint.Compute(final));
        void Save(SeedPreparationCheckpoint state)
        {
            store.SaveAsync(state, path, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision;
            if (state.Stage == "Construction" && state.Candidates.Length == stopAfter && state.Pending is null) pause.Cancel();
        }
    }

    [Fact]
    public void OriginalF4ConflictStillPerformsNoConstructionOrTracing()
    {
        var plan = Plan(); var seed = plan.Seeds[0]; plan = plan with { Seeds = [seed with { Stop = seed.Stop with { ClearSemiDiameterMillimeters = 6.25 } }] };
        using var traces = SequentialTraceMeasurement.Begin();
        var result = SeedPreparationSearch.Run(SeedPreparationSearch.Create("f4-conflict", plan), _ => { });
        Assert.Equal("OriginalExternalStopApertureConflict", result.StopReason); Assert.Empty(result.Candidates);
        Assert.Empty(result.Result!.Seeds); Assert.Equal(0, traces.RayCount); Assert.Equal(0, result.Consumed.FunctionEvaluations);
    }

    [Fact]
    public async Task StoreRejectsStaleWritersBudgetRollbackTaskChangesAndCorruption()
    {
        var state = SeedPreparationSearch.Create("guard", Plan()) with { Revision = 1 }; var store = new SeedPreparationCheckpointStore();
        var path = Path.Combine(Path.GetTempPath(), "preparation-guard-" + Guid.NewGuid().ToString("N"), "saved.preparation.json");
        await store.SaveAsync(state, path, 0);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(state with { Revision = 3 }, path, 2).AsTask());
        var advanced = state with { Revision = 2, Consumed = new() { FunctionEvaluations = 1 } }; await store.SaveAsync(advanced, path, 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(advanced with { Revision = 3, Consumed = new() }, path, 2).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(advanced with { Revision = 3, Plan = advanced.Plan with { Targets = advanced.Plan.Targets with { MaximumRmsRadiusMillimeters = 99 } } }, path, 2).AsTask());
        var text = await File.ReadAllTextAsync(path); await File.WriteAllTextAsync(path, text.Replace("\"randomState\":", "\"randomState\":1"));
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync(path));
    }

    [Fact]
    public void UnsettledReservationIsChargedOnceAndNeverReplayedAsFreeWork()
    {
        var original = SeedPreparationSearch.Create("lost", Plan());
        var state = original with
        {
            Revision = 1,
            Status = SeedPreparationStatus.Running,
            Pending = new()
            {
                Name = "Construction",
                Source = original.Plan.Seeds[0],
                Variables = [],
                InFlight = true,
                Reservation = new() { FunctionEvaluations = 10, TracedRays = 123, Elapsed = TimeSpan.FromSeconds(1) }
            }
        };
        using var measurement = SequentialTraceMeasurement.Begin();
        var stopped = SeedPreparationSearch.Run(state, _ => { }); Assert.Equal(SeedPreparationStatus.Interrupted, stopped.Status);
        Assert.Equal(10, stopped.Consumed.FunctionEvaluations); Assert.Equal(123, stopped.Consumed.TracedRays); Assert.Equal(1, stopped.LostReservations);
        Assert.Empty(stopped.Result!.Seeds); Assert.Equal(0, measurement.RayCount);
        Assert.Equal(stopped, SeedPreparationSearch.Run(stopped, _ => throw new Exception("A settled reservation must never be charged twice.")));
    }

    internal static void Equivalent(SeedPreparationCheckpoint a, SeedPreparationCheckpoint b)
    {
        Assert.Equal(a.RandomState, b.RandomState); Assert.Equal(a.Cursor, b.Cursor); Assert.Equal(a.Stage, b.Stage); Assert.Equal(a.StopReason, b.StopReason);
        Assert.Equal(ContentFingerprint.Compute(a.Candidates.Select(c => c with { Work = c.Work with { Elapsed = TimeSpan.Zero } }).ToArray()),
            ContentFingerprint.Compute(b.Candidates.Select(c => c with { Work = c.Work with { Elapsed = TimeSpan.Zero } }).ToArray()));
        Assert.Equal(ContentFingerprint.Compute(a.Endpoint), ContentFingerprint.Compute(b.Endpoint));
        Assert.Equal(ContentFingerprint.Compute(a.Assessment), ContentFingerprint.Compute(b.Assessment));
        Assert.Equal(ContentFingerprint.Compute(a.Consumed with { Elapsed = TimeSpan.Zero }), ContentFingerprint.Compute(b.Consumed with { Elapsed = TimeSpan.Zero }));
    }

    internal static AutonomousSearchPlan Plan()
    {
        var source = AutonomousDesignExamples.ThreeElement(); var envelope = source.Seeds[0];
        TopologyFace Face(TopologyFace f) => f with { ClearSemiDiameterMillimeters = 5, MechanicalSemiDiameterMillimeters = 5, Profile = new() };
        envelope = envelope with { Stop = envelope.Stop with { ClearSemiDiameterMillimeters = 2 }, Elements = envelope.Elements.Select(e => e with { Front = Face(e.Front), Back = Face(e.Back) }).ToImmutableArray() };
        var preparation = new ParaxialSeedSearchOptions
        {
            ColdStart = new() { Version = 6 },
            MaximumFunctionEvaluations = 200,
            MaximumTracedRays = 1000000,
            TimeLimit = TimeSpan.FromSeconds(600),
            Generation = new()
            {
                Method = StructureGenerationMethod.Delano,
                StopAfterElement = 0,
                MinimumThicknessMillimeters = .5,
                MaximumThicknessMillimeters = 2,
                MinimumBackFocusMillimeters = 5,
                AnnealingProposals = 12,
                Elements = envelope.Elements.Select(e => new StructureElementDescriptor { Materials = [e.Material] }).ToImmutableArray()
            }
        };
        // Flow control only. The real original F8 acceptance is verified separately without these loose gates.
        return source with
        {
            Seeds = [envelope],
            Targets = source.Targets with { MaximumRmsRadiusMillimeters = 1, MaximumSpotRadiusMillimeters = 2, MaximumDistortionFraction = 1 },
            Options = source.Options with { SeedPreparation = preparation }
        };
    }
}
