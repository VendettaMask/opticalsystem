using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class InterferogramFooterTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_INTERFEROGRAM_FOOTER_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) { builder.UseSkia(); }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(FieldDefinitionKind.Angle, AnalysisAxisUnit.Degree)]
    [InlineData(FieldDefinitionKind.ObjectHeight, AnalysisAxisUnit.Millimeter)]
    public void MetadataUsesActualSelectedFieldCoordinatesAndTypedUnits(
        FieldDefinitionKind fieldKind, AnalysisAxisUnit unit)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = fieldKind;
        optic.SurfaceGroup.Items[0].Thickness = fieldKind == FieldDefinitionKind.Angle ? double.PositiveInfinity : 500;
        optic.Fields[^1].X = 2;
        optic.Fields[^1].Y = -10;
        var view = CreateView(optic);
        var data = Assert.IsType<InterferogramSummaryDto>(view.InterferogramSummary);
        Assert.Equal(AnalysisPresentationKind.Interferogram, view.PresentationKind);
        Assert.Equal(2, data.FieldX!.Value, 10);
        Assert.Equal(-10, data.FieldY!.Value, 10);
        Assert.Equal(unit, data.FieldUnit);
        Assert.Equal(optic.Wavelengths.First(wave => wave.IsPrimary).Micrometers, data.WavelengthMicrometers);
        Assert.Equal(optic.SurfaceGroup.Items[^1].Number, data.SurfaceNumber);
        Assert.True(data.IsImageSurface);
        Assert.Equal(Math.Abs(optic.Paraxial.EstimateExitPupilDiameter(data.WavelengthMicrometers!.Value)),
            data.ExitPupilDiameterMillimeters!.Value, 10);
        var printedPv = double.Parse(view.Rows.Single(row => row.Metric == "波峰到波谷").Value, CultureInfo.InvariantCulture);
        Assert.InRange(Math.Abs(printedPv - data.PeakToValleyWaves!.Value), 0, 0.000001);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(600)]
    public async Task FooterUsesTypedSummaryWithoutGenericRowsOrFabricatedFringeSettings(int width)
    {
        var view = CreateView(Optic.CreateCookeTriplet()) with
        {
            Name = "renamed",
            InterferogramSummary = new InterferogramSummaryDto(0.587562, 0, -15,
                AnalysisAxisUnit.Millimeter, 0.59474, 16, true, 10.307),
            Rows = new[] { new AnalysisRowDto("光线数", "999"), new AnalysisRowDto("波峰到波谷", "999") }
        };
        var originalSeries = view.Series;
        var originalReport = view.ReportText;
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(InterferogramFooterTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var footer = BuildFooter(view);
            var window = new Window { Width = width, Height = 160, Content = footer };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var grid = Assert.IsType<Grid>(footer.Child);
                Assert.Equal(132, grid.Height);
                var left = Assert.IsType<StackPanel>(grid.Children[0]);
                Assert.Equal(3, left.Children.Count);
                Assert.Equal("干涉图", Assert.IsType<TextBlock>(left.Children[0]).Text);
                Assert.Equal("2026/9/4", Assert.IsType<TextBlock>(left.Children[1]).Text);
                var summary = Assert.IsType<TextBlock>(left.Children[2]);
                Assert.Equal(new[]
                {
                    "0.5876 µm 对于 -15.00 mm",
                    "峰谷 = 0.5947 个波长，条纹数/波长 = —",
                    "面：像面",
                    "出瞳直径：1.0307E+01 毫米",
                    "X倾斜 = —，Y倾斜 = —"
                }, summary.Text!.Split(Environment.NewLine));
                Assert.DoesNotContain("999", summary.Text);
                Assert.DoesNotContain("其余", summary.Text);
                var bottom = summary.TranslatePoint(new Point(0, summary.Bounds.Height), footer)!.Value.Y;
                Assert.InRange(bottom, 0, footer.Bounds.Height);
                var right = grid.Children.OfType<Border>().Single(child => Grid.GetColumn(child) == 1);
                Assert.InRange(right.Bounds.Width, grid.Bounds.Width / 3 - 1, grid.Bounds.Width / 3 + 1);
                Assert.Equal(132, right.Bounds.Height);
                Assert.Same(originalSeries, view.Series);
                Assert.Equal(originalReport, view.ReportText);
                var directory = Environment.GetEnvironmentVariable("OPTILAND_INTERFEROGRAM_FOOTER_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    bitmap.Save(Path.Combine(directory, $"interferogram-footer-{width}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MissingMetadataIsNotReportedAsZeroAndOtherKindsKeepTheirSummary()
    {
        var view = CreateView(Optic.CreateCookeTriplet()) with
        {
            InterferogramSummary = null,
            Rows = new[] { new AnalysisRowDto("光线数", "99") }
        };
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(InterferogramFooterTests));
        await session.Dispatch(() =>
        {
            var lines = Summary(view);
            Assert.Contains("峰谷 = —", lines);
            Assert.Contains("面：—", lines);
            Assert.DoesNotContain("0.0000", lines);
            Assert.Equal("光线数: 99", Summary(view with { PresentationKind = AnalysisPresentationKind.Standard }));
            var tilted = view with
            {
                InterferogramSummary = new InterferogramSummaryDto(0.55, 2, -3,
                    AnalysisAxisUnit.Degree, 1.25, 5, false, 7)
            };
            Assert.Contains("(X=2.00, Y=-3.00) °", Summary(tilted));
            Assert.Contains("面：5", Summary(tilted));
        }, CancellationToken.None);

        static string Summary(AnalysisViewDto value) => Assert.IsType<TextBlock>(
            Assert.IsType<StackPanel>(Assert.IsType<Grid>(BuildFooter(value).Child).Children[0]).Children[2]).Text!;
    }

    private static AnalysisViewDto CreateView(Optic optic)
    {
        var raw = new WorkbenchRuntime(optic).BuildAnalysisView("Interferogram",
            new Dictionary<string, string> { ["NumRings"] = "3", ["MapSize"] = "17" });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType("OptilandWorkbench.Application.Services.WorkbenchMapper")!;
        return Assert.IsType<AnalysisViewDto>(mapper.GetMethod("ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { raw })) with
        {
            PresentationKind = WorkbenchAnalysisCatalog.PresentationKind("Interferogram")
        };
    }

    private static Border BuildFooter(AnalysisViewDto view)
    {
        var document = new OpticalDocumentSnapshot("test", "test.ZMX", 0, "", false, false,
            0, 0, 0, 0, 17, 3, 3);
        return Assert.IsType<Border>(typeof(AnalysisPanel).GetMethod("BuildAnalysisTitleBlock", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { view, document, new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero) }));
    }
}
