using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class RmsSamplingPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OptilandWorkbench.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    [Theory]
    [InlineData("RMS vs Field", "NumRings")]
    [InlineData("RMS Wavefront vs Field", "RayDensity")]
    [InlineData("RMS vs Wavelength", "NumRings")]
    [InlineData("RMS vs Focus", "NumRings")]
    [InlineData("RMS Field Map", "NumRings")]
    public async Task SavedRectangularDensitySurvivesAndMethodChangesLabelAndBounds(string name, string densityKey)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RmsSamplingPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), name, initialSettings: new Dictionary<string, string>
                { ["Method"] = "RA", [densityKey] = "128", ["GaussianAzimuthalSamples"] = "12" })
            { IsLocked = true };
            // Only exercise settings controls: do not start background optical work or
            // persist presets when the panel attaches to this short-lived test session.
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            var autoApply = (CheckBox)typeof(AnalysisPanel).GetField("_parameterAutoApply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            autoApply.IsChecked = false;
            var host = (Border)typeof(AnalysisPanel).GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            host.IsVisible = true;
            var window = new Window { Content = panel, Width = 1000, Height = 750 };
            try
            {
                window.Show(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                T Input<T>(string key) where T : Control => panel.GetVisualDescendants().OfType<T>()
                    .Single(c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-" + key);
                var density = Input<NumericUpDown>(densityKey);
                var angles = Input<NumericUpDown>("GaussianAzimuthalSamples");
                var method = Input<ComboBox>("Method");
                Assert.Equal(128m, density.Value); Assert.Equal(1024m, density.Maximum);
                Assert.Equal("RA 每边点数", AutomationProperties.GetName(density));
                Assert.False(angles.IsEnabled); Assert.NotNull(ToolTip.GetTip(angles));
                method.SelectedItem = "GQ";
                Assert.Equal(32m, density.Maximum); Assert.True(density.Value <= 32);
                Assert.Equal("GQ 径向环数", AutomationProperties.GetName(density));
                Assert.True(angles.IsEnabled); Assert.Null(ToolTip.GetTip(angles));
                Assert.Null(AutomationProperties.GetHelpText(angles)); Assert.Equal(12m, angles.Value);
                method.SelectedItem = "RA"; density.Value = 256;
                var saved = (IReadOnlyDictionary<string, string>)typeof(AnalysisPanel)
                    .GetMethod("CaptureParameterSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;
                Assert.Equal("256", saved[densityKey]); Assert.Equal("RA", saved["Method"]);
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "RA 每边点数：");
                if (name == "RMS Wavefront vs Field") Assert.NotNull(Input<CheckBox>("UsePolarization"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
