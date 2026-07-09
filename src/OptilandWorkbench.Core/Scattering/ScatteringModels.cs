using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Scattering;

public interface IScatteringModel
{
    string Kind { get; }

    RealRay Scatter(RealRay ray, Vector3D surfaceNormal);

    IScatteringModel Clone();
}

public sealed class LambertianScatteringModel : IScatteringModel
{
    public LambertianScatteringModel(double scatterFraction)
    {
        ScatterFraction = Math.Clamp(scatterFraction, 0, 1);
    }

    public string Kind => "lambertian";

    public double ScatterFraction { get; }

    public RealRay Scatter(RealRay ray, Vector3D surfaceNormal)
    {
        return ray with { Intensity = ray.Intensity * (1.0 - ScatterFraction) };
    }

    public IScatteringModel Clone() => new LambertianScatteringModel(ScatterFraction);
}

public sealed class MeasuredBsdfScatteringModel : IScatteringModel
{
    public MeasuredBsdfScatteringModel(IReadOnlyList<(double AngleDegrees, double Value)> samples)
    {
        Samples = samples.ToArray();
    }

    public string Kind => "measured_bsdf";

    public IReadOnlyList<(double AngleDegrees, double Value)> Samples { get; }

    public RealRay Scatter(RealRay ray, Vector3D surfaceNormal)
    {
        var loss = Samples.Count == 0 ? 0.0 : Math.Clamp(Samples.Average(sample => sample.Value), 0, 1);
        return ray with { Intensity = ray.Intensity * (1.0 - loss) };
    }

    public IScatteringModel Clone() => new MeasuredBsdfScatteringModel(Samples);
}
