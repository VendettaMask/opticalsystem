using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotDiagramSummary(
    int RayCount,
    int VignettedRayCount,
    double Centroid,
    double RmsSpotRadius,
    double MaxSpotRadius);

public sealed record SpotSample(double X, double Y, double Intensity, bool Vignetted);

public sealed record WavefrontSummary(
    int RayCount,
    int VignettedRayCount,
    double ReferenceOpticalPathLength,
    double MeanOpticalPathDifference,
    double RmsOpticalPathDifference,
    double PeakToValleyOpticalPathDifference);

public sealed record EncircledEnergySummary(
    int RayCount,
    int VignettedRayCount,
    double TotalWeight,
    double CentroidX,
    double CentroidY,
    double Radius50,
    double Radius80,
    double Radius95);

public sealed record FieldRmsSummary(
    string FieldLabel,
    double FieldValue,
    double FieldWeight,
    int RayCount,
    int VignettedRayCount,
    double RmsSpotRadius);

public sealed record ThroughFocusPoint(
    double FocusShift,
    double RmsSpotRadius,
    double Radius80);

public sealed record ThroughFocusSummary(
    double FocusStep,
    double BestFocusShift,
    double BestRmsSpotRadius,
    IReadOnlyList<ThroughFocusPoint> Points);

public sealed class AnalysisRunner
{
    private readonly Optic _optic;

    public AnalysisRunner(Optic optic)
    {
        _optic = optic;
    }

    public SpotDiagramSummary EvaluateSpotDiagram()
    {
        var moments = SummarizeImageSamples(CollectFinalImageSamples());

        return new SpotDiagramSummary(
            RayCount: moments.RayCount,
            VignettedRayCount: moments.VignettedRayCount,
            Centroid: moments.CentroidY,
            RmsSpotRadius: moments.RmsRadius,
            MaxSpotRadius: moments.MaxRadius);
    }

    public IReadOnlyList<SpotSample> EvaluateSpotSamples()
    {
        return CollectFinalImageSamples();
    }

    public IReadOnlyList<double> BuildRayFan(int samples = 9)
    {
        var trace = _optic.RealRayTracer.TraceMeridionalRays(samples);
        return trace.Paths
            .Where(path => path.Segments.Count > 0)
            .Select(path => path.Segments[^1].End.Y)
            .ToArray();
    }

    public WavefrontSummary EvaluateWavefront()
    {
        var bundle = _optic.SequentialRayTracer.RayGenerator.Generate();
        var finalSamples = _optic.SequentialRayTracer.TraceFinalSamples(bundle)
            .Where(sample => sample is not null)
            .Select(sample => sample!)
            .ToArray();
        var validSamples = finalSamples
            .Where(sample => !sample.Vignetted && sample.Intensity > 0)
            .ToArray();

        if (finalSamples.Length == 0 || validSamples.Length == 0)
        {
            return new WavefrontSummary(0, 0, 0, 0, 0, 0);
        }

        var reference = validSamples.Average(sample => sample.CumulativeOpticalPathLength);
        var differences = validSamples
            .Select(sample => sample.CumulativeOpticalPathLength - reference)
            .ToArray();
        var mean = differences.Average();
        var rms = Math.Sqrt(differences.Select(difference => difference * difference).Average());
        var peakToValley = differences.Max() - differences.Min();

        return new WavefrontSummary(
            RayCount: finalSamples.Length,
            VignettedRayCount: finalSamples.Count(sample => sample.Vignetted || sample.Intensity <= 0),
            ReferenceOpticalPathLength: reference,
            MeanOpticalPathDifference: mean,
            RmsOpticalPathDifference: rms,
            PeakToValleyOpticalPathDifference: peakToValley);
    }

    public EncircledEnergySummary EvaluateEncircledEnergy()
    {
        var moments = SummarizeImageSamples(CollectFinalImageSamples());
        return new EncircledEnergySummary(
            moments.RayCount,
            moments.VignettedRayCount,
            moments.TotalWeight,
            moments.CentroidX,
            moments.CentroidY,
            moments.Radius50,
            moments.Radius80,
            moments.Radius95);
    }

    public IReadOnlyList<FieldRmsSummary> EvaluateRmsByField()
    {
        return _optic.Fields
            .Select(field =>
            {
                var bundle = _optic.SequentialRayTracer.RayGenerator.GenerateFor(
                    field,
                    applyFieldWeight: false,
                    applyWavelengthWeight: true);
                var moments = SummarizeImageSamples(CollectFinalImageSamples(bundle));
                return new FieldRmsSummary(
                    field.Label,
                    field.Y,
                    field.Weight,
                    moments.RayCount,
                    moments.VignettedRayCount,
                    moments.RmsRadius);
            })
            .ToArray();
    }

    public ThroughFocusSummary EvaluateThroughFocus()
    {
        var fNumber = Math.Abs(_optic.Paraxial.EstimateFNumber());
        var focusStep = Math.Clamp(double.IsFinite(fNumber) && fNumber > 0 ? fNumber * 0.05 : 0.5, 0.25, 2.0);
        var bundle = _optic.SequentialRayTracer.RayGenerator.Generate();
        var points = new[] { -2, -1, 0, 1, 2 }
            .Select(multiplier =>
            {
                var shift = multiplier * focusStep;
                var moments = SummarizeImageSamples(CollectFinalImageSamples(bundle, shift));
                return new ThroughFocusPoint(shift, moments.RmsRadius, moments.Radius80);
            })
            .ToArray();
        var best = points.OrderBy(point => point.RmsSpotRadius).FirstOrDefault()
            ?? new ThroughFocusPoint(0, 0, 0);

        return new ThroughFocusSummary(focusStep, best.FocusShift, best.RmsSpotRadius, points);
    }

    public string BuildTextReport()
    {
        var spot = EvaluateSpotDiagram();
        var wavefront = EvaluateWavefront();
        var focalLength = _optic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = _optic.Paraxial.EstimateFNumber();
        var aberrations = _optic.Aberrations.Estimate();

        return string.Join(Environment.NewLine, new[]
        {
            $"Optic: {_optic.Name}",
            $"Surfaces: {_optic.SurfaceGroup.Items.Count}",
            $"Fields: {_optic.Fields.Count}",
            $"Wavelengths: {_optic.Wavelengths.Count}",
            $"Estimated EFL: {focalLength:0.###} mm",
            $"Estimated F/#: {fNumber:0.###}",
            $"RMS spot radius: {spot.RmsSpotRadius:0.###} mm",
            $"Max spot radius: {spot.MaxSpotRadius:0.###} mm",
            $"Vignetted rays: {spot.VignettedRayCount}/{spot.RayCount}",
            $"RMS OPD: {wavefront.RmsOpticalPathDifference:0.####} mm",
            $"Aberration proxy S/C/A/Ch: {aberrations.Spherical:0.####} / {aberrations.Coma:0.####} / {aberrations.Astigmatism:0.####} / {aberrations.Chromatic:0.####}"
        });
    }

    private IReadOnlyList<SpotSample> CollectFinalImageSamples(RealRayBundle? bundle = null, double imagePlaneOffset = 0)
    {
        bundle ??= _optic.SequentialRayTracer.RayGenerator.Generate();
        return _optic.SequentialRayTracer.TraceFinalSamples(bundle)
            .Where(sample => sample is not null)
            .Select(sampleValue =>
            {
                var sample = sampleValue!;
                var position = sample.Position;
                if (Math.Abs(imagePlaneOffset) > 1e-12 && Math.Abs(sample.Direction.Z) > 1e-12)
                {
                    position += sample.Direction * (imagePlaneOffset / sample.Direction.Z);
                }

                return new SpotSample(position.X, position.Y, sample.Intensity, sample.Vignetted);
            })
            .ToArray();
    }

    private static ImageMoments SummarizeImageSamples(IReadOnlyList<SpotSample> samples)
    {
        var valid = samples.Where(sample => !sample.Vignetted && sample.Intensity > 0).ToArray();
        if (samples.Count == 0 || valid.Length == 0)
        {
            return new ImageMoments(samples.Count, samples.Count, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var totalWeight = valid.Sum(sample => sample.Intensity);
        if (totalWeight <= 1e-12)
        {
            return new ImageMoments(samples.Count, samples.Count - valid.Length, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var centroidX = valid.Sum(sample => sample.X * sample.Intensity) / totalWeight;
        var centroidY = valid.Sum(sample => sample.Y * sample.Intensity) / totalWeight;
        var weightedRadii = valid
            .Select(sample =>
            {
                var dx = sample.X - centroidX;
                var dy = sample.Y - centroidY;
                return new WeightedRadius(Math.Sqrt((dx * dx) + (dy * dy)), sample.Intensity);
            })
            .OrderBy(item => item.Radius)
            .ToArray();
        var rms = Math.Sqrt(weightedRadii.Sum(item => item.Weight * item.Radius * item.Radius) / totalWeight);
        var maxRadius = weightedRadii[^1].Radius;

        return new ImageMoments(
            samples.Count,
            samples.Count - valid.Length,
            totalWeight,
            centroidX,
            centroidY,
            rms,
            maxRadius,
            RadiusAtEnergy(weightedRadii, totalWeight, 0.50),
            RadiusAtEnergy(weightedRadii, totalWeight, 0.80),
            RadiusAtEnergy(weightedRadii, totalWeight, 0.95));
    }

    private static double RadiusAtEnergy(IReadOnlyList<WeightedRadius> weightedRadii, double totalWeight, double fraction)
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

        return weightedRadii.Count == 0 ? 0 : weightedRadii[^1].Radius;
    }

    private sealed record WeightedRadius(double Radius, double Weight);

    private sealed record ImageMoments(
        int RayCount,
        int VignettedRayCount,
        double TotalWeight,
        double CentroidX,
        double CentroidY,
        double RmsRadius,
        double MaxRadius,
        double Radius50,
        double Radius80,
        double Radius95);
}
