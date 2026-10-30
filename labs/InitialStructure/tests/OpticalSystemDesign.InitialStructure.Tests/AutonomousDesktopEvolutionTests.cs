using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task EvolutionModesAndPowerAllocationRoundTripWithoutOpticalWork()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var (source, options) = StructureEvolutionTests.Specification();
            var evolution = options.Evolution! with
            {
                Mode = StructureEvolutionMode.RecoveryAndCompensation,
                PowerAllocation = new() { MaximumAttempts = 2, ClosureFunctionsPerAttempt = 64 }
            };
            options = options with { Evolution = evolution, MaximumTracedRays = 2000000 };
            var plan = source with { Options = source.Options with { SeedPreparation = options } };
            var path = files.Path("m2.topology-plan.json"); var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                SavePicker = (_, _) => Task.FromResult<string?>(path)
            });
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.True(Find<CheckBox>(window, "AutonomousEvolution").IsChecked);
                Assert.True(Find<CheckBox>(window, "AutonomousPowerAllocation").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousEvolutionCompetition").IsChecked);
                Assert.Equal(2000000, Find<NumericUpDown>(window, "AutonomousCompetitionRays").Value);
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var stored = await AutonomousDesignFiles.LoadPlanAsync(path);
                Assert.Equal(ContentFingerprint.Compute(evolution), ContentFingerprint.Compute(stored.Options.SeedPreparation!.Evolution!));
                Assert.Equal(plan.Targets, stored.Targets); Assert.Equal(plan.Constraints, stored.Constraints);
                Assert.Null(window.CurrentCheckpoint);
                Assert.Empty(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                foreach (var locale in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("实验：对当前处方做结构演化"), Find<CheckBox>(window, "AutonomousEvolution").Content);
                    Assert.Equal(UiLocalization.Text("实验：光焦度分配与目标闭合起点"), Find<CheckBox>(window, "AutonomousPowerAllocation").Content);
                }
                Find<CheckBox>(window, "AutonomousCompetition").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousEvolution").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousPowerAllocation").IsChecked);
                Find<CheckBox>(window, "AutonomousPowerAllocation").IsChecked = true;
                Assert.True(Find<CheckBox>(window, "AutonomousEvolution").IsChecked);
                Assert.True(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                Find<CheckBox>(window, "AutonomousEvolutionRecovery").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousEvolutionCompetition").IsChecked);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
