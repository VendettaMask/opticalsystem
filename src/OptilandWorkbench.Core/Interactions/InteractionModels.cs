using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Interactions;

public sealed class RefractiveReflectiveInteractionModel : IInteractionModel
{
    public RefractiveReflectiveInteractionModel(bool isReflective = false)
    {
        IsReflective = isReflective;
    }

    public string Kind => IsReflective ? "reflective" : "refractive";

    public bool IsReflective { get; set; }

    public RealRay Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var normal = context.SurfaceNormal;
        var incoming = ray.Direction;
        if (VectorDot(incoming, normal) > 0)
        {
            normal = -normal;
        }

        var outgoing = IsReflective || context.IsReflective
            ? Reflect(incoming, normal)
            : Refract(incoming, normal, context.RefractiveIndexBefore, context.RefractiveIndexAfter);

        return ray with { Direction = Normalize(outgoing) };
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        var indexRatio = context.RefractiveIndexBefore / Math.Max(1e-9, context.RefractiveIndexAfter);
        return ray with { Angle = ray.Angle * indexRatio };
    }

    public IInteractionModel Clone() => new RefractiveReflectiveInteractionModel(IsReflective);

    private static Vector3D Reflect(Vector3D direction, Vector3D normal)
    {
        return direction - (2 * VectorDot(direction, normal) * normal);
    }

    private static Vector3D Refract(Vector3D direction, Vector3D normal, double n1, double n2)
    {
        var eta = n1 / Math.Max(1e-9, n2);
        var cosI = -VectorDot(normal, direction);
        var sinT2 = eta * eta * (1.0 - (cosI * cosI));
        if (sinT2 > 1.0)
        {
            return Reflect(direction, normal);
        }

        var cosT = Math.Sqrt(1.0 - sinT2);
        return (eta * direction) + ((eta * cosI - cosT) * normal);
    }

    private static double VectorDot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }
}

public sealed class ThinLensInteractionModel : IInteractionModel
{
    public ThinLensInteractionModel(double focalLength)
    {
        FocalLength = focalLength;
    }

    public string Kind => "thin_lens";

    public double FocalLength { get; set; }

    public RealRay Interact(RealRay ray, SurfaceInteractionContext context)
    {
        if (Math.Abs(FocalLength) < 1e-12)
        {
            return ray;
        }

        var target = new Vector3D(0, 0, FocalLength);
        return ray with { Direction = Normalize(target - ray.Origin) };
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        return Math.Abs(FocalLength) < 1e-12
            ? ray
            : ray with { Angle = ray.Angle - (ray.Height / FocalLength) };
    }

    public IInteractionModel Clone() => new ThinLensInteractionModel(FocalLength);

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }
}

public sealed class DiffractiveInteractionModel : IInteractionModel
{
    public DiffractiveInteractionModel(double grooveFrequencyLinesPerMillimeter, int order = 1)
    {
        GrooveFrequencyLinesPerMillimeter = grooveFrequencyLinesPerMillimeter;
        Order = order;
    }

    public string Kind => "diffractive";

    public double GrooveFrequencyLinesPerMillimeter { get; }

    public int Order { get; }

    public RealRay Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var wavelengthMillimeters = context.WavelengthNanometers * 1e-6;
        var delta = Order * wavelengthMillimeters * GrooveFrequencyLinesPerMillimeter;
        var direction = new Vector3D(ray.Direction.X + delta, ray.Direction.Y, ray.Direction.Z);
        return ray with { Direction = direction / direction.Length };
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        var wavelengthMillimeters = context.WavelengthNanometers * 1e-6;
        return ray with { Angle = ray.Angle + (Order * wavelengthMillimeters * GrooveFrequencyLinesPerMillimeter) };
    }

    public IInteractionModel Clone() => new DiffractiveInteractionModel(GrooveFrequencyLinesPerMillimeter, Order);
}

public sealed class PhaseInteractionModel : IInteractionModel
{
    public PhaseInteractionModel(Func<double, double, (double Dx, double Dy)> gradient)
    {
        Gradient = gradient;
    }

    public string Kind => "phase";

    public Func<double, double, (double Dx, double Dy)> Gradient { get; }

    public RealRay Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var gradient = Gradient(ray.Origin.X, ray.Origin.Y);
        var direction = new Vector3D(ray.Direction.X + gradient.Dx, ray.Direction.Y + gradient.Dy, ray.Direction.Z);
        return ray with { Direction = direction / direction.Length };
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        return ray;
    }

    public IInteractionModel Clone() => new PhaseInteractionModel(Gradient);
}
