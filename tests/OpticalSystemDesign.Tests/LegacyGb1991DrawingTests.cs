using System.Text.Json;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class LegacyGb1991DrawingTests
{
    [Theory]
    [InlineData(OpticalDrawingPageSize.A4, false)]
    [InlineData(OpticalDrawingPageSize.A3, false)]
    [InlineData(OpticalDrawingPageSize.A4, true)]
    [InlineData(OpticalDrawingPageSize.A3, true)]
    public void RequirementsUseOriginalColumnsAndPhysicalMillimeters(OpticalDrawingPageSize pageSize, bool cemented)
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = CreateSheet(application, pageSize, cemented) with
        {
            LegacyGb1991 = new LegacyGb1991Requirements
            {
                RefractiveIndexGrade = "3C", AbbeNumberGrade = "3C", HomogeneityGrade = "3",
                AbsorptionGrade = "3", StressGrade = "4", StriaeGrade = "1C", BubbleGrade = "2D",
                PowerFringes = "3", IrregularityFringes = "0.5", TestPlateGrade = "A",
                SurfaceImperfection = "3×0.063", FocalLength = "340", FrontFocalDistance = "300", BackFocalDistance = "300",
                OpticalSurfaceRaMicrometers = 0.01, OtherSurfaceRaMicrometers = 1.6,
                OpticalRaSamplingLengthMillimeters = 0.08, OtherRaSamplingLengthMillimeters = 0.8,
                FringeReferenceWavelengthNanometers = 546.07
            }
        };
        var materials = OpticalDrawingRendererCore.Gb1991MaterialSpecificationRows(sheet);
        Assert.Equal(new[] { "Δn_d", "Δν_d", "光学均匀性", "光吸收系数", "应力双折射", "条纹度", "气泡度" }, materials.Select(row => row.Item));
        Assert.Equal(new[] { "3C", "3C", "3", "3", "4", "1C", "2D" }, materials.Select(row => row.Value));
        var parts = OpticalDrawingRendererCore.Gb1991PartSpecificationRows(sheet);
        Assert.Equal(cemented
            ? new[] { "N", "ΔN", "ΔR", "χ", "B", "f′", "L_f", "L′_f", "θ_I", "θ_II", "D_0" }
            : new[] { "N", "ΔN", "ΔR", "χ", "B", "θ_I", "θ_II", "d", "f′", "L_f", "L′_f", "倒二面角", "倒三面角", "D_0" }, parts.Select(row => row.Item));
        Assert.Contains(parts, row => row.Item == "B" && row.Value == "3×0.063");
        Assert.Contains(parts, row => row.Item == "χ" && row.Value == "1′");
        Assert.Contains(parts, row => row.Item == "D_0" && row.Value == $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}");
        Assert.DoesNotContain(OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(sheet), line => line.Contains("±0.000500", StringComparison.Ordinal));

        const double mm = 72d / 25.4;
        var (width, height) = OpticalDrawingRenderer.PageDimensions(pageSize);
        var regions = OpticalDrawingRendererCore.Gb1991Regions(sheet);
        Assert.Equal(40 * mm, regions.Requirements.Width, 3);
        Assert.Equal((cemented ? 74 : 142) * mm, regions.Requirements.Height, 3);
        Assert.Equal(width - 18, regions.Requirements.Right, 3);
        Assert.Equal(18, regions.Requirements.Top);
        Assert.True(regions.Geometry.Right < regions.Requirements.Left);
        Assert.True(regions.Requirements.Bottom < regions.Notes.Top);
        Assert.True(regions.Notes.Bottom < regions.Title.Top);
        Assert.True(regions.Title.Bottom < height);
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(sheet));

        var png = OpticalDrawingRenderer.RenderPreview(sheet, 1500);
        using var bitmap = SKBitmap.Decode(png);
        var scale = bitmap.Width / width;
        // Measure the exported pixels at the standard's 40-mm table and 8-mm header,
        // rather than assuming the layout helper was actually used by the renderer.
        AssertDark((width - 18 - 40 * mm) * scale, (18 + 8 * mm + 2) * scale);
        AssertDark((width - 18 - 20 * mm) * scale, (18 + 8 * mm) * scale);
        var labelWidth = cemented ? 15 : 25;
        AssertDark((width - 18 - (40 - labelWidth) * mm) * scale, (18 + 8 * mm + 3 * mm) * scale);

        var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GB1991_LAYOUT_CAPTURE_DIR");
        var directory = capture ?? Path.Combine(Path.GetTempPath(), "gb1991-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var stem = $"gb1991-{pageSize}-{(cemented ? "cemented" : "single")}";
            File.WriteAllBytes(Path.Combine(directory, stem + ".png"), png);
            OpticalDrawingRenderer.ExportPdf(Path.Combine(directory, stem + ".pdf"), sheet);
            File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
            {
                pageSize, cemented,
                Requirements = new { regions.Requirements.Left, regions.Requirements.Top, regions.Requirements.Width, regions.Requirements.Height },
                MaterialRows = materials, PartRows = parts, sheet.LegacyGb1991,
                Notes = OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(sheet),
                Roughness = OpticalDrawingRendererCore.Gb1991RoughnessAnnotations(sheet),
                Scope = "Presentation test values transcribed from the standard examples; not a computed focal-length claim for this demo lens."
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        finally { if (capture is null) Directory.Delete(directory, true); }

        void AssertDark(double x, double y)
        {
            var found = false;
            for (var dx = -2; dx <= 2; dx++)
                for (var dy = -2; dy <= 2; dy++)
                {
                    var pixel = bitmap.GetPixel((int)Math.Round(x) + dx, (int)Math.Round(y) + dy);
                    found |= pixel.Red < 90 && pixel.Green < 90 && pixel.Blue < 90;
                }
            Assert.True(found, $"Missing original-table border at {x:0.##},{y:0.##}");
        }
    }

    [Fact]
    public void MissingLegacyGradesAreNotInferredFromIsoIndicationsOrNanometers()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = CreateSheet(application, OpticalDrawingPageSize.A4, false);
        var materials = OpticalDrawingRendererCore.Gb1991MaterialSpecificationRows(sheet);
        Assert.Contains(materials, row => row.Item == "Δn_d" && row.Value == "未规定");
        Assert.Contains(materials, row => row.Item == "Δν_d" && row.Value == "未规定");
        Assert.Contains(materials, row => row.Item == "光学均匀性" && row.Value == "未规定");
        Assert.Contains(materials, row => row.Item == "光吸收系数" && row.Value == "未规定");
        Assert.Contains(materials, row => row.Item == "应力双折射" && row.Value == "未规定");
        var parts = OpticalDrawingRendererCore.Gb1991PartSpecificationRows(sheet);
        foreach (var item in new[] { "N", "ΔN", "ΔR", "B", "f′", "L_f", "L′_f" })
            Assert.Contains(parts, row => row.Item == item && row.Value == "未规定");
        Assert.Contains(parts, row => row.Item == "θ_I" && row.Value == "不适用");
    }

    [Fact]
    public void ExcessiveCellOrTechnicalTextIsRejectedBeforeDrawing()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = CreateSheet(application, OpticalDrawingPageSize.A4, false);
        var longCell = sheet with { LegacyGb1991 = new LegacyGb1991Requirements { PowerFringes = new string('长', 80) } };
        Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(longCell), message => message.Contains("N 单元格", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.RenderPreview(longCell, 800));
        Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(sheet with { EdgeTreatment = new string('边', 3000) }),
            message => message.Contains("技术要求文字过长", StringComparison.Ordinal));
    }

    internal static OpticalDrawingSheet CreateSheet(WorkbenchApplication application, OpticalDrawingPageSize size, bool cemented)
    {
        var element = OpticalManufacturingModel.BuildDrawingElements(application.Prescription.GetSurfaces()).First(item => item.IsCemented == cemented);
        var materials = element.Components.Select(component => application.Materials.FindGlass(component.Material)).ToArray();
        return new OpticalDrawingSheet(element, size, "GB1991-PRESENTATION-TEST", "老国标版式核验", "设计", "审核", "A",
            0.02, -0.02, 0.03, -0.02, 100, 150, 1, 1, "ISO-only test indication", "增透膜", "倒边 0.2 × 45°",
            "10 nm/cm", "ISO bubbles", "ISO homogeneity", MaterialData: materials[0],
            Standard: OpticalDrawingStandard.GbT13323_1991, ComponentMaterialData: materials);
    }
}
