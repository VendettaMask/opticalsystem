using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MeritOperandRowThemeTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OPERAND_COLORS_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task NativeRowPaintSurvivesThemeStatesEditingRecyclingAndDisposal()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MeritOperandRowThemeTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var codes = new[] { "AXCL", "AXCL", "REAY", "REAY", "EFFL", "REAY", "DIFF", "BLNK", "DMFS", "MNCA", "MXCA", "MNEA", "OPDX", "MNCG", "INDX", "RSCE", "DIVI" };
            // Disabled numerical operands isolate display verification from optical evaluation.
            application.Optimization.SetMeritFunction(Enumerable.Range(0, 85).Select(i =>
                new MeritOperandRowDto(i + 1, false, codes[i % codes.Length], 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, "",
                    ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 0, ZemaxData2: 0, ZemaxData3: 0, ZemaxData4: 0, ZemaxData5: 0)).ToArray());
            using var panel = new OptimizationPanel(application.Prescription, application.Optimization, application.Events);
            var window = new Window { Width = 1600, Height = 780, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                var items = grid.ItemsSource!.Cast<MeritOperandEditorRow>().ToArray();
                DataGridRow Row(int index) => grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, items[index]));
                void Away() { window.MouseMove(new Point(1595, 775)); Render(window); }
                void AssertPaint()
                {
                    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
                    {
                        if (row.DataContext is not MeritOperandEditorRow item) continue;
                        var expected = MeritOperandRowPalette.ResolveVisual(item.Type,
                            ThemeRegistry.FromActualVariant(row.ActualThemeVariant), !string.IsNullOrWhiteSpace(item.Error),
                            row.IsPointerOver, row.IsSelected, row.IsEnabled);
                        Assert.Equal(expected.Background, Solid(Background(row).Fill));
                        Assert.Equal(1, Background(row).Opacity);
                        Assert.Equal(expected.Foreground, Solid(row.Foreground));
                        var text = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");
                        Assert.Equal(expected.Foreground, Solid(text.Foreground));
                    }
                }
                foreach (var theme in new[] { "Light", "Dark", "Isekai", "Pixel", "Lumen", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    grid.SelectedItem = null;
                    Away();
                    AssertPaint();
                    AssertPixels(window, grid);
                    Capture(window, $"merit-rows-{theme}.png");
                    if (theme == "Isekai")
                    {
                        Capture(window, "merit-rows-Isekai-150.png", 1.5);
                        Capture(window, "merit-rows-Isekai-200.png", 2);
                    }
                    var row = Row(2);
                    var point = row.TranslatePoint(new Point(220, row.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point); Render(window);
                    Assert.True(row.IsPointerOver);
                    AssertPaint();
                    grid.SelectedItem = items[2];
                    grid.Focus(); Render(window); AssertPaint();
                    AssertPixels(window, grid);
                    Assert.Equal(1, Background(Row(2)).StrokeThickness);
                    Away(); AssertPaint();
                    items[2].Type = "OPDX"; Render(window); AssertPaint();
                    Assert.Equal("OPDX", Row(2).GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem);
                    items[2].Error = "Display test error"; Render(window); AssertPaint();
                    AssertPixels(window, grid);
                    if (theme == "Isekai") Capture(window, "merit-rows-Isekai-error-selected.png");
                    items[2].Error = ""; items[2].Type = "REAY";
                    grid.SelectedItem = null; Away(); AssertPaint();
                }
                // The native type picker persists/rebuilds rows; the new code must repaint too.
                var picker = Row(4).GetVisualDescendants().OfType<ComboBox>().Single();
                picker.SelectedItem = "REAY";
                Render(window); Away(); AssertPaint();
                Assert.Equal("REAY", grid.ItemsSource!.Cast<MeritOperandEditorRow>().ElementAt(4).Type);
                window.Height = 270; Render(window);
                grid.ScrollIntoView(grid.ItemsSource!.Cast<MeritOperandEditorRow>().Last(), grid.Columns[0]);
                Away(); AssertPaint();
                grid.ScrollIntoView(grid.ItemsSource!.Cast<MeritOperandEditorRow>().First(), grid.Columns[0]);
                Away(); AssertPaint();
                window.Width = 880; Render(window); AssertPaint();
                Capture(window, "merit-rows-Light-narrow.png");
                var liveRow = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is MeritOperandEditorRow);
                var liveItem = (MeritOperandEditorRow)liveRow.DataContext!;
                var colour = liveRow.Background;
                panel.Dispose();
                liveItem.Type = "DIVI";
                liveItem.Error = "after disposal";
                Render(window);
                Assert.Same(colour, liveRow.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Rectangle Background(DataGridRow row) => row.GetVisualDescendants().OfType<Rectangle>()
        .Single(r => r.Name == "BackgroundRectangle" && !r.GetVisualAncestors().OfType<DataGridRowHeader>().Any());
    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void AssertPixels(Window window, DataGrid grid)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        image.Render(window);
        using var stream = new MemoryStream();
        image.Save(stream, PngBitmapEncoderOptions.Default);
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            if (row.DataContext is not MeritOperandEditorRow item) continue;
            foreach (var column in new[] { 0, 2, 14 }) // Frozen index, parameter and empty comment cells.
            {
                var content = grid.Columns[column].GetCellContent(row);
                var cell = content?.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
                if (cell is null) continue;
                var point = cell.TranslatePoint(new Point(cell.Bounds.Width - 8, 4), window)!.Value;
                if (point.Y < 145 || point.Y >= window.Bounds.Height - 45 || point.X < 0 || point.X >= window.Bounds.Width - 20) continue;
                var actual = pixels.GetPixel((int)point.X, (int)point.Y);
                Assert.True(Solid(row.Background) == Color.FromRgb(actual.Red, actual.Green, actual.Blue),
                    $"{row.ActualThemeVariant}/{item.Type}/column {column}: rendered {actual}, row {row.Background}");
            }
        }
    }
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static void Capture(Window window, string name, double scale = 1)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_OPERAND_COLORS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
