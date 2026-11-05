using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxSurfaceFrameTests
{
    [Fact]
    public void CompoundBeforeTransformAndAfterPickupsSetTheSurfaceAndFollowingThicknessFrames()
    {
        var optic = OpticalFormatCatalog.Import("""
            MODE SEQ
            UNIT CM
            ENPD .2
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 3
              SCBD 1 0 0 .1 .2 90 0 90
              SCBD 2 1 2 0 0 0 0 0
            SURF 2
              DISZ 2
              SCBD 2 0 3 0 0 0 0 0
            SURF 3
              DISZ 0
            """, ".zmx");
        var surfaces = optic.SurfaceGroup.Items;
        Assert.Equal(4, surfaces.Count);
        var tilted = surfaces[1].CoordinateSystem;
        Near(new(1, 2, 0), tilted.Origin);
        Near(new(0, 0, 1), tilted.ToGlobalDirection(new(1, 0, 0)));
        Near(new(-1, 0, 0), tilted.ToGlobalDirection(new(0, 1, 0)));
        Near(new(0, -1, 0), tilted.ToGlobalDirection(new(0, 0, 1)));
        Near(new(0, 0, 30), surfaces[2].CoordinateSystem.Origin);
        Near(new(0, 0, 1), surfaces[2].CoordinateSystem.ToGlobalDirection(new(0, 0, 1)));
        Near(new(1, -18, 30), surfaces[3].CoordinateSystem.Origin);
        Near(new(0, -1, 0), surfaces[3].CoordinateSystem.ToGlobalDirection(new(0, 0, 1)));
    }

    [Fact]
    public async Task SpectrometerKeepsNativeMirrorAndGratingCoordinatesAcrossStarOptSave()
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
            "src/OpticalSystemDesign.App/Assets/SequentialExamples/Diffractive components/Sample Spectrometer.ZMX");
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source);
        var path = Path.Combine(Path.GetTempPath(), $"spectrometer-surface-frames-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new(imported.Configurations, 0), path);
            var restored = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations);
            var original = imported.ActiveOptic;
            Assert.Equal(original.SurfaceGroup.Items.Select(s => s.CoordinateSystem),
                restored.SurfaceGroup.Items.Select(s => s.CoordinateSystem));
            var report = new SingleRayTraceAnalysis(restored, wavelengthNumber: 2, py: .8, useRayAiming: false).GenerateData();
            var rows = Assert.IsType<double[][]>(report.Values["RealRayData"]);
            Assert.Equal(5, rows.Length);
            // Native 2026 R1 report 0438, after converting native signed-index directions.
            Assert.Equal(.39098314522, rows[1][2], 10);
            Assert.Equal(.1251981533, rows[1][5], 9);
            Assert.Equal(.35713564222, rows[2][1], 10);
            Assert.Equal(.2620577866, rows[2][4], 9);
            Assert.Equal(.055, rows[2][5], 10);
            Assert.Equal(-.27510026986, rows[4][1], 10);
            Assert.Equal(-.069995540591, rows[4][2], 10);
            Assert.Contains("SCBD", Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(restored, ".zmx")).Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Near(Vector3D expected, Vector3D actual) =>
        Assert.InRange((expected - actual).Length, 0, 1e-11);
}
