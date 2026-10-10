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
public sealed class CalculationPathPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OptilandWorkbench.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    [Fact]
    public async Task UnsupportedFoucaultPolarizationExplainsWhyOnlyThatControlIsDisabled()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CalculationPathPanelTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), "Foucault Analysis") { IsLocked = true };
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
