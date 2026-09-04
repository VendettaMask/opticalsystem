using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class UnresolvedZemaxGlassTests
{
    [Theory]
    [InlineData("")]
    [InlineData("0 0 1.7 30")]
    [InlineData("0 0 1.7 30", true)]
    public void MissingGlassDoesNotAbortImportOrSubstituteDispersion(string parameters, bool solve = false)
    {
        var optic = OpticalFormatCatalog.Import(Source(parameters, solve), ".zmx");
        CheckPrescription(optic);
        var missing = Assert.IsType<UnresolvedMaterial>(optic.SurfaceGroup.Items[1].MaterialAfter);
        Assert.Equal("UNAVAILABLE-GLASS", missing.Name);
        Assert.Equal("MISSING-CATALOG", missing.Catalogs);
        Assert.Throws<InvalidOperationException>(() => missing.RefractiveIndex(266));
        Assert.Throws<InvalidOperationException>(() => missing.ExtinctionCoefficient(266));
        var error = Assert.Throws<OpticCapabilityException>(() =>
            OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Analysis));
        Assert.Contains("UNAVAILABLE-GLASS", error.Message);
        CheckPrescription(Optic.FromSnapshot(optic.ToSnapshot()));

        var layout = new Layout2DBuilder(optic);
        Assert.NotEmpty(layout.Build().Surfaces);
        Assert.NotEmpty(layout.Build().LensElements);
        Assert.Empty(layout.Build().Rays);
        var threeD = layout.Build3D();
        Assert.NotEmpty(threeD.Surfaces);
        Assert.Empty(threeD.Rays);
        Assert.Contains(threeD.LensElements, element => double.IsNaN(element.RefractiveIndex));
    }

    [Fact]
    public async Task ApplicationKeepsWarningThroughSaveAndClearsItAfterMaterialIsMatched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"missing-glass-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.zmx");
            var saved = Path.Combine(directory, "saved.staropt");
            await File.WriteAllTextAsync(source, Source("0 0 1.7 30"));
            using var application = WorkbenchApplication.Create("blank");
            await application.Documents.OpenAsync(source);
            AssertWarning(application);
            Assert.Equal(6, application.Prescription.GetSurfaces().Count);
            await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.TwoDimensional));
            await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional));
            var settings = application.Prescription.GetSystemSettings();
            application.Prescription.UpdateSystemSettings(settings with { FirstApodizationParameter = 2 });
            AssertWarning(application);
            await application.Documents.SaveAsync(saved);
            await application.Documents.OpenAsync(saved);
            AssertWarning(application);
            Assert.Equal("UNAVAILABLE-GLASS", application.Prescription.GetSurfaces()[1].Material);
            var surface = application.Prescription.GetSurfaces()[1];
            application.Prescription.UpdateSurface(surface with { Label = "Editable missing glass" });
            AssertWarning(application);
            application.Prescription.UpdateSurface(surface with { Material = "N-BK7" });
            Assert.DoesNotContain("找不到玻璃", application.Documents.GetSnapshot().Status);
            Assert.True(double.IsFinite(application.Documents.GetSnapshot().EffectiveFocalLength));
            await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.TwoDimensional));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertWarning(WorkbenchApplication application)
    {
        var snapshot = application.Documents.GetSnapshot();
        Assert.Contains("找不到玻璃：UNAVAILABLE-GLASS（表面 1）", snapshot.Status);
        Assert.True(double.IsNaN(snapshot.EffectiveFocalLength));
    }

    private static void CheckPrescription(Optic optic)
    {
        Assert.Equal(6, optic.SurfaceGroup.Items.Count);
        Assert.Equal(50, optic.SurfaceGroup.Items[1].Radius);
        Assert.Equal(4, optic.SurfaceGroup.Items[1].Thickness);
        Assert.Equal(5, optic.SurfaceGroup.Items[1].SemiDiameter);
        Assert.IsType<UnresolvedMaterial>(optic.SurfaceGroup.Items[1].MaterialAfter);
        Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[3].MaterialAfter);
        Assert.Equal(266, optic.Wavelengths.Single().Nanometers);
    }

    private static string Source(string parameters, bool solve = false) => $$"""
        MODE SEQ
        GCAT MISSING-CATALOG
        ENPD 2
        GFAC 1.44 1
        WAVM 1 0.266 1
        PWAV 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          CURV 0.02
          DISZ 4
          {{(solve ? "MAZH 0 0" : "")}}
          GLAS UNAVAILABLE-GLASS {{parameters}}
          DIAM 5 1 0 0 1
        SURF 2
          CURV -0.02
          DISZ 10
          DIAM 5 1 0 0 1
        SURF 3
          CURV 0.01
          DISZ 3
          GLAS N-BK7
          DIAM 5 1 0 0 1
        SURF 4
          CURV -0.01
          DISZ 20
          DIAM 5 1 0 0 1
        SURF 5
          DISZ 0
          DIAM 5 1 0 0 1
        """;
}
