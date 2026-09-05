using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LensSurfaceContextMenuTests
{
    private const string CaptureDirectory = "OPTILAND_SURFACE_MENU_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public void ContextRequestCanBeInterceptedBeforeTextBoxMenu()
    {
        Assert.True(InputElement.ContextRequestedEvent.RoutingStrategies.HasFlag(RoutingStrategies.Tunnel));
    }

    [Theory]
    [InlineData(0, 3)] // Insert below surface 2.
    [InlineData(1, 2)] // Insert above surface 2.
    [InlineData(2, 2)] // Delete surface 2 and select its neighbor.
    public async Task RightClickOperatesOnClickedRowWithOnlyThreeCommands(int command, int selected)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensSurfaceContextMenuTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1100, Height = 500, Content = panel };
            try
            {
                window.Show();
                Layout(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                var before = app.Prescription.GetSurfaces();
                grid.SelectedIndex = 1;
                var revision = app.Events.Revision;
                // Right-click directly in a numeric TextBox, not only the row background.
                var cell = Row(grid, 2).GetVisualDescendants().OfType<TextBox>().First();
                RightClick(window, cell);
                var menu = Menu(panel);
                Assert.True(menu.IsOpen);
                Assert.Equal(2, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                Assert.Equal(revision, app.Events.Revision); // Opening must not dirty unchanged cells.
                var items = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(new[] { "下插入", "上插入", "删除" }, items.Select(item => item.Header));
                Assert.All(items, item => Assert.True(item.IsEnabled));
                Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), button =>
                    button.Content is LocalIconLabel label && label.Children.OfType<TextBlock>()
                        .Any(text => text.Text is "添加" or "删除"));
                var captureDirectory = Environment.GetEnvironmentVariable(CaptureDirectory);
                if (command == 0 && !string.IsNullOrWhiteSpace(captureDirectory))
                {
                    Directory.CreateDirectory(captureDirectory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(captureDirectory, "lens-surface-menu.png"), PngBitmapEncoderOptions.Default);
                }
                items[command].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Layout(window);
                Assert.Equal(revision + 1, app.Events.Revision);
                var rows = app.Prescription.GetSurfaces();
                Assert.Equal(before.Count + (command == 2 ? -1 : 1), rows.Count);
                Assert.Equal(selected, Assert.IsType<SurfaceEditorRow>(grid.SelectedItem).Number);
                if (command == 2)
                    Assert.Equal(before[3].Radius, rows[2].Radius);
                else
                    Assert.Equal(40, rows[selected].Radius);
                Assert.False(menu.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EndpointsAreProtectedAndOpenMenuCannotUseStaleSurfaceNumbers()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(LensSurfaceContextMenuTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new LensEditorPanel(app.Prescription, app.Events, new SurfaceSelectionService());
            var window = new Window { Width = 1000, Height = 500, Content = panel };
            try
            {
                window.Show();
                Layout(window);
                var grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
                var menu = Menu(panel);
                RightClick(window, Row(grid, 0));
                Assert.Equal(new[] { true, false, false }, menu.Items.Cast<MenuItem>().Select(item => item.IsEnabled));
                menu.Close();
                RightClick(window, Row(grid, app.Prescription.GetSurfaces().Count - 1));
                Assert.Equal(new[] { false, true, false }, menu.Items.Cast<MenuItem>().Select(item => item.IsEnabled));
                menu.Close();
                RightClick(window, Row(grid, 2));
                var insert = menu.Items.Cast<MenuItem>().First();
                app.Prescription.InsertSurface(1, after: false);
                var revision = app.Events.Revision;
                // Simulate a menu command arriving before the queued refresh.
                insert.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(revision, app.Events.Revision);
                Layout(window);
                Assert.False(menu.IsOpen);
                RightClick(window, grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First());
                Assert.False(menu.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static DataGridRow Row(DataGrid grid, int number) => grid.GetVisualDescendants()
        .OfType<DataGridRow>().Single(row => row.DataContext is SurfaceEditorRow surface && surface.Number == number);

    private static ContextMenu Menu(LensEditorPanel panel) => Assert.IsType<ContextMenu>(typeof(LensEditorPanel)
        .GetField("_surfaceContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel));

    private static void RightClick(Window window, Control target)
    {
        var point = target.TranslatePoint(new Point(8, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Layout(window);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
