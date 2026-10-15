using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CompactRibbonTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOOLBAR_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture }).UseHarfBuzz();
    }

    [Theory]
    [InlineData("Light", 736, 13)]
    [InlineData("Light", 640, 13)]
    [InlineData("Light", 640, 18)]
    [InlineData("Dark", 736, 13)]
    [InlineData("Pixel", 736, 13)]
    [InlineData("Isekai", 736, 13)]
    public async Task CommandsStayHorizontalWithShortSeparatorsAndNativeOverflow(string theme, double width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactRibbonTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            var buttons = new[]
            {
                Button("file-plus", "新建"), Button("folder-open", "打开"), Button("save", "保存"),
                Button("download", "导入", true), Button("upload", "导出", true), Button("aperture", "示例", true),
                Button("undo", "撤销"), Button("redo", "重做")
            };
            MainWindow.UpdateHistoryButtons(buttons[6], buttons[7], true, false);
            var groups = new[]
            {
                Group("文件", true, buttons[..3]), Group("交换与示例", true, buttons[3..6]), Group("编辑", false, buttons[6..])
            };
            var page = (ScrollViewer)Invoke("BuildRibbonPage", new object[] { groups });
            var tabs = new TabControl
            {
                SelectedIndex = 0,
                ItemsSource = new[] { (TabItem)Invoke("RibbonTab", "文件", page) }
            };
            var ribbon = MainWindow.BuildRibbonFrame(tabs);
            var workspace = new Border();
            var layout = MainWindow.BuildShellLayout(ribbon, workspace, new Border { Height = 0 });
            var window = new Window { Width = width, Height = 150, Content = layout };
            DisplayTypography.Apply(window);
            window.Show();
            try
            {
                Tick(window);
                AssertFrame(ribbon, workspace, window, theme);
                Assert.Equal(8, buttons.Length);
                Assert.Equal(2, groups.SelectMany(g => g.GetVisualDescendants()).OfType<Border>().Count(b => b.Name == "RibbonGroupDivider"));
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, page.HorizontalScrollBarVisibility);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, page.VerticalScrollBarVisibility);
                Assert.All(buttons, button =>
                {
                    var content = Assert.IsType<Grid>(button.Content);
                    var icon = Assert.Single(content.Children.OfType<LocalIcon>());
                    var label = Assert.Single(content.Children.OfType<TextBlock>());
                    Assert.Equal(18, icon.Width);
                    Assert.Equal(18, icon.Height);
                    Assert.Equal(6, content.ColumnSpacing);
                    Assert.Equal(0, Grid.GetColumn(icon));
                    Assert.Equal(1, Grid.GetColumn(label));
                    Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
                    Assert.True(button.Bounds.Width >= 56 && button.Bounds.Height >= 36);
                    Assert.True(icon.Bounds.Right <= label.Bounds.Left);
                    Assert.InRange(Math.Abs(icon.Bounds.Center.Y - label.Bounds.Center.Y), 0, 0.5);
                    if (theme == "Light" && button.IsEnabled)
                        Assert.Equal(BlueThemeTokens.Accent, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color);
                });
                Assert.True(page.Bounds.Height <= (fontSize == 13 ? 52 : 60), $"Toolbar too tall: {page.Bounds.Height}");
                foreach (var group in groups)
                {
                    Assert.Equal(new Thickness(0), group.BorderThickness);
                    var divider = group.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == "RibbonGroupDivider");
                    if (divider is null) continue;
                    Assert.Equal(22, divider.Bounds.Height);
                    Assert.False(divider.IsHitTestVisible);
                    Assert.Equal(VerticalAlignment.Center, divider.VerticalAlignment);
                }
                Assert.Equal(1, buttons[3].Content is Grid import ? import.Children.OfType<Polygon>().Count() : 0);
                Assert.Equal(1, buttons[4].Content is Grid export ? export.Children.OfType<Polygon>().Count() : 0);
                Assert.Equal(1, buttons[5].Content is Grid example ? example.Children.OfType<Polygon>().Count() : 0);
                if (theme == "Light" && width == 736 && fontSize == 13)
                    Assert.True(page.Extent.Width <= page.Viewport.Width + 1);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOOLBAR_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)width * 2, 300), new Vector(192, 192));
                    bitmap.Render(window);
                    var path = System.IO.Path.Combine(directory, $"toolbar-{theme}-{width:0}-{fontSize:0}.png");
                    bitmap.Save(path, PngBitmapEncoderOptions.Default);
                    if (theme is "Light" or "Dark") AssertRenderedCorners(path, ribbon, layout);
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Pixel")]
    [InlineData("Isekai")]
    public async Task FrameResourcesSurviveThemePageAndSizeChanges(string initialTheme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactRibbonTests));
        await session.Dispatch(() =>
        {
            var application = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(application, initialTheme);
            var tabs = new TabControl
            {
                SelectedIndex = 0,
                ItemsSource = new[]
                {
                    (TabItem)Invoke("RibbonTab", "文件", new Border { Height = 48 }),
                    (TabItem)Invoke("RibbonTab", "设置", new Border { Height = 60 })
                }
            };
            var ribbon = MainWindow.BuildRibbonFrame(tabs);
            var workspace = new Border();
            var layout = MainWindow.BuildShellLayout(ribbon, workspace, new Border { Height = 20 });
            var window = new Window { Width = 736, Height = 180, Content = layout };
            window.Show();
            try
            {
                foreach (var theme in new[] { initialTheme, "Pixel", "Isekai", "Dark", "Light", initialTheme })
                {
                    ThemeApplicationService.Apply(application, theme);
                    tabs.SelectedIndex = 1 - tabs.SelectedIndex;
                    window.Width = window.Width == 736 ? 480 : 736;
                    Tick(window);
                    AssertFrame(ribbon, workspace, window, theme);
                    Assert.Equal(ThemeRegistry.FromSettings(theme).Palette!.Workspace,
                        Assert.IsAssignableFrom<ISolidColorBrush>(layout.Background).Color);
                    Assert.Equal(window.ClientSize.Width, ribbon.Bounds.Width);
                    Assert.True(workspace.Bounds.Height > 0);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertFrame(Border ribbon, Control workspace, Window window, string theme)
    {
        var radius = theme is "Light" or "Dark" ? new CornerRadius(0, 0, 8, 8) : new CornerRadius(0);
        Assert.Equal(radius, ribbon.CornerRadius);
        Assert.True(ribbon.ClipToBounds);
        Assert.Equal(new Thickness(0, 0, 0, 8), ribbon.Margin);
        var ribbonTop = ribbon.TranslatePoint(default, window)!.Value;
        var workspaceTop = workspace.TranslatePoint(default, window)!.Value;
        Assert.Equal(0, ribbonTop.X);
        Assert.Equal(0, ribbonTop.Y);
        Assert.Equal(8, workspaceTop.Y - ribbonTop.Y - ribbon.Bounds.Height);
    }

    private static void AssertRenderedCorners(string path, Border ribbon, DockPanel layout)
    {
        using var image = SKBitmap.Decode(path);
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(layout.Background).Color;
        var y = (int)(ribbon.Bounds.Bottom * 2) - 3;
        foreach (var x in new[] { 1, image.Width - 2 })
        {
            var corner = image.GetPixel(x, y);
            Assert.InRange(Math.Abs(corner.Red - expected.R), 0, 2);
            Assert.InRange(Math.Abs(corner.Green - expected.G), 0, 2);
            Assert.InRange(Math.Abs(corner.Blue - expected.B), 0, 2);
        }
        // The middle remains the command-page fill, not an entirely missing row.
        Assert.NotEqual(image.GetPixel(1, y), image.GetPixel(image.Width / 2, y));
    }

    [Fact]
    public async Task HistoryAvailabilityExplainsDisabledCommandsAndClearsStaleReasons()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CompactRibbonTests));
        await session.Dispatch(() =>
        {
            var undo = Button("undo", "撤销");
            var redo = Button("redo", "重做");
            MainWindow.UpdateHistoryButtons(undo, redo, false, false);
            Assert.False(undo.IsEnabled); Assert.False(redo.IsEnabled);
            Assert.Equal("当前没有可撤销的操作", ToolTip.GetTip(undo));
            Assert.Equal("当前没有可重做的操作", AutomationProperties.GetHelpText(redo));
            Assert.True(ToolTip.GetShowOnDisabled(undo));
            MainWindow.UpdateHistoryButtons(undo, redo, true, true);
            Assert.True(undo.IsEnabled); Assert.True(redo.IsEnabled);
            Assert.Null(AutomationProperties.GetHelpText(undo));
            Assert.Null(AutomationProperties.GetHelpText(redo));
            Assert.Equal("撤销", ToolTip.GetTip(undo));
            Assert.Equal("重做", ToolTip.GetTip(redo));
            MainWindow.UpdateHistoryButtons(null, null, false, false);
        }, CancellationToken.None);
    }

    private static Button Button(string icon, string label, bool dropdown = false)
    {
        var content = (Control)Invoke(dropdown ? "RibbonDropDownCommandContent" : "RibbonCommandContent", icon, label);
        var button = dropdown ? new DropDownButton() : new Button();
        button.Content = content;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Classes.Add("ribbon-command");
        if (dropdown) button.Classes.Add("ribbon-dropdown");
        AutomationProperties.SetName(button, label);
        Invoke("BindRibbonCommandLayout", button);
        Invoke("BindRibbonCommandChrome", button);
        Invoke("AttachRibbonCommandHover", button, content);
        return button;
    }

    private static Border Group(string title, bool divider, Button[] buttons) =>
        (Border)Invoke("RibbonGroup", title, divider, buttons.Cast<Control>().ToArray());

    private static object Invoke(string name, params object[] arguments) =>
        typeof(MainWindow).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == arguments.Length)
            .Invoke(null, arguments)!;

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
