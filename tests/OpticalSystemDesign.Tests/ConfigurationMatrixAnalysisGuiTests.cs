using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ConfigurationMatrixAnalysisGuiTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CONFIGURATION_SPOT_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task ZoomShowsThreeRealConfigurationColumnsAndCorrectSummary(string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ConfigurationMatrixAnalysisGuiTests));
        await session.Dispatch(async () =>
        {
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            var result = await ConfigurationMatrixAnalysisTests.Run(app);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), "Configuration Matrix Spot Diagram");
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            // Arbitrary display titles must not select the structure/field identity.
            panel.PresentResult(result.View with { PlotPanes = result.View.PlotPanes.Select(pane => pane with { Title = "custom caption" }).ToArray() },
                app.Documents.GetSnapshot(), new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1280, Height = 800, Content = panel };
            try
            {
                window.Show(); Render(window);
                var matrix = panel.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Children.OfType<OptionalSquarePlotHost>().Count() == 9);
                Assert.Equal(4, matrix.ColumnDefinitions.Count);
                Assert.Equal(4, matrix.RowDefinitions.Count);
                var headers = matrix.Children.OfType<TextBlock>().Where(label => Grid.GetRow(label) == 0 && Grid.GetColumn(label) > 0).Select(label => label.Text).ToArray();
                Assert.Equal(Enumerable.Range(1, 3).Select(number => UiLocalization.Text($"结构 {number}", language)), headers);
                var plots = matrix.Children.OfType<OptionalSquarePlotHost>().Select(host => Assert.IsType<AnalysisPlotControl>(host.Child)).ToArray();
                Assert.All(plots, plot => Assert.Equal(3, plot.Series.Count));
                var legend = Assert.IsType<StackPanel>(((Grid)matrix.Parent!).Children[1]);
                Assert.Equal(3, legend.Children.Count);
                var toggle = Assert.IsType<CheckBox>(legend.Children[0]);
                toggle.IsChecked = false;
                Assert.All(plots, plot => Assert.Equal(2, plot.Series.Count));
                toggle.IsChecked = true;
                var summary = panel.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text?.Contains(language == UiLocalization.Chinese
                    ? "点列图数：9" : language == UiLocalization.English ? "Spot diagrams: 9" : "スポット図数：9", StringComparison.Ordinal) == true);
                Assert.DoesNotContain("行：结构 × 视场", summary.Text);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CONFIGURATION_SPOT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    Render(window);
                    using var image = panel.CapturePlotImage();
                    AnalysisPanel.SavePlotImage(image, Path.Combine(directory, $"zoom-3-configurations-{language}.png"));
                }
                window.Width = 640; window.Height = 400; Render(window);
                Assert.All(plots, plot =>
                {
                    var frame = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                    Assert.True(frame.Width >= 64 && frame.Height >= 64, $"Plot frame collapsed: {frame}");
                });
                var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var pageBounds = new Rect(page.Child!.Bounds.Size).TransformToAABB(page.Child.TransformToVisual(window)!.Value);
                var summaryBounds = new Rect(summary.Bounds.Size).TransformToAABB(summary.TransformToVisual(window)!.Value);
                Assert.True(summaryBounds.Bottom <= pageBounds.Bottom + 1, $"Footer {summaryBounds} exceeds page {pageBounds}");
                Assert.True(summaryBounds.Right <= pageBounds.Right + 1);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    using var image = panel.CapturePlotImage();
                    AnalysisPanel.SavePlotImage(image, Path.Combine(directory, $"zoom-3-configurations-{language}-narrow.png"));
                }
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
