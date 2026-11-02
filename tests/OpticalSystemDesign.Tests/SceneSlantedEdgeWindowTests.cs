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
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SceneSlantedEdgeWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task ScenePageUsesCurrentLensSavesLinearDataAndKeepsFrozenReplayAfterDocumentSwitch(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SceneSlantedEdgeWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var fixture = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".optic.json");
            await OpticJsonStore.SaveAsync(SceneSlantedEdgeTests.FiniteConjugateLens(), fixture);
            await app.Documents.OpenAsync(fixture);
            var window = new CameraImagingWindow(app);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-sfr.zip");
            try
            {
                window.Show();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Name == "CameraResults");
                tabs.SelectedItem = ((IEnumerable<object>)tabs.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "camera-scene-edge-tab");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.Equal(8, window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "camera-scene-edge-rings").Value);
                foreach (var (key, value) in new (string, decimal)[] { ("distance", 100), ("cell", 2),
                    ("x", (decimal)(-Math.Sin(5 * Math.PI / 180))), ("y", (decimal)Math.Cos(5 * Math.PI / 180)), ("rings", 2), ("arms", 8) })
                    window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "camera-scene-edge-" + key).Value = value;
                await window.RunSceneEdgeAsync();
                var result = window.SceneEdgeResult!; Assert.NotNull(result); Assert.NotNull(result.Edge); Assert.Null(result.EdgeFailure);
                Assert.Equal(550, result.Request.Bands![0].WavelengthNanometers, 6);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var graph = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "camera-scene-edge-graph");
                var plot = graph.GetVisualDescendants().OfType<AnalysisPlotControl>().Single();
                Assert.Equal(AnalysisCurveWidth.Standard, plot.GetValue(AnalysisPlotControl.CurveWidthProperty));
                var width = window.GetVisualDescendants().OfType<DropDownButton>().Single(c => c.Name == "AnalysisCurveWidth");
                Assert.True(width.IsVisible);
                Assert.IsType<MenuFlyout>(width.Flyout).Items.OfType<MenuItem>().Single(c => c.Header?.ToString() == "粗").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(AnalysisCurveWidth.Thick, plot.GetValue(AnalysisPlotControl.CurveWidthProperty));
                using (var bitmap = window.CaptureSceneEdgeImage()) { Assert.Equal(2404, bitmap.PixelSize.Width); Assert.Equal(1804, bitmap.PixelSize.Height); }
                await app.CameraImaging.ExportSceneSlantedEdgeAsync(result, path);
                await app.Documents.OpenAsync(SlantedEdgeSfrTests.ReferenceLens); Dispatcher.UIThread.RunJobs();
                Assert.Null(window.SceneEdgeResult);
                Assert.True(await window.OpenSceneEdgeFromPathAsync(path));
                await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens); Dispatcher.UIThread.RunJobs();
                Assert.Equal(result.LensSha256, window.SceneEdgeResult!.LensSha256);
                await window.RunSceneEdgeAsync(true);
                Assert.Equal(result.Frame.Bands[0].PixelPowerWatts, window.SceneEdgeResult!.Frame.Bands[0].PixelPowerWatts);
                var evidence = Environment.GetEnvironmentVariable("SCENE_SFR_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var drawing = window.CaptureSceneEdgeImage(); drawing.Save(Path.Combine(evidence, theme.ToLowerInvariant() + "-sfr.png"), PngBitmapEncoderOptions.Default);
                    using var page = window.CaptureRenderedFrame(); page?.Save(Path.Combine(evidence, theme.ToLowerInvariant() + "-page.png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); if (File.Exists(fixture)) File.Delete(fixture); }
        }, CancellationToken.None);
    }
}
