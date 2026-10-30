using Avalonia.Controls;
using System.Text.Json;
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
    public async Task LocalLibraryLoadsSavesAndRoundTripsThreeLanguagesWithoutStartingOpticalSearch()
    {
        using var files = new OwnedFiles();
        var (plan, options, entry) = LensRetrievalTests.Specification();
        var library = new LensLibraryDocument { Entries = [entry] };
        var libraryPath = files.Path("source.lens-library.json"); var planPath = files.Path("retrieval.topology-plan.json");
        await LensLibraryFiles.SaveAsync(library, libraryPath);
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var window = new AutonomousTopologyWindow(new()
            {
                InitialPlan = plan with { Options = plan.Options with { SeedPreparation = options with { Retrieval = null } } },
                RunDirectory = files.Root,
                OpenPicker = (_, _) => Task.FromResult<string?>(libraryPath),
                SavePicker = (_, kind) => Task.FromResult<string?>(kind == "lens-library" ? files.Path("copy.lens-library.json") : planPath)
            });
            try
            {
                using var rays = SequentialTraceMeasurement.Begin();
                window.Show(); window.UpdateLayout();
                Click(window, "AutonomousLibraryOpen"); await window.PendingOperation;
                Assert.True(Find<CheckBox>(window, "AutonomousRetrieval").IsChecked);
                Assert.False(Find<CheckBox>(window, "AutonomousCompetition").IsChecked);
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                var stored = await AutonomousDesignFiles.LoadPlanAsync(planPath);
                Assert.Equal(ContentFingerprint.Compute(library), ContentFingerprint.Compute(stored.Options.SeedPreparation!.Retrieval!.Library));
                Assert.Equal(plan.Targets, stored.Targets); Assert.Equal(plan.Constraints, stored.Constraints);
                Assert.Equal(0, rays.RayCount); Assert.Null(window.CurrentCheckpoint);
                Assert.Empty(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                Click(window, "AutonomousLibrarySave"); await window.PendingOperation;
                Assert.Equal(ContentFingerprint.Compute(library), ContentFingerprint.Compute(await LensLibraryFiles.LoadAsync(files.Path("copy.lens-library.json"))));
                foreach (var locale in new[] { UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("从已有镜头库检索起点"), Find<CheckBox>(window, "AutonomousRetrieval").Content);
                    Assert.Equal(UiLocalization.Text("打开镜头库"), Find<Button>(window, "AutonomousLibraryOpen").Content);
                }
                Find<CheckBox>(window, "AutonomousCompetition").IsChecked = true;
                Assert.False(Find<CheckBox>(window, "AutonomousRetrieval").IsChecked);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExplicitDesktopPreparationActuallyRetrievesRevalidatesAndSavesItsEvidence()
    {
        using var files = new OwnedFiles(); var (plan, options, _) = LensRetrievalTests.Specification();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var window = new AutonomousTopologyWindow(new()
            { InitialPlan = plan with { Options = plan.Options with { SeedPreparation = options } }, RunDirectory = files.Root });
            try
            {
                window.Show(); window.UpdateLayout(); Click(window, "AutonomousPrepareSeeds"); await window.PendingOperation;
                var file = Assert.Single(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                var text = await File.ReadAllTextAsync(file);
                Assert.Contains("initial-structure-lens-retrieval-v1", text);
                Assert.Contains("fixedApertures", text);
                using var document = JsonDocument.Parse(text);
                var assessment = document.RootElement.GetProperty("result").GetProperty("retrieval").GetProperty("trials")[0].GetProperty("assessment");
                Assert.True(assessment.GetProperty("completed").GetBoolean());
                Assert.True(assessment.GetProperty("tracedRayCount").GetInt64() > 0);
                Assert.Equal(plan.Targets.MaximumRmsRadiusMillimeters, assessment.GetProperty("targets").GetProperty("maximumRmsRadiusMillimeters").GetDouble());
                Assert.Null(window.CurrentCheckpoint);
                Assert.Contains(UiLocalization.Text("镜头库：条目 / 原规格合格 / 原输入复用"), Find<TextBlock>(window, "AutonomousSeedSummary").Text);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
