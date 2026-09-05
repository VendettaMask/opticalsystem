using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisSettingsFooterLayoutTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_FOOTER_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) { builder.UseSkia(); }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Image Simulation")]
    public async Task SettingsActionsAreCompactCenteredAndGrowWithLargerText(string analysisName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisSettingsFooterLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            using var analysis = new AnalysisPanel(application.Analyses, application.Visualization,
                application.Documents, application.Events, new AppSettings(), analysisName)
            { IsLocked = true };
            var settings = Assert.IsType<Border>(typeof(AnalysisPanel)
                .GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(analysis));
            settings.IsVisible = true;
            var window = new Window { Width = 900, Height = 700, Content = analysis };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var labels = new[] { "应用", "确定", "取消", "保存", "载入", "重置" };
                var buttons = settings.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Content is string text && labels.Contains(text)).ToArray();
                Assert.Equal(labels, buttons.Select(button => button.Content));
                var footer = Assert.IsType<WrapPanel>(buttons[0].Parent);
                Assert.All(buttons, button =>
                {
                    Assert.Same(footer, button.Parent);
                    Assert.Equal(64, button.MinWidth);
                    Assert.InRange(button.Bounds.Width, 64, 70);
                    Assert.Equal(new Thickness(2, 4), button.Margin);
                    Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
                    Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
                    AssertRenderedTextCentered(button);
                });
                Assert.True(buttons.Sum(button => button.Bounds.Width + button.Margin.Left + button.Margin.Right) < 6 * 86);
                var directory = Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_FOOTER_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    bitmap.Save(Path.Combine(directory, analysisName.Replace(' ', '-') + ".png"), PngBitmapEncoderOptions.Default);
                }

                // MinWidth, not a fixed Width: larger display text must remain readable.
                foreach (var button in buttons) { button.FontSize = 28; }
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Bounds.Width > 64);
                    AssertRenderedTextCentered(button);
                });
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertRenderedTextCentered(Button button)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        var child = Assert.IsAssignableFrom<Control>(presenter.Child);
        var center = child.TranslatePoint(new Rect(child.Bounds.Size).Center, button);
        Assert.NotNull(center);
        Assert.InRange(Math.Abs(center.Value.X - button.Bounds.Width / 2), 0, 1);
        Assert.InRange(Math.Abs(center.Value.Y - button.Bounds.Height / 2), 0, 1);
        Assert.True(child.Bounds.Width <= button.Bounds.Width - button.Padding.Left - button.Padding.Right);
    }
}
