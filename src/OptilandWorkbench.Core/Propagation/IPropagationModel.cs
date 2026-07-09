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
        return ray with
        {
            Origin = ray.Origin + (ray.Direction * distance),
            OpticalPathDifference = ray.OpticalPathDifference + distance
        };
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
        var bentDirection = ray.Direction + new Backend.Vector3D(-ray.Origin.X * RadialGradient, -ray.Origin.Y * RadialGradient, 0);
        bentDirection = bentDirection / bentDirection.Length;
        return ray with
        {
            Origin = ray.Origin + (bentDirection * distance),
            Direction = bentDirection,
            OpticalPathDifference = ray.OpticalPathDifference + distance
        };
    }

    public IPropagationModel Clone() => new GrinPropagationModel(RadialGradient);
}
