using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class CameraImagingService
{
    public async Task<CameraSceneMtfReport> CompareSceneLinearMtfAsync(CameraSceneLinearBatchArtifact artifact,
        CameraSceneRoiSfrRequest request, int pupilAxisSamples = 129, CancellationToken cancellationToken = default)
    {
        if (pupilAxisSamples is < 3 or > 257)
            throw new ArgumentOutOfRangeException(nameof(pupilAxisSamples));
        var measured = await MeasureSceneLinearRoisAsync(artifact, request, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var (plan, optic) = ValidateLinearPlan(artifact.Manifest.Plan);
            var launchedRayBound = measured.Tasks.Sum(t =>
                (long)t.Regions.Count(r => r.Geometry is not null) * plan.Tasks[t.TaskIndex].Nodes.Count
                * pupilAxisSamples * pupilAxisSamples);
            if (launchedRayBound > DirectCameraRenderer.MaximumRays
                || launchedRayBound * optic.SurfaceGroup.Items.Count > DirectCameraRenderer.MaximumSurfaceInteractions)
                throw new ArgumentException("The requested formal reference comparisons exceed the geometric trace budget.");

            var settings = plan.Request;
            var tasks = measured.Tasks.Select(task =>
            {
                var regions = task.Regions.Select(region =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (region.Geometry is null)
                        return new SceneRoiMtfComparison(region, null, "ROI did not pass image measurement quality.");
                    try
                    {
                        var reference = SceneEdgeMtf.EvaluateSceneEdge(optic, settings.Chart!,
                            plan.Tasks[task.TaskIndex].Nodes, region.Geometry, settings.PixelPitchMicrometers,
                            settings.FillFactor, pupilAxisSamples, cancellationToken);
                        return new SceneRoiMtfComparison(region, reference, null);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                    {
                        return new SceneRoiMtfComparison(region, null, ex.Message);
                    }
                }).ToArray();
                return new SceneTaskMtfComparison(task.TaskIndex, task.Name, task.IrradianceSha256, Array.AsReadOnly(regions));
            }).ToArray();
            return new CameraSceneMtfReport(SceneEdgeMtf.SceneEdgeAlgorithm, measured.Algorithm,
                measured.SourceManifestSha256, measured.LensSha256, pupilAxisSamples, Array.AsReadOnly(tasks));
        }, cancellationToken).ConfigureAwait(false);
    }
}
