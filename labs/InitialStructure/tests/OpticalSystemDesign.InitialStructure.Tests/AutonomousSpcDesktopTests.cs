using Avalonia.Automation;
using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task SpcControlsRoundTripInThreeLanguagesWithoutChangingTheOriginalGates()
    {
        using var files = new OwnedFiles(); using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage; var plan = SpcPreparationTests.Plan(); var path = files.Path("spc.topology-plan.json");
            using var rays = SequentialTraceMeasurement.Begin();
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, RunDirectory = files.Root, SavePicker = (_, _) => Task.FromResult<string?>(path) });
            try
            {
                window.Show(); window.UpdateLayout();
                foreach (var selected in new[] { "zh-CN", "en-US", "ja-JP" })
                {
                    UiLocalization.Apply(selected); window.UpdateLayout();
                    Assert.True(Find<CheckBox>(window, "AutonomousSpc").IsChecked);
                    Assert.Equal(UiLocalization.Text("实验：SPC分支准备并自动搜索"), Find<CheckBox>(window, "AutonomousSpc").Content);
                    Assert.Equal(UiLocalization.Text("SPC分支准备"), Find<Button>(window, "AutonomousPrepareSeeds").Content);
                    Assert.Equal(UiLocalization.Text("SPC分支准备"), AutomationProperties.GetName(Find<Button>(window, "AutonomousPrepareSeeds")));
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    var loaded = await AutonomousDesignFiles.LoadPlanAsync(path);
                    Assert.Equal(ContentFingerprint.Compute(plan.Targets), ContentFingerprint.Compute(loaded.Targets));
                    Assert.Equal(ContentFingerprint.Compute(plan.Constraints), ContentFingerprint.Compute(loaded.Constraints));
                    Assert.Equal(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(loaded.Seeds));
                    Assert.Equal(ContentFingerprint.Compute(plan.Options.SeedPreparation!.Spc), ContentFingerprint.Compute(loaded.Options.SeedPreparation!.Spc));
                    Assert.Equal(plan.Options.Expansion!.EnableInsertion, loaded.Options.Expansion!.EnableInsertion);
                    Assert.Null(loaded.Options.SeedPreparation.ColdStart); Assert.Null(loaded.Options.SeedPreparation.Generation);
                }
                Assert.Equal(0, rays.RayCount);
                Find<CheckBox>(window, "AutonomousColdStart").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousSpc").IsChecked);
                Find<CheckBox>(window, "AutonomousSpc").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousColdStart").IsChecked);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AutomaticSpcStopsReopensHandsOffExactlyOnceAndReportsOnlyPhysicalEvidence()
    {
        using var files = new OwnedFiles(); var plan = SpcPreparationTests.Plan();
        plan = plan with { Options = plan.Options with { MaximumTrials = 1, MaximumFunctionEvaluations = 400, MaximumGradientEvaluations = 100,
            MaximumTracedRays = 600000, MaximumStageTracedRays = 200000, FinalValidationRayReserve = 100000, TimeLimit = TimeSpan.FromSeconds(30) } };
        var original = ContentFingerprint.Compute(plan); var session = new AutonomousDesktopSession(files.Root); var stopped = false;
        await session.RunAsync(plan, false, _ => { }, preparationProgress: (_, _) =>
        {
            if (!stopped && session.PreparationCheckpoint?.Spc is { Phase: SpcPreparationPhase.BranchOptimization, Sign: -1, BranchMinimum.Iterations: >= 1 })
            { stopped = true; session.Cancel(); }
        });
        Assert.True(stopped); Assert.Null(session.Checkpoint); Assert.Equal(SeedPreparationStatus.Suspended, session.PreparationCheckpoint!.Status);
        var path = session.PreparationCheckpointPath!; var resumed = new AutonomousDesktopSession(files.Root);
        using (var reads = SequentialTraceMeasurement.Begin()) { await resumed.LoadAsync(path); Assert.Equal(0, reads.RayCount); }
        await resumed.RunAsync(resumed.PreparationCheckpoint!.Plan, true, _ => { });
        Assert.NotNull(resumed.Checkpoint); Assert.Equal(SeedPreparationStatus.Completed, resumed.PreparationCheckpoint!.Status);
        Assert.NotEmpty(resumed.PreparationResult!.Seeds); Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json"));
        Assert.Equal(original, ContentFingerprint.Compute(plan));
        foreach (var seed in resumed.Checkpoint.Plan.Seeds)
        {
            var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(seed, plan.Constraints), plan.Targets);
            Assert.True(assessment.MeetsTargets);
        }
        using (var reads = SequentialTraceMeasurement.Begin())
        {
            var study = AutonomousDesignStudy.Capture("SPC automatic continuation", resumed.Checkpoint, resumed.PreparationCheckpoint);
            var review = AutonomousDesignReview.Build(study);
            Assert.Contains(review.Candidates, c => c.Origin == "SPC实体补偿");
            Assert.All(review.Candidates, c => Assert.False(c.Topology.HasTransitionElements));
            await AutonomousDesignStudyFiles.SaveStudyAsync(study, files.Path("spc.design-study.json"));
            await AutonomousDesignReport.SaveMarkdownAsync(review, files.Path("spc-report.md"));
            var reopened = await AutonomousDesignStudyFiles.LoadStudyAsync(files.Path("spc.design-study.json"));
            Assert.Equal(study.SourceFingerprint, reopened.SourceFingerprint); Assert.Equal(0, reads.RayCount);
        }
        var repeated = new AutonomousDesktopSession(files.Root);
        await repeated.LoadAsync(path);
        using (var rays = SequentialTraceMeasurement.Begin()) { await repeated.RunAsync(repeated.PreparationCheckpoint!.Plan, true, _ => { }); Assert.Equal(0, rays.RayCount); }
        Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json"));
    }

    [Fact]
    public async Task SpcApertureConflictStopsBeforeAutomaticMainSearch()
    {
        using var files = new OwnedFiles(); var plan = SpcPreparationTests.Plan();
        plan = plan with { Targets = plan.Targets with { FNumber = 4 }, Seeds = [plan.Seeds[0] with { Stop = plan.Seeds[0].Stop with { ZMillimeters = 0, ClearSemiDiameterMillimeters = 6.25 } }] };
        var session = new AutonomousDesktopSession(files.Root); using var rays = SequentialTraceMeasurement.Begin();
        await session.RunAsync(plan, false, _ => throw new Exception("A rejected SPC task cannot create a main run"));
        Assert.Null(session.Checkpoint); Assert.Empty(Directory.GetFiles(files.Root, "*.autonomous.json")); Assert.Equal(0, rays.RayCount);
        Assert.Equal("OriginalExternalStopApertureConflict", session.PreparationCheckpoint!.StopReason);
    }
}
