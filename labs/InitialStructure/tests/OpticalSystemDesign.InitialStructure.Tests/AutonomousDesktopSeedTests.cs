using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
    public async Task FirstOrderButtonPreparesAndPersistsNewPlanWithoutStartingSearchAndLocalizesControls()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var plan = Plan() with { Options = Plan().Options with { AxialCompensation = null,
                SeedPreparation = new() { MaximumStarts = 1, RetainedSeeds = 1, MaximumFunctionEvaluations = 40 } } };
            var window = new AutonomousTopologyWindow(new() { RunDirectory = files.Root, InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("planned.topology-plan.json")) });
            try
            {
                window.Show(); window.UpdateLayout();
                using (var trace = SequentialTraceMeasurement.Begin())
                {
                    Find<CheckBox>(window, "AutonomousDiversity").IsChecked = true;
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    var stored = await AutonomousDesignFiles.LoadPlanAsync(files.Path("planned.topology-plan.json"));
                    Assert.NotNull(stored.Options.PoolDiversity); Assert.Equal(plan.Options.SeedPreparation, stored.Options.SeedPreparation);
                    Assert.Null(window.CurrentCheckpoint); Assert.Equal(0, trace.RayCount);
                }
                Find<NumericUpDown>(window, "AutonomousSeedFunctions").Text = "invalid";
                Click(window, "AutonomousPrepareSeeds"); await window.PendingOperation;
                Assert.NotEmpty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.False(Directory.Exists(Path.Combine(files.Root, "preparations")));
                Find<NumericUpDown>(window, "AutonomousSeedFunctions").Text = "40";
                Click(window, "AutonomousPrepareSeeds"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Null(window.CurrentCheckpoint); Assert.True(Find<Button>(window, "AutonomousRun").IsEnabled);
                var preparedPath = Assert.Single(Directory.GetFiles(files.Root, "*.topology-plan.json", SearchOption.AllDirectories), path => path.Contains("preparations"));
                var prepared = await AutonomousDesignFiles.LoadPlanAsync(preparedPath);
                Assert.Equal(plan.Targets, prepared.Targets); Assert.NotNull(prepared.Options.PoolDiversity);
                Assert.NotEqual(ContentFingerprint.Compute(plan.Seeds), ContentFingerprint.Compute(prepared.Seeds));
                Assert.Single(Directory.GetFiles(files.Root, "*.seed-search.json", SearchOption.AllDirectories));
                foreach (var locale in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("生成一阶起点"), Find<Button>(window, "AutonomousPrepareSeeds").Content);
                    Assert.Equal(UiLocalization.Text("生成一阶起点"), AutomationProperties.GetName(Find<Button>(window, "AutonomousPrepareSeeds")));
                    Assert.Equal(UiLocalization.Text("保留不同结构与玻璃组合"), Find<CheckBox>(window, "AutonomousDiversity").Content);
                    Assert.StartsWith(UiLocalization.Text("一阶准备使用独立额度，不代表完整光学验收通过"), Find<TextBlock>(window, "AutonomousSeedSummary").Text);
                }
                window.Width = 480; window.UpdateLayout();
                var budgetInput = Find<NumericUpDown>(window, "AutonomousSeedFunctions"); budgetInput.BringIntoView(); window.UpdateLayout();
                var form = budgetInput.GetVisualAncestors().OfType<ScrollViewer>().First();
                var content = (Control)form.Content!;
                var position = budgetInput.TranslatePoint(default, content)!.Value;
                form.Offset = new Vector(0, Math.Clamp(position.Y - 80, 0, Math.Max(0, form.Extent.Height - form.Viewport.Height)));
                await Task.Delay(30); window.UpdateLayout();
                var point = budgetInput.TranslatePoint(new(budgetInput.Bounds.Width / 2, budgetInput.Bounds.Height / 2), form)!.Value;
                Assert.InRange(point.Y, 0, form.Bounds.Height); Capture(window, "seed-preparation-narrow");
                Click(window, "AutonomousRun"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.NotNull(window.CurrentCheckpoint);
                Assert.Equal(ContentFingerprint.Compute(prepared.Seeds), ContentFingerprint.Compute(window.CurrentCheckpoint.Plan.Seeds));
                Assert.All(window.CurrentCheckpoint.Candidates, candidate => Assert.False(candidate.Assessment.MeetsTargets));
                Assert.True(window.CurrentCheckpoint.Consumed.ValidationEvaluations > 0);
                return true;
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
