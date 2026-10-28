using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraMaterialSpectrumTests
{
    private const double Hc = 6.62607015e-34 * 299792458;
    internal static SpectralMaterialScene Split(double a = .2, double b = .8) => new(2, 1, new[] { 0, 1 },
        new[] { new SpectralMaterial(0, CameraContinuousSpectrumTests.Reflectance(a)), new(1, CameraContinuousSpectrumTests.Reflectance(b)) });
    private static double WhiteCharge(int channel, double rho) => .0001 * rho * new[] { .2, .4, .6 }[channel]
        * 9e-12 * .01 * 1e-9 * (700d * 700 - 400d * 400) / (2 * Hc);

    [Theory]
    [InlineData(BayerPattern.RGGB)] [InlineData(BayerPattern.BGGR)] [InlineData(BayerPattern.GRBG)] [InlineData(BayerPattern.GBRG)]
    public void EachMaterialAndBayerPixelMatchesIndependentPhotonIntegral(BayerPattern pattern)
    {
        var plan = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(), Split(), CameraContinuousSpectrumTests.Qe, new());
        var sensor = CameraContinuousSpectrumTests.Clean with { Pattern = pattern };
        var result = CameraContinuousSpectrum.IntegrateMaterials(CameraContinuousSpectrumTests.White(8), sensor, plan);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
        {
            var channel = CameraSensorPipeline.ColorAt(pattern, y, x);
            Assert.Equal(WhiteCharge(channel, x < 4 ? .2 : .8), result.PhotoElectrons[y * 8 + x], 9);
            Assert.Equal(x < 4 ? .2 : .8, result.OpticalPreview[0, y, x], 12);
        }
        var summary = plan.DescribeSampling(8, 8);
        Assert.Equal(32, summary.PixelCountsById[0]); Assert.Equal(32, summary.PixelCountsById[1]);
        Assert.Equal(.015, summary.MeanReflectedIrradiance, 12);
        CameraContinuousSpectrumTests.Record("material-photon-" + pattern, new { ExpectedLeftRed = WhiteCharge(0, .2),
            ActualLeftRed = result.PhotoElectrons[Enumerable.Range(0, 64).First(i => i % 8 < 4 && CameraSensorPipeline.ColorAt(pattern, i / 8, i % 8) == 0)],
            ExpectedRightRed = WhiteCharge(0, .8), summary.MeanReflectedIrradiance });
    }

    [Fact]
    public void ReflectionHappensBeforeBlurAndOnlyOneOpticalCallPerWavelength()
    {
        var plan = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(), Split(0, 1), CameraContinuousSpectrumTests.Qe, new());
        var calls = 0;
        var result = CameraContinuousSpectrum.IntegrateMaterials(CameraContinuousSpectrumTests.White(8), CameraContinuousSpectrumTests.Clean, plan, (input, _) =>
        {
            calls++; var data = new double[1, 8, 8];
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
                data[0, y, x] = .25 * input[0, y, Math.Max(0, x - 1)] + .5 * input[0, y, x] + .25 * input[0, y, Math.Min(7, x + 1)];
            return new RgbImage(data);
        });
        Assert.Equal(plan.Common.Nodes.Count, calls);
        Assert.Equal(0, result.PhotoElectrons[0]);
        Assert.Equal(WhiteCharge(1, .25), result.PhotoElectrons[3], 9); // light crosses into the zero-reflectance source region
        Assert.Equal(WhiteCharge(0, .75), result.PhotoElectrons[4], 9);
        Assert.Equal(.25, result.OpticalPreview[0, 0, 3], 12); Assert.Equal(.75, result.OpticalPreview[0, 0, 4], 12);
        CameraContinuousSpectrumTests.Record("material-boundary-blur", new { Nodes = plan.Common.Nodes.Count, OpticalCalls = calls,
            ExpectedDarkSideEdge = WhiteCharge(1, .25), ActualDarkSideEdge = result.PhotoElectrons[3],
            ExpectedBrightSideEdge = WhiteCharge(0, .75), ActualBrightSideEdge = result.PhotoElectrons[4] });
    }

    [Fact]
    public void OneMaterialEqualsExistingUniformModelAndStreamedIdentity()
    {
        var scene = new SpectralMaterialScene(1, 1, new[] { 5 }, new[] { new SpectralMaterial(5, CameraContinuousSpectrumTests.Reflectance()) });
        var material = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(), scene, CameraContinuousSpectrumTests.Qe, new());
        var uniform = CameraContinuousSpectrum.BuildPlan(CameraContinuousSpectrumTests.Light(), CameraContinuousSpectrumTests.Reflectance(), CameraContinuousSpectrumTests.Qe, new());
        var input = CameraContinuousSpectrumTests.White(); input.Values[0, 4, 4] = .3;
        var a = CameraContinuousSpectrum.Integrate(input, CameraContinuousSpectrumTests.Clean, uniform);
        var b = CameraContinuousSpectrum.IntegrateMaterials(input, CameraContinuousSpectrumTests.Clean, material);
        var c = CameraContinuousSpectrum.IntegrateMaterials(input, CameraContinuousSpectrumTests.Clean, material, (mask, _) => mask);
        for (var i = 0; i < a.PhotoElectrons.Length; i++)
        { Assert.Equal(a.PhotoElectrons[i], b.PhotoElectrons[i], 9); Assert.Equal(b.PhotoElectrons[i], c.PhotoElectrons[i], 9); }
        var rawA = CameraSensorPipeline.SimulatePhotoElectrons(a.PhotoElectrons, 16, 16, CameraContinuousSpectrumTests.Clean, new());
        var rawB = CameraSensorPipeline.SimulatePhotoElectrons(b.PhotoElectrons, 16, 16, CameraContinuousSpectrumTests.Clean, new());
        Assert.Equal(rawA.RawDn, rawB.RawDn); Assert.Equal(0, rawB.RawNoiseRmseDn);
    }

    [Fact]
    public void NarrowMaterialPeakIsIncludedInSharedQuadratureKnots()
    {
        var peak = new ScalarSpectrum(ScalarSpectrumKind.Reflectance, new[] { new ScalarSpectrumSample(400, 0), new(531.9, 0), new(532, 1), new(532.1, 0), new(700, 0) });
        var scene = new SpectralMaterialScene(1, 1, new[] { 0 }, new[] { new SpectralMaterial(0, peak) });
        var plan = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(), scene, CameraContinuousSpectrumTests.Qe, new());
        var result = CameraContinuousSpectrum.IntegrateMaterials(CameraContinuousSpectrumTests.White(), CameraContinuousSpectrumTests.Clean, plan);
        var expected = .0001 * .1 * 532e-9 / Hc * .2 * 9e-12 * .01;
        Assert.InRange(Math.Abs(result.PhotoElectrons[0] / expected - 1), 0, 2e-12);
        Assert.Equal(.00001, plan.MaterialReflectedIrradiance[0], 12);
    }

    [Fact]
    public void CategoricalNearestNeighborAndFrozenLabelsRejectInvalidMaps()
    {
        var labels = new[] { 0, 2, 1 }; var definitions = new[] { new SpectralMaterial(0, CameraContinuousSpectrumTests.Reflectance()),
            new SpectralMaterial(1, CameraContinuousSpectrumTests.Reflectance()), new SpectralMaterial(2, CameraContinuousSpectrumTests.Reflectance()) };
        var scene = new SpectralMaterialScene(3, 1, labels, definitions); labels[0] = 1;
        Assert.Equal(new[] { 0, 2, 2, 1 }, Enumerable.Range(0, 4).Select(x => scene.SampleId(x, 0, 4, 1)));
        Assert.Throws<ArgumentException>(() => new SpectralMaterialScene(3, 1, new[] { 0, 0, 0 }, definitions));
        Assert.Throws<ArgumentException>(() => new SpectralMaterialScene(2, 1, new[] { 0, 16 }, definitions));
        Assert.Throws<ArgumentException>(() => new SpectralMaterialScene(2, 1, new[] { 0, 1 }, new[] { definitions[0], definitions[0] }));
        Assert.Throws<ArgumentException>(() => new SpectralMaterialScene(1, 1, new[] { 0 }, new[] { new SpectralMaterial(0, CameraContinuousSpectrumTests.Light()) }));
    }

    [Fact]
    public void CancellationAndNonIdentityPixelBudgetRemainEnforced()
    {
        var denseLight = new ScalarSpectrum(ScalarSpectrumKind.Illuminant, Enumerable.Range(400, 301).Select(w => new ScalarSpectrumSample(w, .0001)));
        var plan = CameraContinuousSpectrum.BuildMaterialPlan(denseLight, Split(), CameraContinuousSpectrumTests.Qe, new());
        Assert.Throws<ArgumentException>(() => CameraContinuousSpectrum.IntegrateMaterials(CameraContinuousSpectrumTests.White(512), CameraContinuousSpectrumTests.Clean, plan, (mask, _) => mask));
        using var cts = new CancellationTokenSource(); using var scope = ComputationCancellation.Push(cts.Token); var calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => CameraContinuousSpectrum.IntegrateMaterials(CameraContinuousSpectrumTests.White(), CameraContinuousSpectrumTests.Clean, plan, (mask, _) =>
        { calls++; cts.Cancel(); return mask; }));
        Assert.Equal(1, calls);
    }
}
