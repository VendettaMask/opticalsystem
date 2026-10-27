using System.Text.Json;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;

namespace OpticalSystemDesign.Tests;

public sealed class LegacyGb1991AnnotationTests
{
    [Theory]
    [InlineData(50.123456789, "R50.123456789")]
    [InlineData(-50.123456789, "R50.123456789")]
    [InlineData(0, "R∞")]
    [InlineData(double.PositiveInfinity, "R∞")]
    public void LegacyRadiusIsNominalAndPreservesSignificantDigits(double radius, string expected)
    {
        Assert.Equal(expected, OpticalDrawingRendererCore.Gb1991RadiusDimensionText(radius));
        Assert.DoesNotContain("±", expected);
        Assert.Equal("R50.123 ±0.1", OpticalDrawingRenderer.RadiusDimensionText(-50.123456789, 0.1));
    }

    [Fact]
    public void RadiusToleranceRemainsSharedButDoesNotLeakIntoLegacyRadiusIndication()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        var edited = sheet with { FrontRadiusTolerance = 0.987, BackRadiusTolerance = 0.654 };
        Assert.Equal(OpticalDrawingRenderer.RenderPreview(sheet, 900), OpticalDrawingRenderer.RenderPreview(edited, 900));
        Assert.NotEqual(OpticalDrawingRenderer.RenderPreview(sheet with { Standard = OpticalDrawingStandard.Iso10110 }, 900),
            OpticalDrawingRenderer.RenderPreview(edited with { Standard = OpticalDrawingStandard.Iso10110 }, 900));
    }

    [Fact]
    public void RaMicrometersAndRqNanometersRemainIndependentWithExplicitSurfaceScope()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        Assert.All(OpticalDrawingRendererCore.Gb1991RoughnessAnnotations(sheet), row => Assert.Null(row.RaMicrometers));
        var specified = sheet with
        {
            SurfaceTextureNanometers = 7,
            LegacyGb1991 = new LegacyGb1991Requirements
            {
                OpticalSurfaceRaMicrometers = 0.01, OtherSurfaceRaMicrometers = 1.6,
                OpticalRaSamplingLengthMillimeters = 0.08, OtherRaSamplingLengthMillimeters = 0.8,
                PowerFringes = "S1:3;S2:2", IrregularityFringes = "0.5", TestPlateGrade = "S1:A;S2:B",
                FringeReferenceWavelengthNanometers = 632.8, TechnicalRequirements = "镀膜后检验；光学面按S面号识别。"
            }
        };
        var rows = OpticalDrawingRendererCore.Gb1991RoughnessAnnotations(specified);
        Assert.Equal("S1–S2光学面", rows[0].Scope);
        Assert.Equal(0.01, rows[0].RaMicrometers);
        Assert.Equal(0.08, rows[0].SamplingLengthMillimeters);
        Assert.Equal("其余表面", rows[1].Scope);
        Assert.Equal(1.6, rows[1].RaMicrometers);
        Assert.Contains(OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(specified), text => text.Contains("Rq 7 nm"));
        Assert.Contains(OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(specified), text => text.Contains("632.8 nm"));
        Assert.Contains(OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(specified), text => text.Contains("定心基准：机械外圆轴"));
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(specified));
        Assert.Equal(rows, OpticalDrawingRendererCore.Gb1991RoughnessAnnotations(specified with { SurfaceTextureNanometers = 14 }));
        Capture("annotation-ra-rq-and-scoped-requirements", specified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRoughnessSamplingLengthAndWavelengthCannotBeExported(double value)
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        foreach (var requirements in new[]
        {
            new LegacyGb1991Requirements { OpticalSurfaceRaMicrometers = value },
            new LegacyGb1991Requirements { OtherSurfaceRaMicrometers = value },
            new LegacyGb1991Requirements { OpticalSurfaceRaMicrometers = 0.01, OpticalRaSamplingLengthMillimeters = value },
            new LegacyGb1991Requirements { FringeReferenceWavelengthNanometers = value }
        }) Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.RenderPreview(sheet with { LegacyGb1991 = requirements }, 100));
    }

    [Fact]
    public void SamplingLengthNeedsItsOwnRaAndInvalidPlateGradeDoesNotBecomeMillimeters()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(sheet with
        {
            LegacyGb1991 = new LegacyGb1991Requirements { OpticalRaSamplingLengthMillimeters = 0.08 }
        }), text => text.Contains("同时填写Ra"));
        foreach (var plate in new[] { "0.1", "C", "S3:A", "S1:A;S1:B" })
            Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(sheet with
            { LegacyGb1991 = new LegacyGb1991Requirements { TestPlateGrade = plate } }), text => text.Contains("ΔR样板等级"));
    }

    [Fact]
    public void MechanicalOutsideDiameterAndEffectiveApertureAreSeparate()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        Assert.Equal(12, sheet.Element.Diameter);
        Assert.Equal(4, sheet.Element.ClearSemiDiameter);
        Assert.Contains(OpticalDrawingRendererCore.Gb1991PartSpecificationRows(sheet), row => row.Item == "D_0" && row.Value == "⌀8");
        Assert.Contains(OpticalDrawingRendererCore.Gb1991GeometryDefinitionLines(sheet), text => text.Contains("直径12 mm"));
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(sheet));
        Capture("annotation-mechanical-diameter-12-clear-aperture-8", sheet);
    }

    [Fact]
    public void InvalidMechanicalProfileIsRejectedRatherThanClippedAndDimensionedAsOriginal()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        var component = sheet.Element.Components[0];
        sheet = sheet with { Element = new OpticalDrawingElementDefinition(new[]
        {
            component with { FrontSurface = component.FrontSurface with { Radius = 1, Thickness = 0.2 },
                BackSurface = component.BackSurface with { Radius = -1 } }
        }) };
        Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(sheet), text => text.Contains("机械口径"));
        Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.RenderPreview(sheet, 100));
    }

    [Fact]
    public void CircularPhysicalApertureUsesItsActualRadiusThroughNonLocalizedSnapshot()
    {
        var optic = Optic.CreateDemo();
        var source = optic.SurfaceGroup.Items[1];
        source.PhysicalAperture = new CircularAperture(3);
        var mapped = WorkbenchMapper.ToSurfaceDto(source);
        Assert.Equal(new SurfaceDrawingApertureDto("circular", 3), mapped.DrawingAperture);
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        var component = sheet.Element.Components[0];
        sheet = sheet with { Element = new OpticalDrawingElementDefinition(new[]
        { component with { FrontSurface = component.FrontSurface with
            { DrawingAperture = mapped.DrawingAperture, ApertureKind = "display name is not the authority" } } }) };
        Assert.Contains(OpticalDrawingRendererCore.Gb1991PartSpecificationRows(sheet), row => row.Item == "D_0" && row.Value == "⌀6");
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(sheet));
        Capture("annotation-physical-circle-6-mechanical-12", sheet);
    }

    [Fact]
    public void UnsupportedOrUnknownPhysicalApertureCannotBeRelabeledAsCircularD0()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application);
        var component = sheet.Element.Components[0];
        foreach (var front in new[]
        {
            component.FrontSurface with { DrawingAperture = new("rectangular"), ApertureKind = "圆形" },
            component.FrontSurface with { DrawingAperture = new("circular"), ApertureKind = "圆形" },
            component.FrontSurface with { DrawingAperture = null, ApertureKind = "矩形" }
        })
        {
            var unsupported = sheet with { Element = new OpticalDrawingElementDefinition(new[] { component with { FrontSurface = front } }) };
            Assert.NotEmpty(OpticalDrawingRenderer.ValidateTemplateLayout(unsupported));
            Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.RenderPreview(unsupported, 100));
        }
    }

    [Fact]
    public void HiddenIsoOnlyFieldsDoNotBlockIndependentLegacyIndications()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application) with { SurfaceImperfection = "", StressBirefringence = "", BubblesAndInclusions = "",
            HomogeneityAndStriae = "", FrontRadiusTolerance = -1, BackRadiusTolerance = -1 };
        Assert.Empty(sheet.Validate());
        Assert.NotEmpty(OpticalDrawingRenderer.RenderPreview(sheet, 100));
        Assert.Equal(6, (sheet with { Standard = OpticalDrawingStandard.Iso10110 }).Validate().Count);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("even_asphere")]
    [InlineData("odd_asphere")]
    public void AspheresShowActualExpressionParametersAndBaseRadius(string kind)
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application) with { PageSize = OpticalDrawingPageSize.A3 };
        var component = sheet.Element.Components[0];
        var numbers = new Dictionary<string, double> { ["radius"] = 999, ["conic"] = 999 };
        if (kind != "standard") { numbers["c0"] = 1e-4; numbers["c1"] = 2e-8; }
        var front = component.FrontSurface with { Radius = 50, Conic = -1, GeometryDefinition = new(kind, numbers) };
        sheet = sheet with { Element = new OpticalDrawingElementDefinition(new[] { component with { FrontSurface = front } }) };
        var lines = OpticalDrawingRendererCore.Gb1991GeometryDefinitionLines(sheet);
        Assert.Contains(lines, text => text.Contains("S1 非球面：z="));
        Assert.Contains(lines, text => text.Contains("R=50 mm；k=-1"));
        Assert.DoesNotContain(lines, text => text.Contains("999"));
        // The printed polynomial convention must match shared Core, including the r²/r¹
        // first coefficient (not an assumed A4 convention). For k=-1 the base at r=2 is .04.
        var expected = kind switch { "even_asphere" => 0.04040032, "odd_asphere" => 0.04020008, _ => 0.04 };
        Assert.Equal(expected, ManufacturingGeometryService.Sag(front, 2)!.Value, 12);
        if (kind == "even_asphere") Assert.Contains(lines, text => text.Contains("a2=") && text.Contains("a4="));
        if (kind == "odd_asphere") Assert.Contains(lines, text => text.Contains("a1=") && text.Contains("a2="));
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(sheet));
        Capture("annotation-asphere-" + kind, sheet);
    }

    [Fact]
    public void ForbesRequiresControlledDefinitionReferenceAndKeepsAllCapturedCoefficients()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application) with { PageSize = OpticalDrawingPageSize.A3 };
        var component = sheet.Element.Components[0];
        var definition = new SurfaceGeometryDefinitionDto("forbes_q", new Dictionary<string, double>
        { ["radius"] = 100, ["conic"] = 0, ["normalizationRadius"] = 10, ["q0"] = 0.0001, ["q1"] = -0.00001 });
        sheet = sheet with { Element = new OpticalDrawingElementDefinition(new[]
        { component with { FrontSurface = component.FrontSurface with { GeometryDefinition = definition } } }) };
        Assert.Contains(OpticalDrawingRenderer.ValidateTemplateLayout(sheet), text => text.Contains("须填写面型定义"));
        sheet = sheet with { LegacyGb1991 = new LegacyGb1991Requirements { AsphericDefinitionReference = "受控面型图 Q-001 Rev A（测试引用）" } };
        var lines = OpticalDrawingRendererCore.Gb1991GeometryDefinitionLines(sheet);
        Assert.Contains(lines, text => text.Contains("受控面型图 Q-001 Rev A"));
        Assert.Contains(lines, text => text.Contains("normalizationRadius=10") && text.Contains("q0=") && text.Contains("q1="));
        Assert.Empty(OpticalDrawingRenderer.ValidateTemplateLayout(sheet));
        Capture("annotation-asphere-forbes_q", sheet);
    }

    [Fact]
    public void InvalidAnnotationsPreserveExistingPdfOnFailedExport()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var sheet = Sheet(application) with { LegacyGb1991 = new LegacyGb1991Requirements { OpticalSurfaceRaMicrometers = -0.01 } };
        var path = Path.Combine(Path.GetTempPath(), "legacy-annotations-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllText(path, "previous drawing");
            Assert.Throws<ArgumentException>(() => OpticalDrawingRenderer.ExportPdf(path, sheet));
            Assert.Equal("previous drawing", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static OpticalDrawingSheet Sheet(WorkbenchApplication application)
    {
        var sheet = LegacyGb1991DrawingTests.CreateSheet(application, OpticalDrawingPageSize.A4, false);
        var component = sheet.Element.Components[0];
        return sheet with { Element = new OpticalDrawingElementDefinition(new[]
        {
            component with
            {
                FrontSurface = component.FrontSurface with { Radius = 100, Conic = 0, Thickness = 4, SemiDiameter = 4, MechanicalSemiDiameter = 6,
                    GeometryDefinition = new("standard", new Dictionary<string, double> { ["radius"] = 100, ["conic"] = 0 }), DrawingAperture = new("none") },
                BackSurface = component.BackSurface with { Radius = -100, Conic = 0, SemiDiameter = 4, MechanicalSemiDiameter = 6,
                    GeometryDefinition = new("standard", new Dictionary<string, double> { ["radius"] = -100, ["conic"] = 0 }), DrawingAperture = new("none") }
            }
        }) };
    }

    private static void Capture(string name, OpticalDrawingSheet sheet)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GB1991_ANNOTATION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), OpticalDrawingRenderer.RenderPreview(sheet, 1500));
        OpticalDrawingRenderer.ExportPdf(Path.Combine(directory, name + ".pdf"), sheet);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        { sheet, Definitions = OpticalDrawingRendererCore.Gb1991GeometryDefinitionLines(sheet),
            Notes = OpticalDrawingRendererCore.Gb1991TechnicalRequirementLines(sheet),
            Roughness = OpticalDrawingRendererCore.Gb1991RoughnessAnnotations(sheet),
            Scope = "Explicit synthetic presentation fixtures; shared Core checks only; not external optical numerical certification." },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
