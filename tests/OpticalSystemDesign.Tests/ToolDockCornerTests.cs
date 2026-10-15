using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ToolDockCornerTests
{
    [Theory]
    [InlineData(240, 13)]
    [InlineData(280, 18)]
    public async Task SystemOptionsOuterFrameClipsItsHeaderToRoundedCorners(double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            var settings = new AppSettings { FontSize = fontSize, LeftPaneWidth = width };
            DisplayTypography.Configure(settings);
            using var panels = new PanelManager(application, settings,
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "tool-corners", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = 1000, Height = 700, Content = panels.WorkspaceControl };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                panels.ApplyLayout(new WorkspaceLayoutState(width));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var dock = Assert.Single(window.GetVisualDescendants().OfType<ToolDockControl>());
                var chrome = Assert.Single(dock.GetVisualDescendants().OfType<ToolChromeControl>());
                var header = Assert.Single(chrome.GetVisualDescendants().OfType<Border>(),
                    border => border.TemplatedParent == chrome && border.Name == "PART_Border");
                var originalSize = chrome.Bounds.Size;
                foreach (var theme in new[] { "Light", "Dark", "Pixel", "Isekai", "Light" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
                    foreach (var active in new[] { false, true })
                        foreach (var hovered in new[] { false, true })
                        {
                            chrome.IsActive = active;
                            ((IPseudoClasses)chrome.Classes).Set(":pointerover", hovered);
                            window.UpdateLayout();
                            Assert.Equal(theme == "Light" ? new CornerRadius(6, 6, 0, 0) : default, header.CornerRadius);
                            Assert.Equal(theme == "Light", header.ClipToBounds);
                            Assert.Equal(originalSize, chrome.Bounds.Size);
                            Assert.True(header.Bounds.Width > 0 && header.Bounds.Height > 0);
                            foreach (var name in new[] { "PART_MenuButton", "PART_PinButton" })
                            {
                                var button = Assert.Single(chrome.GetVisualDescendants().OfType<Button>(), button => button.Name == name);
                                Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.IsHitTestVisible);
                            }
                        }
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }
}
