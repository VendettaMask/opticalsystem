using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ManufacturabilityPanelLayoutTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MANUFACTURING_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1440, 960, 13)]
    [InlineData("Dark", "en-US", 1440, 960, 13)]
    [InlineData("Light", "ja-JP", 680, 760, 18)]
    public async Task LayoutKeepsEveryMetricAndFindingAndReflowsAtNarrowWidths(
        string theme, string language, int width, int height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ManufacturabilityPanelLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks();
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new ManufacturabilityPanel(app.Prescription, app.Events);
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show();
                Tick(window);
                var report = OpticalManufacturingModel.Evaluate(app.Prescription.GetSurfaces(), new ManufacturabilitySettings());
                Assert.Equal(report.GeometryMetrics.Count, Named<DataGrid>(panel, "ManufacturingMetrics").ItemsSource.Cast<object>().Count());
                Assert.Equal(report.Findings.Count, Cards(panel).Length);
                Assert.Equal(report.Elements.Count.ToString(), Named<TextBlock>(panel, "ManufacturingElementCount").Text);
                Assert.Equal(report.WarningCount.ToString(), Named<TextBlock>(panel, "ManufacturingWarningCount").Text);
                if (language == UiLocalization.English)
                {
                    var displayed = Named<DataGrid>(panel, "ManufacturingMetrics").ItemsSource.Cast<ManufacturabilityGeometryMetric>().ToArray();
                    Assert.Contains(displayed, item => item.Item == "Mechanical diameter");
                    Assert.DoesNotContain(Cards(panel).SelectMany(card => card.GetVisualDescendants()).OfType<SelectableTextBlock>(),
                        text => text.Text?.Contains("建议不小于", StringComparison.Ordinal) == true);
                }
                var metrics = Named<Border>(panel, "ManufacturingMetricSection");
                var findings = Named<Border>(panel, "ManufacturingFindingSection");
                var metricsPosition = metrics.TranslatePoint(default, panel)!.Value;
                var findingsPosition = findings.TranslatePoint(default, panel)!.Value;
                if (width >= 1120)
                {
                    Assert.True(findingsPosition.X >= metricsPosition.X + metrics.Bounds.Width);
                    var note = Named<TextBlock>(panel, "ManufacturingScreeningNote");
                    Assert.True(note.TranslatePoint(default, panel)!.Value.Y + note.Bounds.Height <= panel.Bounds.Height);
                }
                else
                    Assert.True(findingsPosition.Y >= metricsPosition.Y + metrics.Bounds.Height);
                Assert.True(metrics.Bounds.Width > 0 && metrics.Bounds.Height >= 300);
                Assert.True(findings.Bounds.Width > 0 && findings.Bounds.Height > 200);
                var action = Named<Button>(panel, "ReevaluateManufacturing");
                Assert.Contains("accent", action.Classes);
                var actionPosition = action.TranslatePoint(default, panel)!.Value;
                Assert.True(actionPosition.X + action.Bounds.Width <= panel.Bounds.Width);
                Capture(window, $"manufacturability-{theme}-{language}-{width}.png");
                if (width < 1120)
                {
                    var scroll = Named<ScrollViewer>(panel, "ManufacturingPageScroll");
                    scroll.Offset = new Vector(0, scroll.Extent.Height);
                    Tick(window);
                    Capture(window, $"manufacturability-{theme}-{language}-{width}-findings.png");
                }
            }
            finally
            {
                window.Close();
                DisplayTypography.Configure(new AppSettings());
                UiLocalization.Apply(UiLocalization.Chinese);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReassessmentAndDocumentSwitchUpdateCardsWithoutStaleCounts()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ManufacturabilityPanelLayoutTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new ManufacturabilityPanel(app.Prescription, app.Events);
            var window = new Window { Width = 1440, Height = 960, Content = panel };
            try
            {
                window.Show(); Tick(window);
                Named<NumericUpDown>(panel, "MinimumCenterThickness").Value = 20;
                Named<Button>(panel, "ReevaluateManufacturing").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Tick(window);
                var expected = OpticalManufacturingModel.Evaluate(app.Prescription.GetSurfaces(),
                    new ManufacturabilitySettings(MinimumCenterThickness: 20));
                Assert.Equal(expected.WarningCount.ToString(), Named<TextBlock>(panel, "ManufacturingWarningCount").Text);
                Assert.Equal(expected.Findings.Count, Cards(panel).Length);
                Assert.True(expected.WarningCount > 0);
                UiLocalization.Apply(UiLocalization.English); Tick(window);
                Assert.Equal(UiLocalization.Text("部分项目需要工艺评审"), Named<TextBlock>(panel, "ManufacturingSummary").Text);
                app.Documents.NewBlank(); Tick(window);
                Assert.Equal("0", Named<TextBlock>(panel, "ManufacturingElementCount").Text);
                Assert.Empty(Cards(panel));
                Assert.Empty(Named<DataGrid>(panel, "ManufacturingMetrics").ItemsSource.Cast<object>());
            }
            finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    private static Border[] Cards(Control panel) => panel.GetVisualDescendants().OfType<Border>()
        .Where(card => card.Classes.Contains("manufacturing-finding")).ToArray();

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MANUFACTURING_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
