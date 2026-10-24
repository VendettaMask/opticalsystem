using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class DockWorkspaceCornerTests
{
    [Theory]
    [InlineData(1280, 13)]
    [InlineData(1024, 18)]
    [InlineData(640, 13)]
    public async Task TwelveDipDockCornersFollowResizeResetAndThemeChanges(double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            var settings = new AppSettings { FontSize = fontSize, LeftPaneWidth = 256 };
            DisplayTypography.Configure(settings);
            using var application = WorkbenchApplication.Create();
            var revision = application.Events.Revision;
            using var panels = new PanelManager(application, settings,
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "dock-corners", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = width, Height = 700, Content = panels.WorkspaceControl };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); Tick(window);
                panels.ApplyLayout(new WorkspaceLayoutState(256)); Tick(window);
                AssertCorners(window, rounded: true);
                window.Width = width + 173; window.Height = 780; Tick(window);
                AssertCorners(window, rounded: true);
                foreach (var theme in new[] { "Dark", "Pixel", "Isekai", "Lumen", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme); Tick(window);
                    AssertCorners(window, rounded: theme == "Light");
                }
                panels.ResetLayout(); Tick(window);
                AssertCorners(window, rounded: true);
                Assert.Equal(2, panels.Factory.Descriptors.Count);
                Assert.Equal(WorkspaceDockFactory.LensDocumentId, panels.Factory.PrimaryDocumentDock!.ActiveDockable!.Id);
                Assert.Equal(revision, application.Events.Revision);
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertCorners(Window window, bool rounded)
    {
        var tool = Assert.Single(window.GetVisualDescendants().OfType<ToolDockControl>());
        var dock = Assert.Single(window.GetVisualDescendants().OfType<DocumentDockControl>());
        var document = Assert.Single(window.GetVisualDescendants().OfType<DocumentControl>());
        var frame = Assert.Single(document.GetVisualDescendants().OfType<Border>(),
            border => border.TemplatedParent == document && border.Name == "PART_Border");
        foreach (var visual in new Visual[] { tool, dock, frame })
        {
            if (!rounded) { Assert.Null(visual.Clip); continue; }
            var clip = Assert.IsType<RectangleGeometry>(visual.Clip);
            Assert.Equal(12, clip.RadiusX); Assert.Equal(12, clip.RadiusY);
            Assert.Equal(new Rect(visual.Bounds.Size), clip.Rect);
            Assert.True(clip.Rect.Width > 0 && clip.Rect.Height > 0);
        }
        if (!rounded) return;
        Assert.Equal(new CornerRadius(12), frame.CornerRadius);
        Assert.True(frame.ClipToBounds);
        Assert.Equal(1, frame.BorderThickness.Top);
        var selected = Assert.Single(document.GetVisualDescendants().OfType<DocumentTabStripItem>(), tab => tab.IsSelected);
        var body = Assert.Single(selected.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_TabBody");
        Assert.Equal(new Thickness(0, 0, 2, 0), body.BorderThickness);
        Assert.Equal(ThemeChromeResources.StandardDocumentTabCornerRadius, body.CornerRadius);
        var editor = Assert.Single(document.GetVisualDescendants().OfType<LensEditorPanel>());
        var content = Assert.IsType<DockPanel>(editor.Content);
        Assert.Equal(new Thickness(0), content.Margin);
        var origin = content.TranslatePoint(default, frame)!.Value;
        Assert.Equal(frame.BorderThickness.Left + frame.Padding.Left, origin.X, precision: 6);
        Assert.Equal(frame.Bounds.Width - frame.BorderThickness.Right - frame.Padding.Right,
            origin.X + content.Bounds.Width, precision: 6);
        Assert.Equal(frame.Bounds.Height - frame.BorderThickness.Bottom - frame.Padding.Bottom,
            origin.Y + content.Bounds.Height, precision: 6);
        foreach (var name in new[] { "PART_MenuButton", "PART_PinButton" })
        {
            var button = Assert.Single(tool.GetVisualDescendants().OfType<Button>(), b => b.Name == name);
            Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.IsHitTestVisible);
        }
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
