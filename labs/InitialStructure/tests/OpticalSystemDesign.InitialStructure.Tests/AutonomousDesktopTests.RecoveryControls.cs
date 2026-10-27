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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SplitAndMergePermissionsAreIndependentlyVisibleAndEditable(bool split, bool merge)
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Options = plan.Options with
            {
                Expansion = new()
                {
                    EnableInsertion = false,
                    EnableDeletion = true,
                    EnableGradualDeletion = false,
                    EnableSplit = split,
                    EnableMerge = merge,
                    EnableMaterial = false,
                    EnableProfile = false
                }
            }
        };
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("permissions.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Assert.Equal(split, Find<CheckBox>(window, "AutonomousSplit").IsChecked);
                Assert.Equal(merge, Find<CheckBox>(window, "AutonomousMerge").IsChecked);
                Find<NumericUpDown>(window, "AutonomousTrials").Value++;
                var budget = await Save();
                Assert.Equal(ContentFingerprint.Compute(plan with { Options = plan.Options with { MaximumTrials = plan.Options.MaximumTrials + 1 } }), ContentFingerprint.Compute(budget));
                Find<CheckBox>(window, "AutonomousMerge").IsChecked = !merge;
                var changedMerge = await Save();
                Assert.Equal(split, changedMerge.Options.Expansion!.EnableSplit);
                Assert.Equal(!merge, changedMerge.Options.Expansion.EnableMerge);
                Find<CheckBox>(window, "AutonomousSplit").IsChecked = !split;
                var changedBoth = await Save();
                Assert.Equal(!split, changedBoth.Options.Expansion!.EnableSplit);
                Assert.Equal(!merge, changedBoth.Options.Expansion.EnableMerge);
                Assert.Equal(0, traces.RayCount); Assert.Null(window.CurrentCheckpoint);
                return true;

                async Task<AutonomousSearchPlan> Save()
                {
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                    return await AutonomousDesignFiles.LoadPlanAsync(files.Path("permissions.topology-plan.json"));
                }
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryControlLoadingAndBudgetEditingPreserveNullAndExplicitFalse(bool explicitFalse)
    {
        using var files = new OwnedFiles();
        var plan = Plan(); bool? original = explicitFalse ? false : null;
        plan = plan with
        {
            Options = plan.Options with
            {
                Recovery = plan.Options.Recovery with { UseAutomaticMatrixDerivatives = original, RestorePupilMagnification = original },
                UseAxialVariablesInRecovery = original,
                CompensateConic = original,
                CheckStageFeasibility = original,
                ProtectCompensationRayBudget = original,
                ProtectPostRecoveryRayBudget = original
            }
        };
        var example = AutonomousDesignExamples.ThreeElement();
        Assert.Null(example.Options.Recovery.UseAutomaticMatrixDerivatives); Assert.Null(example.Options.Recovery.RestorePupilMagnification);
        Assert.Null(example.Options.CompensateConic); Assert.Null(example.Options.UseAxialVariablesInRecovery);
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("legacy.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                foreach (var id in new[] { "AutonomousMatrixDerivatives", "AutonomousPupilRecovery", "AutonomousRecoveryAxial", "AutonomousConic", "AutonomousStageChecks", "AutonomousProtectLocalBudget", "AutonomousProtectFullBudget" })
                    Assert.False(Find<CheckBox>(window, id).IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousRecoveryAxial").IsEnabled);
                Assert.NotNull(ToolTip.GetTip(Find<CheckBox>(window, "AutonomousRecoveryAxial")));
                Assert.False(Find<CheckBox>(window, "AutonomousProtectFullBudget").IsEnabled);
                Find<NumericUpDown>(window, "AutonomousTrials").Value++;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var saved = await AutonomousDesignFiles.LoadPlanAsync(files.Path("legacy.topology-plan.json"));
                Assert.Equal(ContentFingerprint.Compute(plan with { Options = plan.Options with { MaximumTrials = plan.Options.MaximumTrials + 1 } }), ContentFingerprint.Compute(saved));
                Assert.Equal(0, traces.RayCount); Assert.Null(window.CurrentCheckpoint);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RecoveryDependenciesAndIndependentConicsAreExplicitAndPreserveInactivePreferences()
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Options = plan.Options with
            {
                CheckStageFeasibility = null,
                ProtectCompensationRayBudget = null,
                ProtectPostRecoveryRayBudget = null
            }
        };
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var originalLanguage = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("recovery.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Find<CheckBox>(window, "AutonomousPupilRecovery").IsChecked = true;
                Assert.True(Find<CheckBox>(window, "AutonomousMatrixDerivatives").IsChecked);
                Find<CheckBox>(window, "AutonomousAxial").IsChecked = true;
                Assert.True(Find<CheckBox>(window, "AutonomousRecoveryAxial").IsEnabled);
                Find<CheckBox>(window, "AutonomousRecoveryAxial").IsChecked = true;
                Find<CheckBox>(window, "AutonomousConic").IsChecked = true;
                Find<CheckBox>(window, "AutonomousStageChecks").IsChecked = true;
                Find<CheckBox>(window, "AutonomousProtectLocalBudget").IsChecked = true;
                Find<CheckBox>(window, "AutonomousProtectFullBudget").IsChecked = true;
                var active = await Save();
                Assert.True(active.Options.Recovery.UseAutomaticMatrixDerivatives); Assert.True(active.Options.Recovery.RestorePupilMagnification);
                Assert.True(active.Options.UseAxialVariablesInRecovery); Assert.NotNull(active.Options.AxialCompensation);
                Assert.True(active.Options.CompensateConic); Assert.Null(active.Options.Expansion);
                Assert.True(active.Options.ProtectPostRecoveryRayBudget); Assert.True(active.Options.ProtectCompensationRayBudget);
                Assert.Equal(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(active.Seeds));
                Assert.Equal(plan.Targets, active.Targets);
                foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("恢复固定光阑瞳倍率"), Find<CheckBox>(window, "AutonomousPupilRecovery").Content);
                    Find<CheckBox>(window, "AutonomousPupilRecovery").BringIntoView(); await Task.Delay(20); window.UpdateLayout();
                    Capture(window, "recovery-controls-" + language);
                    Find<NumericUpDown>(window, "AutonomousFullUpdates").BringIntoView(); await Task.Delay(20); window.UpdateLayout();
                    Capture(window, "recovery-stages-" + language);
                }
                window.Width = 480; await Task.Delay(30); window.UpdateLayout();
                Find<NumericUpDown>(window, "AutonomousFullUpdates").BringIntoView(); await Task.Delay(20); window.UpdateLayout();
                Capture(window, "recovery-stages-narrow-ja-JP");
                Find<CheckBox>(window, "AutonomousMatrixDerivatives").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousPupilRecovery").IsChecked);
                Find<CheckBox>(window, "AutonomousAxial").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousRecoveryAxial").IsEnabled);
                Assert.True(Find<CheckBox>(window, "AutonomousRecoveryAxial").IsChecked);
                Find<CheckBox>(window, "AutonomousStageChecks").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousProtectLocalBudget").IsEnabled);
                Assert.True(Find<CheckBox>(window, "AutonomousProtectLocalBudget").IsChecked);
                var inactive = await Save();
                Assert.NotEqual(true, inactive.Options.Recovery.RestorePupilMagnification);
                Assert.NotEqual(true, inactive.Options.Recovery.UseAutomaticMatrixDerivatives);
                Assert.Null(inactive.Options.AxialCompensation);
                Assert.True(inactive.Options.UseAxialVariablesInRecovery); Assert.True(inactive.Options.ProtectPostRecoveryRayBudget);
                Assert.NotEqual(true, inactive.Options.CheckStageFeasibility);
                Assert.Equal(0, traces.RayCount); Assert.Null(window.CurrentCheckpoint);
                return true;

                async Task<AutonomousSearchPlan> Save()
                {
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                    return await AutonomousDesignFiles.LoadPlanAsync(files.Path("recovery.topology-plan.json"));
                }
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(originalLanguage); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task LocalAndFullUpdateLimitsChangeOnlyTheirStageAllowancesNotTotalCaps()
    {
        using var files = new OwnedFiles();
        var plan = Plan();
        plan = plan with
        {
            Options = plan.Options with
            {
                MaximumFunctionEvaluations = 600,
                MaximumGradientEvaluations = 100,
                Local = plan.Options.Local with { MaximumUpdates = 4, MaximumFunctionEvaluations = 20, MaximumGradientEvaluations = 17 },
                Full = plan.Options.Full with { MaximumUpdates = 8, MaximumFunctionEvaluations = 9, MaximumGradientEvaluations = 9, LearningRate = .001 }
            }
        };
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("stages.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                using var traces = SequentialTraceMeasurement.Begin();
                Find<NumericUpDown>(window, "AutonomousFullUpdates").Value = 99;
                var changed = await Save();
                Assert.Equal(99, changed.Options.Full.MaximumUpdates);
                Assert.Equal(100, changed.Options.Full.MaximumFunctionEvaluations); Assert.Equal(100, changed.Options.Full.MaximumGradientEvaluations);
                Assert.Equal(plan.Options.Local, changed.Options.Local);
                Assert.Equal(ContentFingerprint.Compute(plan with
                {
                    Options = plan.Options with
                    {
                        Full = plan.Options.Full with
                        { MaximumUpdates = 99, MaximumFunctionEvaluations = 100, MaximumGradientEvaluations = 100 }
                    }
                }), ContentFingerprint.Compute(changed));
                Find<NumericUpDown>(window, "AutonomousLocalUpdates").Value = 0;
                var zero = await Save();
                Assert.Equal(0, zero.Options.Local.MaximumUpdates);
                Assert.Equal(20, zero.Options.Local.MaximumFunctionEvaluations); Assert.Equal(17, zero.Options.Local.MaximumGradientEvaluations);
                Assert.Equal(600, zero.Options.MaximumFunctionEvaluations); Assert.Equal(100, zero.Options.MaximumGradientEvaluations);
                Assert.Equal(plan.Options.MaximumTracedRays, zero.Options.MaximumTracedRays);
                Find<NumericUpDown>(window, "AutonomousFullUpdates").Value = 99.5m;
                Click(window, "AutonomousCheck"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Equal(0, traces.RayCount); Assert.Null(window.CurrentCheckpoint);
                return true;

                async Task<AutonomousSearchPlan> Save()
                {
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                    return await AutonomousDesignFiles.LoadPlanAsync(files.Path("stages.topology-plan.json"));
                }
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
