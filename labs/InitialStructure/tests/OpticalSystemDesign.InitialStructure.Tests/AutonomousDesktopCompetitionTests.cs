using Avalonia.Controls;
using System.Text.Json;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task CompetitionPreparationPersistsItsProtocolAndUsesTheExistingExplicitCommand()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var (source, settings) = StructureCompetitionTests.Specification();
            var preparation = settings with { Competition = settings.Competition! with { UpdatesPerStage = 0 } };
            var plan = source with { Options = source.Options with { SeedPreparation = preparation } };
            var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan,
                RunDirectory = files.Root,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("competition.topology-plan.json"))
            });
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.True(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousImageScreening").IsChecked);
                Assert.Null(window.CurrentCheckpoint);
                Assert.Empty(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                Find<NumericUpDown>(window, "AutonomousCompetitionUpdates").Value = 3;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var stored = await AutonomousDesignFiles.LoadPlanAsync(files.Path("competition.topology-plan.json"));
                Assert.Equal(ContentFingerprint.Compute(preparation with { Competition = preparation.Competition! with { UpdatesPerStage = 3 } }),
                    ContentFingerprint.Compute(stored.Options.SeedPreparation!));
                Assert.Equal(plan.Targets, stored.Targets); Assert.Equal(plan.Constraints, stored.Constraints);
                Assert.Equal(plan.Options.Local, stored.Options.Local); Assert.Equal(plan.Options.Full, stored.Options.Full);
                Assert.Null(window.CurrentCheckpoint);
                foreach (var locale in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("多结构候选竞争与分阶段优化"), Find<CheckBox>(window, "AutonomousCompetition").Content);
                    Assert.DoesNotContain("竞争", Find<CheckBox>(window, "AutonomousCompetition").Content!.ToString()!);
                }
                Find<CheckBox>(window, "AutonomousImageScreening").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                Find<CheckBox>(window, "AutonomousCompetition").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousImageScreening").IsChecked);
                Find<CheckBox>(window, "AutonomousThickDelano").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                Find<CheckBox>(window, "AutonomousCompetition").IsChecked = true;
                Find<NumericUpDown>(window, "AutonomousCompetitionUpdates").Value = 0;
                Find<NumericUpDown>(window, "AutonomousSeedStarts").Value = 2;
                Click(window, "AutonomousPrepareSeeds"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var report = Assert.Single(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                Assert.InRange(new FileInfo(report).Length, 1, InitialStructureLimits.MaximumManifestBytes);
                using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(report));
                var evidence = document.RootElement.GetProperty("result");
                Assert.Equal("initial-structure-competition-v1", evidence.GetProperty("algorithm").GetString());
                Assert.Equal(6, evidence.GetProperty("competition").GetProperty("rounds").GetArrayLength());
                Assert.Contains(UiLocalization.Text("竞争候选 / 工作起点 / 原规格合格"), Find<TextBlock>(window, "AutonomousSeedSummary").Text);
                Assert.Null(window.CurrentCheckpoint);
                Assert.True(Find<Button>(window, "AutonomousRun").IsEnabled);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
