using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class ManufacturingInvalidThicknessTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidGlassRemainsInTheFormalPrescriptionAssessment(double thickness)
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Prescription(thickness)));
        var rows = new PrescriptionService(workspace).GetSurfaces();
        var report = OpticalManufacturingModel.Evaluate(rows, new());

        Assert.Equal(2, report.Elements.Count);
        Assert.Equal(new[] { 1, 3 }, report.Elements.Select(element => element.FrontSurface.Number));
        Assert.Contains(report.Findings, finding => finding.Surfaces == "S3-S4"
            && finding.Check == "中心厚度" && finding.Severity == ManufacturabilitySeverity.Error);
        Assert.DoesNotContain(report.Findings, finding => finding.Surfaces == "S3-S4"
            && finding.Severity == ManufacturabilitySeverity.Pass);
        Assert.Contains(report.Findings, finding => finding.Surfaces == "S1-S2"
            && finding.Severity == ManufacturabilitySeverity.Pass);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidGlassCannotBecomeAnIncompleteDrawingOrClippedProfile(double thickness)
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Prescription(thickness)));
        var rows = new PrescriptionService(workspace).GetSurfaces();
        var error = Assert.Throws<InvalidOperationException>(() => OpticalManufacturingModel.BuildDrawingElements(rows));
        Assert.Contains("S3-S4", error.Message);
        var invalid = new OpticalElementDefinition(2, rows[3], rows[4]);
        Assert.Throws<InvalidOperationException>(() => OpticalDrawingRendererCore.BuildManufacturingComponentProfile(invalid, 0));
    }

    [Fact]
    public void PositiveThicknessAndAirGapsKeepBothValidDrawingElements()
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Prescription(1)));
        var rows = new PrescriptionService(workspace).GetSurfaces();
        var report = OpticalManufacturingModel.Evaluate(rows, new());
        Assert.Equal(2, report.Elements.Count);
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(2, report.PassCount);
        Assert.Equal(2, OpticalManufacturingModel.BuildDrawingElements(rows).Count);
    }

    private static Optic Prescription(double thickness)
    {
        var optic = new Optic("Manufacturing thickness control");
        optic.Fields.Add(new FieldPoint { Weight = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            Surface(2, "N-BK7", isStop: true), Surface(10), Surface(thickness, "N-BK7"), Surface(20),
            new OpticalSurface { Thickness = 0, SemiDiameter = 4, SemiDiameterFixed = true }
        ]);
        return optic;
    }

    private static OpticalSurface Surface(double thickness, string material = "Air", bool isStop = false) => new()
    {
        Radius = 100, Thickness = thickness, Material = material, IsStop = isStop,
        SemiDiameter = 4, SemiDiameterFixed = true
    };
}
