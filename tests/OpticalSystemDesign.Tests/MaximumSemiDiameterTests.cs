using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class MaximumSemiDiameterTests
{
    private const string Source = """
        MODE SEQ
        ENPD 2
        FTYP 0 0 2 1 0 0 0
        XFLN 0 0
        YFLN 0 10
        WAVM 1 .55 1
        PWAV 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          DIAM 1 0 0 0 1
          DISZ 10
        SURF 2
          DIAM .1 3 0 0 1
          FLAP 0 .1 0
          DISZ 5
        SURF 3
          DISZ 0
        MNUM 2 1
        THIC 1 1 10
        THIC 1 2 20
        """;

    [Fact]
    public void ImportResolvesTheLargestConfigurationAndPreservesExplicitApertures()
    {
        var result = new ZemaxZmxImporter().ImportConfigurationSet(Source);
        var expected = 1 + 20 * Math.Tan(10 * Math.PI / 180);
        foreach (var optic in result.Configurations)
        {
            var surface = optic.SurfaceGroup.Items[2];
            Assert.True(surface.SemiDiameterMaximum);
            Assert.Equal(expected, surface.SemiDiameter, 10);
            Assert.Equal(expected, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius, 10);
            var restored = Optic.FromSnapshot(optic.ToSnapshot());
            Assert.True(restored.SurfaceGroup.Items[2].SemiDiameterMaximum);
            Assert.Contains("  DIAM ", OpticalFormatCatalog.Export(restored, ".zmx"));
            Assert.Contains(" 3 0 0 1", OpticalFormatCatalog.Export(restored, ".zmx"));
        }
        var limited = new ZemaxZmxImporter().ImportConfigurationSet(Source.Replace("FLAP 0 .1 0", "CLAP 0 .5"));
        foreach (var optic in limited.Configurations)
        {
            Assert.Equal(.5, Assert.IsType<CircularAperture>(optic.SurfaceGroup.Items[2].PhysicalAperture).Radius);
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, 0, .55).Rays[0];
            Assert.False(optic.SequentialRayTracer.DiagnoseApertures(ray).InsideAllApertures);
        }
    }

    [Fact]
    public void MaximumRecomputesFromChangedConfigurationWithoutChangingFixedRadii()
    {
        var result = new ZemaxZmxImporter().ImportConfigurationSet(Source);
        result.Configurations[1].SurfaceGroup.Items[1].Thickness = 30;
        result.Configurations[1].SurfaceGroup.Renumber();
        AutomaticSemiDiameterSolver.UpdateMaximum(result.Configurations);
        foreach (var optic in result.Configurations)
            Assert.Equal(1 + 30 * Math.Tan(10 * Math.PI / 180), optic.SurfaceGroup.Items[2].SemiDiameter, 10);
        var fixedSurface = result.Configurations[0].SurfaceGroup.Items[2];
        fixedSurface.SemiDiameterMaximum = false;
        fixedSurface.SemiDiameterFixed = true;
        fixedSurface.SemiDiameter = .5;
        AutomaticSemiDiameterSolver.UpdateMaximum(result.Configurations);
        Assert.False(fixedSurface.SemiDiameterMaximum);
        Assert.Equal(.5, fixedSurface.SemiDiameter);
    }

    [Fact]
    public async Task ProjectAndApplicationRetainMaximumAndUndoItsReplacement()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(Source);
        var path = Path.Combine(Path.GetTempPath(), $"maximum-aperture-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new(imported.Configurations, 0), path);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(path);
            Assert.Equal(SemiDiameterSolveKind.Maximum, app.Prescription.GetSurfaces()[2].SemiDiameterSolve!.Kind);
            var row = app.Prescription.GetSurfaces()[2];
            app.Prescription.UpdateSurface(row with { Thickness = row.Thickness + 1 });
            Assert.Equal(SemiDiameterSolveKind.Maximum, app.Prescription.GetSurfaces()[2].SemiDiameterSolve!.Kind);
            app.Prescription.SetSemiDiameterSolve(2, new(SemiDiameterSolveKind.Fixed));
            Assert.Equal(SemiDiameterSolveKind.Fixed, app.Prescription.GetSurfaces()[2].SemiDiameterSolve!.Kind);
            Assert.True(app.Documents.Undo());
            Assert.Equal(SemiDiameterSolveKind.Maximum, app.Prescription.GetSurfaces()[2].SemiDiameterSolve!.Kind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OrdinarySnapshotsDoNotAcquireAMaximumField()
    {
        var snapshot = Optic.CreateBlank().ToSnapshot();
        Assert.DoesNotContain("SemiDiameterMaximum", JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public void ExplicitConfigurationDiameterAndPickupReplaceMaximum()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(Source);
        var system = new MultiConfiguration(imported.Configurations, operandRows: [new(MultiConfigurationOperandKind.SemiDiameter, 2)]);
        system.SetOperandVariable(1, 1, true);
        Assert.False(system.Configurations[1].SurfaceGroup.Items[2].SemiDiameterMaximum);
        Assert.True(system.Configurations[0].SurfaceGroup.Items[2].SemiDiameterMaximum);
        system.SetOperandVariable(1, 1, false);
        Assert.Throws<InvalidOperationException>(() => system.SetOperandPickup(1, 1, 1, 0, .5, 0));
        system.Configurations[0].SurfaceGroup.Items[2].SemiDiameterMaximum = false;
        system.SetOperandPickup(1, 1, 1, 0, .5, 0);
        Assert.False(system.Configurations[1].SurfaceGroup.Items[2].SemiDiameterMaximum);
    }

    [Fact]
    public async Task ImportedBiocularBoundaryRemainsAcceptedWithMaximumAcrossBothConfigurations()
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "biocular", "Bi-ocular lens.ZMX"));
        foreach (var optic in imported.Configurations)
        {
            Assert.True(optic.SurfaceGroup.Items[2].SemiDiameterMaximum);
            foreach (var model in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
                foreach (var wavelength in model.Wavelengths)
                {
                    var ray = model.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, -1, 0, wavelength.Micrometers, true).Rays[0];
                    Assert.True(model.SequentialRayTracer.DiagnoseApertures(ray).InsideAllApertures);
                    if (ReferenceEquals(optic, imported.Configurations[1]))
                    {
                        var outside = ray with { Origin = ray.Origin + new Vector3D(-1e-6, 0, 0) };
                        var diagnostic = model.SequentialRayTracer.DiagnoseApertures(outside);
                        Assert.False(diagnostic.InsideAllApertures);
                        Assert.Equal(2, diagnostic.FirstBlockedSurfaceIndex);
                    }
                    Assert.True(Assert.IsType<CircularAperture>(model.SurfaceGroup.Items[2].PhysicalAperture).Contains(
                        new Vector3D(0, 0, 0)));
                }
        }
    }
}
