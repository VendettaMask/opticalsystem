using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Propagation;

public interface IPropagationModel
{
    string Kind { get; }

    RealRay Propagate(RealRay ray, double distance);

    IPropagationModel Clone();
}

public sealed class HomogeneousPropagationModel : IPropagationModel
{
    public string Kind => "homogeneous";

    public RealRay Propagate(RealRay ray, double distance)
    {
        var propagated = ray with
        {
            Origin = ray.Origin + (ray.Direction * distance)
        };
        return propagated.IsNormalized ? propagated : propagated.Normalize();
    }

    public IPropagationModel Clone() => new HomogeneousPropagationModel();
}

public sealed class GrinPropagationModel : IPropagationModel
{
    public GrinPropagationModel(double radialGradient)
    {
        RadialGradient = radialGradient;
    }

    public string Kind => "grin";

    public double RadialGradient { get; }

    public RealRay Propagate(RealRay ray, double distance)
    {
        var normalizedRay = ray.IsNormalized ? ray : ray.Normalize();
        var bentDirection = normalizedRay.Direction + new Backend.Vector3D(
            -normalizedRay.Origin.X * RadialGradient,
            -normalizedRay.Origin.Y * RadialGradient,
            0);
        bentDirection = bentDirection / bentDirection.Length;
        return normalizedRay with
        {
            Origin = normalizedRay.Origin + (bentDirection * distance),
            Direction = bentDirection,
            IsNormalized = true
        };
    }

    public IPropagationModel Clone() => new GrinPropagationModel(RadialGradient);
}
