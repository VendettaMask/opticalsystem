using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SampledApertureRayDiagnostic(double WavelengthNanometers, int PupilIndex,
    double? MinimumClearanceMillimeters, int? LimitingSurfaceIndex, bool ReachedImage);

/// <summary>Unclipped diagnostic spot data cannot establish physical throughput or acceptance.</summary>
public sealed record SampledApertureDiagnosticResult(SampledSpotResult UnclippedSpot,
    IReadOnlyList<SampledApertureRayDiagnostic> Rays)
{
    public bool IsComplete => Rays.Count > 0 && Rays.All(ray => ray.ReachedImage);
}

public static class SampledApertureDiagnostics
{
    /// <summary>
    /// Reports circular aperture margins and geometric, common-centroid image coordinates.
    /// Uses formal ray generation, surface interactions, centering and spot statistics.
    /// Missing intersections and other propagation failures remain failures, not surrogate rays.
    /// </summary>
    public static SampledApertureDiagnosticResult Evaluate(Optic optic, double normalizedFieldX,
        double normalizedFieldY, IReadOnlyList<PupilSample> pupilSamples, int wavelengthNumber = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(pupilSamples);
        cancellationToken.ThrowIfCancellationRequested();
        if (pupilSamples.Count is < 1 or > 65_536 || pupilSamples.Any(sample =>
            !double.IsFinite(sample.Weight) || sample.Weight <= 0))
            throw new ArgumentException("Pupil samples require finite positive weights and 1 to 65536 entries.", nameof(pupilSamples));
        if (wavelengthNumber < 0 || wavelengthNumber > optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNumber));
        var waves = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber).Where(wave => wave.Weight > 0).ToArray();
        if (waves.Length == 0 || optic.SurfaceGroup.Items.Count == 0)
            throw new AnalysisDataUnavailableException("Sampled aperture diagnostic", "no positive-weight wavelengths or image surface");
        var coordinates = ImageSpaceAnalysisSupport.CoordinateDescriptor(optic);
        var target = optic.SurfaceGroup.Items[^1];
        var diagnostics = new List<SampledApertureRayDiagnostic>();
        var waveData = new List<SpotWavelengthData>();
        foreach (var wave in waves)
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                normalizedFieldX, normalizedFieldY, wave.Micrometers, pupilSamples);
            var rays = new List<SpotRayData>();
            for (var index = 0; index < bundle.Rays.Count; index++)
            {
                var diagnostic = optic.SequentialRayTracer.DiagnoseCircularApertures(bundle.Rays[index], cancellationToken);
                var data = diagnostic.UnclippedImage is { } image
                    ? ImageSpaceAnalysisSupport.ToImageSpaceRayData(optic, image, target, coordinates) with
                    { Intensity = bundle.Rays[index].Intensity } : null;
                var reached = data is not null && double.IsFinite(data.X) && double.IsFinite(data.Y) && data.Intensity > 0;
                if (reached) rays.Add(data!);
                diagnostics.Add(new(wave.Nanometers, index, diagnostic.MinimumClearanceMillimeters,
                    diagnostic.LimitingSurfaceIndex, reached));
            }
            waveData.Add(new(wave, rays));
        }
        var field = new SpotFieldData(normalizedFieldX, normalizedFieldY, waveData);
        var centroid = SpotAnalysisEngine.Centroid(field.WeightedRays);
        var centered = field with
        {
            Wavelengths = waveData.Select(wave => wave with
            {
                Rays = wave.Rays.Select(ray => ray with { X = ray.X - centroid.X, Y = ray.Y - centroid.Y }).ToArray()
            }).ToArray()
        };
        var attempted = diagnostics.Count;
        var missing = diagnostics.Count(ray => !ray.ReachedImage);
        var analysis = new SpotAnalysisResult([centered], attempted, missing);
        var metrics = centered.WeightedRays.Any() ? SpotMetricEvaluator.Summarize(analysis, "Unclipped diagnostic spot") : null;
        var result = new SampledSpotResult(attempted, missing, metrics,
            centered.Wavelengths.Select(wave => new SampledSpotWavelength(wave.Wavelength.Nanometers,
                wave.Wavelength.Weight, pupilSamples.Count,
                wave.Rays.Select(ray => new SampledSpotRay(ray.X, ray.Y, ray.Intensity)).ToArray())).ToArray(),
            coordinates.Quantity, coordinates.Unit);
        return new(result, diagnostics);
    }
}
