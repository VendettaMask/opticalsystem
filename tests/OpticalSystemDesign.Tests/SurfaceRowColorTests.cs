using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfaceRowColorTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task UserRowColorSurvivesNativeStatesRecyclingThemeChangesUndoAndReopen(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceRowColorTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, themeName);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1280, Height = 750, Content = panel };
            var saved = Path.Combine(Path.GetTempPath(), $"row-color-{Guid.NewGuid():N}.staropt");
            var color = Color.Parse("#204080");
            try
            {
                window.Show(); Tick(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                SurfaceEditorRow Item(int n) => grid.ItemsSource!.Cast<SurfaceEditorRow>().Single(r => r.Number == n);
                DataGridRow Row(int n) => grid.GetVisualDescendants().OfType<DataGridRow>()
                    .Single(r => r.DataContext is SurfaceEditorRow item && item.Number == n);
                void Away() { window.MouseMove(new Point(window.Width - 5, window.Height - 5)); Tick(window); }
                void AssertRow(bool selected, bool hovered)
                {
                    var row = Row(1);
                    Assert.Contains(CustomSurfaceRowStyles.RowClass, row.Classes);
                    Assert.DoesNotContain("glass-material-row", row.Classes);
                    var expected = CustomSurfaceRowStyles.ColorsFor(color, selected, hovered);
                    var background = Assert.IsAssignableFrom<ISolidColorBrush>(row.GetVisualDescendants()
                        .OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle").Fill).Color;
                    var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(row.Foreground).Color;
                    Assert.Equal(expected.Background, background);
                    Assert.Equal(expected.Foreground, foreground);
                    Assert.True(Contrast(background, foreground) >= 4.5);
                    Assert.All(row.GetVisualDescendants().OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text)), text =>
                        Assert.True(Contrast(background, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color) >= 4.5));
                    Assert.All(row.GetVisualDescendants().OfType<TextBox>()
                        .Where(editor => editor.Classes.Contains(CustomSurfaceRowStyles.NumericEditorClass)), editor =>
                        Assert.True(Contrast(background, Assert.IsAssignableFrom<ISolidColorBrush>(editor.Foreground).Color) >= 4.5));
                    Assert.All(row.GetVisualDescendants().OfType<DataGridCell>(), cell =>
                        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color));
                }
                app.Prescription.UpdateSurfaceDrawingSettings(1, new(RowColor: "#204080")); Tick(window);
                Assert.Equal("#204080", Item(1).ToDto().Drawing!.RowColor);
                grid.SelectedItem = Item(0); Away(); AssertRow(false, false);
                var point = Row(1).TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseMove(point); Tick(window); AssertRow(false, true);
                grid.SelectedItem = Item(1); Away(); AssertRow(true, false);
                window.MouseMove(point); Tick(window); AssertRow(true, true);
                Row(1).IsEnabled = false; Tick(window);
                Assert.NotEqual(CustomSurfaceRowStyles.ColorsFor(color, true, true).Background,
                    Assert.IsAssignableFrom<ISolidColorBrush>(Row(1).GetVisualDescendants()
                        .OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle").Fill).Color);
                Row(1).IsEnabled = true;
                grid.SelectedItem = Item(0); Away();
                foreach (var theme in new[] { "Dark", "Pixel", "Light", themeName })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window); Away();
                    AssertRow(false, false);
                }
                window.Height = 200; Tick(window);
                foreach (var number in new[] { app.Prescription.GetSurfaces()[^1].Number, 0, 1 })
                {
                    grid.ScrollIntoView(Item(number), grid.Columns[0]); Away();
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                        if (row.DataContext is SurfaceEditorRow item)
                            Assert.Equal(item.Number == 1, row.Classes.Contains(CustomSurfaceRowStyles.RowClass));
                }
                window.Height = 750; Tick(window);
                grid.ScrollIntoView(Item(1), grid.Columns[0]); Away();
                app.Prescription.UpdateSurface(Item(1).ToDto() with { Label = "Annotation retained" }); Tick(window);
                Assert.Equal("#204080", Item(1).Drawing.RowColor);
                app.Prescription.UpdateSurfaceDrawingSettings(1, Item(1).Drawing with { RowColor = null }); Tick(window); Away();
                Assert.DoesNotContain(CustomSurfaceRowStyles.RowClass, Row(1).Classes);
                Assert.Contains("glass-material-row", Row(1).Classes);
                Assert.True(app.Documents.Undo()); Tick(window); grid.SelectedItem = Item(0); Away(); AssertRow(false, false);
                await app.Documents.SaveAsync(saved);
                app.Documents.NewBlank(); Tick(window);
                await app.Documents.OpenAsync(saved); Tick(window); grid.SelectedItem = Item(0); Away(); AssertRow(false, false);
                Capture(window, themeName);
            }
            finally { window.Close(); if (File.Exists(saved)) File.Delete(saved); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativePickerAppliesValidColorRejectsInvalidAndRestoresDefault()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceRowColorTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.Apply("zh-CN");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1280, Height = 900, Content = panel };
            try
            {
                window.Show(); Tick(window);
                T Named<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Named<Button>("SurfacePropertiesToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var picker = Named<ComboBox>("SurfaceRowColor");
                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(c => Equals(c.Tag, "custom")); Tick(window);
                var hex = Named<TextBox>("SurfaceRowColorHex");
                hex.Text = "#5a305a";
                Named<Button>("ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("#5A305A", app.Prescription.GetSurfaces().Single(r => r.Number == 1).Drawing!.RowColor);
                var revision = app.Events.Revision;
                picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(c => Equals(c.Tag, "custom")); Tick(window);
                hex.Text = "#GGGGGG";
                Named<Button>("ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal("#5A305A", app.Prescription.GetSurfaces().Single(r => r.Number == 1).Drawing!.RowColor);
                Named<Button>("RevertSurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Equal("#5A305A", hex.Text);
                Capture(window, "picker-Light");
                picker.SelectedIndex = 0;
                Named<Button>("ApplySurfaceProperties").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.Null(app.Prescription.GetSurfaces().Single(r => r.Number == 1).Drawing!.RowColor);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#0000FF")]
    [InlineData("#808080")]
    [InlineData("#FFFF00")]
    public void CustomPaletteRetainsTextContrastInEveryState(string rgb)
    {
        foreach (var selected in new[] { false, true })
            foreach (var hovered in new[] { false, true })
            {
                var palette = CustomSurfaceRowStyles.ColorsFor(Color.Parse(rgb), selected, hovered);
                Assert.True(Contrast(palette.Background, palette.Foreground) >= 4.5);
            }
    }

    private static double Contrast(Color a, Color b)
    {
        var first = CustomSurfaceRowStyles.Luminance(a);
        var second = CustomSurfaceRowStyles.Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Capture(Window window, string theme)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ROW_COLOR_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var scale in theme == "picker-Light" ? new[] { 1d, 1.5, 2 } : new[] { 1d })
        {
            using var image = new RenderTargetBitmap(new PixelSize((int)(window.Width * scale), (int)(window.Height * scale)), new Vector(96 * scale, 96 * scale));
            image.Render(window);
            image.Save(Path.Combine(directory, $"row-color-{theme}-{scale * 100:0}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
