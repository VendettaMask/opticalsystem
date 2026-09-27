using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Application.Services;
using Dock.Model.Mvvm.Controls;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CompactSidebarTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_ACTION_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(240, 1)]
    [InlineData(280, 2)]
    public async Task ExpandedSystemEditorsFitWithoutHorizontalScrolling(double width, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactSidebarTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("tessar");
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = width, Height = 700, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                var scroll = Assert.IsType<ScrollViewer>(panel.Content);
                var sections = Assert.IsType<StackPanel>(scroll.Content);
                foreach (var section in sections.Children.Cast<Border>())
                {
                    var body = Assert.IsType<StackPanel>(section.Child);
                    if (!body.Children[1].IsVisible)
                        ((Button)body.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                Tick(window);
                foreach (var button in panel.GetVisualDescendants().OfType<Button>()
                             .Where(b => b.Classes.Contains("system-property-card-header") && !b.Classes.Contains("system-section-header")).ToArray())
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Tick(window);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                Assert.False(scroll.GetVisualDescendants().OfType<ScrollBar>()
                    .First(b => b.Name == "PART_HorizontalScrollBar").IsVisible);
                var rows = panel.GetVisualDescendants().OfType<Grid>()
                    .Where(g => g.IsVisible && g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[0].Width.Value == 70).ToArray();
                Assert.True(rows.Length >= 20);
                foreach (var row in rows)
                {
                    var label = Assert.IsType<TextBlock>(row.Children[0]);
                    var editor = row.Children[1];
                    Assert.True(editor.Bounds.Left >= label.Bounds.Right);
                    Assert.True(editor.Bounds.Right <= row.Bounds.Width + 1);
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
                    Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1, $"{label.Text}: text {label.TextLayout.Height}, bounds {label.Bounds}");
                    Assert.True(editor.Bounds.Width >= 70, $"{label.Text}: input width {editor.Bounds.Width}");
                }
                var aperture = rows.Single(g => ((TextBlock)g.Children[0]).Text == "孔径值");
                var input = Assert.IsType<NumericUpDown>(aperture.Children[1]);
                var original = input.Value;
                input.BringIntoView(); Tick(window);
                var textBox = input.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(textBox.Focus());
                Assert.Equal(original, input.Value);
                Capture(window, $"sidebar-expanded-{width}");
                scroll.Offset = new Vector(500, scroll.Extent.Height); Tick(window);
                Assert.Equal(0, scroll.Offset.X);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1600)]
    public async Task OldWideSettingsAndLayoutRestoreRemainCompact(double width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactSidebarTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("tessar");
            using var manager = new PanelManager(app, new AppSettings { LeftPaneWidth = 420 },
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "sidebar-layout", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = width, Height = 800, Content = manager.WorkspaceControl };
            try
            {
                window.Show(); Tick(window);
                var dock = WorkspaceDockFactory.EnumerateDockables(manager.Layout).OfType<ToolDock>()
                    .Single(d => d.Id == WorkspaceDockFactory.ToolDockId);
                Assert.Equal(UiDensity.SystemOptionsMaximumWidth, dock.MaxWidth);
                manager.ApplyLayout(new WorkspaceLayoutState(420)); Tick(window);
                Assert.InRange(manager.CaptureLayout().LeftPaneWidth, UiDensity.SystemOptionsMinimumWidth, UiDensity.SystemOptionsMaximumWidth);
                var panel = window.GetVisualDescendants().OfType<SystemPropertiesPanel>().Single();
                Assert.InRange(panel.Bounds.Width, UiDensity.SystemOptionsMinimumWidth - 4, UiDensity.SystemOptionsMaximumWidth);
                Assert.True(panel.Bounds.Width < window.Width * .3);
                Capture(window, $"workspace-sidebar-{width}");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_ACTION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
