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
    public async Task ColdPreparationStopsLoadsAndHandsOffExactlyOnceWithoutReconstructingTheSavedCandidate()
    {
        using var files = new OwnedFiles(); var plan = CheckpointPlan(); var first = new AutonomousDesktopSession(files.Root);
        await first.RunAsync(plan, false, _ => throw new InvalidOperationException("Paused preparation cannot start a main search."),
            preparationProgress: (count, _) => { if (count == 1) first.Cancel(); }, prepareOnly: true);
        Assert.Null(first.Checkpoint); Assert.Equal(SeedPreparationStatus.Suspended, first.PreparationCheckpoint!.Status);
        var prefix = first.PreparationCheckpoint.Candidates[0]; var prefixRays = first.PreparationCheckpoint.Consumed.TracedRays;
        var resumed = new AutonomousDesktopSession(files.Root);
        using (var traces = SequentialTraceMeasurement.Begin())
        {
            await resumed.LoadAsync(first.PreparationCheckpointPath!); Assert.Equal(0, traces.RayCount);
            Assert.True(resumed.CanResumePreparation); Assert.Null(resumed.Checkpoint);
        }
        using (var traces = SequentialTraceMeasurement.Begin())
        {
            await resumed.RunAsync(plan, true, _ => { });
            Assert.NotNull(resumed.Checkpoint); Assert.Single(resumed.PreparationResult!.Seeds);
            Assert.Equal(ContentFingerprint.Compute(prefix), ContentFingerprint.Compute(resumed.PreparationCheckpoint!.Candidates[0]));
            Assert.Equal(traces.RayCount, resumed.PreparationCheckpoint.Consumed.TracedRays - prefixRays + resumed.Checkpoint!.Consumed.TracedRays);
        }
        var repeated = new AutonomousDesktopSession(files.Root); await repeated.LoadAsync(first.PreparationCheckpointPath!);
        using (var traces = SequentialTraceMeasurement.Begin())
        {
            await repeated.RunAsync(plan, true, _ => { }); Assert.Equal(0, traces.RayCount);
        }
        Assert.Single(Directory.GetFiles(files.Root, "*.autonomous.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PreparationRecordLoadsReadOnlyWithoutTracingAndResumesFromTheDesktopControl()
    {
        using var files = new OwnedFiles(); var plan = CheckpointPlan(); var first = new AutonomousDesktopSession(files.Root);
        await first.RunAsync(plan, false, _ => { }, preparationProgress: (count, _) => { if (count == 1) first.Cancel(); }, prepareOnly: true);
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new() { RunDirectory = files.Root, OpenPicker = (_, _) => Task.FromResult(first.PreparationCheckpointPath) });
            try
            {
                window.Show(); window.UpdateLayout();
                using (var traces = SequentialTraceMeasurement.Begin())
                {
                    Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                    Assert.Equal(0, traces.RayCount); Assert.Null(window.CurrentCheckpoint);
                    Assert.True(Find<Button>(window, "AutonomousResume").IsEnabled); Assert.False(Find<Button>(window, "AutonomousRun").IsEnabled);
                    Assert.False(Find<NumericUpDown>(window, "AutonomousSeedFunctions").IsEnabled);
                    Assert.Contains(first.PreparationCheckpointPath!, Find<TextBlock>(window, "AutonomousRecordPath").Text);
                }
                Click(window, "AutonomousResume"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!); Assert.NotNull(window.CurrentCheckpoint);
                Assert.NotEmpty(window.CurrentCheckpoint!.AcceptedArchive);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task DifferentKernelPreparationRemainsViewableButCannotResumeOrTrace()
    {
        using var files = new OwnedFiles(); var state = SeedPreparationSearch.Create("different-kernel", CheckpointPlan()) with { Revision = 1, KernelFingerprint = new string('0', 64) };
        var path = files.Path("different.preparation.json"); await new SeedPreparationCheckpointStore().SaveAsync(state, path, 0);
        using var traces = SequentialTraceMeasurement.Begin(); var session = new AutonomousDesktopSession(files.Root); await session.LoadAsync(path);
        Assert.False(session.CanResumePreparation); Assert.NotNull(session.PreparationCheckpoint); Assert.Equal(0, traces.RayCount);
        Assert.Throws<NotSupportedException>(() => SeedPreparationSearch.Run(state, _ => { })); Assert.Equal(0, traces.RayCount);
    }

    private static AutonomousSearchPlan CheckpointPlan()
    {
        var plan = SeedPreparationCheckpointTests.Plan(); var options = plan.Options.SeedPreparation!;
        options = options with { ColdStart = options.ColdStart! with { BackFocusSlackFractions = [.5], AttemptsPerFraction = 2 } };
        return plan with { Options = plan.Options with { MaximumTrials = 1, SeedPreparation = options } };
    }
}
