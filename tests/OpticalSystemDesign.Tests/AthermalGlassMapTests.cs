using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AthermalGlassMapTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(0.17, 20)]
    [InlineData(-0.17, 25)]
    [InlineData(0, 35)]
    public void RelativeDerivativeAgreesWithIndependentFiniteDifference(double lambdaTk, double referenceTemperature)
    {
        const double wavelength = 587.5618, index = 1.5168, step = 0.001;
        var td = new List<double> { 1.86e-6, 1.31e-8, -1.37e-11, 4.34e-7, 6.27e-10, lambdaTk, referenceTemperature };
        var glass = Glass(td);
        Assert.True(CatalogThermalOptics.TryReferenceThermalPower(glass, wavelength, index, out var power, out var derivative));
        var lambda = wavelength / 1000;
        double Air(double temperature)
        {
            var inverseSquared = 1 / (lambda * lambda);
            var n15 = 1 + (6432.8 + 2949810 / (146 - inverseSquared) + 25540 / (41 - inverseSquared)) * 1e-8;
            return 1 + (n15 - 1) / (1 + (temperature - 15) * 0.0034785);
        }
        double Relative(double delta)
        {
            var absoluteChange = (index * index - 1) / (2 * index) *
                (td[0] * delta + td[1] * delta * delta + td[2] * delta * delta * delta
                    + (td[3] * delta + td[4] * delta * delta) / (lambda * lambda - Math.Sign(lambdaTk) * lambdaTk * lambdaTk));
            return (index * Air(referenceTemperature) + absoluteChange) / Air(referenceTemperature + delta);
        }
        var expected = (Relative(step) - Relative(-step)) / (2 * step);
        Assert.InRange(Math.Abs(derivative - expected), 0, 2e-12);
        Assert.InRange(Math.Abs(power - (expected / (index - 1) - 7.1e-6)), 0, 4e-12);
        Assert.NotEqual((index * index - 1) / (2 * index) * (td[0] + td[3] / (lambda * lambda - Math.Sign(lambdaTk) * lambdaTk * lambdaTk)), derivative);
    }

    [Fact]
    public void ThermalModelRejectsMissingNonfiniteAndSingularDataWithoutGuessingUnits()
    {
        foreach (var td in new[]
        {
            new List<double>(), new() { 1e-6, 0, 0, 0, 0, 0 },
            new() { 1e-6, double.NaN, 0, 0, 0, 0, 20 },
            new() { 1e-6, 0, 0, 0, 0, 0.5875618, 20 }
        }) Assert.False(CatalogThermalOptics.TryReferenceThermalPower(Glass(td), 587.5618, 1.5168, out _, out _));
        var large = Glass(new() { 2.957, 0, 0, 0, 0, 0, 25 });
        Assert.True(CatalogThermalOptics.TryReferenceThermalPower(large, 587.5618, 1.5168, out var raw, out _));
        Assert.True(raw > 1); // Preserve suspect source values rather than silently interpreting them as ppm.
    }

    [Fact]
    public void MapUsesDefinedWavelengthsTypedScaleAndHonestDefaultRange()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Materials.Analyze(new(MaterialAnalysisKind.AthermalGlassMap, "SCHOTT", "SCHOTT:N-BK7"));
        var chosen = before.Series.Single(s => s.Name.StartsWith("所选：", StringComparison.Ordinal)).Points.Single();
        Assert.True(ExternalGlassCatalogDatabase.TryResolve("SCHOTT:N-BK7", null, out var glass));
        var defined = app.Prescription.GetWavelengths();
        var nd = glass.RefractiveIndex(defined.Single(wave => wave.IsPrimary).Nanometers);
        var expectedChromatic = (glass.RefractiveIndex(defined.Min(wave => wave.Nanometers))
            - glass.RefractiveIndex(defined.Max(wave => wave.Nanometers))) / (nd - 1) * 1000;
        Assert.Equal(expectedChromatic, chosen.X, 10);
        Assert.InRange(chosen.Y, -2, 4);
        Assert.Equal(0, before.PlotOptions.XMinimum); Assert.Equal(60, before.PlotOptions.XMaximum);
        Assert.Equal(-30, before.PlotOptions.YMinimum); Assert.Equal(30, before.PlotOptions.YMaximum);
        Assert.True(before.PlotOptions.ShowPointLabels); Assert.True(before.PlotOptions.LegendBelow);
        Assert.True(before.PlotOptions.AvoidOverlappingPointLabels);
        Assert.All(before.Series, series => Assert.Equal(AnalysisAxisUnit.PartsPerThousand, series.XUnit));
        Assert.Equal("色光焦 (10⁻³)", AnalysisAxisFormatting.FormatLabel("色光焦", AnalysisAxisQuantity.ChromaticPower, AnalysisAxisUnit.PartsPerThousand));
        Assert.Equal(15, AnalysisAxisFormatting.Convert(.015, AnalysisAxisUnit.Dimensionless, AnalysisAxisUnit.PartsPerThousand));
        var waves = app.Prescription.GetWavelengths();
        app.Prescription.UpdateWavelength(waves[0] with { Nanometers = 450 });
        app.Prescription.UpdateWavelength(waves[2] with { Nanometers = 700 });
        var after = app.Materials.Analyze(new(MaterialAnalysisKind.AthermalGlassMap, "SCHOTT", "SCHOTT:N-BK7"));
        Assert.True(after.Series.Single(s => s.Name.StartsWith("所选：", StringComparison.Ordinal)).Points.Single().X > chosen.X);
        Assert.Contains(after.Rows, row => row.Metric == "最短 / 主 / 最长波长 (μm)" && row.Value.StartsWith("0.45 /", StringComparison.Ordinal));
        var all = app.Materials.Analyze(new(MaterialAnalysisKind.AthermalGlassMap));
        var count = int.Parse(all.Rows.Single(row => row.Metric == "有效绘图点").Value, CultureInfo.InvariantCulture);
        var inside = int.Parse(all.Rows.Single(row => row.Metric == "默认范围内").Value, CultureInfo.InvariantCulture);
        var outside = int.Parse(all.Rows.Single(row => row.Metric == "默认范围外（保留）").Value, CultureInfo.InvariantCulture);
        Assert.Equal(count, inside + outside); Assert.True(inside > 1000); Assert.True(outside > 0);
        Assert.Contains("OHARA-A:TPH55", all.ReportText);
        Assert.Contains(all.Series.SelectMany(s => s.Points), point => Math.Abs(point.Y) > 1000000);
    }

    [Fact]
    public void NonstandardEnvironmentPressureIsExplicitlyUnavailable()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var environment = app.Prescription.GetEnvironmentSettings();
        app.Prescription.UpdateEnvironmentSettings(environment with { PressureAtmospheres = 0.5, MatchRefractiveIndexData = true });
        var map = app.Materials.Analyze(new(MaterialAnalysisKind.AthermalGlassMap));
        Assert.Equal(AnalysisOutcome.Unavailable, map.Outcome);
        Assert.Empty(map.Series); Assert.Contains("1 atm", map.ReportText);
    }

    [Theory]
    [InlineData("Light", 1500, 1)]
    [InlineData("Dark", 1100, 1.5)]
    [InlineData("Isekai", 900, 2)]
    public async Task NativePanelKeepsFiniteOutliersAndAllowsDefaultAllRangeRoundTrip(string theme, int width, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AthermalGlassMapTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new AppSettings());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var panel = new MaterialAnalysisPanel(app.Materials, MaterialAnalysisKind.AthermalGlassMap);
            var window = new Window { Width = width, Height = 900, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window);
                var plot = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().Single();
                var toggle = panel.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "AthermalShowAllRange");
                if (theme == "Isekai") Assert.Equal("Show full range", toggle.Content);
                var points = plot.Series.SelectMany(s => s.Points).ToArray();
                Assert.Equal(60, plot.PlotOptions.XMaximum);
                Assert.Contains(points, point => Math.Abs(point.Y) > 1000000);
                Capture(window, $"athermal-{theme}-{width}.png", scale);
                toggle.IsChecked = true; Tick(window);
                Assert.Null(plot.PlotOptions.XMaximum); Assert.Null(plot.PlotOptions.YMaximum);
                Assert.Equal(points, plot.Series.SelectMany(s => s.Points));
                Capture(window, $"athermal-{theme}-all-range.png", scale);
                toggle.IsChecked = false; Tick(window);
                Assert.Equal(60, plot.PlotOptions.XMaximum); Assert.Equal(30, plot.PlotOptions.YMaximum);
                Assert.Equal(points, plot.Series.SelectMany(s => s.Points));
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); }
        }, CancellationToken.None);
    }

    private static CatalogGlassMaterial Glass(List<double> td) => new("test", "test", "zemax formula 1", 300, 2500,
        new[] { 2.3, 0.0, 0, 0, 0, 0 }, zemaxData: new OpticalGlassDefinition
        { Name = "test", ThermalCoefficients = td, ThermalExpansionLow = 7.1 });
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Capture(Window window, string name, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ATHERMAL_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        Assert.InRange(Math.Abs(image.PixelSize.Width - window.ClientSize.Width * scale), 0, 1);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
