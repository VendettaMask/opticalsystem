using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Phase;
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
    public PhaseInteractionModel(IPhaseProfile profile, bool isReflective = false)
    {
        Profile = profile;
        IsReflective = isReflective;
    }

    public string Kind => "phase";

    public IPhaseProfile Profile { get; }

    public bool IsReflective { get; }

    public RealRay Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var normal = context.SurfaceNormal;
        var reflective = IsReflective || context.IsReflective;
        var wavelengthMicrometers = context.WavelengthNanometers / 1000.0;
        var waveNumber = 2 * Math.PI / wavelengthMicrometers;
        var refractiveIndexAfter = reflective ? context.RefractiveIndexBefore : context.RefractiveIndexAfter;
        var incidentWaveVector = ray.Direction * (context.RefractiveIndexBefore * waveNumber);
        var gradient = Profile.Gradient(ray.Origin.X, ray.Origin.Y, context.WavelengthNanometers);
        var ambientGradient = new Vector3D(gradient.Dx, gradient.Dy, 0);
        var surfaceGradient = ambientGradient - (Dot(ambientGradient, normal) * normal);
        var incidentTangential = incidentWaveVector - (Dot(incidentWaveVector, normal) * normal);
        var outgoingTangential = incidentTangential + surfaceGradient;
        var normalMagnitudeSquared = (refractiveIndexAfter * waveNumber * refractiveIndexAfter * waveNumber)
            - Dot(outgoingTangential, outgoingTangential);
        var intensity = ray.Intensity;
        if (normalMagnitudeSquared < 0)
        {
            intensity = 0;
        }

        var normalMagnitude = Math.Sqrt(Math.Max(0, normalMagnitudeSquared));
        var outgoingWaveVector = outgoingTangential
            + ((reflective ? -normalMagnitude : normalMagnitude) * normal);
        var outgoingLength = outgoingWaveVector.Length;
        var phase = Profile.Phase(ray.Origin.X, ray.Origin.Y, context.WavelengthNanometers);
        return ray with
        {
            Direction = outgoingLength <= 1e-12 ? ray.Direction : outgoingWaveVector / outgoingLength,
            Intensity = intensity * Profile.Efficiency,
            OpticalPathDifference = ray.OpticalPathDifference - (phase / waveNumber)
        };
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        var wavelengthMicrometers = context.WavelengthNanometers / 1000.0;
        var waveNumber = 2 * Math.PI / wavelengthMicrometers;
        var gradientDeflection = Profile.ParaxialGradient(
            ray.Height,
            context.WavelengthNanometers) / waveNumber;
        return IsReflective || context.IsReflective
            ? ray with { Angle = ray.Angle + (gradientDeflection / context.RefractiveIndexBefore) }
            : ray with
            {
                Angle = (context.RefractiveIndexBefore / context.RefractiveIndexAfter * ray.Angle)
                    - (gradientDeflection / context.RefractiveIndexAfter)
            };
    }

    public IInteractionModel Clone() => new PhaseInteractionModel(Profile.Clone(), IsReflective);

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}
