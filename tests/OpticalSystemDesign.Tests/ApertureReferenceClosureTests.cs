using OpticalSystemDesign.Validation;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Geometries;
using System.Globalization;

namespace OpticalSystemDesign.Tests;

public sealed class ApertureReferenceClosureTests
{
    [Fact]
    public void ImportedAnnulusBlocksItsExactInnerEdgeAfterSnapshotAndZmxRoundTrip()
    {
        const string source = "MODE SEQ\nUNIT CM\nENPD 15\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nCLAP 5 8 0\nDISZ 1\nSURF 2";
        var imported = OpticalFormatCatalog.Import(source, ".zmx");
        var exported = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(imported, ".zmx"), ".zmx");
        foreach (var optic in new[] { imported, Optic.FromSnapshot(imported.ToSnapshot()), exported })
        {
            var aperture = Assert.IsType<AnnularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
            Assert.False(aperture.Contains(new(50, 0, 0)));
            Assert.False(aperture.Contains(new(30, 40, 0)));
            Assert.True(aperture.Contains(new(50.0000005, 0, 0)));
            Assert.False(aperture.Contains(new(49.9999995, 0, 0)));
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 2d / 3, 0, optic.Wavelengths[0].Micrometers, false).Rays.Single();
            Assert.Equal(1, optic.SequentialRayTracer.DiagnoseApertures(ray).FirstBlockedSurfaceIndex);
        }

        Assert.True(new AnnularAperture(80, 50).Contains(new(50, 0, 0)));
    }

    [Fact]
    public void SpiderUsesRadialHalfLinesAndPreservesWidthAndOffsetAcrossFormats()
    {
        const string source = "MODE SEQ\nUNIT CM\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nSPID .2 3 0\nOBDC .15 -.05\nDISZ 1\nSURF 2";
        var original = OpticalFormatCatalog.Import(source, ".zmx");
        var roundTrip = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(original, ".zmx"), ".zmx");
        foreach (var optic in new[] { original, Optic.FromSnapshot(original.ToSnapshot()), roundTrip })
        {
            var aperture = Assert.IsType<SpiderObscurationAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);
            Assert.Equal(3, aperture.ArmCount);
            Assert.Equal(2, aperture.ArmWidth, 12);
            Assert.Equal(1.5, aperture.OffsetX, 12);
            Assert.Equal(-.5, aperture.OffsetY, 12);
            Assert.False(aperture.Contains(new(4.5, 0, 0)));
            Assert.True(aperture.Contains(new(-1.5, -.5, 0)));
        }
    }

    [Theory] [InlineData(2)] [InlineData(20)]
    public void NativeGeometricPlotOpensAtZeroEvenForAnExactlyFocusedBundle(double diameter)
    {
        var source=FormattableString.Invariant($"MODE SEQ\nUNIT MM\nENPD {diameter}\nWAVM 1 .55 1\nPWAV 1\nSURF 0\nCURV 0\nDISZ INFINITY\nSURF 1\nSTOP\nTYPE PARAXIAL\nCURV 0\nPARM 1 100\nDISZ 100\nSURF 2\nCURV 0");
        var optic=OpticalFormatCatalog.Import(source,".zmx");
        var data=new EncircledEnergyAnalysis(optic,32,"uniform",401,1,maximumDistanceMicrometers:5,multiplyByDiffractionLimit:false){ZemaxCompatibleOutput=true}.GenerateData();
        var points=Assert.Single(data.PlotSeries).Points;
        Assert.Equal(0,points[0].X);Assert.Equal(0,points[0].Y);
        Assert.Equal(1,points[4].Y,12);
        Assert.All(points.Zip(points.Skip(1)),p=>Assert.True(p.Second.Y>=p.First.Y));
        var physical=new OpticalSystemDesign.Core.Services.GeometricEnergyDistribution([new(0,0,1)],OpticalSystemDesign.Core.Services.GeometricEnergyReference.Centroid,OpticalSystemDesign.Core.Services.EnergyRegion.Circle);
        Assert.Equal(1,physical.FractionAtDistance(0));
    }

    [Theory] [InlineData(0, 0)] [InlineData(.23, -.4)]
    public void ZeroRadiusObscurationHasNoBlockedPoint(double x, double y)
    {
        IPhysicalAperture aperture = new CircularObscurationAperture(0, 0, x, y);
        foreach (var copy in new[] { aperture, aperture.Clone() })
        foreach (var point in new[] { new Vector3D(x, y, 0), new Vector3D(0, 0, 0), new Vector3D(x + 1e-15, y, 0) })
            Assert.True(copy.Contains(point));
        Assert.False(new CircularObscurationAperture(1).Contains(new(0, 0, 0)));
        Assert.False(new CircularObscurationAperture(1).Contains(new(1, 0, 0)));
    }

    [Fact]
    public void AZeroThicknessCoordinateBreakStopKeepsItsPupilPlaneAfterSnapshot()
    {
        var source = "MODE SEQ\nUNIT MM\nENPD 6\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nTYPE COORDBRK\nPARM 5 37\nDISZ 0\nSURF 2\nCURV .02\nGLAS N-BK7\nDISZ 5\nSURF 3\nCURV -.02\nDISZ 50\nSURF 4";
        var original = OpticalFormatCatalog.Import(source, ".zmx");
        foreach (var optic in new[] { original, Optic.FromSnapshot(original.ToSnapshot()) })
        {
            var stop = Assert.Single(optic.SurfaceGroup.Items, s => s.IsStop);
            Assert.Equal(1, stop.Number); Assert.IsType<CoordinateReferenceInteractionModel>(stop.InteractionModel);
            Assert.Null(stop.PhysicalAperture); Assert.Equal(37, stop.CoordinateSystem.RotationZDegrees, 12);
            Assert.Equal(0, optic.Paraxial.EstimateEntrancePupilLocation(), 12);
        }
    }

    [Fact]
    public void TransparentReferenceMayPrecedeTheSagOfACurvedReflectorAtTheSameVertex()
    {
        var source = "MODE SEQ\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nCURV 0\nDISZ 5\nSURF 2\nTYPE COORDBRK\nDISZ 0\nSURF 3\nCURV -.02\nGLAS MIRROR\nDISZ -5\nSURF 4\nCURV 0\nREAX 2 1 0 0 .2 .3 0 1";
        var original = OpticalFormatCatalog.Import(source, ".zmx");
        foreach (var optic in new[] { original, original.Clone() })
        {
            Assert.IsType<CoordinateReferenceInteractionModel>(optic.SurfaceGroup.Items[2].InteractionModel);
            var sample = optic.TraceGenericFinalSample(0, 0, .4, .2, optic.Wavelengths[0].Micrometers);
            Assert.NotNull(sample); Assert.False(sample.Vignetted);
            Assert.Equal(optic.SurfaceGroup.Items.Count - 1, sample.SurfaceNumber);
        }
    }

    [Theory]
    [InlineData("Toroid.zmx", 0, 1)]
    [InlineData("Toroid.zmx", 1, 0)]
    [InlineData("annular.zmx", 0, 0)]
    public void OriginalExampleChiefRemainsPhysicallyTransmitted(string file, double hx, double hy)
    {
        var original = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-closure", file)), ".zmx");
        foreach (var optic in new[] { original, original.Clone() })
        {
            var wave = optic.Wavelengths.First(w => w.IsPrimary);
            var sample = optic.TraceGenericFinalSample(hx, hy, 0, 0, wave.Micrometers, optic.RayAimingEnabled);
            Assert.NotNull(sample); Assert.False(sample.Vignetted); Assert.True(sample.Intensity > 0);
            Assert.Equal(optic.SurfaceGroup.Items.Count - 1, sample.SurfaceNumber);
        }
    }

    [Fact]
    public void EmptyAndSingletonEnergyPlotsDoNotCreateSamplesOrInvokeASpline()
    {
        Assert.Empty(EnergyPlotSampling.Geometric([]));
        var point = new AnalysisPoint(1, .3);
        Assert.Equal(new[] { point }, EnergyPlotSampling.Geometric([point]));
    }

    [Theory] [InlineData("MM", 1)] [InlineData("CM", 10)] [InlineData("IN", 25.4)]
    public void ToroidalNativeRotationRadiusSlotAndInactiveNormalizationRoundTrip(string unit, double scale)
    {
        var source = string.Create(CultureInfo.InvariantCulture, $"MODE SEQ\nUNIT {unit}\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE TOROIDAL\nCURV {scale / 50:R}\nPARM 1 {80 / scale:R}\nPARM 2 0\nXDAT 1 0\nXDAT 2 100\nDISZ 1\nSURF 2");
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var geometry = Assert.IsType<ToroidalGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(50, geometry.TangentialRadius, 12); Assert.Equal(80, geometry.SagittalRadius, 12);
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("PARM 1 80", exported, StringComparison.Ordinal);
        var restored = Assert.IsType<ToroidalGeometry>(OpticalFormatCatalog.Import(exported, ".zmx").SurfaceGroup.Items[1].Geometry);
        Assert.Equal(geometry.Sag(1, 2), restored.Sag(1, 2), 14);
        var asphericSource = string.Create(CultureInfo.InvariantCulture,
            $"PARM 2 {.01 * scale:R}\nCONI -1");
        var asphere = OpticalFormatCatalog.Import(source.Replace("PARM 2 0", asphericSource, StringComparison.Ordinal), ".zmx");
        var asphericGeometry = Assert.IsType<ToroidalGeometry>(asphere.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(.08, asphericGeometry.Sag(0, 2), 14);
        var roundTrip = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(asphere, ".zmx"), ".zmx");
        Assert.Equal(asphericGeometry.Sag(1, 2), roundTrip.SurfaceGroup.Items[1].Geometry.Sag(1, 2), 14);
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Import(source.Replace("XDAT 1 0", "XDAT 1 1\nXDAT 3 .01", StringComparison.Ordinal), ".zmx"));
    }

    [Fact]
    public void ZemaxQbfsUsesNormalDepartureFromItsBaseConic()
    {
        const string source = "MODE SEQ\nUNIT MM\nENPD 6\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE QED_TYPE\nCURV .1\nCONI -1\nXDAT 1 0\nXDAT 2 1\nXDAT 3 5\nXDAT 4 .1\nDISZ 10\nSURF 2";
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var geometry = Assert.IsType<ForbesNormalSlopeGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        var expected = .45 + Math.Sqrt(1.09) * .36 * .64 * .1;
        Assert.Equal(expected, geometry.Sag(3, 0), 13);
        Assert.Equal(expected, Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1].Geometry.Sag(0, 3), 13);
        Assert.Equal(expected, OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(optic, ".zmx"), ".zmx")
            .SurfaceGroup.Items[1].Geometry.Sag(3, 0), 13);
    }

    [Fact]
    public void NearlyCylindricalToroidRetainsSagAndSlopeWithoutRadiusSubtraction()
    {
        var geometry = new ToroidalGeometry(double.PositiveInfinity, 1e12);
        Assert.InRange(Math.Abs(geometry.Sag(1, 0) - 5e-13), 0, 1e-25);
        Assert.InRange(Math.Abs(geometry.SurfaceNormal(new(1, 0, 0)).X + 1e-12), 0, 1e-24);
        Assert.True(double.IsNaN(new ToroidalGeometry(10, 20).Sag(0, 11)));
    }
}
