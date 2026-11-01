using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Interactions;

public sealed record BirefringentInterfaceData(OpticalIndexEllipsoid Before, OpticalIndexEllipsoid After);

/// <summary>Conserves tangential wave momentum and selects the declared energy branch.</summary>
public sealed class BirefringentInteractionModel(bool isExit = false, double axisDisplayLength = 0) : IInteractionModel
{
    public string Kind => "birefringent_interface";
    public bool IsExit { get; } = isExit;
    public double AxisDisplayLength { get; } = double.IsFinite(axisDisplayLength) && axisDisplayLength >= 0 ? axisDisplayLength
        : throw new ArgumentOutOfRangeException(nameof(axisDisplayLength));
    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var data = context.Birefringent ?? throw new NotSupportedException("Crystal tracing requires located incoming and outgoing index ellipsoids.");
        if (context.IsReflective) throw new NotSupportedException("Reflective crystal entry/exit has not been defined.");
        if (ray.PolarizationMatrix is not null) throw new NotSupportedException("Coherent crystal polarization transport is not implemented.");
        var normal = context.SurfaceNormal / context.SurfaceNormal.Length;
        var incident = ray.Normalize().Direction;
        var momentum = data.Before.Momentum(incident);
        var tangential = momentum - OpticalIndexEllipsoid.Dot(momentum, normal) * normal;
        var sign = Math.CopySign(1, OpticalIndexEllipsoid.Dot(incident, normal));
        var outgoing = Solve(data.After, tangential, normal, sign);
        var kind = RayInteractionKind.Transmitted;
        if (outgoing is null)
        {
            outgoing = Solve(data.Before, tangential, normal, -sign);
            kind = RayInteractionKind.TotalInternalReflection;
        }
        if (outgoing is null) throw new ArithmeticException("The crystal interface has no real energy branch.");
        return new(ray with { Direction = outgoing.Value, IsNormalized = true }, kind);
    }
    private static Vector3D? Solve(OpticalIndexEllipsoid medium, Vector3D tangential, Vector3D normal, double sign)
    {
        var an = medium.Apply(normal); var at = medium.Apply(tangential);
        var a = OpticalIndexEllipsoid.Dot(normal, an); var b = OpticalIndexEllipsoid.Dot(normal, at);
        var c = OpticalIndexEllipsoid.Dot(tangential, at) - 1; var discriminant = b * b - a * c;
        if (!double.IsFinite(discriminant) || a <= 0) throw new ArithmeticException("Invalid crystal interface dispersion.");
        if (discriminant < 0) return null;
        var q = (-b + sign * Math.Sqrt(discriminant)) / a;
        var direction = medium.Apply(tangential + q * normal); return direction / direction.Length;
    }
    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) =>
        new RefractiveReflectiveInteractionModel().Interact(ray, context);
    public IInteractionModel Clone() => new BirefringentInteractionModel(IsExit, AxisDisplayLength);
}
