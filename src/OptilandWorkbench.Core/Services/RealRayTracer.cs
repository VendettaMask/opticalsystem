using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public sealed class RealRayTracer
{
    private readonly Optic _optic;

    public RealRayTracer(Optic optic)
    {
        _optic = optic;
    }

    public RayTraceResult TraceMeridionalRays(int raysPerField = 7)
    {
        var surfaces = _optic.SurfaceGroup.Items;
        if (surfaces.Count == 0 || _optic.Fields.Count == 0 || _optic.Wavelengths.Count == 0)
        {
            return new RayTraceResult(Array.Empty<RayPath>());
        }

        var paths = new List<RayPath>();
        var rayCount = Math.Max(3, raysPerField | 1);
        var raySamples = Enumerable.Range(0, rayCount)
            .Select(index => -1.0 + (2.0 * index / (rayCount - 1)))
            .ToArray();

        foreach (var field in _optic.Fields)
        {
            var normalizedField = FieldCoordinates.Normalize(_optic.Fields, field.X, field.Y);
            foreach (var wavelength in _optic.Wavelengths.Where(item => item.Weight > 0))
            {
                var bundle = _optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    normalizedField.X,
                    normalizedField.Y,
                    wavelength.Micrometers,
                    raySamples.Select(sample => new PupilSample(0, sample * 0.85, 1)));
                var trace = _optic.SequentialRayTracer.Trace(bundle);
                for (var rayIndex = 0; rayIndex < bundle.Rays.Count; rayIndex++)
                {
                    var ray = bundle.Rays[rayIndex];
                    var history = trace.RayHistories[rayIndex];
                    var points = new[] { ray.Origin }
                        .Concat(history.Select(sample => sample.Position))
                        .ToArray();
                    var segments = points.Zip(points.Skip(1), (start, end) => new RaySegment(
                        new RayPoint(start.Z, start.Y),
                        new RayPoint(end.Z, end.Y))).ToArray();
                    paths.Add(new RayPath(
                        field.Clone(),
                        wavelength.Clone(),
                        segments,
                        history.Any(sample => sample.Vignetted || sample.Intensity <= 0)));
                }
            }
        }

        return new RayTraceResult(paths);
    }
}
