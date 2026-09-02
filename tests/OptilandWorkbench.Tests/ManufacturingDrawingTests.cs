using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Visualization;

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
        Assert.Equal(elements.Count * 7, report.GeometryMetrics.Count);
        var firstElementMetrics = report.GeometryMetrics
            .Where(metric => metric.ElementNumber == 1)
            .ToArray();
        Assert.Contains(firstElementMetrics, metric => metric.Item == "机械直径" && metric.Value.EndsWith(" mm", StringComparison.Ordinal));
        Assert.Contains(firstElementMetrics, metric => metric.Item == "中心厚度" && metric.Value.EndsWith(" mm", StringComparison.Ordinal));
        Assert.Contains(firstElementMetrics, metric => metric.Item == "有光焦度面半径绝对值");
        Assert.Contains(firstElementMetrics, metric => metric.Item == "全口径弧高");
        Assert.Contains(firstElementMetrics, metric => metric.Item == "全口径边厚");
        Assert.Contains(firstElementMetrics, metric => metric.Item == "球面边缘倾角");
        Assert.Contains(firstElementMetrics, metric => metric.Item == "表面类型" && metric.Value.Contains("标准面", StringComparison.Ordinal));
    }

    [Fact]
    public void ManufacturabilityGeometryMetricsUseLargestSurfaceDiameter()
    {
        var surfaces = new[]
        {
            Surface(0, "Front", "N-BK7", thickness: 2, semiDiameter: 3, radius: 50),
            Surface(1, "Back", "Air", thickness: 0, semiDiameter: 5, radius: -50)
        };

        var report = OpticalManufacturingModel.Evaluate(surfaces, new ManufacturabilitySettings());
        var element = Assert.Single(report.Elements);

        Assert.Equal(10, element.MechanicalDiameter, precision: 12);
        Assert.Equal(10, element.Diameter, precision: 12);
        var diameter = Assert.Single(report.GeometryMetrics, metric => metric.Item == "机械直径");
        Assert.Equal("10 mm", diameter.Value);
    }

    [Fact]
    public void StandardGeometrySurfacesDisplayAsStandardFaces()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaces = application.Prescription.GetSurfaces().ToArray();

        Assert.DoesNotContain(surfaces, surface =>
            surface.GeometryKind.Equals("其他：standard", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surfaces, surface =>
            surface.GeometryKind == "标准球面/圆锥");
        Assert.All(
            surfaces.Where(surface => surface.GeometryKind is "平面" or "标准球面/圆锥"),
            surface =>
            {
                var row = new SurfaceEditorRow(
                    surface,
                    isLastSurface: surface.Number == surfaces[^1].Number);
                Assert.Equal("标准面", row.SurfaceType);
            });

        var report = OpticalManufacturingModel.Evaluate(
            surfaces,
            new ManufacturabilitySettings());
        Assert.DoesNotContain(report.Findings, finding =>
            finding.Check.Contains("面型", StringComparison.Ordinal)
            && finding.Recommendation.Contains("特殊面型", StringComparison.Ordinal));
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
    public void OpticalDrawingUsesHighMagnificationForTinyCementedElements()
    {
        var first = new OpticalElementDefinition(
            3,
            Surface(6, "S6", "K10", thickness: 0.09, semiDiameter: 0.422, radius: -7.521),
            Surface(7, "S7", "N-SK15", thickness: 0, semiDiameter: 0.422, radius: 1.301));
        var second = new OpticalElementDefinition(
            4,
            Surface(7, "S7", "N-SK15", thickness: 0.339, semiDiameter: 0.422, radius: 1.301),
            Surface(8, "S8", "Air", thickness: 0, semiDiameter: 0.422, radius: -1.522));
        var element = new OpticalDrawingElementDefinition(new[] { first, second });
        var sheet = Sheet(element);

        var designation = OpticalDrawingRenderer.ScaleDesignation(sheet);
        var preview = OpticalDrawingRenderer.RenderPreview(sheet, 800);

        Assert.Equal("100:1", designation);
        Assert.True(preview.Length > 10_000);
    }

    [Fact]
    public void OpticalDrawingElementProfileMatchesTwoDimensionalLayoutBoundary()
    {
        var optic = Optic.CreateDemo();
        optic.SurfaceGroup.Items[3].SemiDiameter = 8;
        optic.SurfaceGroup.Renumber();
        var surfaceSamples = OpticalDrawingRendererCore.ManufacturingSurfaceSamples;
        var scene = new Layout2DBuilder(optic).Build(surfaceSamples);
        var expected = scene.LensElements.First(lens =>
            lens.FrontSurfaceNumber == 2 && lens.BackSurfaceNumber == 3);
        var surfaces = optic.SurfaceGroup.Items.Select(WorkbenchMapper.ToSurfaceDto).ToArray();
        var element = OpticalManufacturingModel.BuildElements(surfaces).Single(item =>
            item.FrontSurface.Number == 2 && item.BackSurface.Number == 3);

        var profile = OpticalDrawingRendererCore.BuildManufacturingComponentProfile(
            element,
            optic.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z,
            surfaceSamples);
        var pairs = profile.Boundary
            .Zip(profile.Boundary.Skip(1), (A, B) => (A, B))
            .ToList();
        pairs.Add((profile.Boundary[^1], profile.Boundary[0]));

        Assert.Equal(expected.Boundary.Count, profile.Boundary.Count);
        Assert.All(
            expected.Boundary.Zip(profile.Boundary),
            pair =>
            {
                Assert.Equal(pair.First.Z, pair.Second.Z, precision: 12);
                Assert.Equal(pair.First.Y, pair.Second.Y, precision: 12);
            });
        Assert.Equal(13, profile.Boundary.Max(point => Math.Abs(point.Y)), precision: 12);
        Assert.Contains(pairs, pair =>
            Close(pair.A.Y, 13)
            && Close(pair.B.Y, 13)
            && Math.Abs(pair.A.Z - pair.B.Z) > 1e-6);
        Assert.Contains(pairs, pair =>
            Close(pair.A.Y, 13)
            && Close(pair.B.Y, 8)
            && Close(pair.A.Z, pair.B.Z));
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
    public void OpticalDrawingSupportsCurrentAndLegacyChineseNationalStandardLayouts()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var element = OpticalManufacturingModel.BuildElements(
            application.Prescription.GetSurfaces())[0];
        var isoSheet = Sheet(element);
        var gb1991Sheet = isoSheet with { Standard = OpticalDrawingStandard.GbT13323_1991 };
        var gb2009Sheet = isoSheet with { Standard = OpticalDrawingStandard.GbT13323_2009 };

        var isoPreview = OpticalDrawingRenderer.RenderPreview(isoSheet, 800);
        var gb1991Preview = OpticalDrawingRenderer.RenderPreview(gb1991Sheet, 800);
        var gb2009Preview = OpticalDrawingRenderer.RenderPreview(gb2009Sheet, 800);

        Assert.Equal("ISO 10110-1:2019 表格式", OpticalDrawingRenderer.StandardDesignation(isoSheet.Standard));
        Assert.Equal("GB/T 13323—1991 光学制图", OpticalDrawingRenderer.StandardDesignation(gb1991Sheet.Standard));
        Assert.Equal("GB/T 13323—2009 光学制图", OpticalDrawingRenderer.StandardDesignation(gb2009Sheet.Standard));
        Assert.False(isoPreview.SequenceEqual(gb1991Preview));
        Assert.False(isoPreview.SequenceEqual(gb2009Preview));
        Assert.False(gb1991Preview.SequenceEqual(gb2009Preview));
        Assert.True(gb1991Preview.Length > 10_000);
        Assert.True(gb2009Preview.Length > 10_000);
    }

    [Fact]
    public void OpticalGlassMarksSeparateCurrentAndLegacyGbPatterns()
    {
        var iso = OpticalDrawingRenderer.OpticalGlassHatchHalfLengths(
            OpticalDrawingStandard.Iso10110);
        var gb1991 = OpticalDrawingRenderer.OpticalGlassHatchHalfLengths(
            OpticalDrawingStandard.GbT13323_1991);
        var gb2009 = OpticalDrawingRenderer.OpticalGlassHatchHalfLengths(
            OpticalDrawingStandard.GbT13323_2009);

        Assert.Equal(iso[0], iso[2]);
        Assert.True(iso[1] > iso[0]);
        Assert.Equal(iso, gb2009);
        Assert.Equal(gb1991[0], gb1991[1]);
        Assert.Equal(gb1991[1], gb1991[2]);
        Assert.Equal("R50 ±0.1", OpticalDrawingRenderer.RadiusDimensionText(50, 0.1));
        Assert.Equal("R∞", OpticalDrawingRenderer.RadiusDimensionText(0, 0.1));
    }

    [Fact]
    public void TessarKeepsSingleLensDrawingsAndAddsCementedAssemblyDrawing()
    {
        using var application = WorkbenchApplication.Create("tessar");

        var drawings = OpticalManufacturingModel.BuildDrawingElements(
            application.Prescription.GetSurfaces());

        Assert.Equal(4, drawings.Count(drawing => !drawing.IsCemented));
        var cemented = Assert.Single(drawings, drawing => drawing.IsCemented);
        Assert.Equal(2, cemented.Components.Count);
        Assert.Equal(
            cemented.Components[0].BackSurface.Number,
            cemented.Components[1].FrontSurface.Number);
        Assert.True(
            cemented.Components[0].FrontSurface.Number
                < cemented.Components[1].FrontSurface.Number);
        Assert.Equal("L1", OpticalDrawingRenderer.CementedComponentLabel(0));
        Assert.Equal("L2", OpticalDrawingRenderer.CementedComponentLabel(1));

        var singlePreview = OpticalDrawingRenderer.RenderPreview(
            Sheet(drawings.First(drawing => !drawing.IsCemented)),
            800);
        var cementedPreview = OpticalDrawingRenderer.RenderPreview(Sheet(cemented), 800);

        Assert.True(singlePreview.Length > 10_000);
        Assert.True(cementedPreview.Length > 10_000);
        Assert.False(singlePreview.SequenceEqual(cementedPreview));
    }

    [Fact]
    public async Task OpticalSystemDrawingRendersLensBodiesAirGapsAndTitleBlock()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var scene = await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(
            SceneDimension.TwoDimensional,
            IncludeAllWavelengths: true,
            RayCount: 3));
        var layout = Assert.IsType<Scene2Dto>(scene.TwoDimensional);
        Assert.NotEmpty(layout.LensElements);
        Assert.NotEmpty(layout.Rays);
        var lensOnlyLayout = layout with
        {
            Surfaces = Array.Empty<SceneSurface2Dto>(),
            LensEdges = Array.Empty<SceneLensEdge2Dto>(),
            Rays = Array.Empty<SceneRay2Dto>(),
            ZMin = -1_000_000,
            ZMax = 1_000_000,
            YExtent = 1_000_000
        };
        var airGaps = OpticalDrawingRenderer.SystemAirGaps(layout);
        Assert.Equal(2, airGaps.Count);
        Assert.All(airGaps, gap => Assert.True(gap > 0));
        Assert.Equal(0.2054, airGaps[0], 4);
        Assert.Equal(0.2243, airGaps[1], 4);

        var sheet = new OpticalSystemDrawingSheet(
            layout,
            OpticalDrawingPageSize.A4,
            "OPT-SYSTEM-TEST",
            "Optical system layout",
            "DES",
            "CHK",
            "A");
        var lensOnlySheet = new OpticalSystemDrawingSheet(
            lensOnlyLayout,
            OpticalDrawingPageSize.A4,
            "OPT-SYSTEM-TEST",
            "Optical system layout",
            "DES",
            "CHK",
            "A");
        var alternateTitleSheet = lensOnlySheet with { DrawingNumber = "OPT-SYSTEM-OTHER" };
        var lensOnlyPreview = OpticalDrawingRenderer.RenderSystemPreview(lensOnlySheet, 800);
        var preview = OpticalDrawingRenderer.RenderSystemPreview(sheet, 800);
        var alternateTitlePreview = OpticalDrawingRenderer.RenderSystemPreview(alternateTitleSheet, 800);

        Assert.True(preview.Length > 10_000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, preview[..4]);
        Assert.True(preview.SequenceEqual(lensOnlyPreview));
        Assert.False(preview.SequenceEqual(alternateTitlePreview));
    }

    private static readonly byte[] TransparentPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static OpticalDrawingSheet Sheet(OpticalDrawingElementDefinition element) => new(
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

    private static SurfaceRowDto Surface(
        int number,
        string label,
        string material,
        double thickness,
        double semiDiameter,
        double radius) => new(
            Number: number,
            Label: label,
            Radius: radius,
            Thickness: thickness,
            Material: material,
            Coating: "None",
            SemiDiameter: semiDiameter,
            Conic: 0,
            IsStop: false,
            GeometryKind: Math.Abs(radius) < 1e-12 ? "平面" : "标准球面/圆锥",
            CoatingKind: "无镀膜",
            InteractionKind: "折射",
            ApertureKind: "无",
            GratingOrder: 1,
            GratingPeriodMicrometers: 1,
            GrooveOrientationAngleDegrees: 0,
            ThinLensFocalLength: 50,
            RadiusVariable: false,
            ThicknessVariable: false,
            SemiDiameterFixed: true);

    private static bool Close(double first, double second) =>
        Math.Abs(first - second) <= 1e-9;
}
