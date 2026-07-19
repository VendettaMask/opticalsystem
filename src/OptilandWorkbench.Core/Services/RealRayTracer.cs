using OptilandWorkbench.Core.Domain;

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
        var aperture = _optic.SurfaceGroup.ApertureRadius();
        var rayCount = Math.Max(3, raysPerField | 1);
        var raySamples = Enumerable.Range(0, rayCount)
            .Select(index => -1.0 + (2.0 * index / (rayCount - 1)))
            .ToArray();

        foreach (var field in _optic.Fields)
        {
            foreach (var wavelength in _optic.Wavelengths.Where(item => item.Weight > 0))
            {
                foreach (var sample in raySamples)
                {
                    paths.Add(TraceSingleRay(field, wavelength, sample * aperture * 0.85));
                }
            }
        }

        return new RayTraceResult(paths);
    }

    private RayPath TraceSingleRay(FieldPoint field, Wavelength wavelength, double initialHeight)
    {
        var segments = new List<RaySegment>();
        var z = 0.0;
        var y = initialHeight;
        var slope = Math.Tan(DegreesToRadians(field.YAngleDegrees)) * 0.08;
        var currentIndex = 1.0;
        var vignetted = false;

        foreach (var surface in _optic.SurfaceGroup.Items)
        {
            var nextZ = z + surface.Thickness;
            var nextY = y + (slope * surface.Thickness);
            segments.Add(new RaySegment(new RayPoint(z, y), new RayPoint(nextZ, nextY)));

            if (surface.SemiDiameter > 0 && Math.Abs(nextY) > surface.SemiDiameter)
            {
                vignetted = true;
            }

            if (!surface.IsPlane)
            {
                var nextIndex = _optic.Materials
                    .Resolve(surface.MaterialAfterName)
                    .RefractiveIndex(wavelength.Nanometers);
                var refractiveDelta = nextIndex - currentIndex;
                var curvatureKick = nextY / surface.Radius * refractiveDelta;
                slope -= curvatureKick;
                currentIndex = nextIndex;
            }

            z = nextZ;
            y = nextY;
        }

        return new RayPath(field.Clone(), wavelength.Clone(), segments, vignetted);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
