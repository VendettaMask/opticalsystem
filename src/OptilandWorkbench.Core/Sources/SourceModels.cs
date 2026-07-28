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
        if (!double.IsFinite(coneAngleDegrees) || coneAngleDegrees is < 0 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coneAngleDegrees),
                "锥角必须是 0 到 90 度之间的有限值。");
        }

        Origin = origin;
        ConeAngleDegrees = coneAngleDegrees;
    }

    public string Kind => "point";

    public Vector3D Origin { get; }

    public double ConeAngleDegrees { get; }

    public RealRayBundle Generate(double wavelengthNanometers, int sampleCount)
    {
        const double GoldenAngle = Math.PI * (3 - 2.23606797749979);
        var count = Math.Max(1, sampleCount);
        var rays = new List<RealRay>(count);
        var maxAngle = ConeAngleDegrees * Math.PI / 180.0;
        var minimumCosine = Math.Cos(maxAngle);
        for (var index = 0; index < count; index++)
        {
            var fraction = count == 1 ? 0 : index / (double)(count - 1);
            var cosine = 1 - (fraction * (1 - minimumCosine));
            var sine = Math.Sqrt(Math.Max(0, 1 - (cosine * cosine)));
            var azimuth = index * GoldenAngle;
            rays.Add(new RealRay(
                Origin,
                new Vector3D(sine * Math.Cos(azimuth), sine * Math.Sin(azimuth), cosine),
                wavelengthNanometers));
        }

        return new RealRayBundle(rays);
    }
}

public sealed class SingleModeFiberSource : ISource
{
    public SingleModeFiberSource(double modeFieldDiameter, double numericalAperture)
    {
        if (!double.IsFinite(modeFieldDiameter) || modeFieldDiameter <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modeFieldDiameter),
                "模场直径必须是大于零的有限值。");
        }

        if (!double.IsFinite(numericalAperture) || numericalAperture is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericalAperture),
                "数值孔径必须是 0 到 1 之间的有限值。");
        }

        ModeFieldDiameter = modeFieldDiameter;
        NumericalAperture = numericalAperture;
    }

    public string Kind => "single_mode_fiber";

    public double ModeFieldDiameter { get; }

    public double NumericalAperture { get; }

    public RealRayBundle Generate(double wavelengthNanometers, int sampleCount)
    {
        var random = new Random(1234);
        var modeRadius = ModeFieldDiameter * 0.5;
        var maximumAngle = Math.Asin(NumericalAperture);
        var minimumCosine = Math.Cos(maximumAngle);
        var rays = Enumerable.Range(0, Math.Max(1, sampleCount)).Select(_ =>
        {
            var radius = modeRadius * Math.Sqrt(-0.5 * Math.Log(Math.Max(1e-12, random.NextDouble())));
            var positionAzimuth = random.NextDouble() * 2 * Math.PI;
            var cosine = 1 - (random.NextDouble() * (1 - minimumCosine));
            var sine = Math.Sqrt(Math.Max(0, 1 - (cosine * cosine)));
            var directionAzimuth = random.NextDouble() * 2 * Math.PI;
            return new RealRay(
                new Vector3D(
                    radius * Math.Cos(positionAzimuth),
                    radius * Math.Sin(positionAzimuth),
                    0),
                new Vector3D(
                    sine * Math.Cos(directionAzimuth),
                    sine * Math.Sin(directionAzimuth),
                    cosine),
                wavelengthNanometers);
        });

        return new RealRayBundle(rays);
    }
}
