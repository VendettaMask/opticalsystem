using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class AxialDistortionReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void PhysicalReferencePerturbationsSurviveAnAxialOnlyFieldTable()
    {
        foreach (var definition in new[] { FieldDefinitionKind.Angle, FieldDefinitionKind.ObjectHeight,
                     FieldDefinitionKind.ParaxialImageHeight, FieldDefinitionKind.RealImageHeight })
        {
            var optic = definition is FieldDefinitionKind.ParaxialImageHeight or FieldDefinitionKind.RealImageHeight
                ? Optic.CreateCookeTriplet() : Plate(definition);
            optic.FieldDefinition = definition;
            optic.Fields.Clear();
            optic.Fields.Add(new FieldPoint());
            output.WriteLine($"Physical reference definition: {definition}");
            optic.ConfigureRayTraceCache(new RayTraceCache(), 17);
            var before = Snapshot(optic);
            var mapping = AnalysisTrace.BuildDistortionReferenceMapping(optic, .55, 1, "f-tan");
            var extended = Optic.FromSnapshot(optic.ToSnapshot());
            extended.Fields.Add(new FieldPoint { Y = .1 });
            var expected = AnalysisTrace.BuildDistortionReferenceMapping(extended, .55, 1, "f-tan");
            Assert.True(double.IsFinite(mapping.M00) && Math.Abs(mapping.M00) > 1e-6);
            Assert.True(double.IsFinite(mapping.M11) && Math.Abs(mapping.M11) > 1e-6);
            Assert.Equal(expected.M00, mapping.M00, 7);
            Assert.Equal(expected.M11, mapping.M11, 7);
            Assert.Equal(before, Snapshot(optic));
            Assert.Equal(0, FieldCoordinates.MaximumRadius(optic.Fields));
        }
    }

    [Fact]
    public void OfficialAxialDoubletCompletesTheOriginalFieldCurvatureRequest()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "single-ray-path",
            "initial-six-primary", "doublet", "source.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        Assert.Equal(0, FieldCoordinates.MaximumRadius(optic.Fields));
        var before = Snapshot(optic);
        var result = new FieldCurvatureAndDistortionAnalysis(optic, numPoints: 101,
            parabasalDelta: 1e-5, distortionType: "F-Tan(Theta)", wavelengthNumber: 2,
            scanDirection: "+y", displayMode: "percent", referenceFieldNumber: 1,
            ignoreVignettingFactors: true).GenerateData();
        Assert.NotEmpty(result.PlotSeries);
        Assert.All(result.PlotSeries, series => Assert.All(series.Points,
            point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y))));
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void AxialReferenceStillRejectsABlockedChiefRay()
    {
        var optic = Plate(FieldDefinitionKind.Angle);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.01, .01, 100, 100);
        Assert.Throws<InvalidOperationException>(() => AnalysisTrace.BuildDistortionReferenceMapping(
            optic, .55, 1, "f-tan", requireUnvignetted: true));
    }

    [Fact]
    public void CachedPhysicalPerturbationsRecalculateAfterMaterialChanges()
    {
        var optic = Plate(FieldDefinitionKind.Angle);
        optic.ConfigureRayTraceCache(new RayTraceCache(), 1);
        var initial = AnalysisTrace.BuildDistortionReferenceMapping(optic, .55, 1, "f-tan");
        Assert.Equal(10 / 1.5, initial.M00, 7);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("SecondGlass", 2);
        var changed = AnalysisTrace.BuildDistortionReferenceMapping(optic, .55, 1, "f-tan");
        Assert.Equal(5, changed.M00, 7);
        Assert.NotEqual(initial.M00, changed.M00);
    }

    [Fact]
    public void CalibratedDistortionUsesItsNonzeroAxialLimitButAbsoluteHeightRemainsZero()
    {
        foreach (var type in new[] { "calibrated-f-tan", "calibrated-f-theta" })
        {
            var optic = Plate(FieldDefinitionKind.Angle);
            optic.Fields.Add(new FieldPoint { Y = 5 });
            var tangent = Math.Tan(5 * Math.PI / 180);
            var actualHeight = 10 * tangent / Math.Sqrt(2.25 + 1.25 * tangent * tangent);
            var linearField = type == "calibrated-f-tan" ? tangent : 5 * Math.PI / 180;
            var fittedScale = actualHeight / linearField;
            var expected = 100 * (10 / 1.5 / fittedScale - 1);
            var percent = new DistortionAnalysis(optic, numPoints: 5,
                distortionType: type, wavelengthNumber: 1).GenerateData();
            Assert.Equal(expected, Assert.Single(percent.PlotSeries).Points[0].X, 7);
            var absolute = new DistortionAnalysis(optic, numPoints: 5,
                distortionType: type, wavelengthNumber: 1, displayMode: "absolute").GenerateData();
            Assert.Equal(0, Assert.Single(absolute.PlotSeries).Points[0].X);
        }
    }

    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(),
        new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });

    private static Optic Plate(FieldDefinitionKind definition)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = definition == FieldDefinitionKind.Angle
                ? double.PositiveInfinity : 100, Geometry = new PlaneGeometry(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(),
                MaterialAfter = new ConstantIndexMaterial("Glass", 1.5), SemiDiameter = 100, IsStop = true },
            new OpticalSurface { Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 100 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.FieldDefinition = definition;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
}
