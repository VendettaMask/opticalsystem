using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraPixelAreaTests
{
    [Theory]
    [InlineData(BayerPattern.RGGB)] [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)] [InlineData(BayerPattern.GBRG)]
    public void AreaAveragePrecedesPhysicalPixelCfaSelection(BayerPattern pattern)
    {
        var values = new double[3, 16, 16];
        for (var c = 0; c < 3; c++) for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            values[c, y, x] = (c + 1) * (x % 4 + 1) / 12d;
        var sensor = CameraContinuousSpectrumTests.Clean with { Pattern = pattern };
        var actual = CameraSensorPipeline.Simulate(new(values), sensor, new(), pixelSampling: new(4));
        var averaged = new double[3, 4, 4];
        for (var c = 0; c < 3; c++) for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++) averaged[c, y, x] = (c + 1) * 2.5 / 12;
        var expected = CameraSensorPipeline.Simulate(new(averaged), sensor, new());
        Assert.Equal(4, actual.Width); Assert.Equal(4, actual.Height);
        for (var i = 0; i < 16; i++) Assert.Equal(expected.PhotoElectrons[i], actual.PhotoElectrons[i], 9);
        Assert.Equal(expected.RawDn, actual.RawDn);
    }

    [Theory]
    [InlineData(2)] [InlineData(4)]
    public void WholePixelQeDoesNotCountFillLossTwiceButActiveAreaQeDoes(int n)
    {
        var source = CameraContinuousSpectrumTests.White(4 * n); var sensor = CameraContinuousSpectrumTests.Clean;
        var full = CameraSensorPipeline.Simulate(source, sensor, new(), pixelSampling: new(n));
        var totalQe = CameraSensorPipeline.Simulate(source, sensor, new(), pixelSampling: new(n, .25));
        var activeQe = CameraSensorPipeline.Simulate(source, sensor, new(), pixelSampling: new(n, .25, false));
        for (var i = 0; i < 16; i++)
        { Assert.Equal(full.PhotoElectrons[i], totalQe.PhotoElectrons[i], 9); Assert.Equal(full.PhotoElectrons[i] / 4, activeQe.PhotoElectrons[i], 9); }
    }

    [Fact]
    public void CenteredApertureHasCorrectAreaWeightsAndSinusoidConvergesTowardBoxIntegral()
    {
        var values = new double[1, 16, 16];
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) values[0, y, x] = x % 4 is 1 or 2 ? 1 : 0;
        Assert.Equal(.5, CameraPixelAperture.Average(new(values), new(4))[0, 0, 0]);
        Assert.Equal(1, CameraPixelAperture.Average(new(values), new(4, .25))[0, 0, 0]);
        var expected = Math.Sin(Math.PI / 4) / (Math.PI / 4);
        double Amplitude(int n)
        {
            var image = new double[1, 4 * n, 4 * n];
            for (var y = 0; y < 4 * n; y++) for (var x = 0; x < 4 * n; x++)
                image[0, y, x] = 1 + .5 * Math.Cos(Math.PI / 2 * ((x + .5) / n - .5));
            return (CameraPixelAperture.Average(new(image), new(n))[0, 0, 0] - 1) / .5;
        }
        Assert.InRange(Math.Abs(Amplitude(4) - expected), 0, .006);
        Assert.True(Math.Abs(Amplitude(4) - expected) < Math.Abs(Amplitude(2) - expected) / 3);
    }

    [Theory]
    [InlineData(1, .5)] [InlineData(3, 1)] [InlineData(4, 0)] [InlineData(4, 1.01)] [InlineData(2, double.NaN)]
    public void UnsupportedSamplingIsRejectedBeforeAllocation(int n, double fill)
        => Assert.Throws<ArgumentException>(() => CameraPixelAperture.OutputDimensions(16, 16, new(n, fill)));

    [Fact]
    public void ExpandedGridBudgetCancellationAndLegacyIdentityArePreserved()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraPixelAperture.OutputDimensions(2048, 2048, new(4)));
        Assert.Throws<ArgumentException>(() => CameraPixelAperture.OutputDimensions(17, 16, new(4)));
        var source = CameraContinuousSpectrumTests.White();
        var a = CameraSensorPipeline.Simulate(source, new(), new());
        var b = CameraSensorPipeline.Simulate(source, new(), new(), pixelSampling: new());
        Assert.Equal(a.PhotoElectrons, b.PhotoElectrons); Assert.Equal(a.RawDn, b.RawDn);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var scope = ComputationCancellation.Push(cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => CameraPixelAperture.Average(source, new(2)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ContinuousAndMaterialIntegrationSelectTheOutputCfaAfterAreaAveraging(bool activeQe)
    {
        var plan = CameraContinuousSpectrum.BuildPlan(CameraContinuousSpectrumTests.Light(), CameraContinuousSpectrumTests.Reflectance(), CameraContinuousSpectrumTests.Qe, new());
        var scene = new SpectralMaterialScene(1, 1, [0], [new(0, CameraContinuousSpectrumTests.Reflectance())]);
        var material = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(), scene, CameraContinuousSpectrumTests.Qe, new());
        var sensor = CameraContinuousSpectrumTests.Clean; var input = CameraContinuousSpectrumTests.White();
        var sampling = new CameraPixelSampling(4, .25, !activeQe);
        var a = CameraContinuousSpectrum.Integrate(input, sensor, plan, pixelSampling: sampling);
        var b = CameraContinuousSpectrum.IntegrateMaterials(input, sensor, material, pixelSampling: sampling);
        var c = CameraContinuousSpectrum.IntegrateMaterials(input, sensor, material, (mask, _) => mask, sampling);
        var d = CameraContinuousSpectrum.Integrate(input, sensor, plan, (mask, _) => mask, sampling);
        var reference = CameraContinuousSpectrum.Integrate(CameraContinuousSpectrumTests.White(4), sensor, plan);
        Assert.Equal(16, a.PhotoElectrons.Length); Assert.Equal(4, a.OpticalPreview.Width);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(reference.PhotoElectrons[i] * (activeQe ? .25 : 1), a.PhotoElectrons[i], 9);
            Assert.Equal(a.PhotoElectrons[i], b.PhotoElectrons[i], 9); Assert.Equal(a.PhotoElectrons[i], c.PhotoElectrons[i], 9);
            Assert.Equal(a.PhotoElectrons[i], d.PhotoElectrons[i], 9);
        }
    }

    [Fact]
    public void MaterialBoundaryInsideAPixelMixesSourceReflectanceBeforeDetection()
    {
        var plan = CameraContinuousSpectrum.BuildMaterialPlan(CameraContinuousSpectrumTests.Light(),
            new SpectralMaterialScene(16, 1, Enumerable.Range(0, 16).Select(x => x % 4 < 2 ? 0 : 1).ToArray(),
                [new(0, CameraContinuousSpectrumTests.Reflectance(0)), new(1, CameraContinuousSpectrumTests.Reflectance(1))]),
            CameraContinuousSpectrumTests.Qe, new());
        var uniform = CameraContinuousSpectrum.BuildPlan(CameraContinuousSpectrumTests.Light(), CameraContinuousSpectrumTests.Reflectance(.5), CameraContinuousSpectrumTests.Qe, new());
        var sensor = CameraContinuousSpectrumTests.Clean; var input = CameraContinuousSpectrumTests.White();
        var a = CameraContinuousSpectrum.IntegrateMaterials(input, sensor, plan, pixelSampling: new(4));
        var b = CameraContinuousSpectrum.Integrate(input, sensor, uniform, pixelSampling: new(4));
        for (var i = 0; i < 16; i++) Assert.Equal(b.PhotoElectrons[i], a.PhotoElectrons[i], 9);
    }

    [Fact]
    public async Task AreaExperimentFreezesSettingsAndReplaysBothModelsWithVersionGuards()
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
        var center = await app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8, Optics: CameraOptics.Ideal));
        // V6 remains a frozen compatibility protocol; new V7 2D runs reject its degenerate partial-fill grid.
        var area = await app.CameraImaging.RecomputeAsync(center with
        { Request = center.Request with { PixelSampling = new(4, .25, false) }, Inputs = center.Inputs! with { EngineVersion = CameraExperiment.PixelAreaEngineVersion } });
        Assert.Equal(64, area.RawDn.Count); Assert.Contains("4x4 optical cells", area.ModelScope);
        var path = files.Path("area.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, area, center), path);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "pixel-area.camera-experiment.zip"), true); }
        var restored = await app.CameraImaging.OpenExperimentAsync(path);
        Assert.Equal(area.Request.PixelSampling, restored.B.Request.PixelSampling);
        Assert.Equal(area.RawDn, (await app.CameraImaging.RecomputeAsync(restored.B)).RawDn);
        var legacy = center with { Inputs = center.Inputs! with { EngineVersion = CameraExperiment.LegacyEngineVersion } };
        Assert.Equal(center.RawDn, (await app.CameraImaging.RecomputeAsync(legacy)).RawDn);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RecomputeAsync(legacy with { Request = area.Request }));
        await app.CameraImaging.ExportAsync(area, files.Path("export.zip"), center);
        using var zip = ZipFile.OpenRead(files.Path("export.zip"));
        using var reader = new StreamReader(zip.GetEntry("B/metadata.json")!.Open());
        Assert.Contains("DetectorSampling", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task RealLensAreaPropagationKeepsTheSensorFieldAndWorkingPrescription()
    {
        using var app = WorkbenchApplication.Create("cooke"); var before = app.Documents.GetSnapshot();
        var a = await app.CameraImaging.SimulateAsync(new(Width: 4, Height: 4, Aberration: CameraAberration.Geometric,
            PixelSampling: new(2)));
        Assert.Equal(16, a.PhotoElectrons.Count); Assert.Equal(4, a.Optical.Width);
        Assert.Equal(before.Revision, app.Documents.GetSnapshot().Revision);
        var replay = await app.CameraImaging.RecomputeAsync(a);
        Assert.Equal(a.RawDn, replay.RawDn); Assert.Equal(a.Inputs!.LensSnapshotJson, replay.Inputs!.LensSnapshotJson);
    }

    [Fact]
    public async Task ExpandedOpticalWorkIsRejectedBeforeTracing()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(new(PixelSampling: new(4))));
        Assert.Contains("资源上限", ex.Message);
    }

    [Fact]
    public async Task ImportedTotalQeCannotBeSilentlyReinterpretedAsActiveAreaQe()
    {
        using var app = WorkbenchApplication.Create();
        var response = new CameraResponseCurve("Absolute total QE", "test control", [new(400, .2, .4, .6), new(700, .2, .4, .6)]);
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8,
            Optics: CameraOptics.Ideal, SpectralResponse: response, PixelSampling: new(2, .25, false))));
        var center = await app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8, Optics: CameraOptics.Ideal, SpectralResponse: response));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(center.Request with { PixelSampling = new(2, .25) }));
        var a = await app.CameraImaging.RecomputeAsync(center with { Request = center.Request with { PixelSampling = new(2, .25) },
            Inputs = center.Inputs! with { EngineVersion = CameraExperiment.PixelAreaEngineVersion } });
        Assert.Equal(64, a.PhotoElectrons.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ContinuousAreaExperimentsFreezeSamplingAndMaterialCellCounts(bool materials)
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
        var response = await app.CameraImaging.ImportResponseAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv"));
        var light = await app.CameraImaging.ImportIlluminantAsync(Path.Combine(CameraSensorFileTests.Examples, "illuminant-flat.csv"));
        var rho = await app.CameraImaging.ImportReflectanceAsync(Path.Combine(CameraSensorFileTests.Examples, "reflectance-neutral.csv"));
        var scene = new CameraMaterialScene(CameraMaterialScene.FormatIdentifier, 1, "One material", "test control", 1, 1, [0], [new(0, "neutral", "test control", rho)]);
        var request = new CameraImagingRequest(Width: 8, Height: 8, Optics: CameraOptics.Ideal, SpectralResponse: response,
            SpectralModel: CameraSpectralModel.Continuous, Illuminant: light, Reflectance: rho,
            SceneMode: materials ? CameraSceneMode.MaterialMap : CameraSceneMode.Uniform, MaterialScene: materials ? scene : null,
            PixelSampling: new(2, .25));
        var center = await app.CameraImaging.SimulateAsync(request with { PixelSampling = null });
        var a = await app.CameraImaging.RecomputeAsync(center with { Request = request with { Sensor = center.Request.Sensor, Isp = center.Request.Isp },
            Inputs = center.Inputs! with { EngineVersion = CameraExperiment.PixelAreaEngineVersion } });
        Assert.Equal(64, a.PhotoElectrons.Count); Assert.Equal(8, a.Source.Width); Assert.Equal(2, a.Integration!.PixelSamplesPerAxis);
        if (materials) Assert.Equal(256, Assert.Single(a.Integration.Materials!).SampledPixels);
        var path = files.Path("continuous-area.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, a), path);
        var restored = await app.CameraImaging.OpenExperimentAsync(path);
        Assert.Equal(a.RawDn, (await app.CameraImaging.RecomputeAsync(restored.B)).RawDn);
    }
}
