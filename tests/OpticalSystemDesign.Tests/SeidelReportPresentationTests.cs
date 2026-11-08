using OpticalSystemDesign.Validation;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SeidelReportPresentationTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SEIDEL_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) { builder.UseSkia(); }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task FullReportReachesTheDesktopAndRemainsScrollableAndSelectable()
    {
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX")), ".zmx");
        var raw = new WorkbenchRuntime(optic).BuildAnalysisView("Seidel Coefficients",
            new Dictionary<string, string> { ["WavelengthNumber"] = "2" });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType(
            "OpticalSystemDesign.Application.Services.WorkbenchMapper")!;
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
            var panel = Assert.IsType<DockPanel>(factory.Invoke(null, new object[]
            {
                view, document, new DateTimeOffset(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Local))
            }));
            var tabs = Assert.Single(panel.Children.OfType<TabControl>());
            var sections = Assert.IsAssignableFrom<IReadOnlyList<AnalysisReportSectionDto>>(view.ReportSections);
            Assert.Equal(new[] { "赛德尔", "波长", "横向", "轴向", "波前汇总", "场曲汇总", "文本" },
                tabs.Items.Cast<TabItem>().Select(tab => tab.Header));
            var window = new Window { Width = 1050, Height = 720, Content = panel };
            try
            {
                window.Show();
                Tick();
                for (var index = 0; index < sections.Count; index++)
                {
                    tabs.SelectedIndex = index;
                    Tick();
                    var grid = Assert.Single(tabs.GetVisualDescendants().OfType<DataGrid>());
                    Assert.True(grid.IsReadOnly);
                    Assert.Equal(1, grid.FrozenColumnCount);
                    Assert.Same(sections[index].Table.Rows, grid.ItemsSource);
                    Assert.Equal(sections[index].Table.Columns, grid.Columns.Select(column => Assert.IsType<string>(column.Header)));
                    grid.ScrollIntoView(sections[index].Table.Rows[^1], grid.Columns[0]);
                    Tick();
                    Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "累计" && block.FontWeight == FontWeight.SemiBold);
                    if (index is 0 or 1) Capture(index == 0 ? "seidel-coefficients.png" : "seidel-waves.png");
                }

                panel.Width = 570;
                tabs.SelectedIndex = 2;
                Tick();
                var narrow = Assert.Single(tabs.GetVisualDescendants().OfType<DataGrid>());
                var horizontal = Assert.Single(narrow.GetVisualDescendants().OfType<ScrollBar>(),
                    scrollBar => scrollBar.Orientation == Avalonia.Layout.Orientation.Horizontal && scrollBar.Maximum > 0);
                narrow.ScrollIntoView(sections[2].Table.Rows[^1], narrow.Columns[^1]);
                Tick();
                Assert.True(horizontal.Value > 0);
                Capture("seidel-narrow.png");

                tabs.SelectedIndex = sections.Count;
                Tick();
                var report = Assert.IsType<TextBox>(Assert.IsType<TabItem>(tabs.Items[^1]).Content);
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
                scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
                Assert.True(scroll.Offset.Y > 0);
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

            void Tick()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            void Capture(string file)
            {
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SEIDEL_CAPTURE_DIR");
                if (string.IsNullOrWhiteSpace(directory)) { return; }
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(directory, file), PngBitmapEncoderOptions.Default);
            }
        }, CancellationToken.None);
    }
}
