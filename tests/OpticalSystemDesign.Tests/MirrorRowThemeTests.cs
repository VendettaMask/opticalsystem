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
public sealed class MirrorRowThemeTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task MirrorsKeepTealThroughStatesMaterialEditsRecyclingThemeSwitchesAndReopen(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MirrorRowThemeTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, themeName);
            var colours = MirrorRowStyles.ColorsFor(ThemeRegistry.FromSettings(themeName));
            using var app = WorkbenchApplication.Create();
            var example = SequentialExampleCatalog.Examples.Single(item =>
                item.RelativePath.Equals("Telescopes/Yolo.zmx", StringComparison.OrdinalIgnoreCase));
            await app.Documents.OpenAsync(example.FilePath);
            var mirror = app.Prescription.GetSurfaces().First(row => row.IsReflective);
            var reference = app.Prescription.GetSurfaces().First(row => row.IsCoordinateReference);
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1580, Height = 650, Content = panel };
            var saved = Path.Combine(Path.GetTempPath(), $"mirror-row-{Guid.NewGuid():N}.staropt");
            try
            {
                window.Show(); Tick(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                SurfaceEditorRow Item(int number) => grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(row => row.Number == number);
                DataGridRow Row(int number) => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(row => row.DataContext is SurfaceEditorRow item && item.Number == number);
                void Away() { window.MouseMove(new Point(window.Width - 10, window.Height - 10)); Tick(window); }
                void CheckClasses()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                        if (row.DataContext is SurfaceEditorRow item)
                        {
                            Assert.Equal(item.IsReflective && !item.IsCoordinateReference, row.Classes.Contains(MirrorRowStyles.RowClass));
                            Assert.Equal(item.HasOpticalMaterial && !item.IsReflective && !item.IsCoordinateReference,
                                row.Classes.Contains("glass-material-row"));
                        }
                }

                grid.ScrollIntoView(Item(mirror.Number), grid.Columns[0]);
                grid.SelectedItem = Item(0); Away();
                Assert.Equal(colours.Normal, Fill(Row(mirror.Number)));
                CheckClasses();
                var point = Row(mirror.Number).TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseMove(point); Tick(window);
                Assert.Equal(colours.Hover, Fill(Row(mirror.Number)));
                grid.SelectedItem = Item(mirror.Number); Away();
                Assert.Equal(colours.Selected, Fill(Row(mirror.Number)));
                window.MouseMove(point); Tick(window);
                Assert.Equal(colours.SelectedHover, Fill(Row(mirror.Number)));
                Row(mirror.Number).IsEnabled = false; Tick(window);
                Assert.NotEqual(colours.SelectedHover, Fill(Row(mirror.Number)));
                Row(mirror.Number).IsEnabled = true;
                grid.SelectedItem = Item(0); Away();
                Assert.Equal(CoordinateReferenceRowStyles.ColorsFor(ThemeRegistry.FromSettings(themeName)).Normal,
                    Fill(Row(reference.Number)));
                Capture(window, themeName);

                // A comment is not an optical identity, and unchanged MIRROR must survive row commits.
                app.Prescription.UpdateSurface(mirror with { Label = "Datum A" }); Tick(window);
                Assert.True(Item(mirror.Number).ToDto().IsReflective);
                app.Prescription.UpdateSurface(reference with { Label = "MIRROR" }); Tick(window);
                Assert.False(Item(reference.Number).IsReflective);

                // Changing the actual material refreshes classification, with undo restoring reflection.
                app.Prescription.UpdateSurface(mirror with { Material = "N-BK7" }); Tick(window); Away();
                Assert.False(Item(mirror.Number).IsReflective);
                Assert.Contains("glass-material-row", Row(mirror.Number).Classes);
                Assert.True(app.Documents.Undo()); Tick(window); Away();
                Assert.True(Item(mirror.Number).IsReflective);
                Assert.Equal(colours.Normal, Fill(Row(mirror.Number)));

                window.Height = 240; Tick(window);
                foreach (var number in new[] { app.Prescription.GetSurfaces()[^1].Number, 0, mirror.Number, reference.Number, mirror.Number })
                {
                    grid.ScrollIntoView(Item(number), grid.Columns[0]); Away(); CheckClasses();
                }
                window.Height = 650; Tick(window);
                foreach (var nextTheme in new[] { "Dark", "Pixel", "Light", themeName })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, nextTheme); Tick(window);
                }
                grid.SelectedItem = Item(0); Away();
                Assert.Equal(colours.Normal, Fill(Row(mirror.Number)));
                await app.Documents.SaveAsync(saved);
                await app.Documents.OpenAsync(saved); Tick(window); Away();
                Assert.True(Item(mirror.Number).ToDto().IsReflective);
                Assert.Equal(colours.Normal, Fill(Row(mirror.Number)));
                CheckClasses();
                app.Documents.NewBlank(); Tick(window); CheckClasses();
                Assert.All(grid.ItemsSource!.Cast<SurfaceEditorRow>(), row => Assert.False(row.IsReflective));
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
    public void MirrorPaletteKeepsBodyTextReadable(string themeName)
    {
        var theme = ThemeRegistry.FromSettings(themeName);
        var colours = MirrorRowStyles.ColorsFor(theme);
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
    private static void Capture(Window window, string themeName)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MIRROR_ROWS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var scale in themeName == "Light" ? new[] { 1d, 1.5, 2 } : new[] { 1d })
        {
            using var image = new RenderTargetBitmap(
                new PixelSize((int)(window.Width * scale), (int)(window.Height * scale)), new Vector(96 * scale, 96 * scale));
            image.Render(window);
            image.Save(Path.Combine(directory, $"mirror-rows-{themeName}-{scale * 100:0}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
