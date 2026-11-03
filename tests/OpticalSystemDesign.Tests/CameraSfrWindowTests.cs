using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraSfrWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task SingleRoiPageExportsOriginalSizeChangesLineWidthAndClearsStaleResults(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraSfrWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(theme == "Light" ? DoubleGaussSfrTests.StandardLens : SlantedEdgeSfrTests.ReferenceLens);
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Name == "CameraResults");
                tabs.SelectedItem = ((IEnumerable<object>)tabs.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "camera-sfr-tab");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "camera-sfr-spectrum-mode").SelectedIndex = 0;
                window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "camera-sfr-radius").Value = 32;
                if (theme == "Light") { await window.RunSfrAsync(); Assert.True(window.SfrResult?.SamplingConverged); }
                else window.PresentSfr(await app.CameraImaging.SimulateSlantedEdgeAsync(new(CheckSampling: false)));
                var result = window.SfrResult!;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var graph = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "camera-sfr-graph");
                var plot = graph.GetVisualDescendants().OfType<AnalysisPlotControl>().Single();
                var legends = graph.GetVisualDescendants().OfType<CheckBox>().ToArray(); Assert.Equal(5, legends.Length);
                Assert.Contains("完整瞳孔对照：通过", window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sfr-info").Text);
                legends[0].IsChecked = false; Assert.Equal(4, plot.Series.Count); legends[0].IsChecked = true;
                Assert.Equal(AnalysisCurveWidth.Standard, plot.GetValue(AnalysisPlotControl.CurveWidthProperty));
                var width = window.GetVisualDescendants().OfType<DropDownButton>().Single(c => c.Name == "AnalysisCurveWidth");
                var menu = Assert.IsType<MenuFlyout>(width.Flyout);
                var thick = menu.Items.OfType<MenuItem>().Single(c => c.Header?.ToString() == "粗");
                thick.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(AnalysisCurveWidth.Thick, plot.GetValue(AnalysisPlotControl.CurveWidthProperty));
                using var bitmap = window.CaptureSfrImage(); Assert.Equal(new PixelSize(1920, 1080), bitmap.PixelSize);
                var evidence = Environment.GetEnvironmentVariable("OPTICAL_SFR_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence); bitmap.Save(Path.Combine(evidence, $"sfr-{theme}.png"), PngBitmapEncoderOptions.Default);
                    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(evidence, $"sfr-page-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
                window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "camera-sfr-pitch").Value = 4;
                Assert.Null(window.SfrResult); Assert.Null(graph.Content);
                window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "camera-sfr-pitch").Text = "invalid";
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "camera-sfr-run").IsEnabled);
                window.PresentSfr(result); app.Documents.NewBlank(); Assert.Null(window.SfrResult);
                window.PresentSfr(result with { Algorithm = CameraSfrResult.PreviousAlgorithm });
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "camera-sfr-replay").IsEnabled);
                Assert.Contains("旧版", window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sfr-info").Text);
                var legacy = await app.CameraImaging.OpenSlantedEdgeAsync(Path.Combine(SlantedEdgeSfrTests.RepositoryRoot,
                    "validation/rechecks/2026-11-02/double-gauss-sfr/reference-lens.sfr-experiment.zip"));
                window.PresentSfr(legacy); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Equal(3, graph.GetVisualDescendants().OfType<CheckBox>().Count());
                Assert.Contains("旧版未提供", window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sfr-info").Text);
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "camera-sfr-replay").IsEnabled);
                if (theme == "Light")
                {
                    await app.Documents.OpenAsync(Path.Combine(SlantedEdgeSfrTests.RepositoryRoot,
                        "local-data/lens-library/originals/user-zmx/project/samples/lenses/Double Gauss 28 degree field_2.zmx"));
                    // Settle the posted document-change notification before invoking a new action.
                    Dispatcher.UIThread.RunJobs();
                    var pitch = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "camera-sfr-pitch");
                    pitch.Value = 3; pitch.Text = "3";
                    await window.RunSfrAsync(); Assert.Null(window.SfrResult);
                    Assert.Contains("相位欠采样", window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sfr-info").Text);
                }
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
