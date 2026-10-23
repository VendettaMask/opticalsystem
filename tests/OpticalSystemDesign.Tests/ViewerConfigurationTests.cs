using Avalonia;
using Avalonia.Automation;
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
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ViewerConfigurationTests
{
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_VIEWER_CONFIGURATION_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, 0, 2)]
    [InlineData(SceneDimension.TwoDimensional, 1, 0)]
    [InlineData(SceneDimension.TwoDimensional, 2, 1)]
    [InlineData(SceneDimension.ThreeDimensional, 0, 2)]
    [InlineData(SceneDimension.ThreeDimensional, 1, 0)]
    [InlineData(SceneDimension.ThreeDimensional, 2, 1)]
    public async Task SelectedSceneUsesThatConfigurationsCoreGeometryRaysAndSummary(SceneDimension dimension, int active, int selected)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        app.MultiConfiguration.Activate(active);
        var revision = app.Events.Revision;
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(
            File.ReadAllText(ConfigurationMatrixAnalysisTests.ZoomPath()));
        var optic = imported.Configurations[selected];
        AutomaticSemiDiameterSolver.Update(optic);
        var options = new LayoutBuildOptions(
            IncludeAllWavelengths: true, RayCount: 3, LowerPupil: -1, UpperPupil: 1);
        var result = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(
            dimension, IncludeAllWavelengths: true, RayCount: 3,
            LowerPupil: -1, UpperPupil: 1, ConfigurationIndex: selected));
        Assert.Equal(new SceneConfigurationDto(selected + 1, 3), result.Configuration);
        if (dimension == SceneDimension.ThreeDimensional)
        {
            var expected3D = WorkbenchMapper.ToScene3Dto(new Layout2DBuilder(optic).Build3D(options: options));
            var actual3D = Assert.IsType<Scene3Dto>(result.ThreeDimensional);
            Assert.Equal(expected3D.Surfaces.SelectMany(surface => surface.Rim),
                actual3D.Surfaces.SelectMany(surface => surface.Rim));
            Assert.Equal(expected3D.LensElements.SelectMany(element => element.FrontFaces.Concat(element.BackFaces))
                    .SelectMany(face => face.Points),
                actual3D.LensElements.SelectMany(element => element.FrontFaces.Concat(element.BackFaces))
                    .SelectMany(face => face.Points));
            Assert.NotEmpty(actual3D.LensElements);
            Assert.NotEmpty(actual3D.Rays);
            Assert.Equal(expected3D.Rays.Count, actual3D.Rays.Count);
            foreach (var (left, right) in expected3D.Rays.Zip(actual3D.Rays))
            {
                Assert.Equal(left.Points, right.Points);
                Assert.Equal(left.WavelengthIndex, right.WavelengthIndex);
                Assert.Equal(left.Segments, right.Segments);
            }
        }
        else
        {
            var expected = new Layout2DBuilder(optic).Build(options: options);
            var actual = Assert.IsType<Scene2Dto>(result.TwoDimensional);
            Assert.Equal(expected.Surfaces.Count, actual.Surfaces.Count);
            foreach (var (left, right) in expected.Surfaces.Zip(actual.Surfaces))
                Assert.Equal(left.Points.Select(p => (p.Z, p.Y)), right.Points.Select(p => (p.Z, p.Y)));
            Assert.Equal(expected.LensElements.Count, actual.LensElements.Count);
            foreach (var (left, right) in expected.LensElements.Zip(actual.LensElements))
                Assert.Equal(left.Boundary.Select(p => (p.Z, p.Y)), right.Boundary.Select(p => (p.Z, p.Y)));
            Assert.NotEmpty(actual.Rays);
            Assert.Equal(expected.Rays.Count, actual.Rays.Count);
            foreach (var (left, right) in expected.Rays.Zip(actual.Rays))
            {
                Assert.Equal(left.Points.Select(p => (p.Z, p.Y)), right.Points.Select(p => (p.Z, p.Y)));
                Assert.Equal(left.WavelengthIndex, right.WavelengthIndex);
                Assert.Equal(left.Segments.Count, right.Segments.Count);
                Assert.Equal(left.Segments.Select(s => (s.Direction.Z, s.Direction.Y)),
                    right.Segments.Select(s => (s.Direction.Z, s.Direction.Y)));
            }
        }
        Assert.Equal(optic.Paraxial.EstimateEffectiveFocalLength(), result.Summary.EffectiveFocalLength, 12);
        Assert.Equal(optic.Paraxial.EstimateFNumber(), result.Summary.FNumber, 12);
        Assert.Equal(optic.SurfaceGroup.TotalTrack, result.Summary.TotalTrack, 12);
        Assert.Equal(revision, result.SourceRevision);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(active, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        var current = await app.Visualization.BuildSceneAsync(dimension);
        Assert.Equal(active + 1, current.Configuration!.Number);
        Assert.NotEqual(current.Summary.EffectiveFocalLength, result.Summary.EffectiveFocalLength);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task InvalidConfigurationIsRejectedWithoutChangingTheDocument(int selected)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Visualization.GetVisualizationOptions(selected));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.Visualization.BuildSceneAsync(
            new VisualizationRequestDto(SceneDimension.TwoDimensional, ConfigurationIndex: selected)));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
    }

    [Fact]
    public async Task FieldAndWavelengthOptionsComeFromTheSelectedConfiguration()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        app.MultiConfiguration.Activate(1);
        app.Prescription.RemoveField(2);
        app.Prescription.RemoveWavelength(2);
        app.MultiConfiguration.Activate(0);
        var revision = app.Events.Revision;
        var current = app.Visualization.GetVisualizationOptions();
        var selected = app.Visualization.GetVisualizationOptions(1);
        Assert.Equal(3, current.ConfigurationCount);
        Assert.Equal(0, selected.ActiveConfigurationIndex);
        Assert.Equal(3, current.Fields.Count);
        Assert.Equal(2, selected.Fields.Count);
        Assert.Equal(3, current.Wavelengths.Count);
        Assert.Equal(2, selected.Wavelengths.Count);
        Assert.Equal(revision, app.Events.Revision);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout, "Light", "zh-CN", 1000)]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout, "Dark", "en-US", 360)]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout, "Light", "ja-JP", 600)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout, "Light", "zh-CN", 1000)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout, "Dark", "en-US", 360)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout, "Light", "ja-JP", 600)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, "Light", "zh-CN", 1000)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, "Dark", "en-US", 360)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel, "Light", "ja-JP", 600)]
    public async Task ToolbarSelectsOnlyIndividualConfigurationsAndFitsBesideRefresh(
        SceneDimension dimension, ViewerPresentationMode presentationMode, string theme, string language, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerConfigurationTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension, presentationMode);
            var window = new Window { Content = viewer, Width = width, Height = 700 };
            try
            {
                window.Show(); Tick(window);
                await WaitForScene(viewer, window, 1);
                var picker = Picker(viewer);
                Assert.True(picker.IsVisible);
                Assert.Equal(new[] { "1 / 3", "2 / 3", "3 / 3" }, picker.Items.Cast<object>().Select(item => item.ToString()));
                var refresh = viewer.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is LocalIcon { IconName: "refresh-cw" });
                Assert.Same(refresh.Parent, picker.Parent);
                var pickerBounds = InWindow(picker, window);
                var refreshBounds = InWindow(refresh, window);
                Assert.True(pickerBounds.Left >= refreshBounds.Right);
                Assert.InRange(Math.Abs(pickerBounds.Center.Y - refreshBounds.Center.Y), 0, 1);
                Assert.InRange(pickerBounds.Right, 0, width);
                var rayToggle = viewer.GetVisualDescendants().OfType<CheckBox>()
                    .Single(control => control.Name == (dimension == SceneDimension.TwoDimensional ? "Viewer2DShowRays" : "Viewer3DShowRays"));
                var rayBounds = InWindow(rayToggle, window);
                Assert.True(pickerBounds.Right <= rayBounds.Left || pickerBounds.Bottom <= rayBounds.Top);
                Assert.Equal(UiLocalization.Text("结构"), AutomationProperties.GetName(picker));
                Assert.Equal(UiLocalization.Text(dimension == SceneDimension.TwoDimensional
                    ? "选择二维图显示的结构" : "选择三维图显示的结构"), ToolTip.GetTip(picker));
                var revision = app.Events.Revision;
                var first = Scene(viewer)!;
                picker.SelectedIndex = 2;
                Assert.Null(Scene(viewer));
                await WaitForScene(viewer, window, 3);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                if (dimension == SceneDimension.TwoDimensional)
                    Assert.NotEqual(first.TwoDimensional!.Surfaces.SelectMany(surface => surface.Points),
                        Scene(viewer)!.TwoDimensional!.Surfaces.SelectMany(surface => surface.Points));
                else
                    Assert.NotEqual(first.ThreeDimensional!.Surfaces.SelectMany(surface => surface.Rim),
                        Scene(viewer)!.ThreeDimensional!.Surfaces.SelectMany(surface => surface.Rim));
                var captureName = $"viewer-{dimension}-{presentationMode}-configuration-{theme}-{language}-{width}";
                Capture(window, $"{captureName}.png");
                picker.IsDropDownOpen = true; Tick(window);
                Capture(window, $"{captureName}-choices.png");
                picker.IsDropDownOpen = false;
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Equal("结构", AutomationProperties.GetName(picker));
                Assert.Equal(2, picker.SelectedIndex);
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel)]
    public async Task DefaultFollowsActiveButExplicitSelectionSurvivesEditsAndResetsOnFileSwitchEvenWhenLocked(
        SceneDimension dimension, ViewerPresentationMode presentationMode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerConfigurationTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension, presentationMode);
            var window = new Window { Content = viewer, Width = 900, Height = 600 };
            try
            {
                window.Show(); Tick(window);
                Assert.False(Picker(viewer).IsVisible);
                await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath()); Tick(window);
                await WaitForScene(viewer, window, 1);
                app.MultiConfiguration.Activate(1); Tick(window);
                await WaitForScene(viewer, window, 2);
                Assert.Equal(1, Picker(viewer).SelectedIndex);
                Picker(viewer).SelectedIndex = 2;
                await WaitForScene(viewer, window, 3);
                app.MultiConfiguration.Activate(0); Tick(window);
                await WaitForScene(viewer, window, 3);
                Assert.Equal(2, Picker(viewer).SelectedIndex);
                app.MultiConfiguration.Add(); Tick(window);
                Assert.Equal(4, Picker(viewer).ItemCount);
                Picker(viewer).SelectedIndex = 3;
                await WaitForScene(viewer, window, 4);
                Assert.True(app.Documents.Undo()); Tick(window);
                await WaitForScene(viewer, window, 1);
                Assert.Equal(3, Picker(viewer).ItemCount);
                viewer.IsLocked = true;
                app.Documents.NewBlank(); Tick(window);
                Assert.Null(Scene(viewer));
                Assert.False(Picker(viewer).IsVisible);
                viewer.IsLocked = false;
                await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath()); Tick(window);
                await WaitForScene(viewer, window, 1);
                Assert.Equal(0, Picker(viewer).SelectedIndex);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static ComboBox Picker(ViewerPanel viewer) => viewer.GetVisualDescendants().OfType<ComboBox>()
        .Single(control => control.Name is "Viewer2DConfigurationPicker" or "Viewer3DConfigurationPicker");
    private static SceneDto? Scene(ViewerPanel viewer) => viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single().Scene;
    private static Rect InWindow(Control control, Window window) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static async Task WaitForScene(ViewerPanel viewer, Window window, int number)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Tick(window);
            if (Scene(viewer)?.Configuration?.Number == number) return;
            await Task.Delay(20);
        }
        Assert.Fail($"Configuration {number} was not displayed.");
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
