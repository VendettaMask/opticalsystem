using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AutomaticVignettingPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_SVIG_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 1000)]
    [InlineData("Dark", 1000)]
    [InlineData("Light", 680)]
    public async Task HelpKeepsThreeLevelsAndExplainsTheExecutableScope(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AutomaticVignettingPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var types = app.Optimization.GetMeritOperandTypes();
            var tree = OperandHelpProjection.BuildTree(types);
            Assert.Equal(8, tree.Count); Assert.Equal(51, tree.Sum(root => root.Children.Count));
            Assert.Equal(387, tree.Sum(root => root.Children.Sum(family => family.Children.Count)));
            var panel = new OperandHelpPanel(app.Optimization);
            var window = new Window { Content = panel, Width = width, Height = 900 };
            try
            {
                window.Show(); Tick();
                panel.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "搜索操作数").Text = "SVIG";
                Tick();
                var navigation = panel.GetVisualDescendants().OfType<TreeView>().Single();
                var leaf = navigation.Items.OfType<TreeViewItem>().SelectMany(c => c.Items.OfType<TreeViewItem>())
                    .SelectMany(f => f.Items.OfType<TreeViewItem>()).Single(i => (i.Tag as OperandHelpNode)?.Key == "SVIG");
                var category = navigation.Items.OfType<TreeViewItem>().Single(c => c.Items.OfType<TreeViewItem>().Any(f => f.Items.Contains(leaf)));
                var family = category.Items.OfType<TreeViewItem>().Single(f => f.Items.Contains(leaf));
                Assert.True(category.IsExpanded); Assert.True(family.IsExpanded);
                navigation.SelectedItem = leaf; Tick();
                Assert.Equal("SVIG", Assert.IsType<OperandHelpNode>(Assert.IsType<TreeViewItem>(navigation.SelectedItem).Tag).Key);
                var text = panel.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();
                Assert.Contains("三级目录 · 具体操作数", text);
                Assert.Contains(text, t => t.Contains("当前主波长"));
                Assert.Contains(text, t => t.Contains("四条边缘通过不保证整个瞳孔"));
                Assert.Contains("Precision", text);
                var directory = Environment.GetEnvironmentVariable("OPTILAND_SVIG_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"svig-help-{theme.ToLowerInvariant()}-{width}.png"), PngBitmapEncoderOptions.Default);
                }
                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
