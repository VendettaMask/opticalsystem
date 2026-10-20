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
    public async Task OnlyLensHeadersAreBoldAndRetainTheirSizeAndAlignment(string theme, string language, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensHeaderTypographyTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var app = WorkbenchApplication.Create();
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
                    Assert.Equal(baselineHeader.FontSize, header.FontSize);
                    foreach (var text in header.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
                        Assert.Equal(FontWeight.Bold, text.FontWeight);
                }
                foreach (var column in grid.Columns.Where(c => c.Header is TextBlock))
                {
                    var header = (TextBlock)column.Header!;
                    Assert.Equal(HorizontalAlignment.Right, header.HorizontalAlignment);
                    Assert.Equal(TextAlignment.Right, header.TextAlignment);
                }
                Assert.All(grid.GetVisualDescendants().OfType<DataGridRow>(), row => Assert.Equal(FontWeight.Normal, row.FontWeight));
                Assert.All(grid.GetVisualDescendants().OfType<TextBox>(), input => Assert.Equal(FontWeight.Normal, input.FontWeight));
                Assert.All(comparison.GetVisualDescendants().OfType<DataGridColumnHeader>(), header => Assert.NotEqual(FontWeight.Bold, header.FontWeight));
                Assert.Equal(revision, app.Events.Revision);
                Capture(window, $"lens-headers-{theme}-{language}-{fontSize}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
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
