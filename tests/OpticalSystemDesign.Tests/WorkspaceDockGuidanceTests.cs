using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WorkspaceDockGuidanceTests(ITestOutputHelper output)
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_DOCK_GUIDANCE_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(false, DockOperation.Left)]
    [InlineData(false, DockOperation.Right)]
    [InlineData(false, DockOperation.Top)]
    [InlineData(false, DockOperation.Bottom)]
    [InlineData(true, DockOperation.Left)]
    [InlineData(true, DockOperation.Right)]
    [InlineData(true, DockOperation.Top)]
    [InlineData(true, DockOperation.Bottom)]
    public async Task DraggingAPageShowsFourDirectionsAndAPreview(bool mdi, DockOperation direction)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            using var panels = new PanelManager(application, new AppSettings(),
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "dock-guidance-" + Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); Render(window);
                if (mdi) { panels.TileAllWindows(); Render(window); }
                var document = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var content = document.Context;
                var lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                var sidebar = window.GetVisualDescendants().OfType<ToolDockControl>().Single();
                var sidebarWidth = sidebar.Bounds.Width;
                var sidebarBounds = Bounds(sidebar, window);
                Assert.InRange(Math.Abs(sidebarBounds.Height - window.Bounds.Height), 0, 2);
                var compass = DragTo(window, document, lens);
                // Root-wide targets would split below/above the system sidebar.
                // Document drags must offer only the local document compass.
                Assert.Empty(window.GetVisualDescendants().OfType<GlobalDockTarget>());
                foreach (var operation in new[] { DockOperation.Left, DockOperation.Right, DockOperation.Top, DockOperation.Bottom })
                {
                    Assert.True(Part(compass, operation + "Selector").IsEffectivelyVisible);
                    Assert.True(Part(compass, operation + "Selector").Bounds.Width > 0);
                }
                var dropPoint = Hover(window, Part(compass, direction + "Selector"));
                var preview = Part(compass, direction + "Indicator");
                Assert.True(preview.Opacity > 0);
                var previewBounds = new Rect(preview.TranslatePoint(default, window)!.Value, preview.Bounds.Size);
                Assert.True(previewBounds.Left >= sidebarBounds.Right);
                var frame = Assert.Single(preview.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("workspace-dock-preview-frame"));
                Assert.Equal(Color.FromArgb(112, 128, 128, 128), Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color);
                Assert.Equal(Color.FromRgb(112, 112, 112), Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
                Capture(window, $"dock-{(mdi ? "mdi" : "tabs")}-{direction}-preview.png");
                window.MouseUp(dropPoint, MouseButton.Left); Render(window);
                Assert.Equal(2, panels.Factory.OpenDocuments().Count);
                Assert.NotSame(document.Owner, lens.Owner);
                Assert.Same(content, document.Context);
                Assert.Equal(sidebarWidth, sidebar.Bounds.Width, 2);
                Assert.Equal(sidebarBounds, Bounds(sidebar, window));
                Assert.InRange(sidebar.Bounds.Width, 240, 280);
                var groups = WorkspaceDockFactory.EnumerateDockables(panels.Layout).OfType<IDocumentDock>().ToArray();
                Assert.Equal(groups.Length, groups.Select(group => group.Id).Distinct().Count());
                var actual = ((Control)document.Context!).FindAncestorOfType<DocumentDockControl>()!;
                var actualBounds = new Rect(actual.TranslatePoint(default, window)!.Value, actual.Bounds.Size);
                output.WriteLine($"Preview={previewBounds}; actual={actualBounds}");
                Assert.InRange(Math.Abs(actualBounds.Width - previewBounds.Width), 0, 8);
                Assert.InRange(Math.Abs(actualBounds.Height - previewBounds.Height), 0, 8);
                Assert.InRange(Math.Abs(actualBounds.X - previewBounds.X), 0, 8);
                Assert.InRange(Math.Abs(actualBounds.Y - previewBounds.Y), 0, 8);
                Assert.Empty(window.GetVisualDescendants().OfType<DockTarget>());
                Capture(window, $"dock-{(mdi ? "mdi" : "tabs")}-{direction}-placed.png");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EscapeCancelsAndCenterMergesWithoutReplacingPageContent(bool mdi, bool reverseMerge)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); Render(window);
                if (mdi) { panels.TileAllWindows(); Render(window); }
                var source = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                var content = source.Context;
                var fullWidth = window.GetVisualDescendants().OfType<DocumentDockControl>().Single().Bounds.Width;
                var owner = source.Owner;
                var compass = DragTo(window, source, lens);
                var point = Hover(window, Part(compass, "LeftSelector"));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Same(owner, source.Owner);
                Assert.Empty(window.GetVisualDescendants().OfType<DockTarget>());
                Assert.False(panels.WorkspaceControl.IsDraggingDock);

                // A second press at exactly the same point is a native double-tap
                // (float the tab), not another drag. Grab a different title point.
                compass = DragTo(window, source, lens, grabOffset: 48);
                point = Hover(window, Part(compass, "RightSelector"));
                window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.NotSame(source.Owner, lens.Owner);
                compass = reverseMerge ? DragTo(window, lens, source) : DragTo(window, source, lens);
                var center = Part(compass, "CenterSelector");
                Assert.True(center.Opacity > 0);
                point = Hover(window, center);
                Assert.True(Part(compass, "CenterIndicator").Opacity > 0);
                window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Same(source.Owner, lens.Owner);
                Assert.Same(content, source.Context);
                Assert.Equal(2, panels.Factory.OpenDocuments().Count);
                var mergedPane = Assert.Single(window.GetVisualDescendants().OfType<DocumentDockControl>(),
                    item => ReferenceEquals(item.DataContext, source.Owner));
                Assert.Equal(fullWidth, mergedPane.Bounds.Width, 2);
                Assert.Single(WorkspaceDockFactory.EnumerateDockables(panels.Layout).OfType<IDocumentDock>());
                Assert.Same(lens.Owner, panels.Factory.PrimaryDocumentDock);
                Assert.Empty(window.GetVisualDescendants().OfType<DockTarget>());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", UiLocalization.Chinese, "停靠到左侧")]
    [InlineData("Dark", UiLocalization.English, "Dock Left")]
    [InlineData("Pixel", UiLocalization.Japanese, "左側にドッキング")]
    [InlineData("Isekai", UiLocalization.English, "Dock Left")]
    public async Task GuidanceUsesCurrentThemeAndLanguage(string theme, string language, string hint)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); panels.TileAllWindows(); Render(window);
                var documents = panels.Factory.OpenDocuments();
                var compass = DragTo(window, documents.Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId),
                    documents.Single(item => item.Id == WorkspaceDockFactory.LensDocumentId));
                var selector = Part(compass, "LeftSelector");
                Assert.Equal(hint, ToolTip.GetTip(selector));
                Assert.Equal(hint, AutomationProperties.GetName(selector));
                UiLocalization.Apply(UiLocalization.Chinese); Render(window);
                Assert.Equal("停靠到左侧", ToolTip.GetTip(selector));
                Assert.Equal("停靠到左侧", AutomationProperties.GetName(selector));
                UiLocalization.Apply(language); Render(window);
                Assert.Equal(hint, ToolTip.GetTip(selector));
                var plate = compass.GetVisualDescendants().OfType<Border>().First(item => item.Classes.Contains("workspace-dock-selector-frame"));
                Assert.NotNull(plate.Background);
                var point = Hover(window, selector);
                Capture(window, $"dock-guidance-{theme}-{language}.png");
                window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Equal(2, panels.Factory.OpenDocuments().Count);
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(DockOperation.Left)]
    [InlineData(DockOperation.Right)]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    public async Task SplitLayoutRoundTripsThroughExplicitSlot(DockOperation direction)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(async () =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); Render(window);
                var source = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                var compass = DragTo(window, source, lens);
                var point = Hover(window, Part(compass, direction + "Selector"));
                window.MouseUp(point, MouseButton.Left); Render(window);
                var before = Bounds((Control)source.Context!, window);
                await panels.SaveLayoutSlotAsync(1);
                panels.DockToSinglePane(); Render(window);
                await panels.LoadLayoutSlotAsync(1); Render(window);
                source = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                Assert.NotSame(source.Owner, lens.Owner);
                Assert.Equal(before, Bounds((Control)source.Context!, window));
                Assert.True(((Control)source.Context!).IsEffectivelyVisible);
                Assert.Equal(2, panels.Factory.OpenDocuments().Count);
                var sidebar = window.GetVisualDescendants().OfType<ToolDockControl>().Single();
                Assert.InRange(Math.Abs(sidebar.Bounds.Height - window.Bounds.Height), 0, 2);
                AssertDocumentBoundary(panels);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DocumentBoundaryAppliesToNewAndPreviouslySavedLayouts(bool nonSequential, bool restored)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(async () =>
        {
            using var application = WorkbenchApplication.Create();
            if (nonSequential) application.Modes.SwitchTo(OpticalSystemDesign.Application.Contracts.OpticalWorkbenchMode.NonSequential);
            using var panels = CreateManager(application);
            panels.ShowUserGuide();
            if (restored)
            {
                // Layouts written before this boundary had no group metadata.
                foreach (var item in WorkspaceDockFactory.EnumerateDockables(panels.Layout)) item.DockGroup = null;
                await panels.SaveLayoutSlotAsync(1);
                await panels.LoadLayoutSlotAsync(1);
            }
            AssertDocumentBoundary(panels);
            var page = panels.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
                "document:boundary-guide", WorkspaceDocumentTypes.UserGuide, "用户指南"));
            AssertDocumentBoundary(panels);
            Assert.True(page.CanFloat);
            Assert.True(page.CanDrag);
            return true;
        }, CancellationToken.None);
    }

    private static void AssertDocumentBoundary(PanelManager panels)
    {
        var dockables = WorkspaceDockFactory.EnumerateDockables(panels.Layout).ToArray();
        var sidebar = dockables.Single(item => item.Id == WorkspaceDockFactory.ToolDockId);
        var system = dockables.Single(item => item.Id == WorkspaceDockFactory.SystemToolId);
        var documents = panels.Factory.OpenDocuments();
        foreach (var source in dockables.Where(item => item is (IDocument and not ITool) or IDocumentDock))
        {
            Assert.False(string.IsNullOrEmpty(DockGroupValidator.GetEffectiveDockGroup(source)));
            Assert.False(DockGroupValidator.ValidateGlobalDocking(source, panels.Layout),
                $"Source {source.Id} group={DockGroupValidator.GetEffectiveDockGroup(source)}; root={panels.Layout.DockGroup}");
            Assert.False(DockGroupValidator.ValidateDockingGroups(source, sidebar));
            Assert.False(DockGroupValidator.ValidateDockingGroups(source, system));
            foreach (var target in documents) Assert.True(DockGroupValidator.ValidateDockingGroups(source, target));
        }
        // Sidebar pin/float and tool-to-root docking remain deliberately available.
        Assert.True(system.CanPin);
        Assert.True(system.CanFloat);
        Assert.True(DockGroupValidator.ValidateGlobalDocking(system, panels.Layout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraggingOverSystemOptionsDoesNotOfferDocumentDocking(bool mdi)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); Render(window);
                if (mdi) { panels.TileAllWindows(); Render(window); }
                var source = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.Viewer2DDocumentId);
                var lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                var owner = source.Owner;
                var sidebar = window.GetVisualDescendants().OfType<ToolDockControl>().Single();
                var before = Bounds(sidebar, window);
                DragTo(window, source, lens);
                var point = Hover(window, sidebar);
                Assert.Empty(window.GetVisualDescendants().OfType<DockTargetBase>());
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Same(owner, source.Owner);
                Assert.Equal(before, Bounds(sidebar, window));
                Assert.False(panels.WorkspaceControl.IsDraggingDock);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedSplitsUseOnlyTheTargetPaneAndPreserveEveryPage()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(WorkspaceDockGuidanceTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            panels.ShowUserGuide();
            var window = new Window { Width = 1400, Height = 900, Content = panels.WorkspaceControl };
            try
            {
                window.Show(); Render(window);
                var lens = panels.Factory.OpenDocuments().Single(item => item.Id == WorkspaceDockFactory.LensDocumentId);
                var sources = panels.Factory.OpenDocuments().Where(item => !ReferenceEquals(item, lens)).ToArray();
                var contents = sources.Select(item => item.Context).ToArray();
                foreach (var source in sources)
                {
                    var compass = DragTo(window, source, lens);
                    var point = Hover(window, Part(compass, "LeftSelector"));
                    var preview = Bounds(Part(compass, "LeftIndicator"), window);
                    window.MouseUp(point, MouseButton.Left); Render(window);
                    var actual = Bounds(((Control)source.Context!).FindAncestorOfType<DocumentDockControl>()!, window);
                    Assert.InRange(Math.Abs(actual.Width - preview.Width), 0, 8);
                    Assert.InRange(Math.Abs(actual.X - preview.X), 0, 8);
                }
                Assert.Equal(3, panels.Factory.OpenDocuments().Count);
                Assert.Equal(3, panels.Factory.OpenDocuments().Select(item => item.Owner).Distinct().Count());
                for (var index = 0; index < sources.Length; index++) Assert.Same(contents[index], sources[index].Context);
                var groups = WorkspaceDockFactory.EnumerateDockables(panels.Layout).OfType<IDocumentDock>().ToArray();
                Assert.Equal(3, groups.Select(group => group.Id).Distinct().Count());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static PanelManager CreateManager(WorkbenchApplication application) => new(application, new AppSettings(),
        new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "dock-guidance-" + Guid.NewGuid().ToString("N"))));

    private static DockTarget DragTo(Window window, Document source, Document target, double grabOffset = 18)
    {
        Control dragArea;
        var page = window.GetVisualDescendants().OfType<MdiDocumentWindow>().SingleOrDefault(item => ReferenceEquals(item.DataContext, source));
        if (page is not null)
        {
            Assert.False(page.GetValue(DockProperties.ShowDockIndicatorOnlyProperty));
            dragArea = page.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_DragHandle");
        }
        else dragArea = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(item => ReferenceEquals(item.DataContext, source));
        var start = dragArea.TranslatePoint(new Point(grabOffset, dragArea.Bounds.Height / 2), window)!.Value;
        window.MouseMove(start); window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(18, dragArea.Bounds.Height + 16), RawInputModifiers.LeftMouseButton); Render(window);
        var targetContent = (Control)target.Context!;
        Control targetPage = targetContent.FindAncestorOfType<MdiDocumentWindow>() is { } targetMdi ? targetMdi
            : window.GetVisualDescendants().OfType<DocumentDockControl>().Single(item => ReferenceEquals(item.DataContext, target.Owner));
        Hover(window, targetPage);
        return Assert.Single(window.GetVisualDescendants().OfType<DockTarget>());
    }

    private static Point Hover(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.LeftMouseButton); Render(window);
        window.MouseMove(point, RawInputModifiers.LeftMouseButton); Render(window);
        return point;
    }

    private static Rect Bounds(Control control, Window window) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static Control Part(DockTarget compass, string name) => compass.GetVisualDescendants()
        .OfType<Control>().Single(control => control.Name == "PART_" + name);

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_DOCK_GUIDANCE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
