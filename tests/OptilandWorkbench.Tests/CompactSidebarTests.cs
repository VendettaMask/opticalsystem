using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
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
                foreach (var label in new[] { "X 瞳孔偏移", "Y 瞳孔偏移", "瞳孔旋转 (°)" })
                    Assert.Equal(app.Prescription.GetFields().Count, rows.Count(g => ((TextBlock)g.Children[0]).Text == label));
                var aperture = rows.Single(g => ((TextBlock)g.Children[0]).Text == "孔径值");
                var input = Assert.IsType<NumericUpDown>(aperture.Children[1]);
                var original = input.Value;
                input.BringIntoView(); Tick(window);
                var textBox = input.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(textBox.Focus());
                Assert.Equal(original, input.Value);
                Capture(window, $"sidebar-expanded-{width}");
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
                    foreach (var sidebarWidth in new[] { width, 256d })
                    {
                        window.Width = sidebarWidth; Tick(window);
                        foreach (var list in panel.GetVisualDescendants().OfType<ListBox>().ToArray())
                        {
                            list.BringIntoView(); Tick(window);
                            scroll.Offset = new Vector(0, scroll.Offset.Y + list.TranslatePoint(default, scroll)!.Value.Y - 32);
                            Tick(window);
                            list.SelectedItem = list.Items.Cast<string>().FirstOrDefault(name => name == "INFRARED") ?? list.Items[0];
                            list.ScrollIntoView(list.SelectedItem!); Tick(window);
                            var listScroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                            var viewport = listScroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
                            var verticalBar = listScroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollBar");
                            var name = list.Height == 210 ? "available" : "current";
                            Capture(window, $"sidebar-catalog-{name}-{theme.ToLowerInvariant()}-{sidebarWidth}-{scaling}x");
                            Assert.Equal(ScrollBarVisibility.Disabled, listScroll.HorizontalScrollBarVisibility);
                            Assert.Equal(ScrollBarVisibility.Hidden, listScroll.VerticalScrollBarVisibility);
                            Assert.False(verticalBar.IsVisible);
                            Assert.True(listScroll.Extent.Width <= listScroll.Viewport.Width + 1);
                            AssertItemsFit();
                            listScroll.Offset = new Vector(500, listScroll.Extent.Height); Tick(window);
                            Assert.Equal(0, listScroll.Offset.X);
                            AssertItemsFit();
                            listScroll.Offset = default; Tick(window);
                            AssertItemsFit();
                            var wheelPoint = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
                            window.MouseWheel(wheelPoint, new Vector(0, -1), RawInputModifiers.None); Tick(window);
                            Assert.True(listScroll.Offset.Y > 0);
                            AssertItemsFit();
                            list.SelectedIndex = 0;
                            list.ScrollIntoView(list.Items[0]!); Tick(window);
                            Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0)).Focus());
                            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
                            window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null); Tick(window);
                            Assert.Equal(list.Items.Count - 1, list.SelectedIndex);
                            Assert.True(listScroll.Offset.Y > 0);
                            AssertItemsFit();

                            void AssertItemsFit()
                            {
                                var right = viewport.Bounds.Width;
                                if (verticalBar.IsVisible)
                                    right = Math.Min(right, verticalBar.TranslatePoint(default, viewport)!.Value.X);
                                var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                                Assert.NotEmpty(items);
                                foreach (var item in items)
                                {
                                    var itemRight = item.TranslatePoint(default, viewport)!.Value.X + item.Bounds.Width;
                                    Assert.True(itemRight <= right + 1,
                                        $"{theme}/{sidebarWidth}/{name}/{item.Content}: item right {itemRight}, visible right {right}, item {item.Bounds}, viewport {viewport.Bounds}");
                                    var presenter = item.GetVisualDescendants().OfType<ContentPresenter>().First();
                                    Assert.True(presenter.Bounds.Right <= item.Bounds.Width + 1);
                                }
                            }
                        }
                    }
                }
                ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
                scroll.Offset = new Vector(500, scroll.Extent.Height); Tick(window);
                Assert.Equal(0, scroll.Offset.X);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PupilEditorsCommitAllFactorsAndKeepThemDuringUnrelatedEdits()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactSidebarTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("tessar");
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = 256, Height = 700, Content = panel };
            try
            {
                window.Show(); Tick(window);
                foreach (var button in panel.GetVisualDescendants().OfType<Button>()
                             .Where(b => b.Classes.Contains("system-property-card-header") && !b.Classes.Contains("system-section-header")).ToArray())
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Tick(window);
                NumericUpDown Input(string name) => Assert.IsType<NumericUpDown>(panel.GetVisualDescendants().OfType<Grid>()
                    .First(g => g.Children.Count == 2 && g.Children[0] is TextBlock label && label.Text == name).Children[1]);
                Input("X 瞳孔偏移").Value = .125m; Tick(window);
                Input("Y 瞳孔偏移").Value = -.25m; Tick(window);
                Input("瞳孔旋转 (°)").Value = 37m; Tick(window);
                Input("权重").Value = 2m; Tick(window);
                var saved = app.Prescription.GetFields()[0];
                Assert.Equal(.125, saved.VignetteDecenterX); Assert.Equal(-.25, saved.VignetteDecenterY);
                Assert.Equal(37, saved.VignetteAngleDegrees); Assert.Equal(2, saved.Weight);
                Input("瞳孔旋转 (°)").BringIntoView(); Tick(window);
                Capture(window, "sidebar-pupil-factors-256");
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
