using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotDiagramSummary(
    int RayCount,
    int VignettedRayCount,
    double Centroid,
    double RmsSpotRadius,
    double MaxSpotRadius);

public sealed record WavefrontSummary(
    int RayCount,
    int VignettedRayCount,
    double ReferenceOpticalPathLength,
    double MeanOpticalPathDifference,
    double RmsOpticalPathDifference,
    double PeakToValleyOpticalPathDifference);

public sealed class AnalysisRunner
{
    private readonly Optic _optic;

    public AnalysisRunner(Optic optic)
    {
        _optic = optic;
    }

    public SpotDiagramSummary EvaluateSpotDiagram()
    {
        var trace = _optic.RealRayTracer.TraceMeridionalRays();
        var finalHeights = trace.Paths
            .Where(path => path.Segments.Count > 0)
            .Select(path => new
            {
                path.Vignetted,
                Height = path.Segments[^1].End.Y
            })
            .ToArray();

        if (finalHeights.Length == 0)
        {
            return new SpotDiagramSummary(0, 0, 0, 0, 0);
        }

        var centroid = finalHeights.Average(item => item.Height);
        var distances = finalHeights.Select(item => Math.Abs(item.Height - centroid)).ToArray();
        var rms = Math.Sqrt(distances.Select(distance => distance * distance).Average());

        return new SpotDiagramSummary(
            RayCount: finalHeights.Length,
            VignettedRayCount: finalHeights.Count(item => item.Vignetted),
            Centroid: centroid,
            RmsSpotRadius: rms,
            MaxSpotRadius: distances.Max());
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
        var trace = _optic.SequentialRayTracer.Trace();
        var finalSamples = trace.RayHistories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
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
}
