using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record SpotDiagramSummary(
    int RayCount,
    int VignettedRayCount,
    double Centroid,
    double RmsSpotRadius,
    double MaxSpotRadius);

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

    public string BuildTextReport()
    {
        var spot = EvaluateSpotDiagram();
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
            $"Aberration proxy S/C/A/Ch: {aberrations.Spherical:0.####} / {aberrations.Coma:0.####} / {aberrations.Astigmatism:0.####} / {aberrations.Chromatic:0.####}"
        });
    }
}
