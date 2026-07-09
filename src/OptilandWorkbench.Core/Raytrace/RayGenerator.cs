using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Raytrace;

public sealed class RayGenerationSettings
{
    public int SamplesPerField { get; set; } = 9;

    public PupilSampling Sampling { get; set; } = PupilSampling.Hexapolar;

    public bool Telecentric { get; set; }

    public double ApodizationPower { get; set; }
}

public sealed class RayGenerator
{
    private readonly Optic _optic;

    public RayGenerator(Optic optic)
    {
        _optic = optic;
    }

    public RayGenerationSettings Settings { get; } = new();

    public RealRayBundle Generate()
    {
        var apertureRadius = _optic.SurfaceGroup.ApertureRadius();
        var samples = ApertureSampler.Generate(Settings.SamplesPerField, Settings.Sampling);
        var rays = new List<RealRay>();

        foreach (var field in _optic.Fields)
        {
            foreach (var wavelength in _optic.Wavelengths)
            {
                var fieldAngle = DegreesToRadians(field.YAngleDegrees);
                foreach (var sample in samples)
                {
                    var origin = new Vector3D(sample.X * apertureRadius, sample.Y * apertureRadius, 0);
                    var direction = Settings.Telecentric
                        ? new Vector3D(0, 0, 1)
                        : Normalize(new Vector3D(0, Math.Sin(fieldAngle), Math.Cos(fieldAngle)));
                    var radius = Math.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y));
                    var apodization = Settings.ApodizationPower <= 0
                        ? 1.0
                        : Math.Pow(Math.Max(0, 1 - (radius * radius)), Settings.ApodizationPower);

                    rays.Add(new RealRay(origin, direction, wavelength.Nanometers, wavelength.Weight * field.Weight * apodization));
                }
            }
        }

        return new RealRayBundle(rays);
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
