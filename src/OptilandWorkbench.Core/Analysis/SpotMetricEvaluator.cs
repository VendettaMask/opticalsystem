using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotMetricSummary(
    int RayCount,
    int VignettedRayCount,
    double RmsSpotRadius,
    double MaximumSpotRadius,
    double Radius80);

/// <summary>Image-space coordinates relative to the requested reference, in the result's typed unit.</summary>
public sealed record SampledSpotRay(double X, double Y, double Weight);

public sealed record SampledSpotWavelength(double Nanometers, double SpectralWeight,
    int AttemptedRayCount, IReadOnlyList<SampledSpotRay> Rays);

public sealed record SampledSpotResult(int RayCount, int VignettedRayCount,
    SpotMetricSummary? Metrics, IReadOnlyList<SampledSpotWavelength> Wavelengths,
    AnalysisAxisQuantity Quantity, AnalysisAxisUnit Unit);

public sealed record FocusMetricPoint(
    double FocusShift,
    double RmsSpotRadius,
    double Radius80);

public sealed record FocusMetricSummary(
    double FocusStep,
    double BestFocusShift,
    double BestRmsSpotRadius,
    IReadOnlyList<FocusMetricPoint> Points);

public sealed class AnalysisDataUnavailableException : InvalidOperationException
{
    public AnalysisDataUnavailableException(string analysisName, string reason)
        : base($"{analysisName} has no valid data: {reason}.")
    {
        AnalysisName = analysisName;
        Reason = reason;
    }

    public string AnalysisName { get; }

    public string Reason { get; }
}

public static class SpotMetricEvaluator
{
    /// <summary>
    /// Uses the same tracing, reference centering and statistics as formal spot analysis,
    /// with caller-selected pupil coordinates. Spectral weight is applied once by Metrics;
    /// each returned ray retains its monochromatic weight. No valid rays yields null Metrics.
    /// </summary>
    public static SampledSpotResult EvaluatePupilSamples(
        Optic optic, double normalizedFieldX, double normalizedFieldY,
        IReadOnlyList<PupilSample> pupilSamples, int wavelengthNumber = 0,
        string reference = "centroid", bool includeSurfaceTransmission = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(pupilSamples);
        cancellationToken.ThrowIfCancellationRequested();
        if (pupilSamples.Count is < 1 or > 65_536 || pupilSamples.Any(sample =>
            !double.IsFinite(sample.Weight) || sample.Weight <= 0))
            throw new ArgumentException("Pupil samples require finite positive weights and 1 to 65536 entries.", nameof(pupilSamples));
        if (reference is not ("absolute" or "centroid"))
            throw new ArgumentException("Explicit pupil evaluation supports absolute or common polychromatic centroid reference.", nameof(reference));
        if (wavelengthNumber < 0 || wavelengthNumber > optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNumber));
        var waves = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber)
            .Where(wave => wave.Weight > 0).ToArray();
        if (waves.Length == 0)
            throw new AnalysisDataUnavailableException("Sampled spot", "no positive-weight wavelengths");
        var result = SpotAnalysisEngine.Generate(optic, [(normalizedFieldX, normalizedFieldY)], waves,
            1, "hexapolar", reference: reference, includeSurfaceTransmission: includeSurfaceTransmission,
            explicitPupilSamples: pupilSamples, cancellationToken: cancellationToken);
        var field = result.Fields.Single();
        var metrics = field.WeightedRays.Any() ? Summarize(result, "Sampled spot") : null;
        var coordinates = ImageSpaceAnalysisSupport.CoordinateDescriptor(optic);
        return new(result.RayCount, result.VignettedRayCount, metrics,
            field.Wavelengths.Select(wave => new SampledSpotWavelength(wave.Wavelength.Nanometers,
                wave.Wavelength.Weight, pupilSamples.Count,
                wave.Rays.Select(ray => new SampledSpotRay(ray.X, ray.Y, ray.Intensity)).ToArray())).ToArray(),
            coordinates.Quantity, coordinates.Unit);
    }

    public static SpotMetricSummary Evaluate(
        Optic optic,
        int rayDensity = 6,
        string pattern = "hexapolar",
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        int surfaceNumber = -1,
        string reference = "centroid",
        bool usePolarization = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var allFields = SpotAnalysisEngine.DefinedFields(optic);
        var fields = (fieldNumber <= 0
            ? allFields
            : new[]
            {
                allFields[Math.Clamp(fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1))]
            }).ToArray();
        var wavelengths = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber).ToArray();
        if (fields.Length == 0 || wavelengths.Length == 0)
        {
            throw new AnalysisDataUnavailableException("Spot metric", "no fields or wavelengths");
        }

        var result = SpotAnalysisEngine.Generate(
            optic,
            fields,
            wavelengths,
            Math.Clamp(rayDensity, 1, 32),
            pattern,
            surfaceNumber: surfaceNumber,
            reference: reference,
            usePolarization: usePolarization);
        return Summarize(result, "Spot metric");
    }

    internal static SpotMetricSummary Summarize(
        SpotAnalysisResult result,
        string analysisName)
    {
        var rays = result.Fields
            .SelectMany(field => field.WeightedRays)
            .ToArray();
        if (rays.Length == 0)
        {
            throw new AnalysisDataUnavailableException(analysisName, "no valid rays reached the selected surface");
        }

        var totalWeight = rays.Sum(ray => Math.Max(0, ray.Intensity));
        if (!(totalWeight > 0) || !double.IsFinite(totalWeight))
        {
            throw new AnalysisDataUnavailableException(analysisName, "valid rays have no finite positive weight");
        }

        var rms = SpotAnalysisEngine.RmsRadius(rays);
        var weightedRadii = rays
            .Select(ray => new WeightedRadius(
                Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y)),
                Math.Max(0, ray.Intensity)))
            .OrderBy(item => item.Radius)
            .ToArray();
        return new SpotMetricSummary(
            result.RayCount,
            result.VignettedRayCount,
            rms,
            weightedRadii[^1].Radius,
            RadiusAtEnergy(weightedRadii, totalWeight, 0.8));
    }

    private static double RadiusAtEnergy(
        IReadOnlyList<WeightedRadius> weightedRadii,
        double totalWeight,
        double fraction)
    {
        var target = totalWeight * Math.Clamp(fraction, 0, 1);
        var cumulative = 0.0;
        foreach (var item in weightedRadii)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
            {
                return item.Radius;
            }
        }

        return weightedRadii[^1].Radius;
    }

    private sealed record WeightedRadius(double Radius, double Weight);
}

public static class FocusMetricEvaluator
{
    public static FocusMetricSummary Evaluate(
        Optic optic,
        double? focusStep = null,
        int focusPlaneCount = 5,
        int rayDensity = 6,
        string pattern = "hexapolar",
        int wavelengthNumber = 0,
        int fieldNumber = 0,
        int surfaceNumber = -1,
        string reference = "centroid",
        bool usePolarization = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var step = focusStep ?? DefaultFocusStep(optic);
        if (!double.IsFinite(step) || step < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(focusStep));
        }

        var count = Math.Clamp(focusPlaneCount, 1, 7);
        if (count % 2 == 0)
        {
            count = Math.Min(7, count + 1);
        }

        var allFields = SpotAnalysisEngine.DefinedFields(optic);
        var fields = (fieldNumber <= 0
            ? allFields
            : new[]
            {
                allFields[Math.Clamp(fieldNumber - 1, 0, Math.Max(0, allFields.Count - 1))]
            }).ToArray();
        var wavelengths = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber).ToArray();
        if (fields.Length == 0 || wavelengths.Length == 0)
        {
            throw new AnalysisDataUnavailableException("Through focus metric", "no fields or wavelengths");
        }

        var sweep = FocusSweepEvaluator.Evaluate(
            optic,
            fields,
            wavelengths,
            count,
            step,
            Math.Clamp(rayDensity, 1, 32),
            pattern,
            surfaceNumber,
            reference,
            usePolarization,
            "Through focus metric");
        return new FocusMetricSummary(
            step,
            sweep.Best.FocusShift,
            sweep.Best.RmsSpotRadius,
            sweep.Points);
    }

    private static double DefaultFocusStep(Optic optic)
    {
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        return Math.Clamp(
            double.IsFinite(fNumber) && fNumber > 0 ? fNumber * 0.05 : 0.5,
            0.25,
            2.0);
    }
}

internal sealed record FocusSweepResult(
    IReadOnlyList<double> Offsets,
    IReadOnlyList<SpotAnalysisResult> SpotResults,
    IReadOnlyList<FocusMetricPoint> Points,
    FocusMetricPoint Best);

internal static class FocusSweepEvaluator
{
    public static FocusSweepResult Evaluate(
        Optic optic,
        IReadOnlyList<(double Hx, double Hy)> fields,
        IReadOnlyList<Wavelength> wavelengths,
        int focusPlaneCount,
        double focusStep,
        int rayDensity,
        string pattern,
        int surfaceNumber,
        string reference,
        bool usePolarization,
        string analysisName)
    {
        var offsets = Enumerable.Range(0, focusPlaneCount)
            .Select(index => (index - (focusPlaneCount / 2)) * focusStep)
            .ToArray();
        var results = offsets.Select(offset => SpotAnalysisEngine.Generate(
            optic,
            fields,
            wavelengths,
            rayDensity,
            pattern,
            imagePlaneOffset: offset,
            surfaceNumber: surfaceNumber,
            reference: reference,
            usePolarization: usePolarization)).ToArray();
        var points = results.Select((result, index) =>
        {
            var metric = SpotMetricEvaluator.Summarize(result, analysisName);
            return new FocusMetricPoint(offsets[index], metric.RmsSpotRadius, metric.Radius80);
        }).ToArray();
        var best = points.MinBy(point => point.RmsSpotRadius)
            ?? throw new AnalysisDataUnavailableException(analysisName, "no focus samples");
        return new FocusSweepResult(offsets, results, points, best);
    }
}
