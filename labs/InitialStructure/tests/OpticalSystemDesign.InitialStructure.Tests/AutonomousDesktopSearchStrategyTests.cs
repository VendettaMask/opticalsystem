using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task ExperimentalOptionsRoundTripWithoutStartingSearchAndLocalize()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var (source, settings) = StructureCompetitionTests.Specification();
            var preparation = settings with
            {
                Competition = settings.Competition! with
                {
                    SearchStrategy = new()
                    { PowerSigns = new() { MaximumPatterns = 8 }, QuickEvaluation = new() { CoordinateStep = .005 } }
                }
            };
            var plan = source with { Options = source.Options with { SeedPreparation = preparation } };
            var path = files.Path("m1.topology-plan.json"); var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                SavePicker = (_, _) => Task.FromResult<string?>(path)
            });
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.True(Find<CheckBox>(window, "AutonomousPowerSigns").IsChecked);
                Assert.True(Find<CheckBox>(window, "AutonomousQuickAberration").IsChecked);
                Assert.Null(window.CurrentCheckpoint);
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var stored = await AutonomousDesignFiles.LoadPlanAsync(path);
                Assert.Equal(preparation.Competition.SearchStrategy, stored.Options.SeedPreparation!.Competition!.SearchStrategy);
                Assert.Equal(plan.Targets, stored.Targets); Assert.Equal(plan.Constraints, stored.Constraints);
                Assert.Empty(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                foreach (var locale in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("实验：光焦度符号覆盖"), Find<CheckBox>(window, "AutonomousPowerSigns").Content);
                    Assert.Equal(UiLocalization.Text("实验：三阶像差快筛与挑战复核"), Find<CheckBox>(window, "AutonomousQuickAberration").Content);
                }
                Find<CheckBox>(window, "AutonomousCompetition").IsChecked = false;
                Assert.False(Find<CheckBox>(window, "AutonomousPowerSigns").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousQuickAberration").IsChecked);
                Find<CheckBox>(window, "AutonomousPowerSigns").IsChecked = true;
                Assert.True(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
