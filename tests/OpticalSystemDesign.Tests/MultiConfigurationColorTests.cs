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
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MultiConfigurationColorTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task SameOperandTypeKeepsOneColourAcrossRowsAndConfigurations(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MultiConfigurationColorTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.Apply(theme == "Isekai" ? "en-US" : "zh-CN");
            using var app = WorkbenchApplication.Create();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
            await app.Documents.OpenAsync(Path.Combine(root!.FullName,
                "src/OpticalSystemDesign.App/Assets/SequentialExamples/Zoom systems/Zoom lens.zmx"));
            var bindings = new MultiConfigurationOperandBindingDto[]
            {
                new(MultiConfigurationParameterKind.Thickness, 8),
                new(MultiConfigurationParameterKind.Curvature, 8),
                new(MultiConfigurationParameterKind.Thickness, 15),
                new(MultiConfigurationParameterKind.Curvature, 15),
                new(MultiConfigurationParameterKind.Conic, 8),
                new(MultiConfigurationParameterKind.SemiDiameter, 15)
            };
            app.MultiConfiguration.ReplaceOperandRows(bindings, app.Events.Revision);
            using var panel = new MultiConfigurationPanel(app.Prescription, app.MultiConfiguration, app.Events);
            var window = new Window { Content = panel, Width = 1100, Height = 650 };
            try
            {
                window.Show(); Tick(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single(control => control.Name == "MultiConfigurationOperands");
                grid.SelectedIndex = 0; Tick(window);
                var original = Cells("THIC-8").Select(cell => Colour(cell.Background)).ToArray();
                Assert.Single(original.Distinct());
                Assert.Single(Cells("THIC-15").Select(cell => Colour(cell.Background)).Distinct());
                Assert.Equal(original[0], Colour(Cells("THIC-15")[0].Background));
                Assert.Equal(Colour(Cells("CRVT-8")[0].Background), Colour(Cells("CRVT-15")[0].Background));
                Assert.Equal(4, new[] { original[0], Colour(Cells("CRVT-8")[0].Background),
                    Colour(Cells("CONN-8")[0].Background), Colour(Cells("SDIA-15")[0].Background) }.Distinct().Count());
                AssertContrast(Cells("THIC-8").Concat(Cells("THIC-15")));
                var headers = Enumerable.Range(3, 3).Select(index => grid.GetVisualDescendants()
                    .OfType<DataGridColumnHeader>().Single(header => ReferenceEquals(header.Content, grid.Columns[index].Header))).ToArray();
                Assert.Single(headers.Select(header => Colour(header.Background)).Distinct());
                var metadata = Metadata("THIC-8");
                Assert.All(metadata, cell => Assert.Equal(original[0], Colour(cell.Background)));
                using (var frame = Frame(window))
                {
                    var right = grid.TranslatePoint(new Point(grid.Bounds.Width, 0), window)!.Value.X;
                    Assert.True(right < window.Width - 100, "The table still stretches past the actual configuration columns.");
                    var blank = frame.GetPixel((int)right + 20, (int)grid.TranslatePoint(new Point(0, 5), window)!.Value.Y);
                    foreach (var control in new Control[] { headers[0], Cells("APER")[0], Cells("THIC-8")[0], Cells("CRVT-8")[0] })
                    {
                        var y = (int)control.TranslatePoint(new Point(0, 5), window)!.Value.Y;
                        Assert.Equal(blank, frame.GetPixel((int)right + 20, y));
                    }
                    AssertDistinctPixels(new[] { Cells("THIC-8")[0], Cells("CRVT-8")[0] }, frame, window);
                    Assert.Equal(Sample(Cells("THIC-8")[0], frame, window), Sample(Cells("THIC-15")[0], frame, window));
                    var samples = metadata.Cast<Control>().Concat(Cells("THIC-8")).Select(control => Sample(control, frame, window)).ToArray();
                    Assert.All(samples, pixel => Assert.Equal(samples[0], pixel));
                }
                Capture(window, $"types-{theme}.png");
                var originalWidth = grid.Bounds.Width;
                grid.Columns[3].Width = new DataGridLength(196); Tick(window); Tick(window);
                Assert.True(grid.Bounds.Width > originalWidth + 40, "Resizing a real column must expand the table boundary.");
                grid.Columns[3].Width = new DataGridLength(132); Tick(window); Tick(window);
                var revision = app.Events.Revision;
                var selected = Cells("THIC-8")[1];
                var point = selected.TranslatePoint(new Point(selected.Bounds.Width / 2, selected.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
                Assert.True(panel.TryFindResource(ThemeResourceBindings.SelectionBackground, out var selection));
                Assert.Equal(Colour((IBrush)selection!), Colour(Cells("THIC-8")[1].Background));
                Assert.Equal(original[0], Colour(Cells("THIC-8")[2].Background));
                AssertContrast(Cells("THIC-8"));

                grid.SelectedIndex = 0; Tick(window);
                app.MultiConfiguration.Activate(2); Tick(window);
                Assert.Equal(original, Cells("THIC-8").Select(cell => Colour(cell.Background)));
                app.MultiConfiguration.ReplaceOperandRows(bindings.Reverse().ToArray(), app.Events.Revision); Tick(window);
                Assert.Equal(original, Cells("THIC-8").Select(cell => Colour(cell.Background)));
                Assert.Equal(original[0], Colour(Cells("THIC-15")[0].Background));
                Assert.Contains("*", ((TextBlock)grid.Columns[5].Header!).Text);
                foreach (var changed in new[] { theme == "Dark" ? "Light" : "Dark", theme })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, changed); Tick(window);
                    Assert.Single(Cells("THIC-8").Select(cell => Colour(cell.Background)).Distinct());
                    Assert.All(Metadata("THIC-8"), cell => Assert.Equal(Colour(Cells("THIC-8")[0].Background), Colour(cell.Background)));
                    AssertContrast(Cells("THIC-8"));
                }
                Assert.Equal(original, Cells("THIC-8").Select(cell => Colour(cell.Background)));
                app.MultiConfiguration.Add(); Tick(window);
                Assert.Equal(4, Cells("THIC-8").Length);
                Assert.Single(Cells("THIC-8").Select(cell => Colour(cell.Background)).Distinct());
                app.Documents.NewBlank(); Tick(window);
                Assert.Equal(4, grid.Columns.Count);
                Assert.Single(Cells("APER"));

                Border[] Cells(string code) => grid.GetVisualDescendants().OfType<Border>()
                    .Where(cell => cell.Name?.StartsWith($"MultiConfigurationCell-{code}-", StringComparison.Ordinal) == true)
                    .OrderBy(cell => int.Parse(cell.Name![(cell.Name!.LastIndexOf('-') + 1)..])).ToArray();
                DataGridCell[] Metadata(string code)
                {
                    var native = grid.GetVisualDescendants().OfType<DataGridRow>()
                        .Single(row => ReferenceEquals(row.DataContext, Cells(code)[0].DataContext));
                    return grid.Columns.Take(3).Select(column => column.GetCellContent(native)!
                        .GetVisualAncestors().OfType<DataGridCell>().First()).ToArray();
                }
            }
            finally { window.Close(); UiLocalization.Apply("zh-CN"); }
        }, CancellationToken.None);
    }

    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void AssertContrast(IEnumerable<Border> cells)
    {
        foreach (var cell in cells)
        {
            var background = Colour(cell.Background);
            var foreground = Colour(Assert.IsType<TextBlock>(cell.Child).Foreground);
            static double L(Color color)
            {
                static double C(byte value) { var channel = value / 255d; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
                return .2126 * C(color.R) + .7152 * C(color.G) + .0722 * C(color.B);
            }
            var ratio = (Math.Max(L(background), L(foreground)) + .05) / (Math.Min(L(background), L(foreground)) + .05);
            Assert.True(ratio >= 4.5, $"Unreadable configuration cell: {foreground} over {background}, contrast {ratio}.");
        }
    }

    private static void AssertDistinctPixels(IEnumerable<Control> controls, SKBitmap frame, Window window)
    {
        var pixels = controls.Select(control => Sample(control, frame, window)).ToArray();
        for (var first = 0; first < pixels.Length; first++)
            for (var second = first + 1; second < pixels.Length; second++)
                Assert.True(Math.Abs(pixels[first].Red - pixels[second].Red) + Math.Abs(pixels[first].Green - pixels[second].Green)
                    + Math.Abs(pixels[first].Blue - pixels[second].Blue) > 12,
                    $"Operand rows have indistinguishable rendered colours: {pixels[first]}, {pixels[second]}.");
    }

    private static SKColor Sample(Control control, SKBitmap frame, Window window)
    {
        var point = control.TranslatePoint(new Point(control is DataGridCell ? control.Bounds.Width - 12 : 10, 5), window)!.Value;
        return frame.GetPixel((int)point.X, (int)point.Y);
    }

    private static SKBitmap Frame(Window window)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MCE_OPERAND_TYPE_COLORS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, 420), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
