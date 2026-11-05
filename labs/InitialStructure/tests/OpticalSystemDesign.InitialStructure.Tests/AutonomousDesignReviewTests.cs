using OpticalSystemDesign.Validation;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class AutonomousDesignReviewTests
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static string Root => DelanoMaterializationTests.Root();
    internal static string Original(int seed = 45) => Path.Combine(Root, $"validation/rechecks/2026-11-01/initial-performance/candidate/seed{seed}/run.preparation.json");

    [Theory]
    [InlineData(42)] [InlineData(43)] [InlineData(44)] [InlineData(45)] [InlineData(46)]
    public async Task OriginalF8EvidenceReportPortfolioAndNativeReadbackRetainAllGates(int seed)
    {
        using var files = new TemporaryFiles();
        var state = await new SeedPreparationCheckpointStore().LoadAsync(Original(seed)); var before = ContentFingerprint.Compute(state);
        AutonomousDesignReview review;
        using (var meter = SequentialTraceMeasurement.Begin())
        {
            var study = AutonomousDesignStudy.Capture("Original F8 " + seed, preparation: state);
            review = AutonomousDesignReview.Build(study);
            await AutonomousDesignStudyFiles.SaveStudyAsync(study, files.Path("study.json"));
            var loaded = await AutonomousDesignStudyFiles.LoadStudyAsync(files.Path("study.json"));
            Assert.Equal(ContentFingerprint.Compute(study), ContentFingerprint.Compute(loaded));
            var portfolio = new AutonomousDesignPortfolio { Entries = [study] };
            await AutonomousDesignStudyFiles.SavePortfolioAsync(portfolio, files.Path("portfolio.json"));
            Assert.Equal(ContentFingerprint.Compute(portfolio), ContentFingerprint.Compute(await AutonomousDesignStudyFiles.LoadPortfolioAsync(files.Path("portfolio.json"))));
            var report = AutonomousDesignReport.BuildMarkdown(review);
            Assert.Contains("F/8", report); Assert.Contains("机械半口径 mm", report); Assert.Contains(state.ProtocolFingerprint, JsonSerializer.Serialize(study));
            Assert.Contains(state.Result!.Algorithm, report); Assert.Contains(state.PlanFingerprint, report);
            Assert.Contains("阶段与淘汰原因", report); Assert.Equal(0, meter.RayCount);
        }
        var final = review.Candidates.Single(c => c.Id == "cold:final"); Assert.True(final.OriginalQualified);
        var source = state.Result!.ColdStart!;
        Assert.Equal(ContentFingerprint.Compute(source.Endpoint!), ContentFingerprint.Compute(final.Topology));
        Assert.Equal(50, final.Assessment!.Targets.EffectiveFocalLengthMillimeters); Assert.Equal(8, final.Assessment.Targets.FNumber);
        Assert.Equal(.05, final.Assessment.Targets.MaximumRmsRadiusMillimeters); Assert.Equal(.15, final.Assessment.Targets.MaximumSpotRadiusMillimeters);
        Assert.Equal(3.125, final.Topology.Stop.ClearSemiDiameterMillimeters); Assert.Equal(65, final.Topology.Image.ZMillimeters);
        Assert.All(final.Topology.Elements.SelectMany(e => new[] { e.Front, e.Back }), f =>
        { Assert.Equal(5, f.ClearSemiDiameterMillimeters); Assert.Equal(5, f.MechanicalSemiDiameterMillimeters); });
        var independent = TopologyValidator.Validate(TopologyCompiler.Compile(final.Topology, state.Plan.Constraints), state.Plan.Targets);
        Assert.Equal(ContentFingerprint.Compute(final.Assessment), ContentFingerprint.Compute(independent));
        Assert.All(independent.Fields, f => Assert.Equal(f.CheckAttemptedRays, f.CheckValidRays));
        var native = await AutonomousDesignFiles.ExportAsync(new(final.Id, null, final.Topology, final.Assessment, 0), state.Plan.Constraints, files.Path("qualified.staropt"));
        var restored = await AutonomousDesignFiles.ImportSeedAsync(native, state.Plan.Constraints);
        // Verify the actual native file exactly; the topology adapter assigns new surface labels/IDs.
        var nativeProject = await StarOptProjectStore.LoadAsync(native);
        Assert.Equal(final.Assessment.OpticFingerprint, ContentFingerprint.Compute(nativeProject.Configurations[nativeProject.ActiveConfigurationIndex].ToSnapshot()));
        Assert.Equal(final.Topology.Stop.ClearSemiDiameterMillimeters, restored.Stop.ClearSemiDiameterMillimeters);
        Assert.Equal(final.Topology.Image.ZMillimeters, restored.Image.ZMillimeters);
        Assert.Equal(final.Topology.Elements.SelectMany(e => new[] { e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters }),
            restored.Elements.SelectMany(e => new[] { e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters }));
        Assert.Equal(final.Topology.Elements.SelectMany(e => new[] { e.Front.MechanicalSemiDiameterMillimeters, e.Back.MechanicalSemiDiameterMillimeters }),
            restored.Elements.SelectMany(e => new[] { e.Front.MechanicalSemiDiameterMillimeters, e.Back.MechanicalSemiDiameterMillimeters }));
        var again = TopologyValidator.Validate(TopologyCompiler.Compile(restored, state.Plan.Constraints), state.Plan.Targets);
        Assert.True(again is { Completed: true, MeetsTargets: true });
        Assert.Equal(ContentFingerprint.Compute(independent.Targets), ContentFingerprint.Compute(again.Targets));
        Assert.All(again.Fields, f => Assert.Equal(f.CheckAttemptedRays, f.CheckValidRays));
        Assert.Equal(before, ContentFingerprint.Compute(state));
        Evidence("original-F8-" + seed, new { seed, Source = before, Qualified = final.OriginalQualified, DisplayAndReportRays = 0,
            OriginalTargets = state.Plan.Targets, SourceWork = state.Consumed, Independent = independent, NativeReadback = again });
    }

    [Fact]
    public async Task OriginalF4ConflictIsVisibleWithoutTracingOrInventingQualifiedCandidates()
    {
        var path = Path.Combine(Root, "validation/rechecks/2026-11-01/initial-performance/f4/run.preparation.json");
        var state = await new SeedPreparationCheckpointStore().LoadAsync(path); var hash = ContentFingerprint.Compute(state);
        using var meter = SequentialTraceMeasurement.Begin();
        var review = AutonomousDesignReview.Build(AutonomousDesignStudy.Capture("Original F4", preparation: state));
        Assert.Empty(review.Candidates); Assert.Contains(review.Decisions, d => d.Outcome == "OriginalExternalStopApertureConflict");
        Assert.Contains("OriginalExternalStopApertureConflict", AutonomousDesignReport.BuildMarkdown(review));
        Assert.Equal(0, state.Consumed.FunctionEvaluations); Assert.Equal(0, state.Consumed.TracedRays); Assert.Equal(0, meter.RayCount);
        Assert.Equal(hash, ContentFingerprint.Compute(state));
        Evidence("original-F4-conflict", new { state.StopReason, state.Consumed, DisplayAndReportRays = meter.RayCount, NoQualifiedCandidate = review.Candidates.IsEmpty });
    }

    [Theory]
    [InlineData("Paraxial", "Starts")] [InlineData("Retrieval", "Retrieval:Validate")]
    [InlineData("Competition", "Competition:Rounds")] [InlineData("EvolutionDirect", "Evolution:Trials")]
    public void SuspendedPreparationShowsOnlySavedOperationsWithoutFinishingThem(string mode, string stage)
    {
        var plan = PreparationModeResumeTests.Plan(mode); using var stop = new CancellationTokenSource();
        var state = SeedPreparationSearch.Run(SeedPreparationSearch.Create("review-partial", plan), next => { if (next.Stage == stage) stop.Cancel(); }, stop.Token);
        Assert.Equal(SeedPreparationStatus.Suspended, state.Status); var before = ContentFingerprint.Compute(state);
        using var meter = SequentialTraceMeasurement.Begin();
        var review = AutonomousDesignReview.Build(AutonomousDesignStudy.Capture("Partial " + mode, preparation: state));
        Assert.NotEmpty(review.Decisions); Assert.All(review.Candidates.Where(c => c.OriginalQualified), c => Assert.True(c.Assessment!.MeetsTargets));
        _ = AutonomousDesignReport.BuildMarkdown(review);
        Assert.Equal(0, meter.RayCount); Assert.Equal(before, ContentFingerprint.Compute(state));
    }

    [Fact]
    public async Task LegacyMainEvidenceRemainsReadOnlyWithItsOriginalProtocolAndRawUnknownReasons()
    {
        var path = Path.Combine(Root, "validation/rechecks/2026-10-29/delano-thick-materialization/runs/thick-delano/run.autonomous.json");
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(path); var before = ContentFingerprint.Compute(state);
        using var meter = SequentialTraceMeasurement.Begin();
        var study = AutonomousDesignStudy.Capture("Legacy <name>|`record`", state);
        var review = AutonomousDesignReview.Build(study);
        var report = AutonomousDesignReport.BuildMarkdown(review);
        Assert.Contains("&lt;name&gt;\\|", report); Assert.Equal(state.Algorithm, study.Main!.Algorithm);
        Assert.Equal(before, ContentFingerprint.Compute(study.Main)); Assert.NotEmpty(review.Candidates);
        Assert.All(review.Candidates, c => Assert.Equal(c.Assessment?.ProtocolFingerprint, state.Candidates.FirstOrDefault(x => "main:" + x.Id == c.Id)?.Assessment.ProtocolFingerprint ?? c.Assessment?.ProtocolFingerprint));
        Assert.Equal(0, meter.RayCount);
    }

    [Fact]
    public async Task RealF8ReductionTransitionRecordsKeepExploratoryPermissionsWithoutQualifyingOrExportingThem()
    {
        var directory = Path.Combine(Root, "validation/rechecks/2026-11-01/initial-product-m36/product-session-v2");
        var main = await new AutonomousSearchCheckpointStore().LoadAsync(EvidenceFiles.GetFiles(directory, "*.autonomous.json").Single());
        var before = ContentFingerprint.Compute(main);
        using var meter = SequentialTraceMeasurement.Begin();
        var review = AutonomousDesignReview.Build(AutonomousDesignStudy.Capture("F8 reduction history", main));
        var transitions = review.Candidates.Where(c => c.Topology.HasTransitionElements).ToArray();
        Assert.NotEmpty(transitions);
        Assert.All(transitions, item => { Assert.False(item.OriginalQualified); Assert.False(item.CanExport); Assert.False(item.OriginalSpecification);
            Assert.True(item.Assessment!.StructuralConstraints.AllowTransitionElements); });
        Assert.False(main.Plan.Constraints.AllowTransitionElements);
        var report = AutonomousDesignReport.BuildMarkdown(review);
        Assert.Contains("过渡结构，仅供研究", report); Assert.Contains("阶段验收条件", report);
        Assert.Equal(before, ContentFingerprint.Compute(main)); Assert.Equal(0, meter.RayCount);
    }

    [Fact]
    public async Task InvalidAndDuplicateEvidenceCannotReplaceAnExistingPortfolioAndForeignAssessmentIsRejected()
    {
        using var files = new TemporaryFiles(); var state = await new SeedPreparationCheckpointStore().LoadAsync(Original());
        var study = AutonomousDesignStudy.Capture("Original", preparation: state);
        var portfolio = new AutonomousDesignPortfolio { Entries = [study] }; var path = files.Path("portfolio.json");
        await AutonomousDesignStudyFiles.SavePortfolioAsync(portfolio, path); var bytes = await EvidenceFiles.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => AutonomousDesignStudyFiles.SavePortfolioAsync(portfolio with { Entries = [study, study] }, path));
        await Assert.ThrowsAsync<InvalidDataException>(() => AutonomousDesignStudyFiles.SavePortfolioAsync(portfolio with { Version = 99 }, path));
        Assert.Equal(bytes, await EvidenceFiles.ReadAllBytesAsync(path));
        var json = JsonNode.Parse(bytes)!; json["sha256"] = "wrong"; await File.WriteAllTextAsync(files.Path("tampered.json"), json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => AutonomousDesignStudyFiles.LoadPortfolioAsync(files.Path("tampered.json")));
        var cold = state.Result!.ColdStart!;
        var bad = state with { Result = state.Result with { ColdStart = cold with { Assessment = cold.Assessment! with { ProtocolFingerprint = new string('0', 64) } } } };
        Assert.Throws<InvalidDataException>(() => AutonomousDesignReview.Build(AutonomousDesignStudy.Capture("foreign", preparation: bad)));
        var incomplete = state with { Result = state.Result with { ColdStart = cold with { Assessment = cold.Assessment! with { Completed = false, MeetsTargets = false } } } };
        var row = AutonomousDesignReview.Build(AutonomousDesignStudy.Capture("incomplete", preparation: incomplete)).Candidates.Single(c => c.Id == "cold:final");
        Assert.False(row.OriginalQualified); Assert.False(row.CanExport);
    }
    private static void Evidence(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_M36_EVIDENCE") is not { } directory) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, EvidenceJson));
    }
    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "review-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) { Directory.CreateDirectory(_root); return System.IO.Path.Combine(_root, name); }
        public void Dispose() { if (EvidenceFiles.DirectoryExists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
