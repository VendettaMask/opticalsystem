using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IsekaiRibbonDetailsTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(13, 1)]
    [InlineData(13, 1.5)]
    [InlineData(13, 2)]
    [InlineData(18, 1)]
    [InlineData(18, 1.5)]
    [InlineData(18, 2)]
    public async Task EngravingAndSwordFollowNativeSlotsWithoutMovingOrCoveringCommands(double fontSize, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiRibbonDetailsTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            var commands = new[] { Command("nonsequential-mode", "非序列模式"), Command("system-settings", "系统选项"),
                Command("lens-prescription", "镜头数据"), Command("configuration-matrix", "多重结构"),
                Command("format-font", "格式与字体"), Command("settings-language", "语言", true) };
            var page = (Control)Invoke("BuildRibbonPage", new object[] { new[]
                { Group(commands[..1]), Group(commands[1..4]), Group(commands[4..5]), Group(commands[5..]) } });
            var tabs = new TabControl { SelectedIndex = 1, ItemsSource = new[]
                { Tab("文件", new Border { Height = 42 }), Tab("设置", page), Tab("视图", new Border { Height = 42 }) } };
            var frame = MainWindow.BuildRibbonFrame(tabs);
            var root = MainWindow.BuildShellLayout(frame, new Border(), new Border { Height = 0 });
            var window = new Window { Width = 1600, Height = 220, Content = root };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                var overlays = frame.GetVisualDescendants().OfType<ThemeChromeOverlay>().ToArray();
                var foreground = overlays.Single(o => o.ForegroundDecoration);
                foreach (var width in new[] { 1600d, 1120d, 640d, 1600d })
                {
                    window.Width = width; Tick(window);
                    var slots = foreground.RibbonLayout;
                    Assert.True(slots.IsReady);
                    Assert.True(slots.SwordBand.Top >= slots.MenuBand.Bottom);
                    Assert.True(slots.SwordBand.Bottom <= slots.Commands.Top + .01);
                    var decoration = IsekaiRibbonAccents.EngravingBounds(slots.VacantEnd);
                    if (width == 1600) Assert.True(decoration.Width > 0, slots.ToString());
                    if (decoration.Width > 0)
                    {
                        Assert.True(decoration.Left > slots.Commands.Right);
                        Assert.True(decoration.Right <= foreground.Bounds.Width);
                        Assert.True(decoration.Top >= slots.Commands.Top && decoration.Bottom <= slots.Commands.Bottom);
                    }
                    else Assert.True(slots.VacantEnd.Width < 66 || slots.VacantEnd.Height < 18);
                    var before = commands.Select(command => command.Bounds).ToArray();
                    foreground.IsVisible = false; Tick(window);
                    Assert.Equal(before, commands.Select(command => command.Bounds));
                    using var noEngraving = window.CaptureRenderedFrame();
                    foreground.IsVisible = true; Tick(window);
                    Assert.Equal(before, commands.Select(command => command.Bounds));
                    Assert.False(foreground.IsHitTestVisible);
                    using var composed = window.CaptureRenderedFrame();
                    Assert.NotNull(composed); Assert.NotNull(noEngraving);
                    Assert.Equal((int)Math.Round(window.ClientSize.Width * scaling), composed.PixelSize.Width);
                    if (decoration.Width > 0) Assert.True(Differences(noEngraving, composed,
                        decoration.TransformToAABB(foreground.TransformToVisual(window)!.Value), scaling) > 0);
                    Save(window, $"ribbon-{fontSize}-{scaling}-{width}.png");
                }
                // Pointer and keyboard still operate the native tab and dropdown above the ink layer.
                var selectedBefore = foreground.RibbonLayout.SelectedTab;
                var view = tabs.Items.Cast<TabItem>().Last(); Click(window, view); Tick(window);
                Assert.Equal(2, tabs.SelectedIndex);
                Assert.NotEqual(selectedBefore, foreground.RibbonLayout.SelectedTab);
                Click(window, tabs.Items.Cast<TabItem>().ElementAt(1)); Tick(window);
                var language = (DropDownButton)commands[^1];
                Click(window, language); Tick(window); Assert.True(language.Flyout!.IsOpen);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                Assert.False(language.Flyout.IsOpen);
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel", "Isekai" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window);
                    if (theme == "Isekai") continue;
                    using var empty = new RenderTargetBitmap(new PixelSize(1600, 220)); empty.Render(foreground);
                    using var data = new MemoryStream(); empty.Save(data, PngBitmapEncoderOptions.Default);
                    using var bitmap = SKBitmap.Decode(data.ToArray());
                    Assert.True(bitmap.Pixels.All(pixel => pixel.Alpha == 0));
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static int Differences(Bitmap first, Bitmap second, Rect rect, double scale)
    {
        using var a = new MemoryStream(); first.Save(a, PngBitmapEncoderOptions.Default);
        using var b = new MemoryStream(); second.Save(b, PngBitmapEncoderOptions.Default);
        using var before = SKBitmap.Decode(a.ToArray()); using var after = SKBitmap.Decode(b.ToArray());
        var count = 0;
        for (var y = Math.Max(0, (int)(rect.Top * scale)); y < Math.Min(after.Height, (int)Math.Ceiling(rect.Bottom * scale)); y++)
            for (var x = Math.Max(0, (int)(rect.Left * scale)); x < Math.Min(after.Width, (int)Math.Ceiling(rect.Right * scale)); x++)
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) count++;
        return count;
    }
    private static Button Command(string icon, string text, bool dropdown = false)
    {
        var button = dropdown ? new DropDownButton { Flyout = new MenuFlyout { Items = { new MenuItem { Header = "简体中文" } } } } : new Button();
        button.Content = Invoke(dropdown ? "RibbonDropDownCommandContent" : "RibbonCommandContent", icon, text);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Classes.Add("ribbon-command"); if (dropdown) button.Classes.Add("ribbon-dropdown");
        AutomationProperties.SetName(button, text);
        Invoke("BindRibbonCommandLayout", button); Invoke("BindRibbonCommandChrome", button);
        return button;
    }
    private static TabItem Tab(string title, Control content) => (TabItem)Invoke("RibbonTab", title, content);
    private static Border Group(Button[] commands) => (Border)Invoke("RibbonGroup", "", true, commands.Cast<Control>().ToArray());
    private static object Invoke(string name, params object[] args) => typeof(MainWindow).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(null, args)!;
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Save(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RIBBON_DETAILS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); using var image = window.CaptureRenderedFrame();
        image!.Save(Path.Combine(folder, name), PngBitmapEncoderOptions.Default);
    }
}
