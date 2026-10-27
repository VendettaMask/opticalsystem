using Avalonia.Controls;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task GeometryRecoveryIsExplicitPreservesLegacyPlansAndUsesTheAutomaticMatrixDependency()
    {
        using var files = new OwnedFiles();
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            foreach (var original in new bool?[] { null, false, true })
            {
                var plan = Plan(); plan = plan with
                {
                    Options = plan.Options with
                    {
                        Recovery = plan.Options.Recovery with
                        { UseGeometryConstraintTangents = original, UseAutomaticMatrixDerivatives = original == true ? true : null }
                    }
                };
                var window = new AutonomousTopologyWindow(new()
                {
                    InitialPlan = plan,
                    SavePicker = (_, _) => Task.FromResult<string?>(files.Path("geometry.topology-plan.json"))
                });
                try
                {
                    window.Show(); window.UpdateLayout();
                    using var noWork = SequentialTraceMeasurement.Begin();
                    var choice = Find<CheckBox>(window, "AutonomousGeometryRecovery");
                    Assert.Equal(original == true, choice.IsChecked);
                    Find<NumericUpDown>(window, "AutonomousTrials").Value++;
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    var saved = await AutonomousDesignFiles.LoadPlanAsync(files.Path("geometry.topology-plan.json"));
                    Assert.Equal(ContentFingerprint.Compute(plan with { Options = plan.Options with { MaximumTrials = plan.Options.MaximumTrials + 1 } }), ContentFingerprint.Compute(saved));
                    choice.IsChecked = true;
                    Assert.True(Find<CheckBox>(window, "AutonomousMatrixDerivatives").IsChecked);
                    Click(window, "AutonomousSavePlan"); await window.PendingOperation;
                    saved = await AutonomousDesignFiles.LoadPlanAsync(files.Path("geometry.topology-plan.json"));
                    Assert.True(saved.Options.Recovery.UseGeometryConstraintTangents); Assert.True(saved.Options.Recovery.UseAutomaticMatrixDerivatives);
                    Find<CheckBox>(window, "AutonomousMatrixDerivatives").IsChecked = false;
                    Assert.False(choice.IsChecked);
                    if (original == true)
                        foreach (var selected in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                        {
                            UiLocalization.Apply(selected); window.UpdateLayout();
                            Assert.Equal(UiLocalization.Text("沿制造约束恢复"), choice.Content);
                            choice.BringIntoView(); await Task.Delay(20); window.UpdateLayout();
                            Capture(window, "geometry-recovery-" + selected);
                        }
                    Assert.Equal(0, noWork.RayCount); Assert.Null(window.CurrentCheckpoint);
                }
                finally { window.RequestSafeClose(); await window.PendingOperation; UiLocalization.Apply(language); }
            }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(1));
    }
}
