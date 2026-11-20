using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.Tests;

public sealed class CameraPupilQuadratureTests
{
    [Theory]
    [InlineData(8)] [InlineData(16)] [InlineData(24)] [InlineData(32)]
    public void StratificationIntegratesDiskAreaMomentsAndClippedAnnuliWithoutRenormalizing(int density)
    {
        var nodes = CameraPupilQuadrature.Generate(new(4, 4, 3, PupilRings: density, PupilArms: 16,
            PupilSampling: CameraPupilSampling.AreaStratifiedDisk));
        Assert.Equal(density * 16, nodes.Count);
        Assert.Equal(1, nodes.Sum(p => p.Weight), 12);
        Assert.Equal(0, nodes.Sum(p => p.Weight * p.X), 12);
        Assert.Equal(0, nodes.Sum(p => p.Weight * p.Y), 12);
        Assert.Equal(.5, nodes.Sum(p => p.Weight * (p.X * p.X + p.Y * p.Y)), 12);
        foreach (var (inner, outer) in new[] { (0d, .17), (.12, .47), (.31, .79), (.6, .99) })
        {
            var area = nodes.Where(p => p.X * p.X + p.Y * p.Y >= inner * inner
                && p.X * p.X + p.Y * p.Y <= outer * outer).Sum(p => p.Weight);
            Assert.InRange(Math.Abs(area - (outer * outer - inner * inner)), 0, 2d / nodes.Count + 1e-12);
        }
    }

    [Fact]
    public void LegacyNodesAndGlobalWorkBudgetsRetainTheirIdentities()
    {
        var settings = new DirectCameraRenderSettings(64, 64, 3, new(2), 32, 16);
        Assert.Equal(ApertureSampler.GenerateGaussianQuadrature(32, 16), CameraPupilQuadrature.Generate(settings));
        var stratified = settings with { PupilSampling = CameraPupilSampling.AreaStratifiedDisk };
        var legacy = DirectCameraRenderer.PlanBatch(settings, [1], 12);
        Assert.Equal(legacy, DirectCameraRenderer.PlanBatch(stratified, [1], 12));
        Assert.Equal(8_388_608, legacy.Rays);
        Assert.False(DirectCameraRenderer.PlanBatch(stratified, [1, 1], 12).WithinBudget);
        Assert.Throws<ArgumentException>(() => DirectCameraRenderer.PlanBatch(stratified with { PupilSampling = (CameraPupilSampling)99 }, [1], 12));
        Assert.Throws<ArgumentException>(() => DirectCameraRenderer.PlanBatch(stratified with { PupilRings = 3, PupilArms = 5 }, [1], 12));
        Assert.DoesNotContain("PupilSampling", JsonSerializer.Serialize(new CameraSceneLinearBatchRequest()));
    }

    [Fact]
    public async Task HistoricalArchiveAndRoiCompanionKeepTheirOriginalSourceIdentity()
    {
        using var app = WorkbenchApplication.Create();
        var root = SlantedEdgeSfrTests.RepositoryRoot;
        var archive = EvidenceFiles.Resolve(Path.Combine(root, "validation/rechecks/2026-11-03/scene-multi-roi-sfr/double-gauss-cross-128.scene-linear.zip"));
        var artifact = await app.CameraImaging.OpenSceneLinearBatchAsync(archive);
        Assert.Equal(CameraPupilSampling.GaussianRings, artifact.Manifest.Plan.Request.PupilSampling);
        Assert.Equal(CameraSceneLinearBatchPlan.CurrentAlgorithm, artifact.Manifest.Plan.Algorithm);
        var report = await app.CameraImaging.OpenSceneLinearRoisAsync(artifact,
            Path.Combine(root, "validation/rechecks/2026-11-03/scene-multi-roi-sfr/double-gauss-cross-128.scene-sfr-rois.json"));
        Assert.Contains(report.Tasks.SelectMany(t => t.Regions), r => r.Edge is not null);
    }

    [Fact]
    public async Task StratifiedArchivesReplayAcrossStripSizesAndRejectMislabelledAlgorithms()
    {
        using var app = WorkbenchApplication.Create(); var fixture = await SceneLinearBatchTests.LoadFixture(app);
        var a = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip"); var b = a + ".replay.zip";
        try
        {
            var request = new CameraSceneLinearBatchRequest(new(4, 4, 2, 100, CenterYMillimeters: 1),
                Width: 32, Height: 40, PixelPitchMicrometers: 10, PupilRings: 4, PupilArms: 8,
                PixelSamplesPerAxis: 2, RowsPerStrip: 7, PupilSampling: CameraPupilSampling.AreaStratifiedDisk);
            var plan = app.CameraImaging.PrepareSceneLinearBatch(request);
            Assert.Equal(CameraSceneLinearBatchPlan.AreaStratifiedAlgorithm, plan.Algorithm);
            var original = await app.CameraImaging.RunSceneLinearBatchAsync(plan, a);
            var reopened = await app.CameraImaging.OpenSceneLinearBatchAsync(a);
            Assert.Equal(request.PupilSampling, reopened.Manifest.Plan.Request.PupilSampling);
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var replay = await app.CameraImaging.RunSceneLinearBatchAsync(reopened.Manifest.Plan with
                { Request = reopened.Manifest.Plan.Request with { RowsPerStrip = 3 } }, b);
            Assert.Equal(original.Manifest.Files.Select(f => f.Sha256), replay.Manifest.Files.Select(f => f.Sha256));
            Assert.Equal(plan.Work.Rays, original.Manifest.CompletedRays);
            var x = await app.CameraImaging.ReadSceneLinearTaskAsync(original, 0);
            var y = await app.CameraImaging.ReadSceneLinearTaskAsync(original, 1);
            Assert.True(x.PowerWatts.Max() > 0);
            for (var i = 0; i < x.PowerWatts.Count; i++) Assert.Equal(x.PowerWatts[i], y.PowerWatts[i] * 2);
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RunSceneLinearBatchAsync(plan with
                { Algorithm = CameraSceneLinearBatchPlan.CurrentAlgorithm }, b));
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RunSceneLinearBatchAsync(plan with
                { Request = plan.Request with { PupilSampling = CameraPupilSampling.GaussianRings } }, b));
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RunSceneLinearBatchAsync(plan with
                { Request = plan.Request with { PupilSampling = (CameraPupilSampling)99 } }, b));
        }
        finally { File.Delete(fixture); File.Delete(a); File.Delete(b); }
    }
}
