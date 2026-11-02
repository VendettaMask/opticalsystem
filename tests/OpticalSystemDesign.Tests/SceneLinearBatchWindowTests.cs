using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SceneLinearBatchWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", 0)] [InlineData("Dark", 1)]
    public async Task FullRasterTaskSelectionFrozenRecomputeAndOriginalBudgetFailureAreVisible(string theme, int mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SceneLinearBatchWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); var fixture = await SceneLinearBatchTests.LoadFixture(app);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip"); var replay = path + ".replay.zip";
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show();
                var outer = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "CameraResults");
                outer.SelectedItem = ((IEnumerable<object>)outer.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "camera-scene-edge-tab");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var inner = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "SceneImagingModes");
                inner.SelectedItem = ((IEnumerable<object>)inner.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "scene-linear-batch-tab");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                NumericUpDown Number(string key) => window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "camera-scene-batch-" + key);
                foreach (var (key, value) in new (string, decimal)[] { ("distance", 100), ("cell", 2), ("columns", 4), ("rows", 4),
                    ("x", 0), ("y", 1), ("width", 32), ("height", 40), ("pitch", 10), ("rings", 1), ("arms", 4), ("strips", 7) }) Number(key).Value = value;
                window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "camera-scene-batch-mode").SelectedIndex = mode;
                await window.RunSceneBatchToPathAsync(path);
                var artifact = window.SceneBatchArtifact!; Assert.NotNull(artifact); Assert.NotNull(window.SceneBatchImage);
                Assert.Equal(3, artifact.Manifest.Outputs.Count); Assert.Equal(32 * 40, window.SceneBatchImage!.PowerWatts.Count);
                Assert.Equal(mode == 0 ? 3 : 24, artifact.Manifest.Plan.Work.SpectralNodes);
                var tasks = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "camera-scene-batch-task");
                tasks.SelectedIndex = 2; await window.ShowSceneBatchTaskAsync();
                Assert.Equal(2, window.SceneBatchImage!.TaskIndex);
                if (mode == 0) Assert.All(window.SceneBatchImage.PowerWatts, v => Assert.Equal(0, v));
                await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens); Dispatcher.UIThread.RunJobs(); Assert.Null(window.SceneBatchArtifact);
                Assert.True(await window.OpenSceneBatchFromPathAsync(path));
                await app.Documents.OpenAsync(SlantedEdgeSfrTests.ReferenceLens); Dispatcher.UIThread.RunJobs();
                Assert.Equal(artifact.Manifest.Plan.LensSha256, window.SceneBatchArtifact!.Manifest.Plan.LensSha256);
                await window.RunSceneBatchToPathAsync(replay, true);
                Assert.Equal(artifact.Manifest.Files.Select(f => f.Sha256), window.SceneBatchArtifact!.Manifest.Files.Select(f => f.Sha256));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var evidence = Environment.GetEnvironmentVariable("SCENE_LINEAR_EVIDENCE");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    var scroller = inner.GetVisualDescendants().OfType<ScrollViewer>().First(); scroller.ScrollToEnd();
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(evidence, theme.ToLowerInvariant() + "-full-frame-page.png"), PngBitmapEncoderOptions.Default);
                }
                Number("width").Value = 5000; Number("height").Value = 3000;
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "camera-scene-batch-run").IsEnabled);
                Assert.Equal(5000, Number("width").Value); Assert.Equal(3000, Number("height").Value);
                Assert.Contains("未缩图", window.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "camera-scene-batch-budget").Text);
                return true;
            }
            finally { window.Close(); File.Delete(path); File.Delete(replay); File.Delete(fixture); }
        }, CancellationToken.None);
    }
}
