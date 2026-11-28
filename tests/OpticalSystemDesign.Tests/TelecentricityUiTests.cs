using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using ContractQuantity = OpticalSystemDesign.Application.Contracts.AnalysisAxisQuantity;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class TelecentricityUiTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light", "zh-CN", 13)]
    [InlineData("Dark", "en-US", 18)]
    [InlineData("Isekai", "en-US", 13)]
    public async Task ActualPanelRunsDoubleGaussWithPlotTableWidthAndImageExport(string theme, string language, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(TelecentricityUiTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(language);
            var settings = new AppSettings { Theme = theme, FontSize = fontSize, Language = language };
            DisplayTypography.Configure(settings);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var before = app.Documents.GetRecoveryState();
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "远心度",
                app.Analyses.MergeSettings("远心度", null)));
            Assert.Equal(OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, result.View.Outcome);
            Assert.Equal(6, result.View.Series.Count);
            Assert.Equal(9, result.View.Table!.Rows.Count);
            Assert.NotNull(result.View.TelecentricitySummary);
            Assert.All(result.View.Series, series => Assert.Equal(ContractQuantity.FieldNumber, series.XQuantity));
            Assert.Equal("Telecentricity", UiLocalization.Text("远心度", "en-US"));
            Assert.Equal("Object Space: λ1", UiLocalization.Text("物方: λ1", "en-US"));
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "远心度");
            Assert.True(await panel.RunForGuiCaptureAsync());
            var window = new Window { Content = panel };
            DisplayTypography.Apply(window);
            try
            {
                window.Show();
                foreach (var size in new[] { (1200, 900), (720, 600) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                    var plot = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
                    Assert.Equal(6, plot.Series.Count);
                    Assert.Equal(AnalysisCurveWidth.Standard, plot.CurveWidth);
                    Assert.All(plot.Series, series => Assert.Equal(ContractQuantity.FieldNumber, series.XQuantity));
                    if (language == "en-US")
                        Assert.All(plot.Series, series =>
                        {
                            Assert.DoesNotContain("物方", series.Name);
                            Assert.DoesNotContain("像方", series.LegendLabel);
                        });
                    var legend = Assert.Single(panel.GetVisualDescendants().OfType<StackPanel>(),
                        stack => stack.Children.Count == 6 && stack.Children.All(child => child is CheckBox));
                    var first = (CheckBox)legend.Children[0];
                    first.IsChecked = false;
                    Assert.Equal(5, plot.Series.Count);
                    first.IsChecked = true;
                    Assert.Equal(6, plot.Series.Count);
                    var tickBounds = (System.Collections.ICollection)typeof(AnalysisPlotControl)
                        .GetField("_xTickLabelBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .GetValue(plot)!;
                    Assert.Equal(3, tickBounds.Count);
                    var export = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => button.Name == "ExportAnalysisImage");
                    Assert.True(export.IsVisible && export.IsEnabled);
                    var width = Assert.Single(panel.GetVisualDescendants().OfType<DropDownButton>(), button => button.Name == "AnalysisCurveWidth");
                    Assert.True(width.IsVisible && width.IsEnabled);
                    Assert.Equal(3, Assert.IsType<MenuFlyout>(width.Flyout).Items.Count);
                    var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TELECENTRICITY_CAPTURE_DIR");
                    if (size.Item1 == 1200)
                    {
                        var page = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                        Assert.False(page.HasOverflow,
                            $"Plot {page.Plot.DesiredSize}, minimum width {ResponsiveAnalysisPlotHost.MinimumReadableWidth(page.Plot)}, caption {page.Caption.DesiredSize}.");
                        using var standard = panel.CapturePlotImage();
                        using var standardData = new MemoryStream(); standard.Save(standardData, PngBitmapEncoderOptions.Default);
                        var menu = Assert.IsType<MenuFlyout>(width.Flyout);
                        Assert.IsType<MenuItem>(menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        Assert.Equal(AnalysisCurveWidth.Thick, plot.CurveWidth);
                        using var thick = panel.CapturePlotImage();
                        using var thickData = new MemoryStream(); thick.Save(thickData, PngBitmapEncoderOptions.Default);
                        Assert.NotEqual(standardData.ToArray(), thickData.ToArray());
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            Directory.CreateDirectory(directory);
                            standard.Save(Path.Combine(directory, $"telecentricity-{theme}-export.png"), PngBitmapEncoderOptions.Default);
                        }
                        Assert.IsType<MenuItem>(menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    }
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        using var bitmap = new RenderTargetBitmap(new PixelSize(size.Item1, size.Item2), new Vector(96, 96));
                        bitmap.Render(window);
                        bitmap.Save(Path.Combine(directory, $"telecentricity-{theme}-{size.Item1}.png"), PngBitmapEncoderOptions.Default);
                    }
                    Assert.Equal(before, app.Documents.GetRecoveryState());
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply("zh-CN"); }
        }, CancellationToken.None);
    }
}
