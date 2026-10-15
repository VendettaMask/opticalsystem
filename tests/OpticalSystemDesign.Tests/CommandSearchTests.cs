using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CommandSearchTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CHROME_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture }).UseHarfBuzz();
    }

    [Theory]
    [InlineData("Light", 640, 13)]
    [InlineData("Light", 1024, 13)]
    [InlineData("Light", 640, 18)]
    [InlineData("Dark", 640, 18)]
    [InlineData("Pixel", 640, 13)]
    [InlineData("Isekai", 640, 13)]
    public async Task CaptionKeepsSearchBesideTitleAndOutsideNativeButtons(string theme, double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            var actions = new ActionManager();
            actions.Register("viewer-2d", "显示二维布局", "视图", () => { });
            var search = new CommandSearchControl(actions);
            var window = new Window { Title = "Untitled optic - 顺序模式 - Optical System Design", Width = width, Height = 280 };
            var tabs = new TabControl
            {
                ItemsSource = new[]
            {
                MakeTab("文件(_F)"), MakeTab("设置(_S)"), MakeTab("视图(_V)"), MakeTab("分析(_A)"),
                MakeTab("优化(_O)"), MakeTab("公差(_T)"), MakeTab("加工与图纸"), MakeTab("数据库"),
                MakeTab("实验室"), MakeTab("窗口(_W)"), MakeTab("帮助(_H)")
            }
            };
            tabs.Classes.Add("ribbon-tabs");
            tabs.Background = Brushes.Transparent;
            var ribbon = new Border { Child = tabs };
            ribbon.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.HeaderBackground);
            window.Content = MainWindowTitleBar.Wrap(window, ribbon, search);
            DisplayTypography.Apply(window);
            window.Show();
            try
            {
                Tick(window);
                var caption = Assert.Single(window.GetVisualDescendants().OfType<MainWindowTitleBar>());
                if (!caption.IsVisible) return; // Other platforms retain native decorations.
                var title = Assert.Single(caption.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "MainWindowTitle");
                var searchPoint = search.TranslatePoint(default, window)!.Value;
                var titlePoint = title.TranslatePoint(default, window)!.Value;
                Assert.True(titlePoint.X + title.Bounds.Width <= searchPoint.X);
                Assert.True(searchPoint.X + search.Bounds.Width <= window.ClientSize.Width - (OperatingSystem.IsWindows() ? MainWindowTitleBar.WindowsCaptionButtonsWidth : 12));
                Assert.Equal(216, search.Bounds.Width);
                Assert.True(searchPoint.Y >= 0 && searchPoint.Y + search.Bounds.Height <= caption.Bounds.Bottom);
                Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(search));
                Assert.Equal(WindowDecorationsElementRole.User, WindowDecorationProperties.GetElementRole(search.SearchBox));
                Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                Assert.True(window.CanMinimize && window.CanMaximize && window.CanResize);
                if (theme == "Light")
                {
                    Assert.Same(BlueThemeTokens.HeaderGradient, caption.Background);
                    Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(search.SearchBox.Foreground).Color);
                    var stops = Assert.IsAssignableFrom<ILinearGradientBrush>(caption.Background).GradientStops;
                    Assert.Equal(Color.Parse("#1876E9"), stops[0].Color);
                    Assert.Equal(Color.Parse("#1455B8"), stops[1].Color);
                }
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CHROME_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)width * 2, 560), new Vector(192, 192));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(directory, $"chrome-{theme}-{width:0}-{fontSize:0}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SearchUsesRegisteredCommandsWithKeyboardNavigationAndEmptyState()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var actions = new ActionManager();
            var ran = new List<string>();
            actions.Register("view-first", "二维视图", "视图", () => ran.Add("first"));
            actions.Register("view-second", "三维视图", "视图", () => ran.Add("second"));
            var search = new CommandSearchControl(actions);
            var window = new Window { Width = 640, Height = 400, Content = search };
            window.Show();
            try
            {
                search.FocusSearch();
                search.SearchBox.Text = "view-";
                Tick(window);
                Assert.True(search.SearchBox.IsFocused);
                Assert.True(search.ResultsPopup.IsOpen);
                Assert.Equal(actions.Search("view-"), search.Results.ItemsSource);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.Equal(1, search.Results.SelectedIndex);
                await search.RunSelectedAsync();
                Tick(window);
                Assert.Equal(new[] { "second" }, ran);
                Assert.False(search.ResultsPopup.IsOpen);
                search.SearchBox.Text = "不存在的功能";
                Tick(window);
                Assert.Equal(0, search.Results.ItemCount);
                Assert.False(search.Results.IsVisible);
                Assert.True(search.ResultsPopup.IsOpen);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.False(search.ResultsPopup.IsOpen);
                await search.RunSelectedAsync();
                Assert.Single(ran);
                search.SearchBox.Text = "二维";
                Tick(window);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Tick(window);
                Assert.Equal(new[] { "second", "first" }, ran);
            }
            finally { window.Close(); Assert.False(search.ResultsPopup.IsOpen); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ControlQFocusesSearchSelectsQueryAndIsNotTakenWhenHidden()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(() =>
        {
            var actions = new ActionManager();
            actions.Register("view", "二维视图", "视图", () => { });
            var search = new CommandSearchControl(actions);
            var editor = new TextBox();
            var window = new Window { Width = 640, Height = 400, Content = new StackPanel { Children = { search, editor } } };
            window.KeyDown += (_, args) => search.TryHandleShortcut(args);
            window.Show();
            try
            {
                search.SearchBox.Text = "二维"; Tick(window); editor.Focus();
                window.KeyPress(Key.Q, RawInputModifiers.Control, PhysicalKey.None, null);
                window.KeyRelease(Key.Q, RawInputModifiers.Control, PhysicalKey.None, null);
                Assert.True(search.SearchBox.IsFocused);
                Assert.Equal("二维", search.SearchBox.SelectedText);
                Assert.True(search.ResultsPopup.IsOpen);
                search.IsVisible = false;
                var key = new KeyEventArgs { Key = Key.Q, KeyModifiers = KeyModifiers.Control };
                Assert.False(search.TryHandleShortcut(key));
                Assert.False(key.Handled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SearchUsesSharedFailureHandlingAndDoesNotExecuteTwice()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(async () =>
        {
            var actions = new ActionManager();
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            actions.Register("pending", "长任务", "测试", async () => { count++; await pending.Task; });
            actions.Register("failure", "失败任务", "测试", () => throw new InvalidOperationException("expected failure"));
            var failures = new List<ActionExecutionFailedEventArgs>();
            actions.ExecutionFailed += (_, args) => failures.Add(args);
            var search = new CommandSearchControl(actions);
            var window = new Window { Width = 640, Height = 400, Content = search };
            window.Show();
            try
            {
                search.FocusSearch(); search.SearchBox.Text = "pending"; Tick(window);
                var first = search.RunSelectedAsync();
                await search.RunSelectedAsync();
                Assert.Equal(1, count);
                pending.SetResult();
                await first;
                search.SearchBox.Text = "failure"; Tick(window);
                await search.RunSelectedAsync();
                Assert.Equal("failure", Assert.Single(failures).Action.Id);
                Assert.False(search.ResultsPopup.IsOpen);
                Assert.Equal("failure", search.SearchBox.Text);
            }
            finally { pending.TrySetResult(); window.Close(); }
        }, CancellationToken.None);
    }

    private static TabItem MakeTab(string text)
    {
        var tab = new TabItem { FontSize = DisplayTypography.Body, Header = new AccessText { Text = text }, Content = new Border(), Padding = new Thickness(10, 4) };
        tab.Classes.Add("ribbon-tab");
        return tab;
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
