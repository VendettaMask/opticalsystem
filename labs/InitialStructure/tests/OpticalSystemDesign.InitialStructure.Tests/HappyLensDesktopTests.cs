using Avalonia;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescriptorAndPopulationControlsSaveVersionedPlanWithoutOpticalWorkInThreeLanguages(bool thick)
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var plan = ParaxialSeedSearchTests.Plan();
            plan = plan with
            {
                Options = plan.Options with
                {
                    SeedPreparation = new()
                    {
                        Generation =
                HappyLensWorkflowTests.Descriptor(StructureGenerationMethod.Delano) with
                { ClearSemiDiameterMillimeters = Math.BitIncrement(5), MinimumThicknessMillimeters = Math.BitIncrement(2) }
                    }
                }
            };
            var window = new AutonomousTopologyWindow(new()
            {
                RunDirectory = files.Root,
                InitialPlan = plan,
                SavePicker = (_, _) => Task.FromResult<string?>(files.Path("descriptor.topology-plan.json"))
            });
            try
            {
                using var trace = SequentialTraceMeasurement.Begin();
                window.Show(); window.UpdateLayout();
                Find<CheckBox>(window, "AutonomousDelano").IsChecked = true;
                Assert.True(Find<CheckBox>(window, "AutonomousDescriptor").IsChecked);
                Find<CheckBox>(window, "AutonomousPopulation").IsChecked = true;
                Find<CheckBox>(window, "AutonomousDiversity").IsChecked = true;
                Find<CheckBox>(window, "AutonomousBounded").IsChecked = true;
                Find<CheckBox>(window, "AutonomousGeometryScreen").IsChecked = true;
                Find<CheckBox>(window, "AutonomousFixedStopRecovery").IsChecked = true;
                Find<CheckBox>(window, "AutonomousThickDelano").IsChecked = thick;
                Find<NumericUpDown>(window, "AutonomousLayerLimit").Value = 4;
                Find<NumericUpDown>(window, "AutonomousStagnation").Value = 2;
                Find<NumericUpDown>(window, "AutonomousSeedPerStart").Value = 100;
                Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                var saved = await AutonomousDesignFiles.LoadPlanAsync(files.Path("descriptor.topology-plan.json"));
                Assert.Equal(StructureGenerationMethod.Delano, saved.Options.SeedPreparation!.Generation!.Method);
                Assert.Equal(3, saved.Options.SeedPreparation.Generation.Elements.Length);
                Assert.Equal(100, saved.Options.SeedPreparation.MaximumEvaluationsPerStart);
                Assert.Equal(ContentFingerprint.Compute(plan.Options.SeedPreparation!.Generation! with { MaterializationVersion = thick ? 2 : null }),
                    ContentFingerprint.Compute(saved.Options.SeedPreparation.Generation));
                Assert.NotNull(saved.Options.PopulationExploration);
                Assert.Equal(8, saved.Options.PopulationExploration.Geometry!.MaximumAttempts);
                Assert.Equal(2, saved.Options.SeedPreparation.FixedStopRecoveryVersion);
                Assert.Equal(4, saved.Options.BoundedSearch!.MaximumAttemptsPerElementCount);
                Assert.Equal(2, saved.Options.BoundedSearch.MaximumUnsuccessfulTrials);
                Assert.Equal(plan.Targets, saved.Targets); Assert.Equal(plan.Constraints, saved.Constraints);
                Assert.Equal(0, trace.RayCount); Assert.Null(window.CurrentCheckpoint);
                foreach (var locale in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("群体探索与局部优化交替"), Find<CheckBox>(window, "AutonomousPopulation").Content);
                    Assert.Equal(UiLocalization.Text("使用 Delano 一阶搜索"), Find<CheckBox>(window, "AutonomousDelano").Content);
                    Assert.Equal(UiLocalization.Text("按实际厚度与固定光阑构造 Delano 起点"), Find<CheckBox>(window, "AutonomousThickDelano").Content);
                    Assert.Equal(UiLocalization.Text("筛检候选并有界停止"), Find<CheckBox>(window, "AutonomousBounded").Content);
                    Assert.Equal(UiLocalization.Text("重生候选几何筛检"), Find<CheckBox>(window, "AutonomousGeometryScreen").Content);
                }
                window.Width = 480; window.UpdateLayout();
                var input = Find<NumericUpDown>(window, "AutonomousGeneratedElements");
                input.BringIntoView(); window.UpdateLayout();
                var form = input.GetVisualAncestors().OfType<ScrollViewer>().First();
                var content = (Control)form.Content!;
                var position = input.TranslatePoint(default, content)!.Value;
                form.Offset = new Vector(0, Math.Clamp(position.Y - 100, 0, Math.Max(0, form.Extent.Height - form.Viewport.Height)));
                await Task.Delay(30); window.UpdateLayout();
                var point = input.TranslatePoint(new(input.Bounds.Width / 2, input.Bounds.Height / 2), form)!.Value;
                Assert.InRange(point.Y, 0, form.Bounds.Height);
                Capture(window, "descriptor-controls");
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
