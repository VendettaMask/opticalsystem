using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CoordinateBreakPanelTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light", "zh-CN", 1)]
    [InlineData("Dark", "en-US", 1)]
    [InlineData("Lumen", "ja-JP", 1)]
    [InlineData("Isekai", "en-US", 2)]
    [InlineData("Pixel", "zh-CN", 1.5)]
    public async Task ActualEditorInsertsEditsAndReturnsCoordinatesInEveryTheme(string theme, string language, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CoordinateBreakPanelTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1450, Height = 1050, Content = panel };
            try
            {
                window.Show(); Render(window);
                Click(Find<Button>(panel, "SurfacePropertiesToggle")); Render(window);
                var navigation = Find<ListBox>(panel, "SurfacePropertyNavigation");
                navigation.SelectedIndex = 4; Render(window);
                Assert.False(Find<Button>(panel, "ApplySurfaceProperties").IsEnabled);
                var initialCount = app.Prescription.GetSurfaces().Count;
                Click(Find<Button>(panel, "InsertCoordinateBefore")); Render(window);
                Assert.Equal(initialCount + 1, app.Prescription.GetSurfaces().Count);
                Assert.True(Assert.IsType<SurfaceEditorRow>(Find<DataGrid>(panel, "LensSurfaceGrid").SelectedItem).IsCoordinateReference);
                Find<TextBox>(panel, "CoordinateValue0").Text = "1.25";
                Find<TextBox>(panel, "CoordinateValue3").Text = "4";
                Find<CheckBox>(panel, "CoordinateVariable0").IsChecked = true;
                Find<ComboBox>(panel, "CoordinateOrder").SelectedIndex = 1;
                Click(Find<Button>(panel, "ApplySurfaceProperties")); Render(window);
                var edit = app.Prescription.GetCoordinateBreak(1);
                Assert.Equal(1.25, edit.Settings.X); Assert.Equal(4, edit.Settings.TiltY);
                Assert.True(edit.Settings.TiltFirst); Assert.True(edit.Settings.Variables![0]);
                Assert.True(app.Documents.Undo()); Render(window);
                Assert.Equal(0, app.Prescription.GetCoordinateBreak(1).Settings.X);
                Assert.True(app.Documents.Redo()); Render(window);
                Find<ComboBox>(panel, "CoordinateReturn").SelectedIndex = 1;
                Find<ComboBox>(panel, "CoordinateReference").SelectedIndex = 0;
                Click(Find<Button>(panel, "ApplySurfaceProperties")); Render(window);
                Assert.Equal(CoordinateReturnKind.Orientation, app.Prescription.GetCoordinateBreak(1).Settings.ReturnKind);
                var value = Find<TextBox>(panel, "CoordinateValue0");
                var revision = app.Events.Revision;
                value.Text = "invalid"; Click(Find<Button>(panel, "ApplySurfaceProperties")); Render(window);
                Assert.Equal(revision, app.Events.Revision); Assert.Equal("invalid", value.Text);
                Click(Find<Button>(panel, "RevertSurfaceProperties")); Render(window);
                Assert.Equal("1.25", value.Text);
                var visibleScroll = Find<ScrollViewer>(panel, "SurfacePropertyScroll");
                var xInput = Find<TextBox>(panel, "CoordinateValue0");
                var tiltInput = Find<TextBox>(panel, "CoordinateValue2");
                Assert.True(tiltInput.TranslatePoint(default, visibleScroll)!.Value.X > xInput.TranslatePoint(default, visibleScroll)!.Value.X + xInput.Bounds.Width);
                Assert.Equal(300, Find<Grid>(panel, "SurfacePropertiesBody").Bounds.Height);
                var returnSelector = Find<ComboBox>(panel, "CoordinateReturn");
                var returnPosition = returnSelector.TranslatePoint(default, visibleScroll)!.Value;
                Assert.InRange(returnPosition.Y, 0, visibleScroll.Bounds.Height - returnSelector.Bounds.Height);
                var lastInput = Find<TextBox>(panel, "CoordinateValue4");
                Assert.InRange(lastInput.TranslatePoint(default, visibleScroll)!.Value.Y, 0, visibleScroll.Bounds.Height - lastInput.Bounds.Height);
                var wideInsertion = Find<Button>(panel, "InsertCoordinateAfter");
                Assert.InRange(wideInsertion.TranslatePoint(default, visibleScroll)!.Value.Y, 0, visibleScroll.Bounds.Height - wideInsertion.Bounds.Height);
                var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_COORDINATE_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    Capture(window, Path.Combine(capture, $"{theme}-{language}-{scale:0.0}.png"), scale);
                    if (theme == "Light") Capture(Find<Grid>(panel, "SurfacePropertiesBody"), Path.Combine(capture, "Light-coordinate-properties.png"), 1);
                }
                // At a narrow pane every command remains reachable through the native vertical scroll.
                window.Width = 800; Render(window);
                var scroller = Find<ScrollViewer>(panel, "SurfacePropertyScroll");
                Assert.True(scroller.Bounds.Width > 350);
                Assert.True(Find<Button>(panel, "ApplySurfaceProperties").Bounds.Width >= 60);
                returnSelector.BringIntoView(); Render(window);
                var narrowReturn = returnSelector.TranslatePoint(default, scroller)!.Value;
                Assert.InRange(narrowReturn.X, 0, scroller.Bounds.Width - returnSelector.Bounds.Width);
                Assert.InRange(narrowReturn.Y, 0, scroller.Bounds.Height - returnSelector.Bounds.Height);
                var insertion = Find<Button>(panel, "InsertCoordinateAfter");
                scroller.ScrollToEnd(); Render(window);
                if (!string.IsNullOrWhiteSpace(capture))
                    Capture(window, Path.Combine(capture, $"{theme}-{language}-{scale:0.0}-narrow.png"), scale);
                Assert.InRange(insertion.TranslatePoint(default, scroller)!.Value.Y, 0, scroller.Bounds.Height - insertion.Bounds.Height);
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
            await Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CentimeterInputsUsePhysicalMillimetersAndFileSwitchDropsTheOldDraft()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CoordinateBreakPanelTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create();
            var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zmx");
            var project = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
            try
            {
                await File.WriteAllTextAsync(source, "MODE SEQ\nUNIT CM\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE COORDBRK\nPARM 1 1\nDISZ 1\nSURF 2\n");
                await app.Documents.OpenAsync(source);
                app.Prescription.ScaleLens(new(1, 0, 2, LensLengthUnit.Centimeter), app.Events.Revision);
                await app.Documents.SaveAsync(project);
                using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
                var window = new Window { Width = 1200, Height = 900, Content = panel };
                try
                {
                    window.Show(); Render(window); Click(Find<Button>(panel, "SurfacePropertiesToggle"));
                    Find<ListBox>(panel, "SurfacePropertyNavigation").SelectedIndex = 4; Render(window);
                    var value = Find<TextBox>(panel, "CoordinateValue0"); Assert.Equal("1", value.Text);
                    value.Text = "2"; Click(Find<Button>(panel, "ApplySurfaceProperties")); Render(window);
                    Assert.Equal(20, app.Prescription.GetCoordinateBreak(1).Settings.X);
                    value.Text = "draft";
                    await app.Documents.OpenAsync(project); Render(window);
                    Assert.Equal("1", value.Text); Assert.Equal(10, app.Prescription.GetCoordinateBreak(1).Settings.X);
                }
                finally { window.Close(); }
            }
            finally { File.Delete(source); File.Delete(project); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static void Capture(Control window, string path, double scale)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
