using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceSolveTests
{
    [Fact]
    public void NewSurfacesUseAutomaticSemiDiameterAndFixedThickness()
    {
        using var app = WorkbenchApplication.Create();
        var inserted = app.Prescription.InsertSurface(0, after: true);
        var row = app.Prescription.GetSurfaces()[inserted];

        Assert.Equal(ThicknessSolveKind.Fixed, row.ThicknessSolve!.Kind);
        Assert.Equal(SemiDiameterSolveKind.Automatic, row.SemiDiameterSolve!.Kind);
        Assert.False(row.SemiDiameterFixed);
    }

    [Fact]
    public async Task ThicknessAndSemiDiameterSolvesFollowSourcesUndoAndRoundTrip()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with
        {
            Thickness = 6,
            SemiDiameter = 12,
            SemiDiameterFixed = true
        });
        app.Prescription.SetThicknessSolve(2, new(ThicknessSolveKind.Pickup, 1, 2, 0.5));
        app.Prescription.SetSemiDiameterSolve(2, new(SemiDiameterSolveKind.Pickup, 1, 0.5));
        var solved = app.Prescription.GetSurfaces()[2];
        Assert.Equal(12.5, solved.Thickness, 12);
        Assert.Equal(6, solved.SemiDiameter, 12);
        Assert.Equal("Air", solved.Material);

        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with
        {
            Thickness = 7,
            SemiDiameter = 14,
            SemiDiameterFixed = true
        });
        solved = app.Prescription.GetSurfaces()[2];
        Assert.Equal(14.5, solved.Thickness, 12);
        Assert.Equal(7, solved.SemiDiameter, 12);
        Assert.True(app.Documents.Undo());
        Assert.Equal(12.5, app.Prescription.GetSurfaces()[2].Thickness, 12);

        var path = Path.Combine(Path.GetTempPath(), $"surface-solves-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var restored = WorkbenchApplication.Create();
            await restored.Documents.OpenAsync(path);
            var row = restored.Prescription.GetSurfaces()[2];
            Assert.Equal(new ThicknessSolveDto(ThicknessSolveKind.Pickup, 1, 2, 0.5), row.ThicknessSolve);
            Assert.Equal(new SemiDiameterSolveDto(SemiDiameterSolveKind.Pickup, 1, 0.5), row.SemiDiameterSolve);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SolveTransitionsAndStructuralEditsPreserveTheIntendedTargets()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.SetThicknessSolve(2, new(ThicknessSolveKind.Variable));
        Assert.True(app.Prescription.GetSurfaces()[2].ThicknessVariable);
        app.Prescription.SetThicknessSolve(2, new(ThicknessSolveKind.Fixed));
        Assert.False(app.Prescription.GetSurfaces()[2].ThicknessVariable);

        app.Prescription.SetThicknessSolve(2, new(ThicknessSolveKind.Pickup, 1, 1));
        app.Prescription.SetSemiDiameterSolve(2, new(SemiDiameterSolveKind.Pickup, 1, 1));
        app.Prescription.InsertSurface(1, after: false);
        Assert.Equal(2, app.Prescription.GetSurfaces()[3].ThicknessSolve!.SourceSurface);
        Assert.Equal(2, app.Prescription.GetSurfaces()[3].SemiDiameterSolve!.SourceSurface);
        app.Prescription.RemoveSurface(2);
        Assert.Equal(ThicknessSolveKind.Fixed, app.Prescription.GetSurfaces()[2].ThicknessSolve!.Kind);
        Assert.Equal(SemiDiameterSolveKind.Fixed, app.Prescription.GetSurfaces()[2].SemiDiameterSolve!.Kind);
    }

    [Fact]
    public void ZemaxSemiDiameterPickupImportsAndExportsAsPickup()
    {
        const string zmx = """
            MODE SEQ
            UNIT MM
            ENPD 10
            SURF 0
              TYPE STANDARD
              CURV 0
              DISZ INFINITY
              DIAM 5 0 0 0 1 ""
            SURF 1
              TYPE STANDARD
              CURV 0.02
              DISZ 5
              DIAM 8 1 0 0 1 ""
            SURF 2
              TYPE STANDARD
              CURV -0.02
              DISZ 20
              DIAM 4 2 1 0 0.5 ""
            SURF 3
              TYPE STANDARD
              CURV 0
              DISZ 0
              DIAM 5 0 0 0 1 ""
            """;

        var optic = OpticalFormatCatalog.Import(zmx, ".zmx");
        var solve = new WorkbenchRuntime(optic).GetSemiDiameterSolve(2);
        Assert.Equal(new SemiDiameterSolveDto(SemiDiameterSolveKind.Pickup, 1, 0.5), solve);
        Assert.Equal(4, optic.SurfaceGroup.Items[2].SemiDiameter, 12);

        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("DIAM 4 2 1 0 0.5", exported, StringComparison.Ordinal);
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");
        Assert.Single(restored.Pickups.SemiDiameterPickups);
    }

    [Fact]
    public void UserDefinedSemiDiameterCreatesAndRemovesOnlyItsOwnFloatingAperture()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        runtime.SetSemiDiameterSolve(1, new(SemiDiameterSolveKind.Fixed));
        var surface = optic.SurfaceGroup.Items[1];
        Assert.True(surface.SemiDiameterDefinesPhysicalAperture);
        Assert.Equal(surface.SemiDiameter, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius, 12);

        surface.SemiDiameter = 8;
        runtime.CommitSurfaceEdit(surface, nameof(surface.SemiDiameter));
        Assert.Equal(8, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius, 12);
        runtime.SetSemiDiameterSolve(1, new(SemiDiameterSolveKind.Automatic));
        Assert.False(surface.SemiDiameterDefinesPhysicalAperture);
        Assert.Null(surface.PhysicalAperture);

        runtime.ApplySurfaceComponents(surface, "标准球面/圆锥", "环形");
        Assert.IsType<AnnularAperture>(surface.PhysicalAperture);
        runtime.SetSemiDiameterSolve(1, new(SemiDiameterSolveKind.Automatic));
        Assert.IsType<AnnularAperture>(surface.PhysicalAperture);
    }
}
