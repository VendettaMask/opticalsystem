using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ViewerImageExportTests
{
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR";
    private static bool RenderImages => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (RenderImages) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !RenderImages });
    }

    [ViewerImageTheory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout, 840, 620, false)]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout, 420, 420, false)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout, 840, 620, false)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout, 420, 420, false)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, 840, 720, false)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, 420, 620, true)]
    public async Task ExportRetainsTheViewportAndMetadataButExcludesControlsAndFocus(
        SceneDimension dimension, ViewerPresentationMode mode, int width, int height, bool cutaway)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var fixture = await Fixture.Create(dimension, mode, width, height);
            var scene = fixture.Scene;
            if (dimension == SceneDimension.ThreeDimensional)
                scene.SetViewPreset(width >= 840 ? OpticSceneViewPreset.Isometric : OpticSceneViewPreset.Right);
            if (cutaway)
            {
                fixture.Viewer.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "Viewer3DRenderMode").SelectedIndex = 1;
                fixture.Viewer.GetVisualDescendants().OfType<CheckBox>().Single(item => item.Name == "Viewer3DCutaway").IsChecked = true;
            }
            var pointer = scene.TranslatePoint(new Point(scene.Bounds.Width / 2, scene.Bounds.Height / 2), fixture.Window)!.Value;
            fixture.Window.MouseDown(pointer, MouseButton.Left, RawInputModifiers.Shift);
            fixture.Window.MouseMove(pointer + new Vector(17, 9), RawInputModifiers.Shift);
            fixture.Window.MouseUp(pointer + new Vector(17, 9), MouseButton.Left, RawInputModifiers.Shift);
            fixture.Window.MouseWheel(pointer, new Vector(0, 1)); Tick(fixture.Window);
            var originalScene = scene.Scene;
            var axes = scene.OrientationAxesForRendering();
            using var image = fixture.Viewer.CaptureViewerImage();
            var expected = Pixels(image);
            Assert.Equal((int)Math.Ceiling(scene.Bounds.Width * fixture.Window.RenderScaling), image.PixelSize.Width);
            Assert.True(image.PixelSize.Height > scene.Bounds.Height * fixture.Window.RenderScaling);
            AssertHasInk(image, image.PixelSize.Height - 30, image.PixelSize.Height);
            scene.Focus(); Tick(fixture.Window);
            using (var focused = fixture.Viewer.CaptureViewerImage()) Assert.Equal(expected, Pixels(focused));
            var settings = fixture.Viewer.GetVisualDescendants().OfType<Button>().Single(item => item.Content is LocalIconLabel);
            fixture.Click(settings);
            using (var withSettings = fixture.Viewer.CaptureViewerImage()) Assert.Equal(expected, Pixels(withSettings));
            foreach (var button in fixture.Viewer.GetVisualDescendants().OfType<Button>()) button.Opacity = 0;
            Tick(fixture.Window);
            using (var noButtons = fixture.Viewer.CaptureViewerImage()) Assert.Equal(expected, Pixels(noButtons));
            foreach (var button in fixture.Viewer.GetVisualDescendants().OfType<Button>()) button.Opacity = 1;
            fixture.Click(settings);
            scene.ShowRays = false; Tick(fixture.Window);
            using (var noRays = fixture.Viewer.CaptureViewerImage()) Assert.NotEqual(expected, Pixels(noRays));
            scene.ShowRays = true; Tick(fixture.Window);
            Assert.Same(originalScene, scene.Scene);
            Assert.Equal(axes, scene.OrientationAxesForRendering());
            Assert.Equal(originalScene!.SourceRevision, fixture.App.Events.Revision);
            Save(image, $"{dimension}-{mode}-{width}x{height}.png");
        }, CancellationToken.None);
    }

    [ViewerImageTheory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel)]
    public async Task ImageButtonSavesPngAndCancellationPreservesTheExistingFile(SceneDimension dimension, ViewerPresentationMode mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(dimension, mode);
            var path = Path.Combine(Path.GetTempPath(), $"viewer-image-{Guid.NewGuid():N}.png");
            try
            {
                var cancel = false;
                FilePickerSaveOptions? options = null;
                SetPicker(fixture.Window, value => { options = value; return Task.FromResult(cancel ? null : StorageFile(path)); });
                File.WriteAllBytes(path, [1, 2, 3]);
                fixture.Click(fixture.Export);
                var saved = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, saved[..8]);
                Assert.NotNull(options); Assert.True(options.ShowOverwritePrompt);
                Assert.Equal("png", options.DefaultExtension); Assert.EndsWith(".png", options.SuggestedFileName);
                Assert.Equal("*.png", Assert.Single(Assert.Single(options.FileTypeChoices!).Patterns!));
                using var decoded = new Bitmap(path);
                AssertHasInk(decoded, 0, decoded.PixelSize.Height);
                cancel = true; fixture.Click(fixture.Export);
                Assert.Equal(saved, File.ReadAllBytes(path)); Assert.True(fixture.Export.IsEnabled);
                var previous = UiLocalization.CurrentLanguage;
                try
                {
                    foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
                    {
                        UiLocalization.Apply(language); Tick(fixture.Window);
                        Assert.Equal(UiLocalization.Text("导出图片（PNG）"), AutomationProperties.GetName(fixture.Export));
                        Assert.Equal(UiLocalization.Text("导出图片（PNG）"), ToolTip.GetTip(fixture.Export));
                    }
                }
                finally { UiLocalization.Apply(previous); }
            }
            finally { File.Delete(path); }
        }, CancellationToken.None);
    }

    [ViewerImageFact]
    public async Task PendingSaveUsesTheClickedSnapshotAfterALockedFileSwitch()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel);
            using var original = fixture.Viewer.CaptureViewerImage();
            var expected = Pixels(original);
            var pending = new TaskCompletionSource<IStorageFile?>();
            SetPicker(fixture.Window, _ => pending.Task);
            var path = Path.Combine(Path.GetTempPath(), $"viewer-snapshot-{Guid.NewGuid():N}.png");
            try
            {
                fixture.Click(fixture.Export); Assert.False(fixture.Export.IsEnabled);
                fixture.Export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(fixture.Export.IsEnabled); // Ignoring re-entry must not clear the first export's busy state.
                fixture.Viewer.IsLocked = true;
                fixture.App.Documents.NewBlank(); Tick(fixture.Window);
                Assert.Null(fixture.Scene.Scene);
                pending.SetResult(StorageFile(path));
                for (var i = 0; i < 100 && !File.Exists(path); i++) { Tick(fixture.Window); await Task.Delay(20); }
                using var saved = new Bitmap(path);
                Assert.Equal(expected, Pixels(saved)); Assert.False(fixture.Export.IsEnabled);
            }
            finally { File.Delete(path); }
        }, CancellationToken.None);
    }

    [ViewerImageFact]
    public async Task ExportIncludesTheCompleteMaterialLegendBeyondTheVisibleScrollViewport()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, 420, 620);
            var original = fixture.Scene.Scene!;
            var model = original.ThreeDimensional!;
            fixture.Scene.Scene = original with
            {
                ThreeDimensional = model with
                {
                    LensElements = Enumerable.Range(0, 24).Select(index => model.LensElements[0] with { Material = $"GLASS-{index:00}-LONG-MATERIAL" }).ToArray()
                }
            };
            typeof(ViewerPanel).GetMethod("UpdateGlassLegend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Viewer, null);
            fixture.Scene.InvalidateVisual(); Tick(fixture.Window);
            using var image = fixture.Viewer.CaptureViewerImage();
            Save(image, "solid-complete-24-material-legend.png");
            Assert.True(image.PixelSize.Height > fixture.Window.Height);
            var color = fixture.Scene.GlassMaterials[^1].Color;
            using var png = new MemoryStream();
            image.Save(png, PngBitmapEncoderOptions.Default);
            using var decoded = SkiaSharp.SKBitmap.Decode(png.ToArray());
            var start = (int)Math.Ceiling(fixture.Scene.Bounds.Height) + 96;
            var found = Enumerable.Range(start * image.PixelSize.Width, (image.PixelSize.Height - start) * image.PixelSize.Width)
                .Count(index => decoded.GetPixel(index % image.PixelSize.Width, index / image.PixelSize.Width)
                    == new SkiaSharp.SKColor(color.R, color.G, color.B, color.A));
            Assert.True(found > 4, "The last legend color must be rendered below the on-screen legend viewport.");
        }, CancellationToken.None);
    }

    [ViewerImageFact]
    public async Task SaveFailureShowsFeedbackAndKeepsSceneMetadata()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout);
            var directory = Path.Combine(Path.GetTempPath(), $"viewer-save-directory-{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(directory);
            try
            {
                var scene = fixture.Scene.Scene;
                SetPicker(fixture.Window, _ => Task.FromResult<IStorageFile?>(StorageFile(directory)));
                fixture.Click(fixture.Export);
                var feedback = fixture.Viewer.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "ViewerImageExportFeedback");
                Assert.True(feedback.IsVisible); Assert.False(string.IsNullOrWhiteSpace(feedback.Text));
                Assert.True(Directory.Exists(directory)); Assert.Empty(Directory.GetFiles(directory));
                Assert.Same(scene, fixture.Scene.Scene); Assert.True(fixture.Export.IsEnabled);
            }
            finally { Directory.Delete(directory); }
        }, CancellationToken.None);
    }

    [ViewerImageTheory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel)]
    public async Task DoubleScaleExportRetainsLogicalLayoutAndDoublesPixelDimensions(SceneDimension dimension, ViewerPresentationMode mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(dimension, mode);
            using var normal = fixture.Viewer.CaptureViewerImage(1);
            using var doubled = fixture.Viewer.CaptureViewerImage(2);
            Assert.Equal(normal.PixelSize.Width * 2, doubled.PixelSize.Width);
            Assert.Equal(normal.PixelSize.Height * 2, doubled.PixelSize.Height);
            AssertHasInk(doubled, doubled.PixelSize.Height - 60, doubled.PixelSize.Height);
            Save(doubled, $"{dimension}-{mode}-2x.png");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel)]
    public async Task EmptyRefreshingAndOutdatedLockedViewsCannotExport(SceneDimension dimension, ViewerPresentationMode mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerImageExportTests));
        await session.Dispatch(async () =>
        {
            using var fixture = await Fixture.Create(dimension, mode);
            Assert.True(fixture.Export.IsEnabled);
            fixture.Viewer.IsLocked = true;
            var previousScene = fixture.Scene.Scene;
            fixture.App.MultiConfiguration.Activate(1); Tick(fixture.Window);
            Assert.Same(previousScene, fixture.Scene.Scene);
            Assert.False(fixture.Export.IsEnabled); Assert.True(ToolTip.GetShowOnDisabled(fixture.Export));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(fixture.Export)));
            Assert.Throws<InvalidOperationException>(() => fixture.Viewer.CaptureViewerImage());
            fixture.Viewer.IsLocked = false;
            Assert.False(fixture.Export.IsEnabled);
            await fixture.WaitReady();
            Assert.Equal(UiLocalization.Text("导出图片（PNG）"), ToolTip.GetTip(fixture.Export));
            fixture.Viewer.IsLocked = true;
            fixture.App.Documents.NewBlank(); Tick(fixture.Window);
            Assert.Null(fixture.Scene.Scene); Assert.False(fixture.Export.IsEnabled);
            Assert.Throws<InvalidOperationException>(() => fixture.Viewer.CaptureViewerImage());
            Assert.True(Assert.IsAssignableFrom<Control>(fixture.Export.Parent).IsEnabled);
        }, CancellationToken.None);
    }

    private sealed class Fixture : IDisposable
    {
        public required WorkbenchApplication App { get; init; }
        public required ViewerPanel Viewer { get; init; }
        public required Window Window { get; init; }
        public OpticSceneControl Scene => Viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
        public Button Export => Viewer.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportViewerImage");
        public static async Task<Fixture> Create(SceneDimension dimension, ViewerPresentationMode mode, int width = 840, int height = 620)
        {
            var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension, mode);
            var fixture = new Fixture { App = app, Viewer = viewer, Window = new Window { Width = width, Height = height, Content = viewer } };
            try { fixture.Window.Show(); await fixture.WaitReady(); return fixture; }
            catch { fixture.Dispose(); throw; }
        }
        public async Task WaitReady()
        {
            for (var i = 0; i < 200 && !Export.IsEnabled; i++) { Tick(Window); await Task.Delay(20); }
            Tick(Window); Assert.NotNull(Scene.Scene); Assert.True(Export.IsEnabled);
        }
        public void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(point); Window.MouseDown(point, MouseButton.Left); Window.MouseUp(point, MouseButton.Left); Tick(Window);
        }
        public void Dispose() { Window.Close(); Viewer.Dispose(); App.Dispose(); }
    }

    private static IStorageFile StorageFile(string path)
    {
        var file = DispatchProxy.Create<IStorageFile, AnalysisImageExportTests.StorageProxy>();
        ((AnalysisImageExportTests.StorageProxy)(object)file).Handler = method => method.Name == "get_Path" ? new Uri(path) : null;
        return file;
    }
    private static void SetPicker(Window window, Func<FilePickerSaveOptions, Task<IStorageFile?>> save)
    {
        FilePickerSaveOptions options = null!;
        var provider = DispatchProxy.Create<IStorageProvider, AnalysisImageExportTests.StorageProxy>();
        var proxy = (AnalysisImageExportTests.StorageProxy)(object)provider;
        proxy.Arguments = args => options = Assert.IsType<FilePickerSaveOptions>(Assert.Single(args!));
        proxy.Handler = method => method.Name switch
        {
            "get_CanSave" => true,
            "SaveFilePickerAsync" => save(options),
            _ => throw new InvalidOperationException(method.Name)
        };
        typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, provider);
    }
    private static byte[] Pixels(Bitmap image)
    {
        var stride = image.PixelSize.Width * 4;
        var bytes = new byte[stride * image.PixelSize.Height];
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try { image.CopyPixels(new PixelRect(image.PixelSize), pointer, bytes.Length, stride); Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    private static void AssertHasInk(Bitmap image, int firstRow, int lastRow)
    {
        var pixels = Pixels(image);
        Assert.True(Enumerable.Range(firstRow * image.PixelSize.Width, (lastRow - firstRow) * image.PixelSize.Width)
            .Count(index => pixels[index * 4 + 3] > 0 && (pixels[index * 4] < 220 || pixels[index * 4 + 1] < 220 || pixels[index * 4 + 2] < 220)) > 40);
    }
    private static void Save(RenderTargetBitmap image, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory)!;
        Directory.CreateDirectory(directory);
        ViewerPanel.SaveViewerImage(image, Path.Combine(directory, name));
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    public sealed class ViewerImageTheoryAttribute : TheoryAttribute
    {
        public ViewerImageTheoryAttribute() { if (!RenderImages) Skip = "Run ViewerImageExportTests in isolation with OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR for real Skia PNG checks."; }
    }
    public sealed class ViewerImageFactAttribute : FactAttribute
    {
        public ViewerImageFactAttribute() { if (!RenderImages) Skip = "Run ViewerImageExportTests in isolation with OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR for real Skia PNG checks."; }
    }
}
