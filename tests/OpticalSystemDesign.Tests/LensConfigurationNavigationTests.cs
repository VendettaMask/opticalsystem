using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LensConfigurationNavigationTests
{
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_CONFIGURATION_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        // Mouse routing through Dock's rounded clip needs real geometry hit
        // testing; the drawing stub does not model the rounded path correctly.
        return AppBuilder.Configure<global::OpticalSystemDesign.App.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    [Fact]
    public async Task ImportedZoomSwitchesThreeRealConfigurationsAndOpensExistingEditor()
    {
        TraceNavigation("starting headless session");
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensConfigurationNavigationTests));
        await session.Dispatch(async () =>
        {
            TraceNavigation("creating application and workspace");
            UiLocalization.Apply(UiLocalization.Chinese);
            using var app = WorkbenchApplication.Create();
            using var panels = new PanelManager(app, new AppSettings(), new WorkspaceSessionStore(
                Path.Combine(Path.GetTempPath(), "configuration-nav-" + Guid.NewGuid().ToString("N"))));
            var window = new Window { Content = panels.WorkspaceControl, Width = 1400, Height = 900 };
            try
            {
                TraceNavigation("showing workspace");
                window.Show(); Tick(window);
                TraceNavigation("opening zoom prescription");
                await app.Documents.OpenAsync(ZoomPath()); Tick(window);
                TraceNavigation("zoom prescription opened");
                Assert.Equal(2, panels.Factory.OpenDocuments().Count);
                var lens = panels.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.LensDocumentId);
                var editor = Assert.IsType<LensEditorPanel>(lens.Context);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                var viewerDocument = panels.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                // An unopened inactive tab has no applied visual template yet.
                // Visit the real viewer before retaining its scene control.
                panels.Factory.SetActiveDockable(viewerDocument); Tick(window);
                TraceNavigation("2D viewer activated");
                var viewer = (Control)viewerDocument.Context!;
                var sceneControl = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                panels.Factory.SetActiveDockable(lens); Tick(window);
                TraceNavigation("lens editor reactivated");
                var sidebar = Assert.IsType<SystemPropertiesPanel>(WorkspaceDockFactory.EnumerateDockables(panels.Layout)
                    .Single(d => d.Id == WorkspaceDockFactory.SystemToolId).Context);
                var aperture = (NumericUpDown)typeof(SystemPropertiesPanel).GetField("_apertureValue",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sidebar)!;
                var expected8 = new[] { 9.48, 4.48, 2.0 };
                var expected15 = new[] { 4.46970613, 21.21, 43.81 };
                var expectedAperture = new[] { 5m, 6.2m, 7.8m };
                var revisions = new List<long>();
                for (var index = 0; index < 3; index++)
                {
                    TraceNavigation($"configuration {index + 1}: checking model and controls");
                    Assert.Equal($"结构 {index + 1}/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                    Assert.Equal(expected8[index], Rows(grid).Single(r => r.Number == 8).Thickness, 8);
                    Assert.Equal(expected15[index], Rows(grid).Single(r => r.Number == 15).Thickness, 8);
                    Assert.Equal(expectedAperture[index], aperture.Value);
                    Assert.Equal(index > 0, Find<Button>(editor, "PreviousLensConfiguration").IsEnabled);
                    Assert.Equal(index < 2, Find<Button>(editor, "NextLensConfiguration").IsEnabled);
                    var scene = await app.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional);
                    TraceNavigation($"configuration {index + 1}: scene built");
                    Assert.Equal(app.Events.Revision, scene.SourceRevision);
                    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
                    while (sceneControl.Scene?.SourceRevision != app.Events.Revision && DateTimeOffset.UtcNow < deadline)
                    {
                        await Task.Delay(25); Tick(window);
                    }
                    Assert.Equal(app.Events.Revision, sceneControl.Scene?.SourceRevision);
                    revisions.Add(scene.SourceRevision);
                    Capture(window, $"zoom-workspace-configuration-{index + 1}.png");
                    TraceNavigation($"configuration {index + 1}: switching configuration");
                    if (index < 2) MouseClick(window, Find<Button>(editor, "NextLensConfiguration"));
                }
                Assert.Equal(3, revisions.Distinct().Count());
                MouseClick(window, Find<Button>(editor, "PreviousLensConfiguration"));
                Assert.Equal("结构 2/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                var revision = app.Events.Revision;
                for (var read = 0; read < 10; read++) Assert.Equal(new MultiConfigurationNavigationDto(3, 1), app.MultiConfiguration.GetNavigationState());
                Assert.Equal(revision, app.Events.Revision);
                panels.Show(WorkspacePanelId.MultiConfiguration); Tick(window);
                TraceNavigation("multi-configuration editor opened");
                var multi = Assert.Single(panels.Factory.OpenDocuments(), d => d.Context is MultiConfigurationPanel);
                Assert.Equal(3, panels.Factory.OpenDocuments().Count);
                panels.Factory.SetActiveDockable(lens); Tick(window);
                panels.Show(WorkspacePanelId.MultiConfiguration); Tick(window);
                Assert.Same(multi, Assert.Single(panels.Factory.OpenDocuments(), d => d.Context is MultiConfigurationPanel));
                Assert.Equal(revision, app.Events.Revision);
                TraceNavigation("workspace assertions completed");
            }
            finally { TraceNavigation("closing workspace"); window.Close(); }
            return true;
        }, CancellationToken.None);
        TraceNavigation("headless dispatch completed");
    }

    private static void TraceNavigation(string stage)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_NAVIGATION_TRACE");
        if (!string.IsNullOrWhiteSpace(path))
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {stage}{Environment.NewLine}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationCommitsValidInputButPreservesInvalidDraft(bool invalid)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensConfigurationNavigationTests));
        await session.Dispatch(async () =>
        {
            UiLocalization.Apply(UiLocalization.Chinese);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ZoomPath());
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService(), app.MultiConfiguration);
            var window = new Window { Content = editor, Width = 1300, Height = 900 };
            try
            {
                window.Show(); Tick(window);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                grid.SelectedItem = Rows(grid).Single(r => r.Number == 8);
                grid.ScrollIntoView(grid.SelectedItem, grid.Columns[4]); Tick(window);
                var cell = grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is SurfaceEditorRow { Number: 8 })
                    .GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ThicknessSolveCell");
                var input = Assert.Single(cell.GetVisualDescendants().OfType<TextBox>());
                input.Focus(); input.Text = invalid ? "not-a-number" : "8.75";
                MouseClick(window, Find<Button>(editor, "NextLensConfiguration"));
                if (invalid)
                {
                    Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                    Assert.Equal("not-a-number", input.Text);
                    Assert.True(Find<TextBlock>(editor, "LensConfigurationMessage").IsVisible);
                    Assert.True(DataValidationErrors.GetHasErrors(input));
                    input.Focus();
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                    MouseClick(window, Find<Button>(editor, "NextLensConfiguration"));
                }
                Assert.Equal(1, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.Equal(4.48, app.Prescription.GetSurfaces()[8].Thickness, 8);
                MouseClick(window, Find<Button>(editor, "PreviousLensConfiguration"));
                Assert.Equal(invalid ? 9.48 : 8.75, app.Prescription.GetSurfaces()[8].Thickness, 8);
                Assert.Equal(8, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.False(Find<TextBlock>(editor, "LensConfigurationMessage").IsVisible);
                if (invalid)
                {
                    // A file switch must clear old configuration-navigation errors.
                    input = grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is SurfaceEditorRow { Number: 8 })
                        .GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ThicknessSolveCell")
                        .GetVisualDescendants().OfType<TextBox>().Single();
                    input.Focus(); input.Text = "not-a-number";
                    MouseClick(window, Find<Button>(editor, "NextLensConfiguration"));
                    Assert.True(Find<TextBlock>(editor, "LensConfigurationMessage").IsVisible);
                    app.Documents.NewBlank(); Tick(window);
                    Assert.False(Find<TextBlock>(editor, "LensConfigurationMessage").IsVisible);
                }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CountsTrackUndoRedoFileResetAndRejectStaleOrDisposedNavigation()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensConfigurationNavigationTests));
        await session.Dispatch(() =>
        {
            UiLocalization.Apply(UiLocalization.Chinese);
            using var app = WorkbenchApplication.Create();
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService(), app.MultiConfiguration);
            var window = new Window { Content = editor, Width = 800, Height = 600 };
            try
            {
                window.Show(); Tick(window);
                Assert.Equal("结构 1/1", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                AssertHeaderCentered(editor, window);
                Capture(window, "configuration-header-single-centered.png");
                Assert.False(Find<Button>(editor, "NextLensConfiguration").IsEnabled);
                Assert.Equal("只有一个结构，无需切换", AutomationProperties.GetHelpText(Find<Button>(editor, "NextLensConfiguration")));
                Assert.True(ToolTip.GetShowOnDisabled(Find<Button>(editor, "NextLensConfiguration")));
                app.MultiConfiguration.Add(); Tick(window);
                Assert.Equal("结构 1/2", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Equal("结构 1/1", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                Assert.True(app.Documents.Redo()); Tick(window);
                Find<Button>(editor, "NextLensConfiguration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                app.Documents.NewBlank(); var revision = app.Events.Revision; Tick(window);
                Assert.Equal(new MultiConfigurationNavigationDto(1, 0), app.MultiConfiguration.GetNavigationState());
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal("结构 1/1", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                app.MultiConfiguration.Add(); Tick(window);
                Find<Button>(editor, "NextLensConfiguration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                revision = app.Events.Revision; editor.Dispose(); Tick(window);
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.Equal(revision, app.Events.Revision);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1000, "结构 1/3")]
    [InlineData("Dark", "en-US", 680, "Configuration 1/3")]
    [InlineData("Pixel", "ja-JP", 360, "構成 1/3")]
    [InlineData("Isekai", "en-US", 600, "Configuration 1/3")]
    public async Task HeaderFitsAndLocalizesLive(string theme, string language, int width, string caption)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensConfigurationNavigationTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ZoomPath());
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService(), app.MultiConfiguration);
            var window = new Window { Content = editor, Width = width, Height = 700 };
            try
            {
                window.Show(); Tick(window);
                var position = Find<TextBlock>(editor, "LensConfigurationPosition");
                var previous = Find<Button>(editor, "PreviousLensConfiguration");
                var next = Find<Button>(editor, "NextLensConfiguration");
                var positionBounds = new Rect(position.TranslatePoint(default, window)!.Value, position.Bounds.Size);
                var previousBounds = new Rect(previous.TranslatePoint(default, window)!.Value, previous.Bounds.Size);
                var nextBounds = new Rect(next.TranslatePoint(default, window)!.Value, next.Bounds.Size);
                Assert.True(previousBounds.Right <= positionBounds.Left && positionBounds.Right <= nextBounds.Left);
                Assert.InRange(Math.Abs(positionBounds.Center.X - (previousBounds.Center.X + nextBounds.Center.X) / 2), 0, 1);
                Assert.DoesNotContain(editor.GetVisualDescendants(), control => control.Name == "OpenLensMultiConfiguration");
                var surfaceItems = Find<StackPanel>(editor, "SurfacePropertiesHeaderItems");
                AssertHeaderCentered(editor, window);
                if (width >= 1000)
                {
                    var surfaceCenter = surfaceItems.TranslatePoint(new Point(0, surfaceItems.Bounds.Height / 2), window)!.Value;
                    var configurationCenter = position.TranslatePoint(new Point(0, position.Bounds.Height / 2), window)!.Value;
                    Assert.InRange(Math.Abs(surfaceCenter.Y - configurationCenter.Y), 0, 1);
                    Assert.True(configurationCenter.X > surfaceCenter.X + surfaceItems.Bounds.Width);
                }
                Assert.Equal(caption, position.Text);
                Assert.Equal("结构 自定义名称", UiLocalization.Text("结构 自定义名称", language));
                Assert.Equal(UiLocalization.Text("上一个结构"), AutomationProperties.GetName(Find<Button>(editor, "PreviousLensConfiguration")));
                Assert.Equal(UiLocalization.Text("已是第一个结构"), ToolTip.GetTip(Find<Button>(editor, "PreviousLensConfiguration")));
                var revision = app.Events.Revision;
                UiLocalization.Apply(UiLocalization.Chinese); Tick(window);
                Assert.Equal("结构 1/3", position.Text);
                AssertHeaderCentered(editor, window);
                UiLocalization.Apply(language); Tick(window);
                Assert.Equal(caption, position.Text);
                AssertHeaderCentered(editor, window);
                Assert.Equal(revision, app.Events.Revision);
                foreach (var resizedWidth in new[] { 1000, 360, width })
                {
                    window.Width = resizedWidth; Tick(window);
                    AssertHeaderCentered(editor, window);
                }
                foreach (var name in new[] { "PreviousLensConfiguration", "NextLensConfiguration" })
                {
                    var control = Find<Button>(editor, name);
                    var bounds = new Rect(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
                    Assert.True(bounds.X >= 0 && bounds.Right <= width && bounds.Y >= 0 && bounds.Bottom < 160, $"{name}: {bounds}");
                }
                Capture(window, $"configuration-header-{theme}-{language}-{width}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ActiveStructureAndSelectionTrackUndoRedoAndProjectRoundTrip()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensConfigurationNavigationTests));
        await session.Dispatch(async () =>
        {
            UiLocalization.Apply(UiLocalization.Chinese);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ZoomPath());
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService(), app.MultiConfiguration);
            var window = new Window { Content = editor, Width = 1000, Height = 700 };
            var path = Path.Combine(Path.GetTempPath(), "configuration-roundtrip-" + Guid.NewGuid().ToString("N") + ".staropt");
            try
            {
                window.Show(); Tick(window);
                var grid = Find<DataGrid>(editor, "LensSurfaceGrid");
                grid.SelectedItem = Rows(grid).Single(r => r.Number == 8);
                MouseClick(window, Find<Button>(editor, "NextLensConfiguration"));
                Assert.Equal("结构 2/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                Assert.True(app.Documents.Undo()); Tick(window);
                Assert.Equal("结构 1/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                Assert.Equal(8, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.True(app.Documents.Redo()); Tick(window);
                Assert.Equal("结构 2/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                await app.Documents.SaveAsync(path);
                app.Documents.NewBlank(); Tick(window);
                await app.Documents.OpenAsync(path); Tick(window);
                Assert.Equal("结构 2/3", Find<TextBlock>(editor, "LensConfigurationPosition").Text);
                Assert.Equal(4.48, Rows(grid).Single(r => r.Number == 8).Thickness, 8);
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
            return true;
        }, CancellationToken.None);
    }

    private static SurfaceEditorRow[] Rows(DataGrid grid) => grid.ItemsSource!.Cast<SurfaceEditorRow>().ToArray();
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void MouseClick(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static string ZoomPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        return Path.Combine(root!.FullName, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Zoom systems/Zoom lens.zmx");
    }
    private static void Capture(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, fileName), PngBitmapEncoderOptions.Default);
        if (window.Content is LensEditorPanel editor)
        {
            var header = Find<Border>(editor, "SurfacePropertiesHeader");
            using var headerBitmap = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(header.Bounds.Width), (int)Math.Ceiling(header.Bounds.Height)), new Vector(96, 96));
            headerBitmap.Render(header);
            headerBitmap.Save(Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + "-header.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static void AssertHeaderCentered(LensEditorPanel editor, Window window)
    {
        static Rect BoundsInWindow(Control control, Window window) =>
            new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
        var header = BoundsInWindow(Find<Border>(editor, "SurfacePropertiesHeader"), window);
        var navigation = BoundsInWindow(Find<StackPanel>(editor, "LensConfigurationToolbar"), window);
        var positionControl = Find<TextBlock>(editor, "LensConfigurationPosition");
        Assert.Equal(Avalonia.Media.FontWeight.Bold, positionControl.FontWeight);
        var position = BoundsInWindow(positionControl, window);
        var surface = BoundsInWindow(Find<StackPanel>(editor, "SurfacePropertiesHeaderItems"), window);
        Assert.InRange(Math.Abs(header.Center.X - navigation.Center.X), 0, 1);
        Assert.InRange(Math.Abs(header.Center.X - position.Center.X), 0, 1);
        Assert.True(surface.Right <= navigation.Left || surface.Bottom <= navigation.Top,
            $"Surface controls {surface} overlap configuration navigation {navigation}");
    }
}
