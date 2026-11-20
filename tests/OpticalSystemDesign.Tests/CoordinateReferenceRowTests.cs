using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Services;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CoordinateReferenceRowTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task RealReferenceRowsRetainTheirColourThroughStatesNamesRecyclingAndReopen(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CoordinateReferenceRowTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new());
            DisplayTypography.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, themeName);
            var theme = ThemeRegistry.FromSettings(themeName);
            var colours = CoordinateReferenceRowStyles.ColorsFor(theme);
            using var app = WorkbenchApplication.Create();
            var example = SequentialExampleCatalog.Examples.Single(item =>
                item.RelativePath.Equals("Telescopes/Yolo.zmx", StringComparison.OrdinalIgnoreCase));
            await app.Documents.OpenAsync(example.FilePath);
            var original = app.Prescription.GetSurfaces();
            var reference = original.First(row => row.IsCoordinateReference);
            var ordinary = original.First(row => row.Number > 0 && !row.IsCoordinateReference && !row.IsStop);
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1580, Height = 820, Content = panel };
            var saved = Path.Combine(Path.GetTempPath(), $"coordinate-row-{Guid.NewGuid():N}.staropt");
            try
            {
                window.Show(); Tick(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                SurfaceEditorRow Item(int number) => grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(row => row.Number == number);
                DataGridRow Row(int number) => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(row => row.DataContext is SurfaceEditorRow item && item.Number == number);
                void Away() { window.MouseMove(new Point(1570, 810)); Tick(window); }
                void CheckClasses()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                        if (row.DataContext is SurfaceEditorRow item)
                        {
                            Assert.Equal(item.IsCoordinateReference, row.Classes.Contains(CoordinateReferenceRowStyles.RowClass));
                            Assert.Equal(item.HasOpticalMaterial && !item.IsCoordinateReference, row.Classes.Contains("glass-material-row"));
                        }
                }

                grid.ScrollIntoView(Item(reference.Number), grid.Columns[0]);
                grid.SelectedItem = Item(0); Away();
                Assert.Equal(colours.Normal, Fill(Row(reference.Number)));
                CheckClasses();
                var point = Row(reference.Number).TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseMove(point); Tick(window);
                Assert.Equal(colours.Hover, Fill(Row(reference.Number)));
                grid.SelectedItem = Item(reference.Number); Away();
                Assert.Equal(colours.Selected, Fill(Row(reference.Number)));
                window.MouseMove(point); Tick(window);
                Assert.Equal(colours.SelectedHover, Fill(Row(reference.Number)));
                Row(reference.Number).IsEnabled = false; Tick(window);
                Assert.NotEqual(colours.SelectedHover, Fill(Row(reference.Number)));
                Row(reference.Number).IsEnabled = true; Away();
                grid.SelectedItem = Item(0); Away();
                Capture(window, $"coordinate-reference-{themeName}.png");

                app.Prescription.UpdateSurface(reference with { Label = "Datum A" });
                app.Prescription.UpdateSurface(ordinary with { Label = "Coordinate break reference plane" });
                Tick(window);
                Assert.True(Item(reference.Number).IsCoordinateReference);
                Assert.False(Item(ordinary.Number).IsCoordinateReference);

                foreach (var number in new[] { original[^1].Number, ordinary.Number, reference.Number, 0, reference.Number })
                {
                    grid.ScrollIntoView(Item(number), grid.Columns[0]); Away(); CheckClasses();
                }
                await app.Documents.SaveAsync(saved);
                await app.Documents.OpenAsync(saved); Tick(window);
                Assert.Equal(original.Where(row => row.IsCoordinateReference).Select(row => row.Number),
                    app.Prescription.GetSurfaces().Where(row => row.IsCoordinateReference).Select(row => row.Number));
                Assert.True(Item(reference.Number).ToDto().IsCoordinateReference);
                CheckClasses();
                app.Documents.NewBlank(); Tick(window); CheckClasses();
                Assert.All(grid.ItemsSource!.Cast<SurfaceEditorRow>(), row => Assert.False(row.IsCoordinateReference));
            }
            finally { window.Close(); if (File.Exists(saved)) File.Delete(saved); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public void ReferencePaletteKeepsBodyTextReadableInAllStates(string themeName)
    {
        var theme = ThemeRegistry.FromSettings(themeName);
        var colours = CoordinateReferenceRowStyles.ColorsFor(theme);
        foreach (var background in new[] { colours.Normal, colours.Hover, colours.Selected, colours.SelectedHover })
        {
            var a = Luminance(theme.Palette!.TextPrimary);
            var b = Luminance(background);
            Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5, $"{themeName} / {background}");
        }
    }

    private static double Luminance(Color colour)
    {
        static double Linear(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Linear(colour.R) + .7152 * Linear(colour.G) + .0722 * Linear(colour.B);
    }
    private static Color Fill(DataGridRow row) => Assert.IsAssignableFrom<ISolidColorBrush>(
        row.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle").Fill).Color;
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_COORDINATE_ROWS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
