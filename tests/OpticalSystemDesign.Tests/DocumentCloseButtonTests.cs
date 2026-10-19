using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class DocumentCloseButtonTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CAPTION_REPAIR_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Pixel")]
    [InlineData("Isekai")]
    public async Task ActualClosePresentersUseRedStatesAndStillCloseOnlyTheirDocument(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DocumentCloseButtonTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var application = WorkbenchApplication.Create();
            using var panels = new PanelManager(application, new AppSettings(),
                new WorkspaceSessionStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caption-repair", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = 800, Height = 500, Content = panels.WorkspaceControl };
            try
            {
                window.Show();
                Tick(window);
                var dock = panels.Factory.PrimaryDocumentDock!;
                var viewer = Assert.Single(dock.VisibleDockables!, document => document.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var tab = Assert.Single(window.GetVisualDescendants().OfType<DocumentTabStripItem>(), item => ReferenceEquals(item.DataContext, viewer));
                var normal = Assert.Single(tab.GetVisualDescendants().OfType<ContentPresenter>(), presenter => presenter.Name == "PART_ClosePresenter");
                var compact = Assert.Single(tab.GetVisualDescendants().OfType<ContentPresenter>(), presenter => presenter.Name == "PART_CompactClosePresenter");
                foreach (var presenter in new[] { normal, compact })
                {
                    var button = Assert.Single(presenter.GetVisualDescendants().OfType<Button>());
                    Assert.True(button.IsEnabled);
                    var path = Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content);
                    var originalData = path.Data;
                    var originalStroke = path.Stroke;
                    AssertColor("#FF5F57", button.Foreground);
                    AssertColor("#FF5F57", path.Fill);
                    ((IPseudoClasses)button.Classes).Set(":pointerover", true);
                    Tick(window);
                    AssertColor("#E5484D", path.Fill);
                    ((IPseudoClasses)button.Classes).Set(":pressed", true);
                    Tick(window);
                    AssertColor("#C9353A", path.Fill);
                    ((IPseudoClasses)button.Classes).Set(":pressed", false);
                    ((IPseudoClasses)button.Classes).Set(":pointerover", false);
                    Tick(window);
                    AssertColor("#FF5F57", path.Fill);
                    Assert.Same(originalData, path.Data);
                    Assert.Equal(originalStroke, path.Stroke);
                    button.IsEnabled = false;
                    Tick(window);
                    Assert.True(button.TryFindResource(ThemeResourceBindings.TextDisabled, button.ActualThemeVariant, out var disabledBrush));
                    Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(disabledBrush).Color,
                        Assert.IsAssignableFrom<ISolidColorBrush>(path.Fill).Color);
                    button.IsEnabled = true;
                    Tick(window);
                    AssertColor("#FF5F57", path.Fill);
                }

                var captureDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CAPTION_REPAIR_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(captureDirectory))
                {
                    Directory.CreateDirectory(captureDirectory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(1600, 1000), new Vector(192, 192));
                    bitmap.Render(window);
                    bitmap.Save(System.IO.Path.Combine(captureDirectory, $"workspace-close-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
                var close = Assert.Single(normal.GetVisualDescendants().OfType<Button>());
                var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Tick(window);
                Assert.DoesNotContain(viewer, dock.VisibleDockables!);
                Assert.Contains(dock.VisibleDockables!, document => document.Id == WorkspaceDockFactory.LensDocumentId);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void AssertColor(string expected, IBrush? brush) =>
        Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
}
