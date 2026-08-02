using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotMetricSummary(
    int RayCount,
    int VignettedRayCount,
    double RmsSpotRadius,
    double MaximumSpotRadius,
    double Radius80);

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
            .SelectMany(field => field.Wavelengths)
            .SelectMany(wavelength => wavelength.Rays)
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

        var rms = Math.Sqrt(rays.Sum(ray =>
        {
            var weight = Math.Max(0, ray.Intensity);
            return weight * ((ray.X * ray.X) + (ray.Y * ray.Y));
        }) / totalWeight);
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

        var offsets = Enumerable.Range(0, count)
            .Select(index => (index - (count / 2)) * step)
            .ToArray();
        var points = offsets.Select(offset =>
        {
            var result = SpotAnalysisEngine.Generate(
                optic,
                fields,
                wavelengths,
                Math.Clamp(rayDensity, 1, 32),
                pattern,
                imagePlaneOffset: offset,
                surfaceNumber: surfaceNumber,
                reference: reference,
                usePolarization: usePolarization);
            var metric = SpotMetricEvaluator.Summarize(result, "Through focus metric");
            return new FocusMetricPoint(offset, metric.RmsSpotRadius, metric.Radius80);
        }).ToArray();
        var best = points.MinBy(point => point.RmsSpotRadius)
            ?? throw new AnalysisDataUnavailableException("Through focus metric", "no focus samples");
        return new FocusMetricSummary(step, best.FocusShift, best.RmsSpotRadius, points);
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
