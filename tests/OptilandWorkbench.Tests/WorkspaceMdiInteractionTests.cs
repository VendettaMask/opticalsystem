using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WorkspaceMdiInteractionTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_MDI_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task TiledDocumentsKeepInteractiveContentAcrossActivationAndLayoutChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceMdiInteractionTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("tessar");
            using var manager = CreateManager(application);
            var window = new Window { Width = 1500, Height = 900, Content = manager.WorkspaceControl };
            try
            {
                window.Show();
                Render(window);
                manager.ShowTolerancingDataViewer();
                Render(window);
                manager.TileAllWindows();
                Render(window);
                var documents = manager.Factory.OpenDocuments().ToArray();
                foreach (var open in documents) AssertContent(open);
                foreach (var document in documents.Concat(documents.Reverse()))
                {
                    var mdi = window.GetVisualDescendants().OfType<MdiDocumentWindow>()
                        .Single(w => ReferenceEquals(w.DataContext, document) && w.IsEffectivelyVisible);
                    var point = mdi.TranslatePoint(new Point(100, 100), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Render(window);
                    Assert.Equal(document.Id, manager.Factory.PrimaryDocumentDock!.ActiveDockable?.Id);
                    foreach (var open in documents) AssertContent(open);
                }

                var lens = documents.Single(d => d.Id == WorkspaceDockFactory.LensDocumentId);
                var grid = ((Control)lens.Context!).GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedIndex = 1;
                grid.CurrentColumn = grid.Columns.Single(c => Equals(c.Header, "标注"));
                grid.ScrollIntoView(grid.SelectedItem, grid.CurrentColumn);
                Render(window);
                var label = grid.CurrentColumn.GetCellContent(grid.SelectedItem);
                Click(window, label!);
                grid.Focus();
                TextBox? editor = null;
                grid.PreparingCellForEdit += (_, args) => editor = Assert.IsType<TextBox>(args.EditingElement);
                var original = ((SurfaceEditorRow)grid.SelectedItem!).Label;
                Assert.True(grid.BeginEdit());
                Render(window);
                Assert.NotNull(editor);
                editor.Focus();
                editor.SelectAll();
                window.KeyTextInput("MDI edit cancelled");
                Key(window, Avalonia.Input.Key.Escape);
                Assert.Equal(original, ((SurfaceEditorRow)grid.SelectedItem!).Label);
                foreach (var open in documents) AssertContent(open);

                var viewer = documents.Single(d => d.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var viewerPanel = Assert.IsType<ViewerPanel>(viewer.Context);
                var rays = viewerPanel.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "显示光线"));
                Click(window, rays);
                Assert.False(rays.IsChecked);
                Assert.False(viewerPanel.GetVisualDescendants().OfType<OpticSceneControl>().Single().ShowRays);
                Assert.Same(viewer, manager.Factory.PrimaryDocumentDock!.ActiveDockable);
                Click(window, rays);
                Assert.True(rays.IsChecked);

                var tolerance = documents.Single(d => d.Context is TolerancingPanel);
                var tolerancePanel = (Control)tolerance.Context!;
                var operandGrid = tolerancePanel.GetVisualDescendants().OfType<DataGrid>().Single(g => g.IsEffectivelyVisible);
                var beforeAdd = operandGrid.ItemsSource.Cast<object>().Count();
                Click(window, ButtonWithText(tolerancePanel, "添加"));
                Assert.Equal(beforeAdd + 1, operandGrid.ItemsSource.Cast<object>().Count());
                Assert.True(manager.HasUnsavedToleranceChanges);

                Capture(window, "mdi-three-interactive-panels.png");
                foreach (var open in documents) AssertContent(open);
                for (var i = 0; i < 2; i++)
                {
                    manager.DockToSinglePane();
                    Render(window);
                    foreach (var document in documents)
                    {
                        var tab = window.GetVisualDescendants().OfType<DocumentTabStripItem>()
                            .Single(t => ReferenceEquals(t.DataContext, document) && t.IsEffectivelyVisible);
                        Click(window, tab);
                        var content = (Control)document.Context!;
                        Assert.NotNull(content.FindAncestorOfType<DocumentControl>());
                        Assert.True(content.IsEffectivelyVisible);
                    }
                    manager.CascadeAllWindows();
                    Render(window);
                    foreach (var open in documents) AssertContent(open);
                    manager.TileAllWindows();
                    Render(window);
                    foreach (var open in documents) AssertContent(open);
                }

                window.Hide();
                window.Show();
                Render(window);
                foreach (var open in documents) AssertContent(open);
                window.SetRenderScaling(2);
                window.Width = 1050;
                Render(window);
                manager.TileAllWindows();
                Render(window);
                foreach (var open in documents) AssertContent(open);
                Capture(window, "mdi-narrow-2x.png");

                manager.FloatAllWindows();
                Render(window);
                foreach (var document in documents)
                {
                    var content = (Control)document.Context!;
                    Assert.NotNull(TopLevel.GetTopLevel(content));
                    Assert.True(content.IsEffectivelyVisible);
                }
                manager.TileAllWindows();
                Render(window);
                foreach (var open in documents) AssertContent(open);
                Assert.Equal(beforeAdd + 1, operandGrid.ItemsSource.Cast<object>().Count());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task MdiChromeStaysCompactAndWindowCommandsPreserveContent(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceMdiInteractionTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var application = WorkbenchApplication.Create("tessar");
            using var manager = CreateManager(application);
            var window = new Window { Width = 1300, Height = 800, Content = manager.WorkspaceControl };
            try
            {
                window.Show();
                Render(window);
                manager.TileAllWindows();
                Render(window);
                var viewer = manager.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var content = (Control)viewer.Context!;
                var mdi = content.FindAncestorOfType<MdiDocumentWindow>()!;
                var buttons = mdi.GetVisualDescendants().OfType<Button>()
                    .Where(b => ReferenceEquals(b.TemplatedParent, mdi)).ToArray();
                foreach (var button in buttons.Where(b => b.IsEffectivelyVisible))
                {
                    Assert.Equal(new Size(20, 20), button.Bounds.Size);
                    Assert.Equal(new Thickness(0), button.BorderThickness);
                    var icon = button.GetVisualDescendants().OfType<Viewbox>().Single();
                    Assert.Equal(12, icon.Bounds.Width);
                    Assert.InRange(icon.Bounds.Height, 1, 12);
                }
                var maximize = buttons.Single(b => b.Name == "PART_MaximizeRestoreButton");
                var minimize = buttons.Single(b => b.Name == "PART_MinimizeButton");
                var close = buttons.Single(b => b.Name == "PART_CloseButton");
                var header = mdi.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Header");
                Assert.InRange(header.Bounds.Height, 24, 36);
                Assert.True(mdi.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_ContentBorder").ClipToBounds);
                if (theme == "Light")
                {
                    var point = Point(close, window);
                    var presenter = close.GetVisualDescendants().OfType<ContentPresenter>().First(p => ReferenceEquals(p.TemplatedParent, close));
                    window.MouseMove(point);
                    Render(window);
                    Equal("#EAF1FF", presenter.Background);
                    window.MouseDown(point, MouseButton.Left);
                    Render(window);
                    Equal("#CDDDFA", presenter.Background);
                    // Release outside cancels close and restores the normal title button.
                    window.MouseMove(new Point(2, 2));
                    window.MouseUp(new Point(2, 2), MouseButton.Left);
                    Render(window);
                    Assert.Contains(viewer, manager.Factory.OpenDocuments());
                    Equal("#FFFFFF", presenter.Background);
                    close.Focus(NavigationMethod.Tab);
                    Assert.NotNull(close.FocusAdorner);
                }
                Click(window, maximize);
                Assert.Equal(MdiWindowState.Maximized, viewer.MdiState);
                AssertContent(viewer);
                Click(window, maximize);
                Assert.Equal(MdiWindowState.Normal, viewer.MdiState);
                minimize.IsEnabled = false;
                Click(window, minimize);
                Assert.Equal(MdiWindowState.Normal, viewer.MdiState);
                minimize.IsEnabled = true;
                Click(window, minimize);
                Assert.Equal(MdiWindowState.Minimized, viewer.MdiState);
                manager.TileAllWindows();
                Render(window);
                AssertContent(viewer);
                Capture(window, $"mdi-compact-{theme}.png");
                close.Focus(NavigationMethod.Tab);
                Key(window, Avalonia.Input.Key.Enter);
                Assert.DoesNotContain(viewer, manager.Factory.OpenDocuments());
                manager.ShowViewer(OpticSceneViewMode.TwoDimensional);
                Render(window);
                var reopened = manager.Factory.OpenDocuments().Single(d => d.Id == viewer.Id);
                Assert.Null(content.Parent);
                Assert.IsType<ViewerPanel>(reopened.Context);
                AssertContent(reopened);
                var lens = manager.Factory.OpenDocuments().Single(d => d.Id == WorkspaceDockFactory.LensDocumentId);
                var lensMdi = ((Control)lens.Context!).FindAncestorOfType<MdiDocumentWindow>()!;
                Assert.False(lensMdi.GetVisualDescendants().OfType<Button>()
                    .Single(b => ReferenceEquals(b.TemplatedParent, lensMdi) && b.Name == "PART_CloseButton").IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static PanelManager CreateManager(WorkbenchApplication application) => new(application, new AppSettings(),
        new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "optical-mdi-tests", Guid.NewGuid().ToString("N"))));

    private static Button ButtonWithText(Control parent, string text) => parent.GetVisualDescendants().OfType<Button>()
        .Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static Point Point(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Control control)
    {
        var point = Point(control, window);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    private static void Key(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Render(window);
    }

    private static void Equal(string color, IBrush? brush) =>
        Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static void AssertContent(Document document)
    {
        var content = Assert.IsAssignableFrom<Control>(document.Context);
        Assert.True(content.IsEffectivelyVisible, $"{document.Id} is hidden; parent={content.Parent}");
        var mdi = content.FindAncestorOfType<MdiDocumentWindow>();
        Assert.True(mdi is not null, $"{document.Id}: {string.Join(" > ", content.GetVisualAncestors().Select(v => v.GetType().Name))}");
        Assert.Same(document, mdi.DataContext);
        Assert.True(content.Bounds.Width > 0 && content.Bounds.Height > 0);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_MDI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width * 2, (int)window.Height * 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
