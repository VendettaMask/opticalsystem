using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Modules;
using SkiaSharp;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed partial class DesktopTests
{
    [Fact]
    public void DomainShadingUnionsAllParticipatingRangesAndOnlyIncludesAnActiveExitMedium()
    {
        var source = ReferenceExamples.Create("demo-edge");
        var ids = source.Layers.Select(l => l.MaterialId).Distinct().ToArray();
        var table = source.Materials.First(m => m.HoldTabulatedEndpoints);
        var unused = table with { Id = "unused-domain-test", MinimumNm = 550, MaximumNm = 600 };
        var d = source with
        {
            Materials = [.. source.Materials.Select(m => m.Id == ids[0] ? table with { Id = m.Id, MinimumNm = 500, MaximumNm = 650 }
                : m.Id == ids[1] ? table with { Id = m.Id, MinimumNm = 450, MaximumNm = 700 } : m), unused]
        };
        Assert.Equal([new SpectralBand(400, 500), new(650, 800)], CoatingPresentation.ExtrapolatedBands(d, 400, 800));
        Assert.Empty(CoatingPresentation.ExtrapolatedBands(d, 500, 650));
        Assert.Equal([new SpectralBand(350, 375)], CoatingPresentation.ExtrapolatedBands(d, 350, 375));
        var plate = d with { StackGeometry = new(unused.Id) };
        Assert.Equal([new SpectralBand(400, 550), new(600, 800)], CoatingPresentation.ExtrapolatedBands(plate, 400, 800));
        Assert.Equal(CoatingPresentation.ExtrapolatedBands(d, 400, 800),
            CoatingPresentation.ExtrapolatedBands(plate with { StackGeometry = plate.StackGeometry! with { IncludeBackSurface = false } }, 400, 800));
        var disjoint = d with { Materials = d.Materials.Select(m => m.Id == ids[1] ? m with { MinimumNm = 680 } : m).ToArray() };
        Assert.Equal([new SpectralBand(400, 800)], CoatingPresentation.ExtrapolatedBands(disjoint, 400, 800));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task OutOfDomainBackgroundRemainsDarkerInLiveAndPngThroughViewChanges(string theme)
    {
        var previousSettings = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
        var directory = Path.Combine(Path.GetTempPath(), "coating-domain-shading-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory);
        try
        {
            using var host = new Host();
            await host.Run(async () =>
            {
                DisplayTypography.Configure(new AppSettings { Theme = theme });
                Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme(theme);
                var window = new MainWindow { Width = 1500, Height = 960, ChooseUnsavedChanges = DiscardChanges };
                try
                {
                    window.Show(); Tick(window); Click(window, "示例：demo-edge"); await window.PendingOperation; Tick(window);
                    var plot = WindowControls(window).OfType<SpectrumPlot>().Single();
                    await plot.PendingEvaluation; Tick(window); Assert.True(plot.HasData, plot.DisplayError);
                    var data = plot.DisplayData; var computations = plot.DisplayComputations;
                    var fingerprint = window.CurrentExperiment.Fingerprint();
                    foreach (var q in Enum.GetValues<CoatingQuantity>()) foreach (var p in Enum.GetValues<CoatingCurvePolarization>())
                        plot.SetCurveVisible(q, p, false);
                    plot.SetGrid(false); plot.SetTargets(false); Tick(window);
                    Point PointAt(double wavelength) => new(plot.PlotRect.Left + (wavelength - plot.Range.MinimumNm)
                        / (plot.Range.MaximumNm - plot.Range.MinimumNm) * plot.PlotRect.Width, plot.PlotRect.Top + .43 * plot.PlotRect.Height);
                    SKColor PixelAt(SKBitmap bitmap, Point point)
                    {
                        var frame = OpticalSystemDesign.App.Services.ImageExportResolution.FitFrame(plot.Bounds.Size,
                            new PixelSize(bitmap.Width, bitmap.Height), true);
                        return bitmap.GetPixel(frame.X + (int)((point.X + 1) * frame.Width / (plot.Bounds.Width + 2)),
                            frame.Y + (int)((point.Y + 1) * frame.Height / (plot.Bounds.Height + 2)));
                    }
                    void AssertDarker()
                    {
                        Tick(window);
                        using var png = SKBitmap.Decode(plot.CapturePng());
                        var outside = PixelAt(png, PointAt(450)); var inside = PixelAt(png, PointAt(550));
                        Assert.True(outside.Red < inside.Red - 8 && outside.Green < inside.Green - 8 && outside.Blue < inside.Blue - 8,
                            $"{theme}: out-of-domain {outside}, in-domain {inside}");
                    }
                    AssertDarker();
                    using (var frame = window.CaptureRenderedFrame())
                    using (var stream = new MemoryStream())
                    {
                        Assert.NotNull(frame); frame.Save(stream, PngBitmapEncoderOptions.Default);
                        using var live = SKBitmap.Decode(stream.ToArray());
                        using var png = SKBitmap.Decode(plot.CapturePng());
                        var point = plot.TranslatePoint(PointAt(450), window)!.Value;
                        var livePixel = live.GetPixel((int)(point.X * live.Width / window.Bounds.Width), (int)(point.Y * live.Height / window.Bounds.Height));
                        Assert.Equal(PixelAt(png, PointAt(450)), livePixel);
                    }
                    plot.ApplyRange(new(425, 575, 0, 100)); AssertDarker();
                    plot.SetUnits(CoatingWavelengthUnit.Micrometers, CoatingValueUnit.OpticalDensity); AssertDarker();
                    plot.SetUnits(CoatingWavelengthUnit.Nanometers, CoatingValueUnit.Percent);
                    plot.ApplyRange(new(550, 700, 0, 100)); Tick(window);
                    using (var png = SKBitmap.Decode(plot.CapturePng()))
                        Assert.Equal(PixelAt(png, PointAt(575)), PixelAt(png, PointAt(675)));
                    plot.ResetView(); AssertDarker();
                    Assert.Same(data, plot.DisplayData); Assert.Equal(computations, plot.DisplayComputations);
                    Assert.Equal(fingerprint, window.CurrentExperiment.Fingerprint());
                    plot.SetGrid(true); plot.SetTargets(true);
                    foreach (var q in Enum.GetValues<CoatingQuantity>()) plot.SetCurveVisible(q, CoatingCurvePolarization.Average, true);
                    Capture(window, $"domain-shading-{theme}-1500");
                    var evidence = Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE");
                    if (!string.IsNullOrWhiteSpace(evidence)) File.WriteAllBytes(Path.Combine(evidence, $"domain-spectrum-{theme}.png"), plot.CapturePng());
                    window.MouseMove(plot.TranslatePoint(PointAt(450), window)!.Value); Tick(window);
                    var tooltip = Assert.IsType<StackPanel>(ToolTip.GetTip(plot));
                    Assert.Contains(tooltip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("非实测") == true);
                }
                finally
                {
                    window.CloseAfterHostApproval(); DisplayTypography.Configure(new AppSettings());
                    Assert.IsType<global::OpticalSystemDesign.App.App>(Avalonia.Application.Current).ApplyTheme("Light");
                }
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousSettings);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
