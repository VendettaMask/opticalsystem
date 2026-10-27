using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 2)]
    public async Task HistoricalTransmissionPlansAndRecordsStayReadableWithoutOfferingExecution(bool local, int? version)
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        var prototype = plan.Options.Full with
        {
            TransmissionFirst = local ? true : null,
            ActiveParameterLimit = local ? null : 2,
            TransmissionPolicyVersion = version,
            Sampling = plan.Options.Full.Sampling with { IncludeBoundaryClearance = true }
        };
        plan = plan with { Options = plan.Options with { Local = local ? prototype : plan.Options.Local, Full = local ? plan.Options.Full : prototype } };
        var original = AutonomousSearchService.Create("historical-ui-" + local, plan) with { Revision = 1 };
        var path = files.Path("historical.autonomous.json");
        await new AutonomousSearchCheckpointStore().SaveAsync(original, path, 0);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var blocked = new AutonomousDesktopSession(files.Path("blocked"));
        using (var traces = SequentialTraceMeasurement.Begin())
        {
            Assert.True(AutonomousSearchService.IsHistoricalReadOnlyPlan(plan));
            Assert.False(AutonomousSearchService.IsHistoricalReadOnlyPlan(AutonomousDesignExamples.ThreeElement()));
            Assert.False(AutonomousSearchService.IsHistoricalReadOnlyPlan(Plan()));
            await Assert.ThrowsAsync<NotSupportedException>(() => blocked.RunAsync(plan, false, _ => { }));
            Assert.False(blocked.IsBusy); Assert.Null(blocked.Checkpoint); Assert.Null(blocked.RecordPath);
            Assert.False(Directory.Exists(files.Path("blocked"))); Assert.Equal(0, traces.RayCount);
        }
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                OpenPicker = (_, _) => Task.FromResult<string?>(path),
                SavePicker = (_, kind) => Task.FromResult<string?>(files.Path(kind == "plan" ? "saved.topology-plan.json" : "copy.autonomous.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Assert.False(Find<Button>(window, "AutonomousRun").IsEnabled);
                Click(window, "AutonomousCheck"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Equal(UiLocalization.Text("此通光原型仅供查看和导出，请新建版本化计划"), Find<TextBlock>(window, "AutonomousStatus").Text);
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                Assert.Equal(ContentFingerprint.Compute(plan), ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(files.Path("saved.topology-plan.json"))));
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                Assert.Equal(ContentFingerprint.Compute(original), ContentFingerprint.Compute(window.CurrentCheckpoint!));
                foreach (var selected in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(selected); window.UpdateLayout();
                    Assert.False(Find<Button>(window, "AutonomousResume").IsEnabled);
                    Assert.Equal(UiLocalization.Text("此通光原型仅供查看和导出，请新建版本化计划"), ToolTip.GetTip(Find<Button>(window, "AutonomousResume")));
                    Assert.Equal(UiLocalization.Text("此通光原型仅供查看和导出，请新建版本化计划"), Find<TextBlock>(window, "AutonomousStatus").Text);
                    if (local) Capture(window, "historical-readonly-" + selected);
                }
                Click(window, "AutonomousCopy"); await window.PendingOperation;
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(files.Path("copy.autonomous.json")));
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
                Assert.Equal(0, traces.RayCount); Assert.False(window.IsBusy);
                Click(window, "AutonomousNew"); await window.PendingOperation;
                Assert.True(Find<Button>(window, "AutonomousRun").IsEnabled);
                Assert.Null(window.CurrentCheckpoint);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task EditedCompensationFlagsReachExecutionAndSurviveDurableCancelAndDesktopResume()
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Options = plan.Options with
            {
                MaximumTrials = 1,
                Full = plan.Options.Full with { MaximumUpdates = 4, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 5 }
            }
        };
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            string? record = null;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                OpenPicker = (_, _) => Task.FromResult(record),
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("edited.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using (var noWork = SequentialTraceMeasurement.Begin())
                {
                    foreach (var id in new[] { "AutonomousPupilRecovery", "AutonomousAxial", "AutonomousRecoveryAxial", "AutonomousConic", "AutonomousStageChecks", "AutonomousProtectLocalBudget", "AutonomousProtectFullBudget" })
                        Find<CheckBox>(window, id).IsChecked = true;
                    Find<CheckBox>(window, "AutonomousStructure").IsChecked = false;
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!); Assert.Equal(0, noWork.RayCount);
                }
                var edited = await AutonomousDesignFiles.LoadPlanAsync(files.Path("edited.topology-plan.json"));
                var session = new AutonomousDesktopSession(files.Root);
                await session.RunAsync(edited, false, _ => { }, (_, step) => { if (step.Attempt == 1) session.Cancel(); });
                var stopped = session.Checkpoint!;
                Assert.Equal(AutonomousSearchStatus.Cancelled, stopped.Status);
                Assert.NotNull(stopped.Pending!.Continuation); Assert.NotNull(stopped.Pending.AxialAnchors);
                Assert.Contains(stopped.Pending.Variables, item => item.Kind == OpticalDesignParameterKind.Conic);
                Assert.Contains(stopped.Pending.Variables, item => item.Kind == OpticalDesignParameterKind.AxialPosition);
                record = session.RecordPath!;
                var loaded = await new AutonomousSearchCheckpointStore().LoadAsync(record);
                Assert.Equal(ContentFingerprint.Compute(stopped), ContentFingerprint.Compute(loaded));
                using (var noWork = SequentialTraceMeasurement.Begin())
                {
                    Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                    Assert.Equal(0, noWork.RayCount);
                }
                Assert.True(Find<Button>(window, "AutonomousResume").IsEnabled);
                foreach (var id in new[] { "AutonomousMatrixDerivatives", "AutonomousPupilRecovery", "AutonomousAxial", "AutonomousRecoveryAxial", "AutonomousConic", "AutonomousStageChecks", "AutonomousProtectLocalBudget", "AutonomousProtectFullBudget" })
                {
                    Assert.True(Find<CheckBox>(window, id).IsChecked);
                    Assert.False(Find<CheckBox>(window, id).IsEnabled);
                }
                Click(window, "AutonomousResume"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var final = window.CurrentCheckpoint!;
                Assert.Contains(final.Status, new[] { AutonomousSearchStatus.Completed, AutonomousSearchStatus.BudgetExhausted });
                Assert.Equal(stopped.PlanFingerprint, final.PlanFingerprint);
                Assert.Equal(ContentFingerprint.Compute(stopped.Events), ContentFingerprint.Compute(final.Events.Take(stopped.Events.Length).ToImmutableArray()));
                Assert.Equal(final.Consumed.FunctionEvaluations, final.Events.Sum(item => item.Work.FunctionEvaluations));
                Assert.Equal(final.Consumed.GradientEvaluations, final.Events.Sum(item => item.Work.GradientEvaluations));
                Assert.Equal(final.Consumed.TracedRays, final.Events.Sum(item => item.Work.TracedRays));
                Assert.InRange(final.Consumed.TracedRays, 1, edited.Options.MaximumTracedRays);
                Assert.True(final.Revision > stopped.Revision);
                var optimization = final.Events.Last(item => item.Optimization is not null).Optimization!;
                Assert.Contains(optimization.Variables, item => item.Kind == OpticalDesignParameterKind.Conic);
                Assert.Equal(ContentFingerprint.Compute(final), ContentFingerprint.Compute(await new AutonomousSearchCheckpointStore().LoadAsync(record)));
                var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M6_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, "compensation-plan.json"), JsonSerializer.Serialize(edited, Json));
                    await File.WriteAllTextAsync(System.IO.Path.Combine(evidence, "compensation-cancelled.json"), JsonSerializer.Serialize(stopped, Json));
                    await File.WriteAllBytesAsync(System.IO.Path.Combine(evidence, "compensation-final.autonomous.json"), await File.ReadAllBytesAsync(record));
                }
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
