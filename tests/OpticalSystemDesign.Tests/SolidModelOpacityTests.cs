using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
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
public sealed class SolidModelOpacityTests
{
    private static string Source => Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
        "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Binocular_System.ZMX");
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_SOLID_OPACITY_CAPTURE_DIR";
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task SolidOccludesRearGeometryWhileTransparentModeAndExplicitOpacityRevealIt(
        bool polygon, bool cutaway, bool highlighted)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SolidModelOpacityTests));
        var completed = false;
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Source);
            var template = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 1));
            var control = new OpticSceneControl
            {
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = OpticSceneVisualStyle.SolidModel,
                ShowRays = false,
                ShowScaleBar = false,
                CutawayEnabled = cutaway,
                HighlightedSurfaceNumber = highlighted ? 101 : null
            };
            control.SetViewPreset(OpticSceneViewPreset.Front);
            var window = new Window { Content = control, Width = 700, Height = 500 };
            try
            {
                window.Show();
                byte[] Paint(string rear, double opacity = 1)
                {
                    control.Scene = TwoLayers(template, rear, polygon, cutaway, opacity);
                    Tick(window);
                    return Pixels(window);
                }
                control.RenderMode = OpticSceneRenderMode.Solid;
                var opaque = Paint("#F010D0");
                Assert.Equal(0, CenterDifference(opaque, Paint("#F0E010")));
                control.RenderMode = OpticSceneRenderMode.Transparent;
                Assert.True(CenterDifference(Paint("#F010D0"), Paint("#F0E010")) > 100,
                    "Transparent mode must reveal rear geometry through the front surface.");
                control.RenderMode = OpticSceneRenderMode.Solid;
                Assert.Equal(opaque, Paint("#F010D0"));
                Assert.True(CenterDifference(Paint("#F010D0", .4), Paint("#F0E010", .4)) > 100,
                    "An explicit surface drawing opacity must remain effective.");
                control.VisualStyle = OpticSceneVisualStyle.OpticalLayout;
                Assert.True(CenterDifference(Paint("#F010D0"), Paint("#F0E010")) > 100,
                    "The separate 3D Layout must retain its prior material rendering.");
            }
            finally { window.Close(); }
            completed = true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task NativeSolidModeSelectorDefaultsToOpaqueAndRoundTripsWithoutRetracing()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SolidModelOpacityTests));
        var completed = false;
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Source);
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(),
                SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel);
            var window = new Window { Content = viewer, Width = 1200, Height = 760 };
            try
            {
                window.Show();
                var scene = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                for (var i = 0; i < 200 && scene.Scene is null; i++) { Tick(window); await Task.Delay(20); }
                Tick(window); Assert.NotNull(scene.Scene);
                var picker = viewer.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "Viewer3DRenderMode");
                var snapshot = scene.Scene;
                var revision = app.Events.Revision;
                Assert.Equal(0, picker.SelectedIndex); Assert.Equal(OpticSceneRenderMode.Solid, scene.RenderMode);
                Capture(window, "solid-default.png");
                var opaque = Pixels(window);
                picker.SelectedIndex = 1; Tick(window);
                Assert.Equal(OpticSceneRenderMode.Transparent, scene.RenderMode);
                Capture(window, "transparent-selected.png");
                Assert.True(opaque.Zip(Pixels(window)).Count(pair => pair.First != pair.Second) > 1000);
                picker.SelectedIndex = 2; Tick(window);
                Assert.Equal(OpticSceneRenderMode.Wireframe, scene.RenderMode);
                picker.SelectedIndex = 0; Tick(window);
                Assert.Equal(OpticSceneRenderMode.Solid, scene.RenderMode);
                Assert.Same(snapshot, scene.Scene); Assert.Equal(revision, app.Events.Revision);
                scene.ShowRays = false; scene.CutawayEnabled = true; Tick(window);
                Capture(window, "solid-cutaway.png");
            }
            finally { window.Close(); }
            completed = true;
        }, CancellationToken.None);
        Assert.True(completed);
    }

    private static SceneDto TwoLayers(SceneDto template, string rearColor, bool polygon, bool cutaway, double frontOpacity)
    {
        SceneSurface3Dto Surface(int number, double x, double radius, string color, double opacity = 1)
        {
            ScenePoint3Dto[] points = [new(x, -radius, -radius), new(x, radius, -radius), new(x, radius, radius), new(x, -radius, radius)];
            return new(number, "occlusion control", false, false, "N-BK7", points.Append(points[0]).ToArray(), [], [],
                [new(points)], SurfaceColor: color, Opacity: opacity, IsPolygonGroup: polygon);
        }
        var frontX = cutaway ? -1 : 3;
        var back = Surface(100, frontX - 2, 2, rearColor);
        var front = Surface(101, frontX, 5, "#50B090", frontOpacity);
        SceneLensElement3Dto Element(SceneSurface3Dto surface) => new(surface.SurfaceNumber, surface.SurfaceNumber,
            surface.Material, 1.5, [], [], surface.Faces, [], []);
        return template with { ThreeDimensional = new([back, front], [Element(back), Element(front)], [], 6, 6, -6, 6) };
    }

    private static void Tick(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static int CenterDifference(byte[] first, byte[] second)
    {
        var changed = 0;
        for (var y = 230; y < 270; y++) for (var x = 330; x < 370; x++)
            for (var channel = 0; channel < 3; channel++)
                if (first[(y * 700 + x) * 4 + channel] != second[(y * 700 + x) * 4 + channel]) changed++;
        return changed;
    }
    private static byte[] Pixels(Window window)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), memory, bytes.Length, bitmap.PixelSize.Width * 4); Marshal.Copy(memory, bytes, 0, bytes.Length); }
        finally { Marshal.FreeHGlobal(memory); }
        return bytes;
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
