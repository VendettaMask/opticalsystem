using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Sources;

public interface ISource
{
    string Kind { get; }

    RealRayBundle Generate(double wavelengthNanometers, int sampleCount);
}

public sealed class PointSource : ISource
{
    public PointSource(Vector3D origin, double coneAngleDegrees)
    {
        Origin = origin;
        ConeAngleDegrees = coneAngleDegrees;
    }

    public string Kind => "point";

    public Vector3D Origin { get; }

    public double ConeAngleDegrees { get; }

    public RealRayBundle Generate(double wavelengthNanometers, int sampleCount)
    {
        var rays = new List<RealRay>();
        var maxAngle = ConeAngleDegrees * Math.PI / 180.0;
        for (var index = 0; index < Math.Max(1, sampleCount); index++)
        {
            var angle = maxAngle * (index / (double)Math.Max(1, sampleCount - 1));
            rays.Add(new RealRay(Origin, new Vector3D(0, Math.Sin(angle), Math.Cos(angle)), wavelengthNanometers));
        }

        return new RealRayBundle(rays);
    }
}

public sealed class SingleModeFiberSource : ISource
{
    public SingleModeFiberSource(double modeFieldDiameter, double numericalAperture)
    {
        ModeFieldDiameter = modeFieldDiameter;
        NumericalAperture = numericalAperture;
    }

    public string Kind => "single_mode_fiber";

    public double ModeFieldDiameter { get; }

    public double NumericalAperture { get; }

    public RealRayBundle Generate(double wavelengthNanometers, int sampleCount)
    {
        var random = new Random(1234);
        var rays = Enumerable.Range(0, Math.Max(1, sampleCount)).Select(_ =>
        {
            var radius = ModeFieldDiameter * 0.5 * Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, random.NextDouble())));
            var theta = random.NextDouble() * 2 * Math.PI;
            var angle = NumericalAperture * random.NextDouble();
            return new RealRay(
                new Vector3D(radius * Math.Cos(theta), radius * Math.Sin(theta), 0),
                new Vector3D(0, Math.Sin(angle), Math.Cos(angle)),
                wavelengthNanometers);
        });

        return new RealRayBundle(rays);
    }
}
