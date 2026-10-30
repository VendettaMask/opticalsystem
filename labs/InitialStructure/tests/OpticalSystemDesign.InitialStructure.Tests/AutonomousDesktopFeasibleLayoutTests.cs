using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task FeasibleThickOptionsPersistAndMutuallyExcludeLegacyConstructionWithoutChangingOptimizer()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var plan = Plan(); var locale = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new() { InitialPlan = plan, RunDirectory = files.Root,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("feasible.topology-plan.json")) });
            try
            {
                window.Show(); window.UpdateLayout();
                Find<CheckBox>(window, "AutonomousThickDelano").IsChecked = true;
                Find<CheckBox>(window, "AutonomousFeasibleThickDelano").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousThickDelano").IsChecked);
                Assert.True(Find<CheckBox>(window, "AutonomousFixedStopRecovery").IsChecked);
                Find<CheckBox>(window, "AutonomousStopPartitions").IsChecked = true;
                Find<NumericUpDown>(window, "AutonomousConstructionLayouts").Value = 2;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var stored = await AutonomousDesignFiles.LoadPlanAsync(files.Path("feasible.topology-plan.json"));
                var generation = stored.Options.SeedPreparation!.Generation!;
                Assert.Equal(3, generation.MaterializationVersion);
                Assert.Equal(2, generation.FeasibleLayouts!.MaximumLayouts);
                Assert.Equal(new[] { generation.StopAfterElement, 0, generation.Elements.Length / 2, generation.Elements.Length }.Distinct(), generation.FeasibleLayouts.StopAfterElementCandidates);
                Assert.Equal(plan.Options.Local, stored.Options.Local);
                Assert.Equal(plan.Options.Full, stored.Options.Full);
                Assert.Equal(plan.Targets, stored.Targets); Assert.Equal(plan.Constraints, stored.Constraints);
                Assert.Null(window.CurrentCheckpoint);
                foreach (var language in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(language); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("多布局厚透镜构造：先通过几何再补偿"),
                        Find<CheckBox>(window, "AutonomousFeasibleThickDelano").Content);
                }
                Find<CheckBox>(window, "AutonomousThickDelano").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousFeasibleThickDelano").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousStopPartitions").IsChecked);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(locale); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
