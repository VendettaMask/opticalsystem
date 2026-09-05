using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class DocumentTabCornerTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_DOCUMENT_TAB_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public void TabRadiusResourcesKeepCustomThemesUnchanged()
    {
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            var expected = theme.SettingsValue is "Light" or "Dark" ? 6 : 0;
            Assert.Equal(new CornerRadius(expected), theme.BuildResources()["DockDocumentTabItemCornerRadius"]);
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task RealTabBodyIsRoundedInEveryStateAndSurvivesThemeSwitches(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DocumentTabCornerTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, theme);
            var strip = new DocumentTabStrip
            {
                ItemsSource = new[]
                {
                    new Document { Id = "lens", Title = "镜头数据", CanClose = false },
                    new Document { Id = "layout", Title = "二维视图", CanClose = true }
                },
                HeaderTemplate = new FuncDataTemplate<Document>((document, _) => new TextBlock { Text = document?.Title }),
                SelectedIndex = 0,
                IsActive = true,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var window = new Window
            {
                Width = 500,
                Height = 100,
                Content = new StackPanel { Margin = new Thickness(10), Children = { strip } }
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var tabs = strip.GetVisualDescendants().OfType<DocumentTabStripItem>().ToArray();
                Assert.Equal(2, tabs.Length);
                var lens = tabs[0];
                var body = lens.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_TabBody");
                var originalSize = lens.Bounds.Size;
                // Isolate the radius from theme-specific font metrics when checking size.
                lens.CornerRadius = new CornerRadius(0);
                window.UpdateLayout();
                Assert.Equal(originalSize, lens.Bounds.Size);
                lens.ClearValue(TemplatedControl.CornerRadiusProperty);
                window.UpdateLayout();
                Assert.Equal(new CornerRadius(6), body.CornerRadius);
                Assert.Equal(originalSize, lens.Bounds.Size);

                foreach (var selection in new[] { theme, "Pixel", theme, "Isekai", theme })
                {
                    ThemeApplicationService.Apply(app, selection);
                    var expected = new CornerRadius(selection is "Light" or "Dark" ? 6 : 0);
                    foreach (var selected in new[] { false, true })
                        foreach (var active in new[] { false, true })
                            foreach (var hovered in new[] { false, true })
                            {
                                lens.IsSelected = selected;
                                lens.IsActive = active;
                                ((IPseudoClasses)lens.Classes).Set(":pointerover", hovered);
                                window.UpdateLayout();
                                Assert.Equal(expected, lens.CornerRadius);
                                Assert.Equal(expected, body.CornerRadius);
                                Assert.Equal(lens.Background, body.Background);
                            }
                }

                ((IPseudoClasses)lens.Classes).Set(":pointerover", false);
                lens.IsSelected = true;
                lens.IsActive = true;
                window.UpdateLayout();
                Assert.All(tabs, tab => Assert.Equal(new CornerRadius(6), tab.CornerRadius));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var directory = Environment.GetEnvironmentVariable("OPTILAND_DOCUMENT_TAB_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"document-tabs-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
