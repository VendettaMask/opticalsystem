using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IsekaiCraftedWorkbenchTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(1600, 1100, 13)]
    [InlineData(1120, 850, 18)]
    public async Task RealWorkbenchKeepsNativeDockAndCommandsInsideCraftedMaterials(double width, double height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiCraftedWorkbenchTests));
        await session.Dispatch(async () =>
        {
            using var workbench = WorkbenchApplication.Create("cooke");
            var original = workbench.Documents.GetRecoveryState();
            var settings = new AppSettings { Theme = "Isekai", FontSize = fontSize, WindowWidth = width, WindowHeight = height };
            var store = new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "isekai-crafted", Guid.NewGuid().ToString("N")));
            var window = new MainWindow(settings, workbench, store);
            var ready = false;
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var n = 0; n < 250 && !ready; n++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var panels = (PanelManager)typeof(MainWindow).GetField("_panels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                panels.ShowAnalysis("Huygens MTF"); Tick(window);
                var analysisDocument = panels.Factory.OpenDocuments().Single(d => d.Id.StartsWith("analysis:", StringComparison.Ordinal));
                var analysis = window.GetVisualDescendants().OfType<AnalysisPanel>().Single(p => p.IsEffectivelyVisible);
                Assert.True(await analysis.RunForGuiCaptureAsync().WaitAsync(TimeSpan.FromSeconds(40)));
                var ribbon = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedItem = ribbon.Items.Cast<TabItem>().Single(t => AutomationProperties.GetName(t) == "分析");
                for (var n = 0; n < 12; n++) { await Task.Delay(20); Tick(window); }
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
                var docTabs = window.GetVisualDescendants().OfType<DocumentTabStripItem>().Where(t => t.IsEffectivelyVisible).ToArray();
                Assert.True(docTabs.Length >= 3);
                foreach (var tab in docTabs)
                    Assert.IsType<DrawingBrush>(tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_TabBody").Background);
                var selectedHeader = docTabs.Single(t => t.IsSelected).GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(p => p.Name == "PART_HeaderPresenter");
                Assert.Equal(Color.Parse("#513723"), Assert.IsAssignableFrom<ISolidColorBrush>(selectedHeader.Foreground).Color);
                var commands = analysis.GetVisualDescendants().OfType<WrapPanel>().Single(c => c.Classes.Contains("analysis-actions"));
                foreach (var command in commands.Children.OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled))
                    foreach (var text in command.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
                        Assert.True(ScientificIconPalette.Contrast(Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color,
                            Color.Parse("#E4C295")) >= 4.5, "Command text must remain readable on aged paper: " + text.Text);
                foreach (var icon in commands.GetVisualDescendants().OfType<LocalIcon>().Where(i => i.IsEffectivelyVisible && i.IsEffectivelyEnabled))
                    Assert.True(ScientificIconPalette.Contrast(Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color,
                        Color.Parse("#E4C295")) >= 4.5);
                var copy = Assert.IsType<Button>(commands.Children[2]);
                var copyText = copy.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "复制文本");
                var enabledInk = Assert.IsAssignableFrom<ISolidColorBrush>(copyText.Foreground).Color;
                copy.IsEnabled = false; Tick(window);
                Assert.NotEqual(enabledInk, Assert.IsAssignableFrom<ISolidColorBrush>(copyText.Foreground).Color);
                copy.IsEnabled = true; Tick(window);
                Capture(window, $"workbench-{width}-{fontSize}.png");
                var book = analysis.GetVisualDescendants().OfType<AnalysisImageFrame>().Single(f => f.Name == "AnalysisWorkspaceBookFrame");
                // The light toolbar ink must not leak into its dark native flyout.
                var curveWidth = commands.Children.OfType<DropDownButton>().Single(b => b.Name == "AnalysisCurveWidth");
                Click(window, curveWidth); Tick(window);
                var menu = Assert.IsType<MenuFlyout>(curveWidth.Flyout); Assert.True(menu.IsOpen);
                foreach (var choice in menu.Items.OfType<MenuItem>())
                {
                    Assert.True(ScientificIconPalette.Contrast(Assert.IsAssignableFrom<ISolidColorBrush>(choice.Foreground).Color,
                        IsekaiTheme.Palette.Surface) >= 4.5);
                    foreach (var text in choice.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)))
                        Assert.True(ScientificIconPalette.Contrast(Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color,
                            IsekaiTheme.Palette.Surface) >= 4.5, $"Flyout label '{text.Text}' has unreadable ink {text.Foreground}");
                }
                Capture(window, $"menu-{width}-{fontSize}.png");
                menu.Hide(); Tick(window);
                var settingsButton = Assert.IsType<Button>(commands.Children[0]);
                Click(window, settingsButton); Tick(window);
                var settingsCard = (Border)typeof(AnalysisPanel).GetField("_settingsHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(analysis)!;
                Assert.True(settingsCard.IsVisible);
                Capture(window, $"settings-{width}-{fontSize}.png");
                Click(window, settingsButton); Tick(window); Assert.False(settingsCard.IsVisible);
                // Exercise the actual close button, not a decorative image of an X.
                var selected = docTabs.Single(t => t.IsSelected);
                var close = selected.GetVisualDescendants().OfType<ContentPresenter>()
                    .Where(p => p.Name == "PART_ClosePresenter" && p.IsEffectivelyVisible)
                    .SelectMany(p => p.GetVisualDescendants().OfType<Button>()).Single();
                Assert.True(close.IsHitTestVisible && close.IsEffectivelyEnabled);
                Click(window, close); Tick(window);
                Assert.DoesNotContain(analysisDocument, panels.Factory.OpenDocuments());
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }
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
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CRAFTED_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(dir, name), PngBitmapEncoderOptions.Default);
        if (name.StartsWith("workbench-", StringComparison.Ordinal))
        {
            var panel = window.GetVisualDescendants().OfType<AnalysisPanel>().Single(p => p.IsEffectivelyVisible);
            using var panelImage = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(panel.Bounds.Width), (int)Math.Ceiling(panel.Bounds.Height)));
            panelImage.Render(panel);
            panelImage.Save(Path.Combine(dir, "analysis-" + name), PngBitmapEncoderOptions.Default);
        }
    }
}
