using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SceneMultiRoiSfrWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Fact]
    public async Task DragOnLetterboxedRasterKeepsOriginalPixelCoordinatesAfterResize()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SceneMultiRoiSfrWindowTests));
        await session.Dispatch<bool>(() =>
        {
            using var view = new LinearRoiImageView { Height = double.NaN };
            view.SetImage(new(200, 100, new byte[200 * 100 * 3]));
            var window = new Window { Width = 700, Height = 500, Content = view }; SfrRegion? drawn = null;
            view.RegionDrawn += r => drawn = r;
            try
            {
                window.Show(); Tick(window);
                foreach (var size in new[] { new Size(700, 500), new Size(460, 650) })
                {
                    window.Width = size.Width; window.Height = size.Height; Tick(window);
                    var rect = view.ImageRectangle;
                    Point P(double x, double y) => view.TranslatePoint(new(rect.X + x * rect.Width / 200, rect.Y + y * rect.Height / 100), window)!.Value;
                    var a = P(151.4, 79.8); var b = P(20.1, 12.2);
                    window.MouseDown(a, MouseButton.Left); window.MouseMove(b); window.MouseUp(b, MouseButton.Left); Tick(window);
                    Assert.Equal(new SfrRegion(20, 12, 132, 68), drawn);
                    drawn = null;
                    if (rect.Y > 2)
                    {
                        var margin = view.TranslatePoint(new(rect.Center.X, 1), window)!.Value;
                        window.MouseDown(margin, MouseButton.Left); window.MouseMove(P(100, 50)); window.MouseUp(P(100, 50), MouseButton.Left);
                        Assert.Null(drawn);
                    }
                }
                return Task.FromResult(true);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task FullImageRoiManagementBothDirectionsProfilesAndFrozenRecordsRemainUsable(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SceneMultiRoiSfrWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); var fixture = await SceneLinearBatchTests.LoadFixture(app);
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip"); var reportPath = path + ".scene-sfr-rois.json";
            var artifact = await app.CameraImaging.RunSceneLinearBatchAsync(app.CameraImaging.PrepareSceneLinearBatch(SceneMultiRoiSfrTests.CrossRequest()), path);
            var window = new CameraImagingWindow(app) { Width = 1440, Height = 1050 };
            try
            {
                window.Show(); Tick(window); Assert.True(await window.OpenSceneBatchFromPathAsync(path));
                var outer = Find<TabControl>(window, "CameraResults");
                outer.SelectedItem = ((IEnumerable<object>)outer.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "camera-scene-edge-tab"); Tick(window);
                var inner = Find<TabControl>(window, "SceneImagingModes");
                inner.SelectedItem = ((IEnumerable<object>)inner.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "scene-roi-sfr-tab"); Tick(window);
                foreach (var roi in SceneMultiRoiSfrTests.CrossRois())
                {
                    Find<TextBox>(window, "camera-roi-name").Text = roi.Id;
                    Find<ComboBox>(window, "camera-roi-direction").SelectedIndex = (int)roi.Orientation;
                    foreach (var (key, value) in new[] { ("x", roi.Region.X), ("y", roi.Region.Y), ("width", roi.Region.Width), ("height", roi.Region.Height), ("window", roi.HalfWindowPixels) })
                        Find<NumericUpDown>(window, "camera-roi-" + key).Value = value;
                    Find<Button>(window, "camera-roi-add").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                }
                Assert.Equal(4, window.SceneRois.Count); Assert.True(Find<Button>(window, "camera-roi-run").IsEnabled);
                await window.RunSceneRoisAsync(); Tick(window); var report = window.SceneRoiReport!; Assert.NotNull(report);
                Assert.Equal(LinearSfrRoiStatus.Measured, report.Tasks[0].Regions[0].Status);
                Assert.Equal(LinearSfrRoiStatus.Measured, report.Tasks[0].Regions[1].Status);
                Assert.Contains("4 项取得曲线", Find<TextBlock>(window, "camera-roi-info").Text);
                Find<ListBox>(window, "camera-roi-list").SelectedIndex = 1; Tick(window);
                Assert.Contains("近水平", ((IEnumerable<string>)Find<ListBox>(window, "camera-roi-list").ItemsSource!).ElementAt(1));
                Assert.Contains("法线", Find<TextBlock>(window, "camera-roi-detail").Text);
                foreach (var profile in new[] { 0, 1, 2 })
                {
                    Find<ComboBox>(window, "camera-roi-profile").SelectedIndex = profile; Tick(window);
                    Assert.NotNull(Find<ContentControl>(window, "camera-roi-graph").Content);
                    using var plot = window.CaptureSceneRoiImage(); Assert.True(plot.PixelSize.Width > 1000);
                    var evidence = Environment.GetEnvironmentVariable("SCENE_ROI_EVIDENCE");
                    if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); plot.Save(Path.Combine(evidence, $"{theme.ToLowerInvariant()}-profile-{profile}.png"), PngBitmapEncoderOptions.Default); }
                }
                await app.CameraImaging.SaveSceneLinearRoisAsync(report, reportPath);
                // Switching to the all-zero wavelength shows failure details and no stale curve.
                Find<ComboBox>(window, "camera-roi-task").SelectedIndex = 2; await window.ShowSceneBatchTaskAsync(); Tick(window);
                Assert.Null(Find<ContentControl>(window, "camera-roi-graph").Content);
                Assert.Contains("没有入射光", Find<TextBlock>(window, "camera-roi-detail").Text);
                Assert.All(((IEnumerable<string>)Find<ListBox>(window, "camera-roi-list").ItemsSource!).Take(3), s => Assert.Contains("失败", s));
                // Edit invalidates the report and its plot, while keeping the authoritative source image.
                Find<Button>(window, "camera-roi-delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Null(window.SceneRoiReport); Assert.Equal(3, window.SceneRois.Count); Assert.NotNull(window.SceneBatchImage);
                Assert.True(await window.OpenSceneRoisAsync(reportPath)); Assert.Equal(4, window.SceneRois.Count); Assert.NotNull(window.SceneRoiReport);
                await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens); Tick(window);
                Assert.NotNull(window.SceneRoiReport); Assert.Equal(artifact.Manifest.Plan.LensSha256, window.SceneRoiReport!.LensSha256);
                Assert.True(await window.OpenSceneBatchFromPathAsync(path)); Assert.Null(window.SceneRoiReport); Assert.Empty(window.SceneRois);
                return true;
            }
            finally { window.Close(); File.Delete(path); File.Delete(reportPath); File.Delete(fixture); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
