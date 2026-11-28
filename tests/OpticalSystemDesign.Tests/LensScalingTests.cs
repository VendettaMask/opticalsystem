using System.Text.Json;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class LensScalingTests
{
    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    [InlineData(10)]
    public void WholeLensScalesPhysicalDimensionsAndFocalLength(double factor)
    {
        var original = Optic.CreateCookeTriplet();
        var before = original.ToSnapshot();
        var scaled = LensScaler.Scale(original, factor, 0, before.Surfaces[^1].Number);
        Near(original.Paraxial.EstimateEffectiveFocalLength() * factor, scaled.Paraxial.EstimateEffectiveFocalLength());
        Near(original.Paraxial.EstimateFNumber(), scaled.Paraxial.EstimateFNumber());
        Near(original.SurfaceGroup.TotalTrack * factor, scaled.SurfaceGroup.TotalTrack);
        Assert.Equal(before.Wavelengths, scaled.ToSnapshot().Wavelengths);
        Assert.Equal(before.MeritOperands, scaled.ToSnapshot().MeritOperands);
        Assert.Equal(before.Fields, scaled.ToSnapshot().Fields);
        Assert.Equal(Json(original.ToSnapshot()), Json(before));
        for (var i = 0; i < before.Surfaces.Count; i++)
        {
            var surface = scaled.ToSnapshot().Surfaces[i];
            Near(before.Surfaces[i].SemiDiameter * factor, surface.SemiDiameter);
            Assert.Equal(before.Surfaces[i].Conic, surface.Conic);
            Assert.Equal(JsonSerializer.Serialize(before.Surfaces[i].Components!.MaterialAfterComponent), JsonSerializer.Serialize(surface.Components!.MaterialAfterComponent));
        }
    }

    [Theory]
    [InlineData("even")]
    [InlineData("odd")]
    [InlineData("extended")]
    [InlineData("biconic")]
    [InlineData("toroid")]
    [InlineData("forbes")]
    [InlineData("normal-slope")]
    [InlineData("polynomial")]
    [InlineData("chebyshev")]
    [InlineData("chebyshev-sag")]
    [InlineData("zernike")]
    [InlineData("irregular")]
    [InlineData("fringe-sag")]
    [InlineData("grid-sag")]
    public void SurfaceSagObeysDimensionalSimilarity(string kind)
    {
        IGeometry shape = kind switch
        {
            "even" => new EvenAsphereGeometry(60, -.3, [1e-4, -2e-8, 3e-12]),
            "odd" => new OddAsphereGeometry(60, -.3, [.0002, .0001, -.000001]),
            "extended" => new NormalizedPolynomialGeometry(60, -.3, 6, SagPolynomialBasis.EvenRadial, 2, [.02, -.01]),
            "biconic" => new BiconicGeometry(60, -70, -.3, .2),
            "toroid" => new ToroidalGeometry(60, 70, -.3, [1e-4, -2e-8]),
            "forbes" => new ForbesQGeometry(60, -.3, 6, [.02, -.01]),
            "normal-slope" => new ForbesNormalSlopeGeometry(60, -.3, 6, [.02, -.01]),
            "polynomial" => new PolynomialGeometry(new Dictionary<(int, int), double> { [(2, 1)] = .001, [(1, 0)] = .003 }),
            "chebyshev" => new ChebyshevGeometry(new Dictionary<(int, int), double> { [(2, 1)] = .02 }, 6, 5),
            "chebyshev-sag" => new ChebyshevSagGeometry(60, 1, 1, 6, 5, [.02, -.01, .001]),
            "zernike" => new ZernikeGeometry(new Dictionary<(int, int), double> { [(2, 0)] = .02 }, 6),
            "irregular" => new IrregularGeometry(60, -.3, 6, .2, -.1, 0, 0, .02, -.01, .003, 20),
            "fringe-sag" => new FringeSagGeometry(60, -.3, [1e-4, -2e-8], 6, [.02, -.01, .003, -.002], .2, -.1),
            "grid-sag" => new GridSagGeometry(60, -.3, [1e-4, -2e-8], 5, 5, 2, 2, .2, -.1, false,
                Enumerable.Range(0, 25).Select(i => new GridSagPoint(.002 * i, .001 * (i % 5), -.002 * (i / 5), .0001 * i)).ToArray()),
            _ => throw new ArgumentException(kind)
        };
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Geometry = shape;
        var scaled = LensScaler.Scale(optic, 2.5, 0, optic.SurfaceGroup.Items[^1].Number);
        foreach (var (x, y) in new[] { (.3, .7), (1.2, -.5), (-1.3, 2.1) })
            Near(2.5 * shape.Sag(x, y), scaled.SurfaceGroup.Items[1].Geometry.Sag(2.5 * x, 2.5 * y));
    }

    [Fact]
    public void DimensionalScalingPreservesZeroAndSmallSurfaceSemiDiameters()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[0].SemiDiameter = 0;
        optic.SurfaceGroup.Items[1].SemiDiameter = .08;
        var scaled = LensScaler.Scale(optic, .5, 0, optic.SurfaceGroup.Items[^1].Number);
        Assert.Equal(0, scaled.SurfaceGroup.Items[0].SemiDiameter);
        Near(.04, scaled.SurfaceGroup.Items[1].SemiDiameter);
    }

    [Fact]
    public void InvalidIrregularNormalizationKeepsThePreviousGeometryAndAperture()
    {
        var surface = new OpticalSurface
        {
            SemiDiameter = 6,
            Geometry = new IrregularGeometry(60, 0, 6, 0, 0, 0, 0, .02, 0, 0, 0),
            PhysicalAperture = new CircularAperture(6),
            SemiDiameterDefinesPhysicalAperture = true
        };
        var geometry = surface.Geometry;
        var aperture = surface.PhysicalAperture;
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.SemiDiameter = 0);
        Assert.Equal(6, surface.SemiDiameter);
        Assert.Same(geometry, surface.Geometry);
        Assert.Same(aperture, surface.PhysicalAperture);
    }

    [Fact]
    public void PartialScaleMovesDownstreamSurfacesAndPreservesUnselectedShape()
    {
        var optic = Optic.CreateCookeTriplet();
        var old = optic.ToSnapshot();
        var scaled = LensScaler.Scale(optic, 2, 1, 1).ToSnapshot();
        Assert.Equal(old.Aperture, scaled.Aperture);
        Assert.Equal(old.Fields, scaled.Fields);
        Near(old.Surfaces[1].Thickness * 2, scaled.Surfaces[1].Thickness);
        for (var i = 2; i < scaled.Surfaces.Count; i++)
        {
            Near(old.Surfaces[i].Radius, scaled.Surfaces[i].Radius);
            Near(old.Surfaces[i].CoordinateSystem!.OriginZ + old.Surfaces[1].Thickness, scaled.Surfaces[i].CoordinateSystem!.OriginZ);
        }
    }

    [Fact]
    public void InfiniteObjectAndDecenteredRotatedCoordinatesRemainConsistent()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[0].Thickness = double.PositiveInfinity;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new Vector3D(2, -3, 10), 12, 4, -6);
        var scaled = LensScaler.Scale(optic, 3, 0, optic.SurfaceGroup.Items[^1].Number);
        Assert.True(double.IsPositiveInfinity(scaled.SurfaceGroup.Items[0].Thickness));
        for (var i = 0; i < optic.SurfaceGroup.Items.Count; i++)
        {
            var a = optic.SurfaceGroup.Items[i].CoordinateSystem;
            var b = scaled.SurfaceGroup.Items[i].CoordinateSystem;
            Near(a.Origin.X * 3, b.Origin.X); Near(a.Origin.Y * 3, b.Origin.Y); Near(a.Origin.Z * 3, b.Origin.Z);
            Assert.Equal(a.RotationXDegrees, b.RotationXDegrees); Assert.Equal(a.RotationYDegrees, b.RotationYDegrees);
        }
    }

    [Fact]
    public void AperturesAndPickupsRetainTheirDimensions()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new PolygonAperture([(1, 2), (3, 0), (0, -2)]);
        optic.Pickups.LinkRadius(1, 2, -.8, 2);
        optic.Pickups.SetThicknessPickup(1, 3, .6, 1.2);
        var scaled = LensScaler.Scale(optic, 2, 2, 3);
        var pickup = Assert.Single(scaled.Pickups.RadiusPickups);
        Near(-1.6, pickup.Scale); Near(4, pickup.Offset);
        var thickness = Assert.Single(scaled.Pickups.ThicknessPickups);
        Near(1.2, thickness.Scale); Near(2.4, thickness.Offset);
        var whole = LensScaler.Scale(optic, 2, 0, optic.SurfaceGroup.Items[^1].Number);
        var polygon = Assert.IsType<PolygonAperture>(whole.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.Equal(3, polygon.Vertices.Count); Assert.Equal((2d, 4d), polygon.Vertices[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e-300)]
    [InlineData(double.MaxValue)]
    public void InvalidScalingLeavesOriginalUntouched(double factor)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Json(optic.ToSnapshot());
        Assert.ThrowsAny<ArgumentException>(() => LensScaler.Scale(optic, factor, 0, optic.SurfaceGroup.Items[^1].Number));
        Assert.Equal(before, Json(optic.ToSnapshot()));
    }

    [Fact]
    public async Task ApplicationScaleAndUnitsAreUndoableAndPersistWithoutPhysicalDrift()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var first = app.Prescription.GetSurfaces()[1];
        var settings = app.Prescription.GetLensScaleSettings();
        app.Prescription.ScaleLens(new(2, 0, settings.Surfaces[^1].Number), settings.Revision);
        Near(first.Radius * 2, app.Prescription.GetSurfaces()[1].Radius);
        Assert.True(app.Documents.Undo()); Near(first.Radius, app.Prescription.GetSurfaces()[1].Radius);
        Assert.True(app.Documents.Redo()); Near(first.Radius * 2, app.Prescription.GetSurfaces()[1].Radius);
        var beforeUnits = app.Documents.GetSnapshot();
        settings = app.Prescription.GetLensScaleSettings();
        app.Prescription.ScaleLens(new(1, 0, settings.Surfaces[^1].Number, LensLengthUnit.Centimeter), settings.Revision);
        Near(beforeUnits.EffectiveFocalLength, app.Documents.GetSnapshot().EffectiveFocalLength);
        var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[1]);
        Near(first.Radius * 2, row.Radius);
        Assert.Equal("cm", row.LengthUnitSymbol);
        row.RadiusDisplay = "3"; row.ThicknessDisplay = "0.4"; row.SemiDiameterDisplay = "0.5";
        Near(30, row.ToDto().Radius); Near(4, row.ToDto().Thickness); Near(5, row.ToDto().SemiDiameter);
        var path = Path.Combine(Path.GetTempPath(), $"lens-scale-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path); app.Documents.NewBlank(); await app.Documents.OpenAsync(path);
            Assert.Equal(LensLengthUnit.Centimeter, app.Prescription.GetLensScaleSettings().Unit);
            Near(first.Radius * 2, app.Prescription.GetSurfaces()[1].Radius);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void StaleRevisionAndInvalidRangeDoNotPublishOrCreateHistory()
    {
        using var app = WorkbenchApplication.Create("cooke"); var settings = app.Prescription.GetLensScaleSettings();
        var before = app.Documents.GetSnapshot(); var changes = 0;
        app.Events.Changed += (_, _) => changes++;
        Assert.Throws<InvalidOperationException>(() => app.Prescription.ScaleLens(new(2, 0, 2), settings.Revision - 1));
        Assert.Throws<ArgumentException>(() => app.Prescription.ScaleLens(new(2, 2, 1), settings.Revision));
        Assert.Throws<ArgumentException>(() => app.Prescription.ScaleLens(new(2, 0, 999), settings.Revision));
        app.Prescription.ScaleLens(new(1, 0, 2), settings.Revision);
        Assert.Equal(0, changes); Assert.Equal(before, app.Documents.GetSnapshot());
    }

    [Fact]
    public void HeightFieldsAndActiveTotalTrackSolveScaleWithoutLaterDrift()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight; optic.Fields[^1].Y = 10;
        optic.Solves.KeepImageAtBackFocus = true; optic.Solves.DesiredBackFocus = optic.SurfaceGroup.TotalTrack + 3;
        optic.Solves.ApplyAll();
        var scaled = LensScaler.Scale(optic, 2, 1, 2);
        var imageThickness = scaled.SurfaceGroup.Items[^1].Thickness;
        scaled.Solves.ApplyAll(); Near(imageThickness, scaled.SurfaceGroup.Items[^1].Thickness);
        var whole = LensScaler.Scale(optic, 2, 0, optic.SurfaceGroup.Items[^1].Number);
        Near(20, whole.Fields[^1].Y);
        var wholeImage = whole.SurfaceGroup.Items[^1].Thickness;
        whole.Solves.ApplyAll(); Near(wholeImage, whole.SurfaceGroup.Items[^1].Thickness);
    }

    [Fact]
    public void UnsupportedPhaseSurfaceFailsBeforeOriginalIsChanged()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].InteractionModel = new PhaseInteractionModel(new ConstantPhaseProfile(.3));
        var before = Json(optic.ToSnapshot());
        var error = Assert.Throws<NotSupportedException>(() => LensScaler.Scale(optic, 2, 0, optic.SurfaceGroup.Items[^1].Number));
        Assert.Contains("表面 1", error.Message); Assert.Equal(before, Json(optic.ToSnapshot()));
    }

    [Fact]
    public void EveryConfigurationAndCrossSurfaceCurvaturePickupScaleTogether()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([
            new(MultiConfigurationParameterKind.Curvature, 1), new(MultiConfigurationParameterKind.Curvature, 2)
        ], app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(2, 1, new(1, 0, -.7, .002), app.Events.Revision);
        var before = app.MultiConfiguration.GetOperandRows();
        var settings = app.Prescription.GetLensScaleSettings(); Assert.Equal(2, settings.ConfigurationCount);
        app.Prescription.ScaleLens(new(2, 2, 2), settings.Revision);
        var after = app.MultiConfiguration.GetOperandRows();
        Near(before[0].Values[0], after[0].Values[0]); Near(before[1].Values[1] / 2, after[1].Values[1]);
        Near(-.35, after[1].Pickups[1]!.Scale); Near(.001, after[1].Pickups[1]!.Offset);
        Assert.True(app.Documents.Undo()); Near(before[1].Values[1], app.MultiConfiguration.GetOperandRows()[1].Values[1]);
    }

    [Theory]
    [InlineData(LensLengthUnit.Millimeter, 1)]
    [InlineData(LensLengthUnit.Centimeter, 10)]
    [InlineData(LensLengthUnit.Meter, 1000)]
    [InlineData(LensLengthUnit.Inch, 25.4)]
    public void UnitEntryAndOverflowValidationUseActualPhysicalLengths(LensLengthUnit unit, double millimeters)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var row = new SurfaceEditorRow(app.Prescription.GetSurfaces()[1] with { LensLengthUnit = unit });
        row.RadiusDisplay = "2"; row.ThicknessDisplay = "3";
        Near(millimeters * 2, row.Radius); Near(millimeters * 3, row.Thickness);
        Assert.Equal("2", row.RadiusDisplay); Assert.Equal("3", row.ThicknessDisplay);
        var before = row.Thickness;
        Assert.Throws<FormatException>(() => row.ThicknessDisplay = "1e999"); Near(before, row.Thickness);
    }

    private static string Json(OpticSnapshot snapshot) => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)), $"Expected {expected:G17}; actual {actual:G17}");
}
