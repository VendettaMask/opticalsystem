using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class TelecentricityTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(3, 4)]
    [InlineData(-3, -4)]
    public void FreePropagationMatchesIndependentFiniteConjugateGeometry(double x, double y)
    {
        var optic = FreeSpace(x, y);
        var before = JsonSerializer.Serialize(optic.ToSnapshot());
        var data = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        var expected = Math.Atan2(Math.Sqrt(x * x + y * y), 100) * 180 / Math.PI;
        Assert.Equal(expected, (double)data.Values["MaximumObjectTelecentricityDegrees"], 7);
        Assert.Equal(expected, (double)data.Values["MaximumImageTelecentricityDegrees"], 7);
        Assert.Equal(-Math.Atan2(x, 100) * 180 / Math.PI, double.Parse(data.Table!.Rows[0][5]), 7);
        Assert.Equal(-Math.Atan2(y, 100) * 180 / Math.PI, double.Parse(data.Table.Rows[0][6]), 7);
        Assert.Equal(before, JsonSerializer.Serialize(optic.ToSnapshot()));
    }

    [Fact]
    public void StopAtFrontFocalPlaneIsImageTelecentricWithoutAssumingAFlag()
    {
        var optic = FreeSpace(0, 5);
        optic.FieldDefinition = FieldDefinitionKind.Angle;
        optic.SurfaceGroup.Replace([Surface(double.PositiveInfinity), Surface(50, stop: true),
            Surface(50), Surface(0)]);
        optic.SurfaceGroup.Items[2].InteractionModel = new ThinLensInteractionModel(50);
        var data = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(5, (double)data.Values["MaximumObjectTelecentricityDegrees"], 7);
        Assert.InRange((double)data.Values["MaximumImageTelecentricityDegrees"], 0, 1e-7);
    }

    [Fact]
    public void ObjectTelecentricLaunchMatchesTheIndependentBackFocalStopGeometry()
    {
        var optic = FreeSpace(0, 5);
        optic.SurfaceGroup.Replace([Surface(50), Surface(50), Surface(10, stop: true), Surface(0)]);
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        // Infinite entrance pupils use the object's NA/telecentric launch
        // contract, rather than a finite entrance-pupil diameter at infinity.
        optic.ObjectSpaceTelecentric = true;
        optic.Aperture.Kind = ApertureKind.NumericalAperture;
        optic.Aperture.Value = .1;
        var data = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.InRange((double)data.Values["MaximumObjectTelecentricityDegrees"], 0, 1e-7);
        Assert.Equal(Math.Atan(.1) * 180 / Math.PI, (double)data.Values["MaximumImageTelecentricityDegrees"], 7);
    }

    [Fact]
    public void ImageUsesItsLocalAxisAndIncidentRatherThanRefractedDirection()
    {
        var optic = FreeSpace(0, 0);
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = new CoordinateSystem(image.CoordinateSystem.Origin, RotationYDegrees: 7);
        image.MaterialAfter = new ConstantIndexMaterial("Test detector", 1.5);
        var data = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(0, (double)data.Values["MaximumObjectTelecentricityDegrees"], 8);
        Assert.Equal(7, (double)data.Values["MaximumImageTelecentricityDegrees"], 8);
    }

    [Fact]
    public void FieldNumbersAndEveryWavelengthRemainIndependentWithoutSignedCancellation()
    {
        var optic = FreeSpace(0, 5);
        optic.Fields.Add(new FieldPoint { X = 0, Y = -5, Label = "On axis" });
        optic.Wavelengths.Add(new Wavelength { Micrometers = .65, Weight = 0, IsPrimary = false });
        var all = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(4, all.PlotSeries.Count);
        Assert.Equal(4, all.Table!.Rows.Count);
        Assert.Equal(4, all.Values["ValidChiefRayCount"]);
        Assert.All(all.PlotSeries, series =>
        {
            Assert.Equal(AnalysisAxisQuantity.FieldNumber, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.Degree, series.YUnit);
            Assert.Equal(new[] { 1d, 2d }, series.Points.Select(point => point.X));
            Assert.All(series.Points, point => Assert.True(point.Y > 2));
            Assert.DoesNotContain("On axis", string.Join(" ", series.Points.Select(point => point.Label)));
        });
        var selected = new TelecentricityAnalysis(optic, 2).GenerateData();
        Assert.Equal(2, selected.PlotSeries.Count);
        Assert.All(selected.Table!.Rows, row => Assert.Equal("2", row[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelecentricityAnalysis(optic, 3).GenerateData());
    }

    [Fact]
    public void BlockedChiefRaysStayUnavailableAndNeverLookPerfectlyTelecentric()
    {
        var optic = FreeSpace(0, 5);
        optic.SurfaceGroup.Items[^1].PhysicalAperture = new CircularAperture(.1);
        var unavailable = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, unavailable.Outcome);
        Assert.All(unavailable.PlotSeries, series => Assert.True(double.IsNaN(series.Points[0].Y)));
        Assert.Equal("—", unavailable.Table!.Rows[0][7]);
        optic.Fields.Add(new FieldPoint());
        var partial = new TelecentricityAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, partial.Outcome);
        Assert.Equal(1, partial.Values["ValidChiefRayCount"]);
        Assert.Equal(1, partial.Values["InvalidChiefRayCount"]);
        Assert.Equal(0, (double)partial.Values["MaximumImageTelecentricityDegrees"]);
        Assert.All(partial.PlotSeries, series => Assert.True(double.IsNaN(series.Points[0].Y)));
    }

    [Fact]
    public void CatalogRuntimeParametersAndTypedAxisMappingAreConnected()
    {
        var optic = FreeSpace(0, 5);
        Assert.IsType<TelecentricityAnalysis>(optic.Analyses.Create("Telecentricity"));
        Assert.Equal("Telecentricity", WorkbenchAnalysisCatalog.CanonicalKey("远心度"));
        var command = Assert.Single(WorkbenchAnalysisCatalog.RibbonCommands, item => item.Id == "analysis-telecentricity");
        Assert.Contains(WorkbenchAnalysisCatalog.RibbonMenus, menu => menu.CommandIds.Contains(command.Id));
        var runtime = new WorkbenchRuntime(optic);
        Assert.Contains(runtime.GetAnalysisParameters("远心度"), parameter => parameter.Key == "WavelengthNumber");
        var view = runtime.BuildAnalysisView("远心度");
        Assert.Equal(AnalysisOutcome.Success, view.Outcome);
        Assert.Equal("远心度", view.Name);
        Assert.DoesNotContain(view.Rows, row => row.Metric.Contains("TelecentricityDegrees"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TelecentricityAnalysis(optic).GenerateData(cancellation.Token));
    }

    private static Optic FreeSpace(double x, double y)
    {
        var optic = Optic.CreateBlank();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { X = x, Y = y });
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 5;
        optic.SurfaceGroup.Replace([Surface(100), Surface(10, stop: true), Surface(0)]);
        return optic;
    }

    private static OpticalSurface Surface(double thickness, bool stop = false) => new()
    {
        Thickness = thickness,
        IsStop = stop,
        Geometry = new PlaneGeometry(),
        SemiDiameter = 30,
        MaterialBefore = new AirMaterial(),
        MaterialAfter = new AirMaterial(),
        InteractionModel = new RefractiveReflectiveInteractionModel()
    };
}
