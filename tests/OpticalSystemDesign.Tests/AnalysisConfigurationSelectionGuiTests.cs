using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisConfigurationSelectionGuiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CONFIGURATION_SELECTION_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task EveryAnalysisSettingsLayoutIncludesTheCommonSelectorOnlyForMultipleConfigurations()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisConfigurationSelectionGuiTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            foreach (var multi in new[] { false, true })
            {
                if (multi) await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
                foreach (var descriptor in WorkbenchAnalysisCatalog.DescriptorsForMode(OpticalWorkbenchMode.Sequential))
                {
                    using var panel = CreatePanel(app, descriptor.CanonicalKey);
                    Assert.Equal(multi, Controls(panel).ContainsKey("Configuration"));
                    if (multi)
                    {
                        var combo = Assert.IsType<ComboBox>(Controls(panel)["Configuration"]);
                        Assert.Equal("AnalysisConfigurationSelector", combo.Name);
                        var expected = descriptor.CanonicalKey == "Configuration Matrix Spot Diagram" ? "All" : "Current";
                        Assert.Equal(expected, Assert.IsType<ComboBoxItem>(combo.SelectedItem).Tag);
                    }
                }
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task SelectorUsesTranslatedLabelsStableSettingsAndRefreshesAfterConfigurationAndFileChanges(string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisConfigurationSelectionGuiTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            using var panel = CreatePanel(app, "Spot Diagram");
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Spot Diagram",
                app.Analyses.MergeSettings("Spot Diagram", new Dictionary<string, string> { ["Configuration"] = "All", ["RayDensity"] = "2" })));
            panel.PresentResult(result.View, result.SourceDocument!, DateTimeOffset.Now);
            var window = new Window { Width = 1280, Height = 800, Content = panel };
            try
            {
                window.Show(); Render(window);
                var plots = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                Assert.Equal(3, plots.Length);
                Assert.All(plots, plot => Assert.Equal(9, plot.Series.Count));
                Assert.All(plots.SelectMany(plot => plot.Series), series =>
                {
                    Assert.True(AnalysisPlotControl.TryGetWavelengthNanometers(series, out var nanometers));
                    Assert.Equal(AnalysisPlotControl.WavelengthColor(nanometers), AnalysisPlotControl.SeriesColor(series));
                });
                var legends = panel.GetVisualDescendants().OfType<CheckBox>().Where(check => check.Content is StackPanel).ToArray();
                Assert.Equal(9, legends.Length);
                AssertLegendInsidePage(panel, window, legends);
                legends[0].IsChecked = false;
                Assert.All(plots, plot => Assert.Equal(8, plot.Series.Count));
                legends[0].IsChecked = true;
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CONFIGURATION_SELECTION_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var image = panel.CapturePlotImage();
                    AnalysisPanel.SavePlotImage(image, Path.Combine(directory, $"overlay-{language}.png"));
                    window.Width = 640; window.Height = 400; Render(window);
                    AssertLegendInsidePage(panel, window, legends);
                    using var narrow = panel.CapturePlotImage();
                    AnalysisPanel.SavePlotImage(narrow, Path.Combine(directory, $"overlay-{language}-narrow.png"));
                    window.Width = 1280; window.Height = 800; Render(window);
                }
                var host = (Border)typeof(AnalysisPanel).GetField("_settingsHost", Private)!.GetValue(panel)!;
                host.IsVisible = true; Render(window);
                var selector = Assert.IsType<ComboBox>(Controls(panel)["Configuration"]);
                Assert.Equal(UiLocalization.Text("当前的", language), Assert.IsType<TextBlock>(Assert.IsType<ComboBoxItem>(selector.SelectedItem).Content).Text);
                selector.IsDropDownOpen = true; Render(window);
                selector.IsDropDownOpen = false; Render(window);
                var alternate = language == UiLocalization.English ? UiLocalization.Japanese : UiLocalization.English;
                UiLocalization.Apply(alternate); Render(window);
                Assert.Equal("Current", Capture(panel)["Configuration"]);
                Assert.Equal(UiLocalization.Text("当前的", alternate), Assert.IsType<TextBlock>(Assert.IsType<ComboBoxItem>(selector.SelectedItem).Content).Text);
                UiLocalization.Apply(language); Render(window);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    selector.SelectedIndex = 0; Render(window);
                    panel.PresentResult(result.View, result.SourceDocument!, DateTimeOffset.Now); Render(window);
                    using var image = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
                    image.Render(window); AnalysisPanel.SavePlotImage(image, Path.Combine(directory, $"settings-{language}.png"));
                    selector = Assert.IsType<ComboBox>(Controls(panel)["Configuration"]);
                }
                selector.SelectedIndex = 3; Render(window); // All, Current, 1/N, 2/N
                Assert.Equal("2", Capture(panel)["Configuration"]);
                app.MultiConfiguration.Activate(2); Render(window);
                Assert.Equal("2", Capture(panel)["Configuration"]);
                app.MultiConfiguration.Add(); Render(window);
                selector = Assert.IsType<ComboBox>(Controls(panel)["Configuration"]);
                Assert.Equal("2", Assert.IsType<ComboBoxItem>(selector.SelectedItem).Tag);
                Assert.Equal("2 / 4", Assert.IsType<TextBlock>(Assert.IsType<ComboBoxItem>(selector.SelectedItem).Content).Text);
                panel.IsLocked = true;
                app.Documents.NewBlank(); Render(window);
                Assert.DoesNotContain("Configuration", Controls(panel).Keys);
                Assert.Null(typeof(AnalysisPanel).GetField("_view", Private)!.GetValue(panel));
                await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath()); Render(window);
                Assert.Equal("Current", Assert.IsType<ComboBoxItem>(Assert.IsType<ComboBox>(Controls(panel)["Configuration"]).SelectedItem).Tag);
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }

    private static AnalysisPanel CreatePanel(WorkbenchApplication app, string key)
    {
        var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), key);
        typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
        ((CheckBox)typeof(AnalysisPanel).GetField("_parameterAutoApply", Private)!.GetValue(panel)!).IsChecked = false;
        return panel;
    }

    private static Dictionary<string, Control> Controls(AnalysisPanel panel) =>
        (Dictionary<string, Control>)typeof(AnalysisPanel).GetField("_parameterControls", Private)!.GetValue(panel)!;

    private static Dictionary<string, string> Capture(AnalysisPanel panel) =>
        (Dictionary<string, string>)typeof(AnalysisPanel).GetMethod("CaptureParameterSettings", Private)!.Invoke(panel, null)!;

    private static void AssertLegendInsidePage(AnalysisPanel panel, Window window, IEnumerable<CheckBox> legends)
    {
        var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
        var bounds = new Rect(page.Bounds.Size).TransformToAABB(page.TransformToVisual(window)!.Value);
        foreach (var legend in legends)
        {
            var actual = new Rect(legend.Bounds.Size).TransformToAABB(legend.TransformToVisual(window)!.Value);
            Assert.True(actual.Right <= bounds.Right + 1 && actual.Bottom <= bounds.Bottom + 1, $"Legend {actual} exceeds plot page {bounds}");
        }
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
