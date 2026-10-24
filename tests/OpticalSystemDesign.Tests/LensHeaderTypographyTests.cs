using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LensHeaderTypographyTests
{
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_LENS_HEADER_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", "zh-CN", 13)]
    [InlineData("Light", "zh-CN", 18)]
    [InlineData("Dark", "en-US", 13)]
    [InlineData("Dark", "en-US", 18)]
    [InlineData("Pixel", "ja-JP", 13)]
    [InlineData("Pixel", "ja-JP", 18)]
    [InlineData("Isekai", "en-US", 13)]
    [InlineData("Isekai", "en-US", 18)]
    [InlineData("Lumen", "zh-CN", 13)]
    [InlineData("Lumen", "zh-CN", 18)]
    public async Task OnlyLensHeadersAreBoldCenteredAndRetainTheirSize(string theme, string language, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensHeaderTypographyTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var app = WorkbenchApplication.Create();
            app.Prescription.AddSurface();
            app.Prescription.AddSurface();
            var stop = app.Prescription.GetSurfaces().Single(surface => surface.Number == 1);
            app.Prescription.UpdateSurface(stop with { IsStop = true });
            var revision = app.Events.Revision;
            using var editor = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            DisplayTypography.Apply(editor);
            var comparison = new DataGrid { AutoGenerateColumns = false, FontSize = fontSize, ItemsSource = new[] { new { Value = "Plain data" } } };
            comparison.Columns.Add(new DataGridTextColumn { Header = "Other table", Binding = new Avalonia.Data.Binding("Value") });
            var root = new Grid { RowDefinitions = new RowDefinitions("*,100"), Children = { editor, comparison } };
            Grid.SetRow(comparison, 1);
            var window = new Window { Content = root, Width = 1500, Height = 650 };
            try
            {
                window.Show(); Tick(window);
                var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                var headers = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is not null).ToArray();
                var baselineHeader = comparison.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => h.Content is not null);
                Assert.Equal(grid.Columns.Count, headers.Length);
                foreach (var header in headers)
                {
                    Assert.Equal(FontWeight.Bold, header.FontWeight);
                    Assert.Equal(HorizontalAlignment.Center, header.HorizontalContentAlignment);
                    Assert.Equal(VerticalAlignment.Center, header.VerticalContentAlignment);
                    Assert.Equal(baselineHeader.FontSize, header.FontSize);
                    foreach (var text in header.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
                        Assert.Equal(FontWeight.Bold, text.FontWeight);
                }
                foreach (var column in grid.Columns.Where(c => c.Header is TextBlock))
                {
                    var header = (TextBlock)column.Header!;
                    Assert.Equal(HorizontalAlignment.Center, header.HorizontalAlignment);
                    Assert.Equal(TextAlignment.Center, header.TextAlignment);
                }
                Assert.All(grid.GetVisualDescendants().OfType<DataGridRow>(), row => Assert.Equal(FontWeight.Normal, row.FontWeight));
                Assert.All(grid.GetVisualDescendants().OfType<TextBox>(), input => Assert.Equal(FontWeight.Normal, input.FontWeight));
                Assert.All(comparison.GetVisualDescendants().OfType<DataGridColumnHeader>(), header => Assert.NotEqual(FontWeight.Bold, header.FontWeight));
                AssertSurfaceTypeAlignment(grid);
                Capture(window, $"lens-headers-{theme}-{language}-{fontSize}.png");
                grid.Columns[1].Width = new DataGridLength(240);
                Tick(window);
                AssertSurfaceTypeAlignment(grid);
                grid.Columns[1].Width = new DataGridLength(140);
                Tick(window);
                AssertSurfaceTypeAlignment(grid);
                Assert.Equal(revision, app.Events.Revision);
                Capture(window, $"lens-type-narrow-{theme}-{language}-{fontSize}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertSurfaceTypeAlignment(DataGrid grid)
    {
        var rows = grid.ItemsSource!.Cast<SurfaceEditorRow>().ToArray();
        Assert.Equal(3, rows.Count(row => row.ShowSurfaceRole));
        Assert.Contains(rows, row => !row.ShowSurfaceRole);
        foreach (var row in rows)
        {
            var cell = Assert.IsType<Grid>(grid.Columns[1].GetCellContent(row));
            var role = cell.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0);
            var type = cell.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 1);
            Assert.Equal(row.ShowSurfaceRole, role.IsVisible);
            Assert.Equal(HorizontalAlignment.Left, role.HorizontalAlignment);
            Assert.Equal(TextAlignment.Left, role.TextAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, type.HorizontalAlignment);
            Assert.Equal(TextAlignment.Right, type.TextAlignment);
            Assert.Equal(TextTrimming.CharacterEllipsis, type.TextTrimming);
            Assert.NotNull(ToolTip.GetTip(type));
            var typeOrigin = type.TranslatePoint(default, cell)!.Value;
            Assert.Equal(cell.Bounds.Width - 8, typeOrigin.X + type.Bounds.Width, precision: 6);
            var dataCell = cell.GetVisualAncestors().OfType<DataGridCell>().First();
            var typeRight = type.TranslatePoint(new Point(type.Bounds.Width, 0), dataCell)!.Value.X;
            Assert.InRange(dataCell.Bounds.Width - typeRight, 8, 9);
            if (role.IsVisible)
            {
                var roleOrigin = role.TranslatePoint(default, cell)!.Value;
                Assert.Equal(8, roleOrigin.X, precision: 6);
                Assert.True(roleOrigin.X + role.Bounds.Width <= typeOrigin.X);
            }
        }
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, fileName), PngBitmapEncoderOptions.Default);
    }
}
