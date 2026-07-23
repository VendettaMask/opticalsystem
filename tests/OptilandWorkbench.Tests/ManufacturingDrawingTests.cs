using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Manufacturing;

namespace OptilandWorkbench.Tests;

public sealed class ManufacturingDrawingTests
{
    [Fact]
    public void CookePrescriptionBuildsManufacturableOpticalElements()
    {
        using var application = WorkbenchApplication.Create("cooke");

        var elements = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces());
        var report = OpticalManufacturingModel.Evaluate(
            application.Prescription.GetSurfaces(),
            new ManufacturabilitySettings());

        Assert.Equal(3, elements.Count);
        Assert.All(elements, element =>
        {
            Assert.True(element.Diameter > 0);
            Assert.True(element.CenterThickness > 0);
            Assert.False(element.Material.Equals("Air", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Equal(elements.Count, report.Elements.Count);
        Assert.NotEmpty(report.Findings);
    }

    [Fact]
    public void ManufacturabilityFlagsSurfaceOutsideRealSagDomain()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces().ToArray();
        surfaces[1] = surfaces[1] with
        {
            Radius = 2,
            SemiDiameter = 10,
            Conic = 0
        };

        var report = OpticalManufacturingModel.Evaluate(
            surfaces,
            new ManufacturabilitySettings());

        Assert.Contains(
            report.Findings,
            finding => finding.Severity == ManufacturabilitySeverity.Error
                && finding.Check.Contains("有效域", StringComparison.Ordinal));
    }

    [Fact]
    public void OpticalDrawingRendersPreviewAndVectorPdf()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];
        var material = application.Materials.GetGlasses()
            .FirstOrDefault(glass => glass.Name.Equals(
                element.Material,
                StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(material);
        Assert.True(material.RefractiveIndexD > 1);
        Assert.True(material.AbbeNumber > 0);

        using var font = typeof(OpticalDrawingRenderer).Assembly.GetManifestResourceStream(
            "OptilandWorkbench.App.Assets.Fonts.NotoSansCJKsc-Regular.otf");
        Assert.NotNull(font);
        Assert.True(font.Length > 15_000_000);
        using var companyLogo = typeof(OpticalDrawingRenderer).Assembly.GetManifestResourceStream(
            "OptilandWorkbench.App.Assets.Brand.CompanyLogo.png");
        Assert.NotNull(companyLogo);
        Assert.True(companyLogo.Length > 10_000);

        var sheet = Sheet(element) with { MaterialData = material };
        Assert.Equal(0.0005, sheet.RefractiveIndexTolerance);
        Assert.Equal(0.5, sheet.AbbeNumberTolerance);
        var path = Path.Combine(Path.GetTempPath(), $"optical-drawing-{Guid.NewGuid():N}.pdf");
        var page = OpticalDrawingRenderer.PageDimensions(sheet.PageSize);

        Assert.True(page.Height > page.Width);
        Assert.InRange(page.Width / page.Height, 0.706f, 0.708f);

        try
        {
            var preview = OpticalDrawingRenderer.RenderPreview(sheet, 800);
            var logoSheet = sheet with { CompanyLogoPng = TransparentPng };
            var logoPreview = OpticalDrawingRenderer.RenderPreview(logoSheet, 800);
            OpticalDrawingRenderer.ExportPdf(path, logoSheet);
            var pdf = File.ReadAllBytes(path);

            Assert.True(preview.Length > 10_000);
            Assert.True(logoPreview.Length > 10_000);
            Assert.False(preview.SequenceEqual(logoPreview));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, preview[..4]);
            Assert.True(pdf.Length > 5_000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpticalDrawingRejectsInvertedToleranceLimits()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];
        var sheet = Sheet(element) with
        {
            DiameterUpperDeviation = -0.02,
            DiameterLowerDeviation = 0.02,
            FrontRadiusTolerance = -0.1
        };

        var errors = sheet.Validate();

        Assert.Contains(errors, error => error.Contains("直径上偏差", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("S1 曲率半径公差", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.RenderPreview(sheet, 400));
    }

    [Fact]
    public void OpticalDrawingUsesIsoRecommendedScaleDesignation()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];

        var designation = OpticalDrawingRenderer.ScaleDesignation(Sheet(element));

        Assert.Contains(
            designation,
            new[] { "10:1", "5:1", "2:1", "1:1", "1:2", "1:5", "1:10" });
    }

    [Fact]
    public void OpticalDrawingRequiresMaterialAndSurfaceIndications()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];
        var sheet = Sheet(element) with
        {
            SurfaceImperfection = " ",
            BubblesAndInclusions = string.Empty
        };

        var errors = sheet.Validate();

        Assert.Contains(errors, error => error.StartsWith("5/", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("1/", StringComparison.Ordinal));
    }

    [Fact]
    public void OpticalDrawingSupportsCurrentChineseNationalStandardLayout()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];
        var isoSheet = Sheet(element);
        var gbSheet = isoSheet with { Standard = OpticalDrawingStandard.GbT13323_2009 };

        var isoPreview = OpticalDrawingRenderer.RenderPreview(isoSheet, 800);
        var gbPreview = OpticalDrawingRenderer.RenderPreview(gbSheet, 800);

        Assert.Equal("ISO 10110-1:2019 表格式", OpticalDrawingRenderer.StandardDesignation(isoSheet.Standard));
        Assert.Equal("GB/T 13323—2009 光学制图", OpticalDrawingRenderer.StandardDesignation(gbSheet.Standard));
        Assert.False(isoPreview.SequenceEqual(gbPreview));
        Assert.True(gbPreview.Length > 10_000);
    }

    [Fact]
    public void IsoOpticalGlassMarksUseShortLongShortLinePattern()
    {
        var iso = OpticalDrawingRenderer.OpticalGlassHatchHalfLengths(
            OpticalDrawingStandard.Iso10110);
        var gb = OpticalDrawingRenderer.OpticalGlassHatchHalfLengths(
            OpticalDrawingStandard.GbT13323_2009);

        Assert.Equal(iso[0], iso[2]);
        Assert.True(iso[1] > iso[0]);
        Assert.Equal(gb[0], gb[1]);
        Assert.Equal(gb[1], gb[2]);
        Assert.Equal("R50 ±0.1", OpticalDrawingRenderer.RadiusDimensionText(50, 0.1));
        Assert.Equal("R∞", OpticalDrawingRenderer.RadiusDimensionText(0, 0.1));
    }

    private static readonly byte[] TransparentPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static OpticalDrawingSheet Sheet(OpticalElementDefinition element) => new(
        element,
        OpticalDrawingPageSize.A4,
        "OPT-TEST-001",
        "测试光学元件",
        "设计",
        "审核",
        "A",
        0.02,
        -0.01,
        0.03,
        -0.02,
        100,
        100,
        1,
        1,
        "2 × 0.16",
        "增透膜",
        "倒边 0.2 × 45°",
        "10 nm/cm",
        "2 × 0.1",
        "2；2");
}
