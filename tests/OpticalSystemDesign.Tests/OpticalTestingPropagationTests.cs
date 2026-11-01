using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class OpticalTestingPropagationTests
{
    [Theory]
    [InlineData("Double-pass null test", 1)]
    [InlineData("Mach Zender Interferometer", 1)]
    [InlineData("Mach Zender Interferometer", 2)]
    [InlineData("Twyman Green Interferometer", 1)]
    [InlineData("Twyman Green Interferometer", 2)]
    public async Task FoldedConfigurationsAndApertureDiagnosticsReachTheSameImage(string lens, int configuration)
    {
        var imported = await Import(lens);
        var optic = imported.Configurations[configuration - 1];
        foreach (var model in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
            foreach (var aiming in new[] { false, true })
                foreach (var pupil in new[] { (0d, 0d), (.18375321194042835, 0d), (0d, .5), (-.5, 0d) })
                {
                    var source = model.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, pupil.Item1, pupil.Item2, model.Wavelengths[0].Micrometers, aiming).Rays.Single();
                    var history = model.SequentialRayTracer.Trace(new([source])).RayHistories.Single();
                    var final = history.Last();
                    Assert.Equal(model.SurfaceGroup.Items.Count - 1, final.SurfaceNumber);
                    Assert.False(final.Vignetted);
                    var diagnostic = model.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true);
                    Assert.NotNull(diagnostic.UnclippedImage);
                    Assert.True(diagnostic.InsideAllApertures);
                    Assert.InRange((diagnostic.UnclippedImage.Position - final.Position).Length, 0, 1e-9);
                }
    }

    [Fact]
    public async Task NonzeroCoordinateBreakPropagationRetainsNativeFirstOrderPower()
    {
        var imported = await Import("Mach Zender Interferometer");
        foreach (var model in imported.Configurations)
            foreach (var optic in new[] { model, Optic.FromSnapshot(model.ToSnapshot()) })
            {
                Assert.Equal(17, optic.SurfaceGroup.Items.Count);
                Assert.Equal(-1.7380276116030424, optic.Paraxial.EstimateEffectiveFocalLength(), 11);
                Assert.Equal(.3476055223206085, optic.Paraxial.EstimateFNumber(), 11);
                var table = (double[][])new SingleRayTraceAnalysis(optic, useRayAiming: false).GenerateData().Values["RealRayData"];
                Assert.InRange(Math.Abs(table[7][10] - 45), 0, 1e-6);
                Assert.InRange(Math.Abs(table[12][10]), 0, 1e-5);
            }
    }

    [Fact]
    public async Task VirtualExitPupilUsesForwardSphereAndNativeOpdValues()
    {
        var optic = (await Import("Twyman Green Interferometer")).Configurations[0];
        var pupils = new[] { (.5, 0d), (0d, .5), (0d, -.5), (0d, .9), (0d, -.9) };
        // OpticStudio 2026 R1 OPDX controls from the unchanged example.
        var expected = new[] { 29.91645529052299, 33.517649209938206, 26.03355413613178, 48.89322945465765, 30.145521577160054 };
        foreach (var model in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
        {
            var phase = WavefrontEngine.GenerateChiefRaySamples(model, (0, 0), model.Wavelengths[0], pupils, aimAtStop: false);
            Assert.True(phase.ReferenceSphere!.IsVirtual);
            Assert.InRange(Math.Abs(model.Paraxial.EstimateExitPupilLocation() - 1.1690341525286223), 0, 2e-5);
            foreach (var pair in phase.Samples.Zip(expected))
                Assert.InRange(Math.Abs(pair.First.OpdWaves - pair.Second), 0, 1e-5 + .001 * Math.Abs(pair.Second));
        }
    }

    [Fact]
    public async Task VirtualEntrancePupilDoesNotReverseFiniteObjectLaunch()
    {
        var optic = (await Import("Double-pass null test")).ActiveOptic;
        foreach (var aiming in new[] { false, true })
        {
            var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .3, .2, optic.Wavelengths[0].Micrometers, aiming).Rays.Single();
            var forward = optic.SurfaceGroup.Items[0].CoordinateSystem.ToGlobalDirection(new(0, 0, 1));
            Assert.True(source.Direction.X * forward.X + source.Direction.Y * forward.Y + source.Direction.Z * forward.Z > 0);
            Assert.True(optic.SequentialRayTracer.DiagnoseFirstFailure(source).ReachedImage);
        }
    }

    [Fact]
    public async Task ReturnFacesFollowTheConfiguredGlassCurvatureThicknessAndAsphere()
    {
        var imported = await Import("Twyman Green Interferometer");
        var first = imported.Configurations[0]; var second = imported.Configurations[1];
        Assert.Equal("BK7", first.SurfaceGroup.Items[8].MaterialAfter.Name);
        Assert.Equal("Air", second.SurfaceGroup.Items[8].MaterialAfter.Name);
        Assert.Equal(0, second.SurfaceGroup.Items[7].Thickness);
        Assert.Equal(0, second.SurfaceGroup.Items[8].Thickness);
        Assert.Equal(0, second.SurfaceGroup.Items[6].Radius);
        Assert.Equal(0, second.SurfaceGroup.Items[8].Radius);
        Assert.IsType<EvenAsphereGeometry>(second.SurfaceGroup.Items[9].Geometry);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new(imported.Configurations.ToList(), 1), file);
            var restored = (await StarOptProjectStore.LoadAsync(file)).Configurations[1];
            restored.SurfaceGroup.Items[5].Thickness = 2;
            restored.SurfaceGroup.Items[6].Thickness = 1;
            restored.Pickups.ApplyAll();
            Assert.Equal(-1, restored.SurfaceGroup.Items[7].Thickness);
            Assert.Equal(-2, restored.SurfaceGroup.Items[8].Thickness);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("MM", 1d)]
    [InlineData("CM", 10d)]
    public void ThicknessPickupOffsetsAndExtendedConfigurationValuesUseLensUnits(string units, double scale)
    {
        var optic = new ZemaxZmxImporter().ImportConfigurationSet($"""
            MODE SEQ
            UNIT {units}
            ENPD 1
            WAVM 1 .55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              DISZ 2
            SURF 2
              TYPE FZERNSAG
              XDAT 1 8
              XDAT 2 3
              XDAT 7 0
              DISZ 99
              PZUP 1 -2 .5 0
            SURF 3
              DISZ 0
            MNUM 2 1
            THIC 1 1 2
            THIC 1 2 4
            EDVA 2 1 0 0 7
            EDVA 2 2 .003 0 7
            """);
        Assert.Equal(-3.5 * scale, optic.Configurations[0].SurfaceGroup.Items[2].Thickness);
        Assert.Equal(-7.5 * scale, optic.Configurations[1].SurfaceGroup.Items[2].Thickness);
        var original = optic.Configurations[0].SurfaceGroup.Items[2].Geometry.Sag(1 * scale, 0);
        var configured = optic.Configurations[1].SurfaceGroup.Items[2].Geometry.Sag(1 * scale, 0);
        Assert.True(Math.Abs(configured - original) > .0001 * scale);
    }

    [Theory]
    [InlineData("PZUP 2 1 0 0", "PZUP 1 1 0 0")]
    [InlineData("GLAS BK7 2 2 1.5 40", "GLAS BK7 2 1 1.5 40")]
    [InlineData("CURV .1 4 2 1 0", "CURV .1 4 1 1 0")]
    public void CyclicImportedSolvesAreRejected(string first, string second)
    {
        var text = $"MODE SEQ\nUNIT MM\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\n{first}\nDISZ 1\nSURF 2\n{second}\nDISZ 1\nSURF 3\nDISZ 0";
        Assert.Throws<InvalidDataException>(() => new ZemaxZmxImporter().Import(text));
    }

    private static Task<ZemaxZmxImportResult> Import(string lens) => new ZemaxZmxImporter().ImportConfigurationSetFileAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "optical-testing", lens + ".zmx"));
}
