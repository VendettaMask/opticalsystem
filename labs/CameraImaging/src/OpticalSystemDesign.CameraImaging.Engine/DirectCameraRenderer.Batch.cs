using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

/// <summary>All coordinates, pixel centers and pupil bounds refer to the complete sensor, including across strip boundaries.</summary>
public sealed record DirectCameraStrip(int SourceIndex, int FirstRow, int RowCount,
    IReadOnlyList<SpectralDetectorPowerBand> Bands, RgbImage VisibleReflectance,
    long LaunchedRays, long PassedLensRays, long SceneHitRays, long SurfaceInteractions,
    long ShadowQueries, long ShadowedLightSamples, double PupilPlaneZMillimeters, double PupilPlaneRadiusMillimeters);
public sealed record DirectCameraBatchPlan(long Pixels, int SpectralNodes, long Rays, long MaximumInteractions,
    bool WithinBudget, string? Failure);
public sealed record DirectCameraBatchProgress(int SourceIndex, int CompletedRows, int FullHeight, long CompletedRays, long TotalRays);
public sealed record DirectCameraSourceStatistics(long LaunchedRays, long PassedLensRays, long SceneHitRays, long SurfaceInteractions);
public sealed record DirectCameraBatchStatistics(DirectCameraBatchPlan Plan, IReadOnlyList<DirectCameraSourceStatistics> Sources);

public static partial class DirectCameraRenderer
{
    /// <summary>One global budget for all pixels, sources, spectral nodes and surfaces. No per-strip resets.</summary>
    public static DirectCameraBatchPlan PlanBatch(DirectCameraRenderSettings settings, IReadOnlyList<int> sourceBandCounts, int physicalSurfaceCount)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(sourceBandCounts);
        if (settings.Width < 4 || settings.Height < 4 || settings.Width > 16384 || settings.Height > 16384
            || sourceBandCounts.Count is < 1 or > 16 || sourceBandCounts.Any(n => n is < 1 or > 16)
            || physicalSurfaceCount is < 1 or > 256 || settings.PupilRings is < 1 or > MaximumPupilRings || settings.PupilArms is < 4 or > 32
            || !double.IsFinite(settings.PixelPitchMicrometers) || settings.PixelPitchMicrometers is < .1 or > 100)
            throw new ArgumentException("Invalid complete-sensor batch settings.");
        var pixelNodes = CameraPixelAperture.Quadrature(settings.PixelSampling).Count;
        var pixels = checked((long)settings.Width * settings.Height); var nodes = sourceBandCounts.Sum();
        var rays = checked(pixels * pixelNodes * settings.PupilRings * settings.PupilArms * nodes);
        var interactions = checked(rays * physicalSurfaceCount);
        var failure = settings.Width > 1024 || settings.Height > 1024 || pixels > 262144
            ? "完整传感器超出既有 1024/边、262144 像元上限；未缩图或截取 ROI。"
            : rays > MaximumRays || interactions > MaximumSurfaceInteractions
                ? "全部任务合计超出既有光线/表面计算预算；未按波段或分块重置限额。" : null;
        return new(pixels, nodes, rays, interactions, failure is null, failure);
    }

    public static DirectCameraBatchStatistics RenderBatch(Optic optic, IReadOnlyList<DirectCameraScene> scenes,
        DirectCameraRenderSettings settings, int rowsPerStrip, Action<DirectCameraStrip> consume,
        IProgress<DirectCameraBatchProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(optic); ArgumentNullException.ThrowIfNull(scenes); ArgumentNullException.ThrowIfNull(consume);
        scenes = Array.AsReadOnly(scenes.ToArray());
        if (rowsPerStrip is < 1 or > 256 || scenes.Any(s => s is null)) throw new ArgumentException("Invalid strip/source request.");
        var frozen = optic.Clone();
        var plan = PlanBatch(settings, scenes.Select(s => s.WavelengthsNanometers.Count).ToArray(), frozen.SurfaceGroup.Items.Count - 2);
        if (!plan.WithinBudget) throw new ArgumentException(plan.Failure);
        Inspect(frozen.SurfaceGroup.Items.ToArray(), scenes.SelectMany(s => s.WavelengthsNanometers).Distinct().ToArray());
        var statistics = new List<DirectCameraSourceStatistics>(); long completedRays = 0, totalInteractions = 0;
        for (var source = 0; source < scenes.Count; source++)
        {
            long launched = 0, passed = 0, hit = 0, interactions = 0;
            for (var first = 0; first < settings.Height; first += rowsPerStrip)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var rows = RenderRows(frozen, scenes[source], settings, first, Math.Min(rowsPerStrip, settings.Height - first)) with { SourceIndex = source };
                launched += rows.LaunchedRays; passed += rows.PassedLensRays; hit += rows.SceneHitRays; interactions += rows.SurfaceInteractions;
                completedRays += rows.LaunchedRays; totalInteractions += rows.SurfaceInteractions;
                if (completedRays > plan.Rays || totalInteractions > plan.MaximumInteractions) throw new InvalidOperationException("Global camera work accounting exceeded the declared batch.");
                consume(rows); ComputationCancellation.ThrowIfCancellationRequested();
                progress?.Report(new(source, first + rows.RowCount, settings.Height, completedRays, plan.Rays));
            }
            if (passed == 0) throw new InvalidOperationException($"任务 {source + 1} 的全部通光样本被拒绝；没有用其他模型补图。");
            statistics.Add(new(launched, passed, hit, interactions));
        }
        if (completedRays != plan.Rays) throw new InvalidOperationException("Incomplete full-sensor batch.");
        return new(plan, Array.AsReadOnly(statistics.ToArray()));
    }
}
