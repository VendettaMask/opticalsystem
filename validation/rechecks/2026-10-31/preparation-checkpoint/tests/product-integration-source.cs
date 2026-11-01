using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

public sealed class RealPipelineTests
{
    [Fact]
    public async Task OriginalF8StopsLoadsResumesAndHandsOffOnceBeforeQualifiedNativeReadback()
    {
        var input = Environment.GetEnvironmentVariable("F8_REAL_PIPELINE_INPUT");
        var output = Environment.GetEnvironmentVariable("F8_REAL_PIPELINE_OUTPUT");
        Assert.False(string.IsNullOrWhiteSpace(input)); Assert.False(string.IsNullOrWhiteSpace(output));
        Assert.False(Directory.Exists(output)); Directory.CreateDirectory(output!);
        var json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        using var original = JsonDocument.Parse(await File.ReadAllBytesAsync(input!));
        var source = original.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var preparation = original.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
        preparation = preparation with
        {
            ColdStart = new() { Version = 6 },
            Retrieval = null,
            Evolution = null,
            Competition = null,
            ImageScreening = null,
            MaximumFunctionEvaluations = 600,
            MaximumEvaluationsPerStart = 600,
            MaximumTracedRays = 10000000,
            TimeLimit = TimeSpan.FromSeconds(600)
        };
        // This extra integration run is separate from the frozen paired study.
        // Only the subsequent main-search trial count is limited to one so the
        // test verifies the accepted seed handoff rather than opening new edits.
        var plan = source with { Options = source.Options with { Seed = 45, MaximumTrials = 1, SeedPreparation = preparation } };
        await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(output!, "input.topology-plan.json"));
        var session = new AutonomousDesktopSession(output!);
        long prefixRays;
        using (var first = SequentialTraceMeasurement.Begin())
        {
            await session.RunAsync(plan, false, _ => throw new InvalidOperationException("Suspended preparation must not dispatch the main search."),
                preparationProgress: (stage, _) => { if (stage == 1) session.Cancel(); });
            prefixRays = first.RayCount;
        }
        var prefix = Assert.IsType<SeedPreparationCheckpoint>(session.PreparationCheckpoint);
        Assert.Equal(SeedPreparationStatus.Suspended, prefix.Status); Assert.Null(session.Checkpoint);
        var checkpointPath = session.PreparationCheckpointPath!;
        File.Copy(checkpointPath, Path.Combine(output!, "suspended.preparation.json"));
        session = new AutonomousDesktopSession(output!);
        using (var loading = SequentialTraceMeasurement.Begin())
        { await session.LoadAsync(checkpointPath); Assert.Equal(0, loading.RayCount); }
        long suffixRays;
        using (var second = SequentialTraceMeasurement.Begin())
        {
            await session.RunAsync(plan, true, _ => { }, preparationProgress: (stage, functions) =>
                File.AppendAllText(Path.Combine(output!, "progress.log"), DateTimeOffset.Now.ToString("O") + $" stage={stage}, fn={functions}\n"));
            suffixRays = second.RayCount;
        }
        var prepared = Assert.IsType<ParaxialSeedSearchResult>(session.PreparationResult);
        Assert.Equal("initial-structure-cold-design-v6", prepared.Algorithm); Assert.Single(prepared.Seeds);
        Assert.True(prepared.ColdStart!.Qualified); Assert.True(prepared.Work.Elapsed <= TimeSpan.FromSeconds(600));
        var state = Assert.IsType<AutonomousSearchCheckpoint>(session.Checkpoint);
        Assert.Equal(ContentFingerprint.Compute(prepared.Seeds), ContentFingerprint.Compute(state.Plan.Seeds));
        Assert.Equal(ContentFingerprint.Compute(source.Targets), ContentFingerprint.Compute(state.Plan.Targets));
        Assert.Equal(ContentFingerprint.Compute(source.Constraints), ContentFingerprint.Compute(state.Plan.Constraints));
        Assert.Equal(ContentFingerprint.Compute(prefix.Candidates[0]), ContentFingerprint.Compute(session.PreparationCheckpoint!.Candidates[0]));
        Assert.Equal(prefixRays + suffixRays, prepared.Work.TracedRays + state.Consumed.TracedRays);
        var duplicate = new AutonomousDesktopSession(output!); await duplicate.LoadAsync(checkpointPath);
        using (var replay = SequentialTraceMeasurement.Begin())
        { await duplicate.RunAsync(plan, true, _ => { }); Assert.Equal(0, replay.RayCount); }
        Assert.Single(Directory.GetFiles(output!, "*.autonomous.json", SearchOption.AllDirectories));
        var accepted = Assert.Single(state.AcceptedArchive.Where(c => c.Assessment.OpticFingerprint == prepared.ColdStart.Assessment!.OpticFingerprint));
        Assert.True(accepted.Assessment is { Completed: true, MeetsTargets: true });
        Assert.Equal(prepared.ColdStart.Assessment!.OpticFingerprint, accepted.Assessment.OpticFingerprint);
        var path = await AutonomousDesignFiles.ExportAsync(accepted, source.Constraints, Path.Combine(output!, "qualified.staropt"));
        var restored = await AutonomousDesignFiles.ImportSeedAsync(path, source.Constraints);
        Assert.Equal(source.Seeds[0].Stop.ZMillimeters, restored.Stop.ZMillimeters);
        Assert.Equal(source.Seeds[0].Stop.ClearSemiDiameterMillimeters, restored.Stop.ClearSemiDiameterMillimeters);
        Assert.Equal(source.Seeds[0].Image.ZMillimeters, restored.Image.ZMillimeters);
        Assert.Equal(source.Seeds[0].Elements.Length, restored.Elements.Length);
        for (var i = 0; i < restored.Elements.Length; i++)
            foreach (var pair in new[] { (restored.Elements[i].Front, source.Seeds[0].Elements[i].Front), (restored.Elements[i].Back, source.Seeds[0].Elements[i].Back) })
            {
                Assert.Equal(pair.Item2.ClearSemiDiameterMillimeters, pair.Item1.ClearSemiDiameterMillimeters);
                Assert.Equal(pair.Item2.MechanicalSemiDiameterMillimeters, pair.Item1.MechanicalSemiDiameterMillimeters);
            }
        var checkedAgain = TopologyValidator.Validate(TopologyCompiler.Compile(restored, source.Constraints), source.Targets);
        Assert.True(checkedAgain is { Completed: true, MeetsTargets: true });
        await File.WriteAllTextAsync(Path.Combine(output!, "summary.json"), JsonSerializer.Serialize(new
        {
            FrozenStudyInput = false,
            TargetsChanged = false,
            PhysicalCapsChanged = false,
            PreparationCost = prepared.Work,
            MainSearchCost = state.Consumed,
            PreparationReport = session.PreparationReportPath,
            MainRecord = session.RecordPath,
            NativePath = path,
            checkedAgain,
            PreparationStoppedAndLoaded = true,
            LoadAndRepeatedHandoffRays = 0,
            ActualPrefixRays = prefixRays,
            ActualSuffixRays = suffixRays,
            PhysicalAndDiscreteWorkSettled = true
        }, json));
    }
}
