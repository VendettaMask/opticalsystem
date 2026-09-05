using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SeidelReportPresentationTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_SEIDEL_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) { builder.UseSkia(); }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task FullReportReachesTheDesktopAndRemainsScrollableAndSelectable()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX")), ".zmx");
        var raw = new WorkbenchRuntime(optic).BuildAnalysisView("Seidel Coefficients",
            new Dictionary<string, string> { ["WavelengthNumber"] = "2" });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper")!;
        var view = Assert.IsType<AnalysisViewDto>(mapper.GetMethod("ToAnalysisViewDto",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { raw }));
        Assert.Equal(raw.ReportText, view.ReportText);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SeidelReportPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var document = new OpticalDocumentSnapshot("Imported Zemax ZMX", "[MS-L7](10X大NA大视场).ZMX",
                0, "", false, false, 0, 0, 0, 0, 17, 3, 3);
            var factory = typeof(AnalysisPanel).GetMethod("BuildSeidelCoefficientsReport",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var report = Assert.IsType<TextBox>(factory.Invoke(null, new object[]
            {
                view, document, new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
            }));
            var window = new Window { Width = 1200, Height = 720, Content = report };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(report.IsReadOnly);
                Assert.Contains(view.ReportText, report.Text);
                Assert.Contains("W040", report.Text);
                Assert.Contains("TLAC", report.Text);
                Assert.Contains("LLAC", report.Text);
                Assert.Contains("W220T", report.Text);
                Assert.Equal(ScrollBarVisibility.Auto, report.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
                Assert.Equal(ScrollBarVisibility.Auto, report.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
                var scroll = Assert.Single(report.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                Capture("seidel-report-top.png");
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                Assert.True(scroll.Offset.Y > 0);
                window.UpdateLayout();
                Capture("seidel-report-bottom.png");
                window.Width = 600;
                // Constrain the control explicitly: native resize notifications
                // are asynchronous under the Skia headless platform.
                report.Width = 570;
                window.UpdateLayout();
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
                scroll.Offset = new Vector(scroll.Extent.Width - scroll.Viewport.Width, scroll.Offset.Y);
                window.UpdateLayout();
                Assert.True(scroll.Offset.X > 0);
                report.SelectAll();
                Assert.Equal(report.Text, report.SelectedText);
            }
            finally
            {
                window.Close();
            }

            void Capture(string file)
            {
                var directory = Environment.GetEnvironmentVariable("OPTILAND_SEIDEL_CAPTURE_DIR");
                if (string.IsNullOrWhiteSpace(directory)) { return; }
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(directory, file), PngBitmapEncoderOptions.Default);
                File.WriteAllText(Path.Combine(directory, "seidel-report.txt"), report.Text);
            }
        }, CancellationToken.None);
    }
}
