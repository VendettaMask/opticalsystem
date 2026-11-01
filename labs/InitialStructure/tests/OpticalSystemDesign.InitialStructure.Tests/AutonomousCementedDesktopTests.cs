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
    public async Task CementedControlsRoundTripInThreeLanguagesWithoutChangingOriginalPermissions()
    {
        using var files = new OwnedFiles(); using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage; var plan = AutonomousCementedTests.Plan(); var path = files.Path("cemented.topology-plan.json");
            plan = plan with { Options = plan.Options with { Expansion = plan.Options.Expansion! with { EnableInsertion = false,
                Cemented = new() { MaximumGroupElements = 3, InitialThicknessMillimeters = 1.5, ThicknessMultipliers = [1, 3] } } } };
            using var rays = SequentialTraceMeasurement.Begin();
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, RunDirectory = files.Root, SavePicker = (_, _) => Task.FromResult<string?>(path) });
            try
            {
                window.Show(); window.UpdateLayout();
                foreach (var selected in new[] { "zh-CN", "en-US", "ja-JP" })
                {
                    UiLocalization.Apply(selected); window.UpdateLayout();
                    Assert.True(Find<CheckBox>(window, "AutonomousCemented").IsChecked);
                    Assert.Equal(selected == "zh-CN" ? "实验：自主胶合结构" : selected == "en-US" ? "Experimental: autonomous cemented structures" : "実験：自律接合構成",
                        Find<CheckBox>(window, "AutonomousCemented").Content);
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Equal(ContentFingerprint.Compute(plan), ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(path)));
                }
                Assert.Equal(0, rays.RayCount);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CementedDesktopSessionStopsReopensAndKeepsOneMainRecord()
    {
        using var files = new OwnedFiles(); var plan = AutonomousCementedTests.Plan(); var session = new AutonomousDesktopSession(files.Root);
        var paused = false;
        await session.RunAsync(plan, false, _ => { }, (pending, _) =>
        { if (!paused && pending.Operation == "cemented-insert" && pending.Phase == AutonomousSearchPhase.Full) { paused = true; session.Cancel(); } });
        Assert.True(paused); Assert.Equal(AutonomousSearchStatus.Cancelled, session.Checkpoint!.Status);
        var reopened = new AutonomousDesktopSession(files.Root); using (var reads = SequentialTraceMeasurement.Begin())
        { await reopened.LoadAsync(session.RecordPath!); Assert.Equal(0, reads.RayCount); }
        await reopened.RunAsync(reopened.Checkpoint!.Plan, true, _ => { });
        Assert.Equal(AutonomousSearchStatus.Completed, reopened.Checkpoint!.Status); Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json"));
        using var rays = SequentialTraceMeasurement.Begin();
        var study = AutonomousDesignStudy.Capture("Cemented automatic continuation", reopened.Checkpoint);
        var review = AutonomousDesignReview.Build(study);
        Assert.Contains(review.Candidates, c => c.Origin == "自主胶合");
        await AutonomousDesignStudyFiles.SaveStudyAsync(study, files.Path("cemented.design-study.json"));
        await AutonomousDesignReport.SaveMarkdownAsync(review, files.Path("cemented-report.md"));
        Assert.Contains("cementedKernelFingerprint", await File.ReadAllTextAsync(files.Path("cemented.design-study.json")));
        Assert.Equal(0, rays.RayCount);
    }
}
