using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CalculationPathPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    [Theory]
    [InlineData("PSF", "2048", "8192")]
    [InlineData("MTF", "2048", "6144")]
    [InlineData("FFT PSF Cross Section", "4096", "4096")]
    public async Task FourierPanelExposesAutomaticRecoveryAndTheAdaptivePupilLimit(
        string name, string automaticLimit, string adaptiveLimit)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CalculationPathPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), name) { IsLocked = true };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AnalysisPanel).GetField("_initialRunRequested", flags)!.SetValue(panel, true);
            ((CheckBox)typeof(AnalysisPanel).GetField("_parameterAutoApply", flags)!.GetValue(panel)!).IsChecked = false;
            ((Border)typeof(AnalysisPanel).GetField("_settingsHost", flags)!.GetValue(panel)!).IsVisible = true;
            var window = new Window { Content = panel, Width = 1000, Height = 750 };
            try
            {
                window.Show();
                window.UpdateLayout();
                ComboBox FindChoice(string key) => Assert.Single(panel.GetVisualDescendants().OfType<ComboBox>(),
                    control => AutomationProperties.GetAutomationId(control) == "analysis-parameter-" + key);
                var mode = FindChoice("FourierSamplingMode");
                var maximum = FindChoice("FourierMaximumSampling");
                Assert.Equal("按需加密", mode.SelectedItem);
                Assert.Equal(automaticLimit, maximum.SelectedItem);
                Assert.Contains("固定采样", mode.Items);
                mode.SelectedItem = "自适应精度";
                maximum.SelectedItem = adaptiveLimit;
                var settings = (IReadOnlyDictionary<string, string>)typeof(AnalysisPanel)
                    .GetMethod("CaptureParameterSettings", flags)!.Invoke(panel, null)!;
                Assert.Equal(adaptiveLimit, settings["FourierMaximumSampling"]);
                Assert.Equal("自适应精度", settings["FourierSamplingMode"]);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("Huygens Through Focus MTF")]
    public async Task CheckedFocalPupilIsAnOptInAnalysisSetting(string name)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CalculationPathPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), name) { IsLocked = true };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AnalysisPanel).GetField("_initialRunRequested", flags)!.SetValue(panel, true);
            ((CheckBox)typeof(AnalysisPanel).GetField("_parameterAutoApply", flags)!.GetValue(panel)!).IsChecked = false;
            ((Border)typeof(AnalysisPanel).GetField("_settingsHost", flags)!.GetValue(panel)!).IsVisible = true;
            var window = new Window { Content = panel, Width = 1000, Height = 750 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var input = Assert.Single(panel.GetVisualDescendants().OfType<CheckBox>(),
                    control => AutomationProperties.GetAutomationId(control) == "analysis-parameter-UseCheckedFocusedPupil");
                Assert.False(input.IsChecked);
                input.IsChecked = true;
                var settings = (IReadOnlyDictionary<string, string>)typeof(AnalysisPanel)
                    .GetMethod("CaptureParameterSettings", flags)!.Invoke(panel, null)!;
                Assert.True(bool.Parse(settings["UseCheckedFocusedPupil"]));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnsupportedFoucaultPolarizationExplainsWhyOnlyThatControlIsDisabled()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CalculationPathPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), "Foucault Analysis")
            { IsLocked = true };
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            ((CheckBox)typeof(AnalysisPanel).GetField("_parameterAutoApply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!).IsChecked = false;
            ((Border)typeof(AnalysisPanel).GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!).IsVisible = true;
            var window = new Window { Content = panel, Width = 1000, Height = 750 };
            try
            {
                window.Show(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var controls = panel.GetVisualDescendants().OfType<Control>().ToArray();
                var polarization = Assert.Single(controls, c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-UsePolarization");
                Assert.True(polarization.IsEnabled);
                Assert.Contains("Jones", ToolTip.GetTip(polarization)!.ToString());
                Assert.Contains("GRIN", AutomationProperties.GetHelpText(polarization));
                var sampling = Assert.Single(controls, c => AutomationProperties.GetAutomationId(c) == "analysis-parameter-Sampling");
                Assert.True(sampling.IsEnabled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
