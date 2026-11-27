using netDxf;
using netDxf.Entities;
using netDxf.Units;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using static OpticalSystemDesign.Tests.ManufacturingDrawingTests;

namespace OpticalSystemDesign.Tests;

public sealed class OpticalDrawingDxfTests
{
    [Theory]
    [InlineData(OpticalDrawingStandard.Iso10110, OpticalDrawingPageSize.A4)]
    [InlineData(OpticalDrawingStandard.Iso10110, OpticalDrawingPageSize.A3)]
    [InlineData(OpticalDrawingStandard.GbT13323_1991, OpticalDrawingPageSize.A4)]
    public void PartMeasures25MillimetersInCad(OpticalDrawingStandard standard, OpticalDrawingPageSize page)
    {
        var front = Surface(1, "S1", "N-BK7", 6, 10, 60) with { MechanicalSemiDiameter = 12.5 };
        var back = Surface(2, "S2", "Air", 0, 10, -45) with { MechanicalSemiDiameter = 12.5 };
        var sheet = Sheet(new OpticalElementDefinition(1, front, back)) with { PageSize = page, Standard = standard };
        var doc = ExportAndRead($"{standard}-{page}", path => OpticalDrawingDxf.Export(path, sheet));
        Assert.Equal(DrawingUnits.Millimeters, doc.DrawingVariables.InsUnits);
        var contour = Assert.Single(doc.Entities.Polylines2D);
        Assert.True(contour.IsClosed);
        Assert.Equal("OUTLINE", contour.Layer.Name);
        Assert.Equal(25, contour.Vertexes.Max(v => v.Position.Y) - contour.Vertexes.Min(v => v.Position.Y), 10);
        Assert.Equal(4, contour.Vertexes.Count(v => v.Bulge != 0));
        Assert.Equal("AXIS", Assert.Single(doc.Entities.Lines).Layer.Name);
        var diameter = Assert.Single(doc.Entities.Dimensions, d => d.Style.Name == "OuterDiameter");
        Assert.Equal(25, diameter.Measurement, 10);
        Assert.Equal(0.02, diameter.Style.Tolerances.UpperLimit, 10);
        Assert.Equal(0.01, diameter.Style.Tolerances.LowerLimit, 10);
        Assert.Equal("Ø25  +0.02/-0.01", Assert.Single(diameter.Block.Entities.OfType<MText>()).Value);
        Assert.Collection(doc.Entities.Dimensions.OfType<RadialDimension>(),
            d => Assert.Equal(60, d.Measurement, 9), d => Assert.Equal(45, d.Measurement, 9));
        Assert.Equal(6, Assert.Single(doc.Entities.Dimensions, d => d.Style.Name == "CenterThickness").Measurement, 10);
        Assert.All(doc.Entities.Dimensions, d =>
        {
            Assert.Equal("DIMENSIONS", d.Layer.Name);
            Assert.NotEmpty(d.Block.Entities);
        });
        Assert.Contains(doc.Entities.Texts, t => t.Layer.Name == "NOTES" && t.Value == sheet.PartName);
    }

    [Fact]
    public void SmallAsphereUsesPrescriptionSagAtItsActualAperture()
    {
        var front = Surface(1, "S1", "N-BK7", .012, .025, .12) with
        {
            Conic = -1,
            GeometryDefinition = new SurfaceGeometryDefinitionDto("even_asphere",
                new Dictionary<string, double> { ["radius"] = .12, ["conic"] = -1, ["c0"] = .17 })
        };
        var back = Surface(2, "S2", "Air", 0, .03, 0);
        var sheet = Sheet(new OpticalElementDefinition(1, front, back));
        var doc = ExportAndRead("micro-asphere", path => OpticalDrawingDxf.Export(path, sheet));
        var contour = Assert.Single(doc.Entities.Polylines2D);
        Assert.All(contour.Vertexes, v => Assert.Equal(0, v.Bulge));
        var expected = OpticalManufacturingModel.Sag(front, .025)!.Value;
        Assert.Contains(contour.Vertexes, v => Vector2.Distance(v.Position, new Vector2(expected, .025)) < 1e-12);
        Assert.Contains(contour.Vertexes, v => Vector2.Distance(v.Position, new Vector2(expected, .03)) < 1e-12);
        Assert.Equal(.06, Assert.Single(doc.Entities.Dimensions, d => d.Style.Name == "OuterDiameter").Measurement, 12);
        Assert.Empty(doc.Entities.Dimensions.OfType<RadialDimension>());
    }

    [Fact]
    public void CementedAssemblyKeepsEveryComponentAndSharedVertex()
    {
        using var app = WorkbenchApplication.Create("tessar");
        var element = OpticalManufacturingModel.BuildDrawingElements(app.Prescription.GetSurfaces()).First(e => e.IsCemented);
        var doc = ExportAndRead("cemented", path => OpticalDrawingDxf.Export(path, Sheet(element)));
        var bodies = doc.Entities.Polylines2D.ToArray();
        Assert.Equal(element.Components.Count, bodies.Length);
        var joint = new Vector2(element.Components[0].CenterThickness, 0);
        Assert.All(bodies.Take(2), p => Assert.Contains(p.Vertexes, v => Vector2.Distance(v.Position, joint) < 1e-9));
        var thicknesses = doc.Entities.Dimensions.Where(d => d.Style.Name == "ComponentThickness").ToArray();
        Assert.Equal(element.Components.Count, thicknesses.Length);
        for (var i = 0; i < thicknesses.Length; i++)
            Assert.Equal(element.Components[i].CenterThickness, thicknesses[i].Measurement, 9);
        Assert.Contains(doc.Entities.Texts, t => t.Value.StartsWith("S3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SystemLayoutKeepsGlobalCoordinatesAndAirGaps()
    {
        using var app = WorkbenchApplication.Create("tessar");
        var scene = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(
            SceneDimension.TwoDimensional, IncludeAllWavelengths: false, RayCount: 1));
        var layout = Assert.IsType<Scene2Dto>(scene.TwoDimensional);
        var sheet = new OpticalSystemDrawingSheet(layout, OpticalDrawingPageSize.A3, "SYS", "Tessar", "DES", "CHK", "A");
        var doc = ExportAndRead("system", path => OpticalDrawingDxf.Export(path, sheet));
        var lenses = OpticalDrawingRendererCore.BuildSystemLensGeometry(layout);
        var contours = doc.Entities.Polylines2D.ToArray();
        Assert.Equal(lenses.Count, contours.Length);
        for (var i = 0; i < lenses.Count; i++)
        {
            Assert.True(contours[i].IsClosed);
            Assert.Equal(lenses[i].Boundary.Count, contours[i].Vertexes.Count);
            Assert.Equal(lenses[i].Boundary[0].Z, contours[i].Vertexes[0].Position.X, 9);
            Assert.Equal(lenses[i].Boundary[0].Y, contours[i].Vertexes[0].Position.Y, 9);
        }
        Assert.Collection(doc.Entities.Dimensions, d => Assert.Equal(0.2054, d.Measurement, 4),
            d => Assert.Equal(0.2243, d.Measurement, 4));
    }

    [Fact]
    public void InvalidProfilePreservesExistingFile()
    {
        var sheet = Sheet(new OpticalElementDefinition(1, Surface(1, "S1", "BK7", 2, 10, 2),
            Surface(2, "S2", "Air", 0, 10, -2)));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            Assert.Throws<InvalidDataException>(() => OpticalDrawingDxf.Export(path, sheet));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static DxfDocument ExportAndRead(string name, Action<string> write)
    {
        var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_DXF_CAPTURE_DIR");
        var path = capture is null ? Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dxf")
            : Path.Combine(Directory.CreateDirectory(capture).FullName, name + ".dxf");
        try
        {
            write(path);
            return Assert.IsType<DxfDocument>(DxfDocument.Load(path));
        }
        finally { if (capture is null) File.Delete(path); }
    }
}
