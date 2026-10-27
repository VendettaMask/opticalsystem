using System.Collections.Immutable;
using System.Text.Json;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedRecoveryCostsAndRayGradientBudgetRemainDistinctInEveryLanguage(bool automatic)
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Targets = plan.Targets with
            {
                FocalLengthRelativeTolerance = 1,
                FNumberRelativeTolerance = 1,
                MaximumRmsRadiusMillimeters = 5,
                MaximumSpotRadiusMillimeters = 10,
                MaximumDistortionFraction = 1,
                MinimumValidRayFraction = .1
            },
            Options = plan.Options with
            {
                CheckStageFeasibility = null,
                Recovery = new()
                {
                    MaximumFunctionEvaluations = 30,
                    ProfileRadialSamples = 8,
                    UseAutomaticMatrixDerivatives = automatic,
                    RestorePupilMagnification = automatic ? true : null
                },
                Expansion = new()
                {
                    EnableInsertion = false,
                    EnableDeletion = true,
                    EnableGradualDeletion = false,
                    EnableSplit = false,
                    EnableMerge = false,
                    EnableMaterial = false,
                    EnableProfile = false
                }
            }
        };
        var state = await Task.Run(() => AutonomousSearchService.Run(AutonomousSearchService.Create("ui-accounting-" + automatic, plan)));
        Assert.True(state.Consumed.MatrixEvaluations > 0);
        Assert.True(state.Consumed.JacobianBuilds > 0);
        Assert.True(automatic ? state.Consumed.DifferenceEvaluations == 0 : state.Consumed.DifferenceEvaluations > 0);
        if (automatic) Assert.Contains(state.Events, entry => entry.Recovery?.Work.PupilEvaluations > 0);
        var path = files.Path("accounting.autonomous.json");
        await new AutonomousSearchCheckpointStore().SaveAsync(state with { Revision = 1 }, path, 0);
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                OpenPicker = (_, _) => Task.FromResult<string?>(path)
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                foreach (var selected in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(selected); window.UpdateLayout();
                    var budget = Find<TextBlock>(window, "AutonomousBudget");
                    Assert.Contains($"{UiLocalization.Text("光线梯度")} {state.Consumed.GradientEvaluations}/{state.Plan.Options.MaximumGradientEvaluations}", budget.Text);
                    Assert.Contains($"{UiLocalization.Text("矩阵")} {state.Consumed.MatrixEvaluations}", budget.Text);
                    Assert.Contains($"Jacobian {state.Consumed.JacobianBuilds}", budget.Text);
                    Assert.Contains($"{UiLocalization.Text("差分探测")} {state.Consumed.DifferenceEvaluations}", budget.Text);
                    Assert.Contains($"{UiLocalization.Text("瞳查询")} " + (automatic
                        ? state.Events.Sum(entry => entry.Recovery?.Work.PupilEvaluations ?? 0).ToString() : UiLocalization.Text("未记录")), budget.Text);
                    Assert.NotNull(ToolTip.GetTip(budget));
                    var history = Find<DataGrid>(window, "AutonomousEvents");
                    var rows = history.ItemsSource.Cast<TopologyEventRow>().ToArray();
                    foreach (var entry in state.Events.Where(entry => entry.Recovery is not null))
                    {
                        var row = Assert.Single(rows, item => item.Sequence == entry.Sequence);
                        Assert.Equal($"{entry.Work.MatrixEvaluations} / {entry.Work.JacobianBuilds} / {entry.Work.DifferenceEvaluations}", row.RecoveryWork);
                        Assert.Equal(entry.Recovery!.Work.PupilEvaluations?.ToString() ?? UiLocalization.Text("未记录"), row.PupilWork);
                    }
                    Assert.Contains(history.Columns, column => Equals(column.Header, UiLocalization.Text("恢复矩阵 / Jacobian / 差分")));
                    if (automatic)
                    {
                        Find<TabControl>(window, "AutonomousTabs").SelectedIndex = 1;
                        window.UpdateLayout();
                        history.ScrollIntoView(rows[0], history.Columns[7]); window.UpdateLayout();
                        Capture(window, "accounting-wide-" + selected);
                    }
                }
                window.Width = 480; await Task.Delay(30); window.UpdateLayout();
                Find<TextBlock>(window, "AutonomousBudget").BringIntoView(); window.UpdateLayout();
                if (automatic) Capture(window, "accounting-narrow-ja-JP");
                Assert.Equal(0, traces.RayCount);
                Assert.False(window.IsBusy);
                Assert.Equal(state.PlanFingerprint, window.CurrentCheckpoint!.PlanFingerprint);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetEditsPreserveNullAndAsymmetricBranchPoliciesAndUneditedPhysicalValues(bool asymmetric)
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        var source = plan.Seeds[0] with
        {
            Fields = [new("Axis", 0, 0), new("Edge", 0, 5.123456789012345)],
            Stop = plan.Seeds[0].Stop with { ClearSemiDiameterMillimeters = 2.000000000000007, MechanicalSemiDiameterMillimeters = 2.1 }
        };
        plan = plan with
        {
            Seeds = [source],
            Options = plan.Options with
            {
                TimeLimit = TimeSpan.FromTicks(450000123),
                FixedTopologyOnly = false,
                AxialCompensation = new() { MaximumDisplacementMillimeters = .253761234567891 },
                Expansion = asymmetric ? new()
                {
                    EnableInsertion = false,
                    EnableDeletion = true,
                    EnableGradualDeletion = false,
                    EnableSplit = true,
                    EnableMerge = false,
                    EnableMaterial = false,
                    EnableProfile = false
                } : null
            }
        };
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("edited.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Find<NumericUpDown>(window, "AutonomousTrials").Value = plan.Options.MaximumTrials + 1;
                var edited = await Save();
                Assert.Equal(ContentFingerprint.Compute(plan with { Options = plan.Options with { MaximumTrials = plan.Options.MaximumTrials + 1 } }), ContentFingerprint.Compute(edited));
                Find<CheckBox>(window, "AutonomousStructure").IsChecked = false;
                var fixedPlan = await Save();
                Assert.True(fixedPlan.Options.FixedTopologyOnly);
                Assert.Equal(JsonSerializer.Serialize(plan.Options.Expansion, Json), JsonSerializer.Serialize(fixedPlan.Options.Expansion, Json));
                Assert.NotNull(ToolTip.GetTip(Find<CheckBox>(window, "AutonomousSplit")));
                Find<CheckBox>(window, "AutonomousStructure").IsChecked = true;
                var restored = await Save();
                Assert.Equal(false, restored.Options.FixedTopologyOnly);
                Assert.Equal(JsonSerializer.Serialize(plan.Options.Expansion, Json), JsonSerializer.Serialize(restored.Options.Expansion, Json));
                Find<CheckBox>(window, "AutonomousMaterial").IsChecked = true;
                var allowed = await Save();
                Assert.True(allowed.Options.Expansion!.EnableMaterial);
                Assert.Equal(asymmetric, allowed.Options.Expansion.EnableSplit);
                Assert.False(allowed.Options.Expansion.EnableMerge);
                Assert.Equal(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(allowed.Seeds));
                Find<NumericUpDown>(window, "AutonomousStop").Text = "invalid";
                Click(window, "AutonomousCheck"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Equal(0, traces.RayCount);
                return true;

                async Task<AutonomousSearchPlan> Save()
                {
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                    return await AutonomousDesignFiles.LoadPlanAsync(files.Path("edited.topology-plan.json"));
                }
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ReservationUnitsAndUnknownAuxiliaryCostsAreVisibleWithoutDispatch()
    {
        using var files = new OwnedFiles();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var path = System.IO.Path.Combine(directory!.FullName, "validation/rechecks/2026-10-26/autonomous-search-m4/checkpoint-interrupted.json");
        var store = new AutonomousSearchCheckpointStore();
        var interrupted = await store.LoadAsync(path);
        Assert.Equal(interrupted.Plan.Options.MaximumTrials, interrupted.TrialCursor);
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            string open = path;
            var window = new AutonomousTopologyWindow(new() { InitialPlan = interrupted.Plan, OpenPicker = (_, _) => Task.FromResult<string?>(open) });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                var reserve = interrupted.Reservation!.Maximum;
                var budget = Find<TextBlock>(window, "AutonomousBudget").Text!;
                var lastLine = budget.Split('\n')[^1];
                Assert.Contains(UiLocalization.Text("在途预约"), lastLine);
                Assert.Contains($"{UiLocalization.Text("评价")} {reserve.FunctionEvaluations}", lastLine);
                Assert.Contains($"{UiLocalization.Text("光线梯度")} {reserve.GradientEvaluations}", lastLine);
                Assert.Contains($"{UiLocalization.Text("追迹")} {reserve.TracedRays}", lastLine);
                Assert.False(window.IsBusy);
                var recovered = AutonomousSearchService.Run(interrupted) with { Revision = 1 };
                await store.SaveAsync(recovered, files.Path("charged.autonomous.json"), 0);
                open = files.Path("charged.autonomous.json");
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                Assert.Contains(UiLocalization.Text("未知中断阶段按预约计费，辅助工作只显示已记录数量"), Find<TextBlock>(window, "AutonomousBudget").Text);
                var unknown = Assert.Single(Find<DataGrid>(window, "AutonomousEvents").ItemsSource.Cast<TopologyEventRow>(),
                    row => row.Outcome == UiLocalization.Text("未知中断已按预约计费"));
                Assert.Equal(UiLocalization.Text("未记录"), unknown.RecoveryWork);
                Assert.Equal(UiLocalization.Text("未记录"), unknown.PupilWork);
                Assert.Equal(0, traces.RayCount);
                Assert.False(window.IsBusy);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
