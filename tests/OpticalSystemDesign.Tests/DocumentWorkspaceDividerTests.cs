using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class DocumentWorkspaceDividerTests
{
    [Theory]
    [InlineData(1280, 13)]
    [InlineData(1024, 18)]
    [InlineData(640, 13)]
    public async Task ActiveDocumentWorkspaceUsesOnlyASubtleSinglePixelDivider(double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var settings = new AppSettings { FontSize = fontSize, LeftPaneWidth = 256 };
            DisplayTypography.Configure(settings);
            using var application = WorkbenchApplication.Create();
            using var panels = new PanelManager(application, settings,
                new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "document-divider", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = width, Height = 700, Content = panels.WorkspaceControl };
            DisplayTypography.Apply(window);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                panels.ApplyLayout(new WorkspaceLayoutState(256));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var document = Assert.Single(window.GetVisualDescendants().OfType<DocumentControl>());
                var strip = Assert.Single(document.GetVisualDescendants().OfType<DocumentTabStrip>());
                var stripeHost = Assert.Single(document.GetVisualDescendants().OfType<Panel>(),
                    panel => panel.TemplatedParent == document && panel.Name == "PART_DocumentSeperatorHost");
                var frame = Assert.Single(document.GetVisualDescendants().OfType<Border>(),
                    border => border.TemplatedParent == document && border.Name == "PART_Border");
                foreach (var theme in new[] { "Light", "Dark", "Pixel", "Isekai", "Light" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
                    foreach (var active in new[] { false, true })
                        foreach (var hovered in new[] { false, true })
                        {
                            document.IsActive = active;
                            strip.IsActive = active;
                            ((IPseudoClasses)document.Classes).Set(":pointerover", hovered);
                            window.UpdateLayout();
                            if (theme == "Light")
                            {
                                Assert.Equal(0, stripeHost.MaxHeight);
                                Assert.True(stripeHost.ClipToBounds);
                                if (stripeHost.IsVisible) Assert.Equal(0, stripeHost.Bounds.Height);
                                Assert.Equal(1, frame.BorderThickness.Top);
                                Assert.Equal(BlueThemeTokens.Divider, Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
                                Assert.Equal(strip.Bounds.Bottom, frame.Bounds.Top);
                                var selected = Assert.Single(strip.GetVisualDescendants().OfType<DocumentTabStripItem>(), tab => tab.IsSelected);
                                var body = Assert.Single(selected.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_TabBody");
                                Assert.Equal(new Thickness(0, 0, 2, 0), body.BorderThickness);
                                Assert.Equal(BlueThemeTokens.Accent, Assert.IsAssignableFrom<ISolidColorBrush>(body.BorderBrush).Color);
                            }
                            else
                            {
                                Assert.True(double.IsPositiveInfinity(stripeHost.MaxHeight));
                                Assert.False(stripeHost.ClipToBounds);
                            }
                        }
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }
}
