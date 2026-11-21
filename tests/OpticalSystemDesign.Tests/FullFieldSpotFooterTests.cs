using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using AxisUnit = OpticalSystemDesign.Application.Contracts.AnalysisAxisUnit;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class FullFieldSpotFooterTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    public void ChiefReferenceUsesTheFirstDeclaredFieldAndTheSelectedSpectrum(int fieldNumber, int wavelengthNumber)
    {
        var optic = OffAxisFirstField();
        var settings = new SpotDiagramSettings(RayDensity: 2, Reference: "chief", FieldNumber: fieldNumber, WavelengthNumber: wavelengthNumber);
        var raw = Raw(optic, settings);
        var wavelengths = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber);
        var wave = wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelengths[0];
        var origin = Chief(optic, SpotAnalysisEngine.DefinedFields(optic)[0], wave);
        var data = new SpotDiagramVariantAnalysis(optic, SpotDiagramVariant.FullField, settings).GenerateData();
        AssertRadii(data, raw, origin.X, origin.Y, 1000);
        Assert.Equal(0, data.PlotOptions!.XMinimum!.Value + data.PlotOptions.XMaximum!.Value);
        Assert.Equal(0, data.PlotOptions.YMinimum!.Value + data.PlotOptions.YMaximum!.Value);
        Assert.Equal((double)data.Values["ScaleBarMicrometers"], data.PlotOptions.XMaximum.Value - data.PlotOptions.XMinimum.Value);
        Assert.True((double)data.Values["RmsRadius"] > 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CentroidUsesAllSelectedRaysWithSpectralWeightsAndManualScaleIsTheCompleteWidth(int wavelengthNumber)
    {
        var optic = OffAxisFirstField();
        optic.Wavelengths[0].Weight = 0.25; optic.Wavelengths[1].Weight = 4; optic.Wavelengths[2].Weight = 1;
        var settings = new SpotDiagramSettings(RayDensity: 2, Reference: "centroid", WavelengthNumber: wavelengthNumber, PlotScaleMicrometers: 12345.5);
        var raw = Raw(optic, settings);
        var rays = raw.Fields.SelectMany(f => f.WeightedRays).ToArray();
        var total = rays.Sum(r => r.Intensity);
        var x = rays.Sum(r => r.X * r.Intensity) / total;
        var y = rays.Sum(r => r.Y * r.Intensity) / total;
        var data = new SpotDiagramVariantAnalysis(optic, SpotDiagramVariant.FullField, settings).GenerateData();
        AssertRadii(data, raw, x, y, 1000);
        Assert.Equal(-12345.5 / 2, data.PlotOptions!.XMinimum);
        Assert.Equal(12345.5 / 2, data.PlotOptions.XMaximum);
        Assert.Equal(12345.5, data.Values["ScaleBarMicrometers"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedTraceHonorsTheOpticAimingFlagAndStandardSpotTransmissionWeightPolicy(bool aiming, bool polarization)
    {
        var optic = OffAxisFirstField(); optic.RayAimingEnabled = aiming;
        var settings = new SpotDiagramSettings(RayDensity: 3, Reference: "chief", UsePolarization: polarization);
        var raw = Raw(optic, settings);
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var origin = Chief(optic, SpotAnalysisEngine.DefinedFields(optic)[0], wave);
        var data = new SpotDiagramVariantAnalysis(optic, SpotDiagramVariant.FullField, settings).GenerateData();
        AssertRadii(data, raw, origin.X, origin.Y, 1000);
        Assert.Equal(aiming, data.Values["UseRayAiming"]);
        Assert.Equal(raw.RayCount, data.Values["RayCount"]);
        Assert.Equal(raw.VignettedRayCount, data.Values["VignettedRayCount"]);
    }

    [Fact]
    public void ExaggerationDoesNotFalsifyThePhysicalRadiusAndAfocalFooterUsesAngularUnits()
    {
        var optic = OffAxisFirstField(); optic.ImageSpaceAfocal = true;
        var settings = new SpotDiagramSettings(RayDensity: 2, Reference: "chief", PlotScaleMicrometers: 100);
        var ordinary = new SpotDiagramVariantAnalysis(optic, SpotDiagramVariant.FullField, settings).GenerateData();
        var exaggerated = new SpotDiagramVariantAnalysis(optic, SpotDiagramVariant.FullField, settings with { Magnification = 20 }).GenerateData();
        Assert.Equal(ordinary.Values["RmsRadius"], exaggerated.Values["RmsRadius"]);
        Assert.Equal(ordinary.Values["GeometricRadius"], exaggerated.Values["GeometricRadius"]);
        Assert.Equal(100.0, ordinary.Values["ScaleBarMilliradians"]);
        Assert.All(ordinary.PlotSeries, series => Assert.Equal(OpticalSystemDesign.Core.Analysis.AnalysisAxisUnit.Milliradian, series.XUnit));
        var view = new WorkbenchRuntime(optic).BuildAnalysisView("Full Field Spot Diagram", new Dictionary<string, string> { ["RayDensity"] = "2" });
        Assert.Equal(AxisUnit.Milliradian, view.FullFieldSpotSummary!.Unit);
        Assert.Contains(view.Rows, r => r.Metric == "RMS 半径 (mrad)");
        Assert.DoesNotContain(view.Rows, r => r.Metric == "RMS 半径 (µm)");
        Assert.Contains("RMS 半径 (mrad)", view.ReportText);
    }

    [Fact]
    public void MsL7DefaultFooterReproducesTheUserReportedRoundedValuesWithoutChangingThePrescription()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(MsL7Source), ".zmx");
        var before = optic.ToSnapshot();
        var view = new WorkbenchRuntime(optic).BuildAnalysisView("Full Field Spot Diagram");
        var summary = Assert.IsType<FullFieldSpotSummaryDto>(view.FullFieldSpotSummary);
        Assert.Equal(1055.16, Math.Round(summary.RmsRadius, 2));
        Assert.Equal(1495.61, Math.Round(summary.GeometricRadius, 2));
        Assert.Equal(4000, summary.ScaleBar);
        Assert.Equal(1, summary.ReferenceFieldNumber);
        Assert.Equal("1055.16", view.Rows.Single(r => r.Metric == "RMS 半径 (µm)").Value);
        Assert.Equal("1495.61", view.Rows.Single(r => r.Metric == "GEO 半径 (µm)").Value);
        Assert.Contains("缩放标尺 (µm)：4000.00", view.ReportText);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot()));
    }

    [Theory]
    [InlineData("Light", "zh-CN", 1)]
    [InlineData("Dark", "ja-JP", 1.5)]
    [InlineData("Isekai", "en-US", 2)]
    public async Task RealFooterUsesTypedValuesAndSurvivesThemeLanguageAndExportLayout(string theme, string language, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme, Language = language }); UiLocalization.Apply(language);
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(MsL7Source), ".zmx");
            var runtime = new WorkbenchRuntime(optic);
            var raw = runtime.BuildAnalysisView("Full Field Spot Diagram");
            var view = WorkbenchMapper.ToAnalysisViewDto(raw) with { PresentationKind = AnalysisPresentationKind.FullFieldSpot };
            var falseRows = view with { Name = "renamed", Rows = [new("RMS 半径 (µm)", "999"), new("参考", "999")] };
            var footer = BuildFooter(falseRows);
            var document = new OpticalDocumentSnapshot("MS-L7", Path.GetFileName(MsL7Source), 0, "", false, false, 0, 0, 0, 0, 17, 3, 3);
            var result = (Control)typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [view, document, new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), null])!;
            var window = new Window { Width = 1200, Height = 900, Content = result };
            try
            {
                window.Show(); window.SetRenderScaling(scaling); UiLocalization.Attach(window); DisplayTypography.Apply(window); Tick(window);
                var lines = SummaryText(footer);
                Assert.Contains("1055.16", lines); Assert.Contains("1495.61", lines); Assert.Contains("4000.00", lines);
                Assert.DoesNotContain("999", lines);
                var pages = result.GetVisualDescendants().OfType<OpticalSystemDesign.App.Controls.UniformDiagramPage>().ToArray();
                Assert.NotEmpty(pages); Assert.All(pages, page => Assert.False(page.HasOverflow));
                var displayed = result.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                Assert.Contains(displayed, text => text.Contains("1055.16"));
                if (language == "en-US") Assert.Contains(displayed, text => text.Contains("GEO radius: 1495.61"));
                if (language == "ja-JP") Assert.Contains(displayed, text => text.Contains("GEO半径：1495.61"));
                var directory = Environment.GetEnvironmentVariable("FULL_FIELD_SPOT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, theme + ".png"), PngBitmapEncoderOptions.Default);
                }
                var angular = view with { FullFieldSpotSummary = new(1.25, 2.5, 10, AxisUnit.Milliradian,
                    SpotReferenceKind.Centroid, SpotColorGrouping.Field, 5, false) };
                Assert.Contains("mrad", SummaryText(BuildFooter(angular)));
                Assert.Contains("图例对应于视场", SummaryText(BuildFooter(angular)));
                Assert.Contains("参考：质心", SummaryText(BuildFooter(angular)));
                Assert.Equal("暂无摘要数据", SummaryText(BuildFooter(view with { FullFieldSpotSummary = null })));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    internal static string MsL7Source
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "OpticalSystemDesign.Core"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root"),
                "validation", "zemax", "2026-r1", "energy-repair-2026-09-06", "ms-l7.ZMX");
        }
    }

    private static Optic OffAxisFirstField()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { X = 1, Y = 3, Label = "off-axis first" });
        optic.Fields.Add(new FieldPoint { X = 0, Y = 0, Label = "on-axis later" });
        optic.Fields.Add(new FieldPoint { X = -2, Y = 8, Label = "edge" });
        return optic;
    }

    private static SpotAnalysisResult Raw(Optic optic, SpotDiagramSettings settings)
    {
        var fields = SpotAnalysisEngine.DefinedFields(optic);
        return SpotAnalysisEngine.Generate(optic, settings.FieldNumber > 0 ? [fields[settings.FieldNumber - 1]] : fields,
            AnalysisTrace.SelectWavelengths(optic, settings.WavelengthNumber), settings.RayDensity, settings.Pattern,
            reference: "absolute", usePolarization: settings.UsePolarization, aimAtStop: optic.RayAimingEnabled,
            includeSurfaceTransmission: settings.UsePolarization);
    }

    private static SpotRayData Chief(Optic optic, (double Hx, double Hy) field, Wavelength wave)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy, 0, 0, wave.Micrometers, optic.RayAimingEnabled);
        var index = optic.SurfaceGroup.Items.Count - 1;
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([index]));
        var sample = trace.GetSurfaceSamples(index)[0]!.Value.ToRayTraceSample();
        return ImageSpaceAnalysisSupport.ToImageSpaceRayData(optic, sample, optic.SurfaceGroup.Items[index],
            ImageSpaceAnalysisSupport.CoordinateDescriptor(optic));
    }

    private static void AssertRadii(AnalysisData data, SpotAnalysisResult raw, double x, double y, double scale)
    {
        var rays = raw.Fields.SelectMany(f => f.WeightedRays).ToArray();
        var expectedRms = Math.Sqrt(rays.Sum(r => ((r.X - x) * (r.X - x) + (r.Y - y) * (r.Y - y)) * r.Intensity) / rays.Sum(r => r.Intensity)) * scale;
        var expectedGeo = rays.Max(r => Math.Sqrt((r.X - x) * (r.X - x) + (r.Y - y) * (r.Y - y))) * scale;
        Assert.Equal(expectedRms, (double)data.Values["RmsRadius"], 9);
        Assert.Equal(expectedGeo, (double)data.Values["GeometricRadius"], 9);
    }

    private static Border BuildFooter(AnalysisViewDto view) => (Border)typeof(AnalysisPanel)
        .GetMethod("BuildAnalysisTitleBlock", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
            [view, new OpticalDocumentSnapshot("test", "test.ZMX", 0, "", false, false, 0, 0, 0, 0, 17, 3, 3),
                new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero)])!;
    private static string SummaryText(Border footer) => footer.GetLogicalDescendants().OfType<TextBlock>()
        .Select(text => text.Text).Single(text => text is not null && (text.Contains("RMS 半径：") || text == "暂无摘要数据"))!;
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
