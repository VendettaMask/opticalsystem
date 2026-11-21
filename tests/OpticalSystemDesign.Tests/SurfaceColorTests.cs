using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfaceColorTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Fact]
    public async Task ColorPersistsThroughHistoryFilesAndScenesWithoutChangingRays()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateSurfaceComponents(1, Update(app.Prescription.GetSurfaces()[1]));
        var before = app.Prescription.GetSurfaces();
        var sceneBefore = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        app.Prescription.UpdateSurfaceComponents(1, Update(before[1]) with { SurfaceColor = " #ab12ef " });
        Assert.Equal("#AB12EF", app.Prescription.GetSurfaces()[1].SurfaceColor);
        Assert.True(app.Documents.Undo());
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.True(app.Documents.Redo());
        var three = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        var two = await app.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional);
        Assert.Equal("#AB12EF", three.ThreeDimensional!.Surfaces.Single(s => s.SurfaceNumber == 1).SurfaceColor);
        Assert.Equal("#AB12EF", two.TwoDimensional!.Surfaces.Single(s => s.SurfaceNumber == 1).SurfaceColor);
        Assert.Null(three.ThreeDimensional.Surfaces.Single(s => s.SurfaceNumber == 2).SurfaceColor);
        Assert.Equal(JsonSerializer.Serialize(sceneBefore.ThreeDimensional!.Rays), JsonSerializer.Serialize(three.ThreeDimensional.Rays));
        var path = Path.Combine(Path.GetTempPath(), $"surface-color-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var reopened = WorkbenchApplication.Create();
            await reopened.Documents.OpenAsync(path);
            Assert.Equal("#AB12EF", reopened.Prescription.GetSurfaces()[1].SurfaceColor);
            var row = reopened.Prescription.GetSurfaces()[1];
            reopened.Prescription.UpdateSurfaceComponents(1, Update(row)); // Omitted color preserves it.
            Assert.Equal("#AB12EF", reopened.Prescription.GetSurfaces()[1].SurfaceColor);
            reopened.Prescription.UpdateSurfaceComponents(1, Update(row) with { SurfaceColor = "" });
            Assert.Null(reopened.Prescription.GetSurfaces()[1].SurfaceColor);
            Assert.True(reopened.Documents.Undo());
            Assert.Equal("#AB12EF", reopened.Prescription.GetSurfaces()[1].SurfaceColor);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        var legacy = Optic.CreateCookeTriplet().ToSnapshot();
        Assert.DoesNotContain("SurfaceColor", JsonSerializer.Serialize(legacy, new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Assert.All(Optic.FromSnapshot(legacy).SurfaceGroup.Items, s => Assert.Null(s.SurfaceColor));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#123")]
    [InlineData("#FF112233")]
    [InlineData("#12GG56")]
    public void InvalidColorDoesNotChangePrescriptionRevisionOrUndoHistory(string color)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var rows = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateSurfaceComponents(1,
            Update(rows[1]) with { SurfaceColor = color, GeometryKind = "平面光栅" }));
        Assert.Equal(rows, app.Prescription.GetSurfaces());
        Assert.Equal(revision, app.Events.Revision);
        Assert.False(app.Documents.Undo());
        var snapshot = Optic.CreateCookeTriplet().ToSnapshot();
        snapshot.Surfaces[1] = snapshot.Surfaces[1] with { SurfaceColor = color };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Theory]
    [InlineData("Light", 1200, 1)]
    [InlineData("Dark", 1200, 1.5)]
    [InlineData("Lumen", 1200, 1)]
    [InlineData("Isekai", 1200, 2)]
    [InlineData("Pixel", 600, 1)]
    public async Task ColorEditorAppliesPresetsCustomDefaultAndRejectsInvalidInput(string theme, int width, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceColorTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = width, Height = 720, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window);
                Click(Find<Button>(panel, "SurfacePropertiesToggle"));
                var grid = Find<DataGrid>(panel, "LensSurfaceGrid");
                grid.SelectedItem = grid.ItemsSource.Cast<SurfaceEditorRow>().Single(row => row.Number == 1);
                Tick(window);
                var picker = Find<ComboBox>(panel, "SurfaceColor");
                var hex = Find<TextBox>(panel, "SurfaceColorHex");
                var apply = Find<Button>(panel, "ApplySurfaceProperties");
                Assert.True(picker.IsEnabled);
                picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "#D62728"));
                Click(apply); Tick(window);
                Assert.Equal("#D62728", app.Prescription.GetSurfaces()[1].SurfaceColor);
                picker.SelectedIndex = picker.ItemCount - 1;
                hex.Text = "#12g456";
                var revision = app.Events.Revision;
                Click(apply); Tick(window);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal("#D62728", app.Prescription.GetSurfaces()[1].SurfaceColor);
                hex.Text = "#11cc88";
                Click(apply); Tick(window);
                Assert.Equal("#11CC88", app.Prescription.GetSurfaces()[1].SurfaceColor);
                Assert.Equal(Color.Parse("#11CC88"), Assert.IsAssignableFrom<ISolidColorBrush>(Find<Border>(panel, "SurfaceColorSwatch").Background).Color);
                CaptureWindow(window, $"editor-{theme}-{scale:0.0}.png");
                grid.SelectedItem = grid.ItemsSource.Cast<SurfaceEditorRow>().Single(row => row.Number == 2);
                Tick(window);
                Assert.Equal("", ((ComboBoxItem)picker.SelectedItem!).Tag);
                grid.SelectedItem = grid.ItemsSource.Cast<SurfaceEditorRow>().Single(row => row.Number == 1);
                Tick(window);
                Assert.Equal("#11CC88", hex.Text);
                picker.SelectedIndex = 0;
                Click(apply); Tick(window);
                Assert.Null(app.Prescription.GetSurfaces()[1].SurfaceColor);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Equal("#11CC88", hex.Text);
                grid.SelectedItem = grid.ItemsSource.Cast<SurfaceEditorRow>().Single(row => row.Number == 0);
                Tick(window);
                picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "#3366CC"));
                Click(apply); Tick(window);
                Assert.Equal("#3366CC", app.Prescription.GetSurfaces()[0].SurfaceColor);
                Assert.Equal("#11CC88", app.Prescription.GetSurfaces()[1].SurfaceColor);
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Transparent, false)]
    [InlineData(true, true, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, true)]
    public async Task SurfaceColorChangesRenderedPixelsAndResetRestoresOriginal(bool threeD, bool solid, OpticSceneRenderMode mode, bool cutaway)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(threeD ? SceneDimension.ThreeDimensional : SceneDimension.TwoDimensional);
        var colored = source with
        {
            TwoDimensional = source.TwoDimensional is { } two ? two with
            { Surfaces = two.Surfaces.Select(s => s.SurfaceNumber == 1 ? s with { SurfaceColor = "#FF0000" } : s).ToArray() } : null,
            ThreeDimensional = source.ThreeDimensional is { } three ? three with
            { Surfaces = three.Surfaces.Select(s => s.SurfaceNumber == 1 ? s with { SurfaceColor = "#FF0000" } : s).ToArray() } : null
        };
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceColorTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var view = new OpticSceneControl
            {
                Scene = source, ShowRays = false, RenderMode = mode, CutawayEnabled = cutaway,
                ViewMode = threeD ? OpticSceneViewMode.ThreeDimensional : OpticSceneViewMode.TwoDimensional,
                VisualStyle = solid ? OpticSceneVisualStyle.SolidModel : OpticSceneVisualStyle.OpticalLayout
            };
            var window = new Window { Width = 1000, Height = 660, Content = view };
            try
            {
                window.Show(); Tick(window);
                var before = Capture(view);
                view.Scene = colored; view.InvalidateVisual(); Tick(window);
                var after = Capture(view, $"render-{threeD}-{solid}-{mode}-{cutaway}.png");
                Assert.NotEqual(before, after);
                view.Scene = source; view.InvalidateVisual(); Tick(window);
                Assert.Equal(before, Capture(view));
                Assert.Null(view.HighlightedSurfaceNumber);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static SurfaceComponentUpdateDto Update(SurfaceRowDto row) => new(row.GeometryKind, row.ApertureKind,
        row.GratingOrder, row.GratingPeriodMicrometers, row.GrooveOrientationAngleDegrees, row.ThinLensFocalLength);
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static string Capture(Control control, string? name = null, double scale = 1)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width * scale),
            (int)Math.Ceiling(control.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        image.Render(control);
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SURFACE_COLOR_CAPTURE_DIR");
        if (directory is not null && name is not null)
        { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), stream.ToArray()); }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void CaptureWindow(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SURFACE_COLOR_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
