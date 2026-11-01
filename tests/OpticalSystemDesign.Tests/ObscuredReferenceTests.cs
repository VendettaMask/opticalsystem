using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class ObscuredReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometricPhaseDatumPreservesThePhysicalAnnulusAndRelativePhase(bool aiming)
    {
        var clear = Optic.CreateCookeTriplet();
        var optic = Obscured(clear);
        var wave = optic.Wavelengths[0];
        var pupils = new[] { (0d, 0d), (.6, 0d), (-.6, 0d), (0d, .6), (0d, -.6) };
        var before = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator
            .GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils.Select(p => new PupilSample(p.Item1, p.Item2, 1)), aiming));
        Assert.Null(before[0]);
        Assert.Throws<InvalidOperationException>(() => WavefrontEngine.GenerateChiefRaySamples(
            optic, (0, 0), wave, pupils, aimAtStop: aiming));
        var reference = WavefrontEngine.GenerateChiefRaySamples(clear, (0, 0), clear.Wavelengths[0], pupils, aimAtStop: aiming);
        var actual = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, pupils,
            aimAtStop: aiming, allowGeometricReference: true);
        Assert.True(actual.GeometricChiefReferenceUsed);
        Assert.Equal(0, actual.Samples[0].Intensity);
        Assert.Equal(1, actual.VignettedRayCount);
        for (var i = 1; i < pupils.Length; i++)
        {
            Assert.NotNull(before[i]);
            Assert.True(actual.Samples[i].Intensity > 0);
            Assert.InRange(Math.Abs(actual.Samples[i].OpdWaves - reference.Samples[i].OpdWaves), 0, 1e-7);
        }
        Assert.Null(optic.TraceGenericFinalSample(0, 0, 0, 0, wave.Micrometers, aiming));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusedHuygensUsesGeometricCenterButOnlyPhysicalPupilPower(bool aiming)
    {
        var clear = Optic.CreateCookeTriplet();
        var optic = Obscured(clear);
        var wave = optic.Wavelengths[0];
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, aiming);
        var expected = DiffractionEngine.CreateHuygensImageFrame(clear, (0, 0), clear.Wavelengths[0], aiming);
        Assert.InRange((frame.Center - expected.Center).Length, 0, 1e-10);
        var actual = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 16, .002, aimAtStop: aiming);
        var full = DiffractionEngine.ComputeHuygensPsf(clear, (0, 0), clear.Wavelengths[0], 32, 16, .002, aimAtStop: aiming);
        Assert.True(actual.GeometricChiefReferenceUsed);
        Assert.All(actual.Values.Cast<double>(), value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.Contains(actual.Values.Cast<double>().Zip(full.Values.Cast<double>()), p => Math.Abs(p.First - p.Second) > .001);
        Assert.Null(optic.TraceGenericFinalSample(0, 0, 0, 0, wave.Micrometers, aiming));
    }

    [Fact]
    public void FieldScanGeometryDoesNotPromoteBlockedRaysToStrictOperandSamples()
    {
        var clear = Optic.CreateCookeTriplet();
        var optic = Obscured(clear);
        var actual = new FieldCurvatureAndDistortionAnalysis(optic, numPoints: 7).GenerateData();
        var expected = new FieldCurvatureAndDistortionAnalysis(clear, numPoints: 7).GenerateData();
        foreach (var (a, b) in actual.PlotPanes!.Zip(expected.PlotPanes!))
            foreach (var (x, y) in a.Series.SelectMany(s => s.Points).Zip(b.Series.SelectMany(s => s.Points)))
            {
                Assert.InRange(Math.Abs(x.X - y.X), 0, 1e-7);
                Assert.InRange(Math.Abs(x.Y - y.Y), 0, 1e-7);
            }
        Assert.Throws<InvalidOperationException>(() => AnalysisTrace.FinalSample(optic, 0, 0, 0, 0,
            optic.Wavelengths[0].Micrometers, requireUnvignetted: true));
    }

    [Fact]
    public void AnEntirelyOpaquePupilCannotBeReplacedByItsGeometricDatum()
    {
        var optic = Obscured(Optic.CreateCookeTriplet());
        optic.SurfaceGroup.Items.First(s => s.IsStop).PhysicalAperture = new RectangularAperture(.01, .01, 100, 100);
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 16, 8, .002));
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeFftPsf(
            optic, (0, 0), optic.Wavelengths[0], 32, 64));
    }

    private static Optic Obscured(Optic source)
    {
        var optic = Optic.FromSnapshot(source.ToSnapshot());
        var stop = optic.SurfaceGroup.Items.First(s => s.IsStop);
        stop.PhysicalAperture = new AnnularAperture(stop.SemiDiameter, .2 * stop.SemiDiameter);
        return optic;
    }

    [Fact]
    public void ChiefReferencedSpotsDoNotSilentlySwitchToCentroidWhenObscured()
    {
        var clear = Optic.CreateCookeTriplet();
        var optic = Obscured(clear);
        var wave = optic.Wavelengths[0];
        var absolute = SpotAnalysisEngine.Generate(optic, [(0, 1)], [wave], 13, "uniform", reference: "absolute");
        var relative = SpotAnalysisEngine.Generate(optic, [(0, 1)], [wave], 13, "uniform", reference: "chief");
        var frame = DiffractionEngine.CreateHuygensImageFrame(clear, (0, 1), clear.Wavelengths[0]);
        var center = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(frame.Center);
        foreach (var (a, b) in absolute.Fields.Single().WeightedRays.Zip(relative.Fields.Single().WeightedRays))
        {
            Assert.Equal(a.X - center.X, b.X, 10);
            Assert.Equal(a.Y - center.Y, b.Y, 10);
            Assert.Equal(a.Intensity, b.Intensity);
        }
    }

    [Theory]
    [InlineData("MM", 1)]
    [InlineData("CM", 10)]
    public async Task CircularObscurationKeepsItsComplementAndOffsetsAcrossProjectAndZmxRoundTrips(string unit, double scale)
    {
        var source = $"MODE SEQ\nUNIT {unit}\nENPD 10\nFTYP 0 0 1 1 0 0 0\nXFLN 0\nYFLN 0\nWAVM 1 .55 1\nPWAV 1\n"
            + "SURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 5 1 0 0 1\nDISZ 10\nOBSC .5 2 0\nOBDC .25 -.1\nSURF 2\nDISZ 0";
        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        void Check(Optic model)
        {
            var aperture = Assert.IsType<CircularObscurationAperture>(model.SurfaceGroup.Items[1].PhysicalAperture);
            Assert.Equal(2 * scale, aperture.OuterRadius); Assert.Equal(.5 * scale, aperture.InnerRadius);
            Assert.Equal(.25 * scale, aperture.OffsetX); Assert.Equal(-.1 * scale, aperture.OffsetY);
            Assert.True(aperture.Contains(new(.25 * scale, -.1 * scale, 0)));
            Assert.False(aperture.Contains(new(1.25 * scale, -.1 * scale, 0)));
            Assert.True(aperture.Contains(new(20 * scale, 0, 0)), "An obscuration must not add an outer clear aperture.");
        }
        Check(optic); Check(Optic.FromSnapshot(optic.ToSnapshot()));
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), file);
            Check((await StarOptProjectStore.LoadAsync(file)).Configurations[0]);
        }
        finally { File.Delete(file); }
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("OBSC", exported);
        Check(OpticalFormatCatalog.Import(exported, ".zmx"));
    }
}
