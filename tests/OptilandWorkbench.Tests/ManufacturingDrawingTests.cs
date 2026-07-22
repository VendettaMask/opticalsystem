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
        "0.16 × 2",
        "增透膜",
        "倒边 0.2 × 45°",
        "≤ 10 nm/cm",
        "0.1 × 2",
        "2；2");
}
