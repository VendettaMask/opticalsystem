using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.NonSequential;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSamplingBudgetTests
{
    private static DirectCameraScene Scene(int objects = 1, bool dark = false, bool overlap = false)
    {
        var planes = Enumerable.Range(0, objects).Select(i => NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        {
            LocalCoordinateSystem = new(new(i == 0 || overlap ? 0 : 1000 * i, 0, -100)), Parameters = new PlaneRectangleParameters(100, 100)
        }).ToArray();
        return new(new("exact-support control", [new("green", 550, 1, true)], planes),
            planes.Select(p => new DirectSceneMaterial(p.Id, [0], [dark ? 0 : 1])).ToArray(), []);
    }

    [Fact]
    public void EmptyWindowSchedulingKeepsEverySourceAndPupilSampleAndEveryOutputBit()
    {
        var lens = CameraSamplingTests.Lens(); var scene = Scene(); var sensor = new DirectCameraRenderSettings(4, 4, 3, new(2));
        var sampling = new SceneDiffractionSettings(8, 1, 3, 8);
        var original = DiffractionCameraRenderer.Render(lens, scene, sensor, sampling);
        var optimized = DiffractionCameraRenderer.RenderRegion(lens, scene, sensor, sampling, new(0, 0, 4, 4)).Result;
        Assert.Equal(original.Image.DetectorPower.Bands[0].PixelBandPowerWatts, optimized.Image.DetectorPower.Bands[0].PixelBandPowerWatts);
        Assert.Equal(original.Diffraction.SurfaceSamples, optimized.Diffraction.SurfaceSamples);
        Assert.Equal(original.Image.PassedLensRays, optimized.Image.PassedLensRays);
        Assert.Equal(original.Image.SceneHitRays, optimized.Image.SceneHitRays);
        Assert.Equal(original.Diffraction.SourceAreaSquareMillimeters, optimized.Diffraction.SourceAreaSquareMillimeters);
        Assert.Equal(original.Diffraction.CollectedPowerWatts, optimized.Diffraction.CollectedPowerWatts);
        Assert.Equal(original.Diffraction.ScalarCellEvaluations, optimized.Diffraction.ScalarCellEvaluations);
        Assert.True(optimized.Diffraction.AvoidedAuxiliaryRays > 0);
        Assert.Equal(original.Image.LaunchedRays - optimized.Image.LaunchedRays, optimized.Diffraction.AvoidedAuxiliaryRays);
    }

    [Fact]
    public void StreamedCandidateVisitsRetainActualSurfaceLimitAndOriginalFullImagePreflight()
    {
        var lens = CameraSamplingTests.Lens(); var sensor = new DirectCameraRenderSettings(4, 4, 3);
        var sampling = new SceneDiffractionSettings(4, 4, 1, 24); var scene = Scene(4, dark: true);
        Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.Render(lens, scene, sensor, sampling));
        var result = DiffractionCameraRenderer.RenderRegion(lens, scene, sensor, sampling, new(0, 0, 4, 4)).Result;
        Assert.Equal(173056, result.Diffraction.SourceCandidateVisits);
        Assert.Equal(43264, result.Diffraction.SurfaceSamples); Assert.Equal(0, result.Image.DetectorPower.TotalPowerWatts);
        var error = Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.RenderRegion(lens, Scene(2, dark: true, overlap: true), sensor, sampling, new(0, 0, 4, 4)));
        Assert.Contains("实际源表面点", error.Message);
    }

    [Fact]
    public async Task AllAdjacentLevelsSaveReplayAndRejectBudgetOrChainTampering()
    {
        using var app = WorkbenchApplication.Create(); var source = await CameraSamplingTests.Source(app.CameraImaging, 4, 4);
        source = await app.CameraImaging.RecomputeAsync(source, source.Request with { Render3D = new(Diffraction: new(4, 1, 1, 4)) });
        var before = source.RawDn.ToArray(); var region = new CameraSamplingRegion(0, 0);
        var report = await app.CameraImaging.CheckSamplingAsync(source, region, CameraSamplingAxis.Pupil, completeLadder: true);
        var pupil = Assert.Single(report.Comparisons); var levels = new[] { pupil }.Concat(pupil.FurtherRefinements!).ToArray();
        Assert.Equal(new[] { 8, 16, 32, 64 }, levels.Select(c => c.RequestedSettings.Diffraction.PupilSamplesAcross));
        Assert.All(levels, c => { Assert.Equal(CameraSamplingComparisonStatus.Compared, c.Status); Assert.NotNull(c.Refined!.Execution); });
        report = await app.CameraImaging.CheckSamplingAsync(source, region, CameraSamplingAxis.Scene, report, completeLadder: true);
        var scene = report.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Scene);
        Assert.Equal(4, Assert.Single(scene.FurtherRefinements!).RequestedSettings.Diffraction.SceneSubdivisions);
        Assert.Equal(before, source.RawDn); Assert.All(report.SavedBandDifferences, d => Assert.Equal(0, d.MaximumAbsolute));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("levels.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, source, SamplingReport: report), path);
        var opened = await app.CameraImaging.OpenExperimentAsync(path); app.Documents.NewBlank();
        var replay = await app.CameraImaging.CheckSamplingAsync(opened.B, region, CameraSamplingAxis.Pupil, completeLadder: true);
        var actual = replay.Comparisons.Single();
        foreach (var (a, b) in levels.Zip(new[] { actual }.Concat(actual.FurtherRefinements!)))
        {
            Assert.Equal(a.ElectronDifference, b.ElectronDifference); Assert.Equal(a.Refined!.PhotoElectrons, b.Refined!.PhotoElectrons);
            Assert.Equal(a.Refined.Execution, b.Refined.Execution);
            for (var i = 0; i < 3; i++) Assert.Equal(a.Refined.Bands[i].PixelPowerWatts, b.Refined.Bands[i].PixelPowerWatts);
        }
        var badLevel = pupil.FurtherRefinements![0] with { Axis = CameraSamplingAxis.Scene };
        var badPupil = pupil with { FurtherRefinements = new[] { badLevel } };
        var bad = report with { Comparisons = report.Comparisons.Select(c => c.Axis == CameraSamplingAxis.Pupil ? badPupil : c).ToArray() };
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSamplingAsync(source, bad, files.Path("bad.json")));
        badPupil = pupil with { Refined = pupil.Refined! with { Execution = pupil.Refined!.Execution! with { MaximumRays = long.MaxValue } } };
        bad = report with { Comparisons = [badPupil] };
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, source, SamplingReport: bad), files.Path("bad.zip")));
    }

    [Fact]
    public async Task OriginalSamplingReportRemainsReadableWithItsOriginalSourceHashAndFailedStatuses()
    {
        using var app = WorkbenchApplication.Create();
        var original = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-sampling-v1-frozen.zip"));
        Assert.Equal(5, original.SamplingReport!.Comparisons.Count); Assert.Null(original.SamplingReport.Baseline.Execution);
        Assert.Equal(CameraSamplingComparisonStatus.Unavailable, original.SamplingReport.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pupil).Status);
        Assert.All(original.SamplingReport.Comparisons, c => Assert.Null(c.FurtherRefinements));
        using var files = new CameraSensorFileTests.TemporaryFiles();
        await app.CameraImaging.ExportSamplingAsync(original.B, original.SamplingReport, files.Path("original.json"));
    }
}
