using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MainWindowTitleBarTests
{
    [Fact]
    public async Task CaptionTracksDocumentTitleAndThemeWithoutReplacingNativeWindowActions()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            var window = new Window { Title = "Untitled optic", Width = 640, Height = 480 };
            var content = new Border();
            var root = Assert.IsType<DockPanel>(MainWindowTitleBar.Wrap(window, content));
            window.Content = root;
            window.Show();
            try
            {
                window.UpdateLayout();
                var caption = Assert.IsType<MainWindowTitleBar>(root.Children[0]);
                var title = Assert.IsType<TextBlock>(caption.Child);
                Assert.Same(content, root.Children[1]);
                Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                Assert.True(window.CanMinimize && window.CanMaximize && window.CanResize);
                Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(caption));
                Assert.False(title.IsHitTestVisible);
                Assert.Equal(Color.Parse("#2F6FD1"), ((ISolidColorBrush)caption.Background!).Color);
                Assert.Equal(Color.Parse("#4A82D8"), ((ISolidColorBrush)caption.BorderBrush!).Color);
                Assert.Equal(Color.Parse("#FFFFFF"), ((ISolidColorBrush)title.Foreground!).Color);
                window.Title = "Cooke * - 顺序模式 - Optical System Design";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(window.Title, title.Text);
                foreach (var theme in new[] { "Dark", "Pixel", "Isekai", "Light", "System", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var expected = OperatingSystem.IsMacOS() && window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
                    Assert.Equal(expected, window.ExtendClientAreaToDecorationsHint);
                    Assert.Equal(expected, caption.IsVisible);
                    Assert.Equal(expected ? 40 : -1, window.ExtendClientAreaTitleBarHeightHint);
                    Assert.Equal(expected ? 40 : 0, content.Bounds.Y);
                }
                window.WindowState = WindowState.FullScreen;
                Dispatcher.UIThread.RunJobs();
                Assert.False(caption.IsVisible);
                window.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(OperatingSystem.IsMacOS(), caption.IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
