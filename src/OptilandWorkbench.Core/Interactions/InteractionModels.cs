using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
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

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var normal = context.SurfaceNormal;
        var incoming = ray.Direction;
        if (VectorDot(incoming, normal) > 0)
        {
            normal = -normal;
        }

        if (IsReflective || context.IsReflective)
        {
            return new RealRayInteractionResult(
                ray with { Direction = Normalize(Reflect(incoming, normal)) },
                RayInteractionKind.Reflected);
        }

        var refraction = Refract(
            incoming,
            normal,
            context.RefractiveIndexBefore,
            context.RefractiveIndexAfter);
        return new RealRayInteractionResult(
            ray with { Direction = Normalize(refraction.Direction) },
            refraction.Kind);
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

    private static (Vector3D Direction, RayInteractionKind Kind) Refract(
        Vector3D direction,
        Vector3D normal,
        double n1,
        double n2)
    {
        var eta = n1 / Math.Max(1e-9, n2);
        var cosI = -VectorDot(normal, direction);
        var sinT2 = eta * eta * (1.0 - (cosI * cosI));
        if (sinT2 > 1.0)
        {
            return (
                Reflect(direction, normal),
                RayInteractionKind.TotalInternalReflection);
        }

        var cosT = Math.Sqrt(1.0 - sinT2);
        return (
            (eta * direction) + ((eta * cosI - cosT) * normal),
            RayInteractionKind.Transmitted);
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
    public ThinLensInteractionModel(double focalLength, bool isReflective = false)
    {
        FocalLength = focalLength;
        IsReflective = isReflective;
    }

    public string Kind => "thin_lens";

    public double FocalLength { get; set; }

    public bool IsReflective { get; }

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var reflective = IsReflective || context.IsReflective;
        var indexBefore = context.RefractiveIndexBefore;
        var indexAfter = reflective ? -indexBefore : context.RefractiveIndexAfter;
        var inputSlopeX = ray.Direction.X / ray.Direction.Z;
        var inputSlopeY = ray.Direction.Y / ray.Direction.Z;
        var outputSlopeX = ((indexBefore * inputSlopeX) - (ray.Origin.X / FocalLength)) / indexAfter;
        var outputSlopeY = ((indexBefore * inputSlopeY) - (ray.Origin.Y / FocalLength)) / indexAfter;
        var opd = ray.OpticalPathDifference
            - (((ray.Origin.X * ray.Origin.X) + (ray.Origin.Y * ray.Origin.Y)) / (2 * FocalLength));
        return new RealRayInteractionResult(
            ray with
            {
                Direction = new Vector3D(outputSlopeX, outputSlopeY, 1),
                OpticalPathDifference = opd,
                IsNormalized = false
            },
            reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        var reflective = IsReflective || context.IsReflective;
        return reflective
            ? ray with
            {
                Angle = (ray.Height / (FocalLength * context.RefractiveIndexBefore)) - ray.Angle
            }
            : ray with
            {
                Angle = ((context.RefractiveIndexBefore * ray.Angle) - (ray.Height / FocalLength))
                    / context.RefractiveIndexAfter
            };
    }

    public IInteractionModel Clone() => new ThinLensInteractionModel(FocalLength, IsReflective);
}

public sealed class DiffractiveInteractionModel : IInteractionModel
{
    public DiffractiveInteractionModel(bool isReflective = false)
    {
        IsReflective = isReflective;
    }

    public DiffractiveInteractionModel(double grooveFrequencyLinesPerMillimeter, int order = 1)
    {
        if (!double.IsFinite(grooveFrequencyLinesPerMillimeter) || grooveFrequencyLinesPerMillimeter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grooveFrequencyLinesPerMillimeter));
        }

        GrooveFrequencyLinesPerMillimeter = grooveFrequencyLinesPerMillimeter;
        Order = order;
    }

    public string Kind => "diffractive";

    public bool IsReflective { get; }

    public double? GrooveFrequencyLinesPerMillimeter { get; }

    public int? Order { get; }

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
    {
        var reflective = IsReflective || context.IsReflective;
        if (context.Geometry is not IGratingGeometry
            && GrooveFrequencyLinesPerMillimeter is double legacyFrequency)
        {
            var wavelengthMillimeters = context.WavelengthNanometers * 1e-6;
            var delta = (Order ?? 1) * wavelengthMillimeters * legacyFrequency;
            return new RealRayInteractionResult(
                ray with
                {
                    Direction = Normalize(new Vector3D(
                        ray.Direction.X + delta,
                        ray.Direction.Y,
                        ray.Direction.Z))
                },
                reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
        }

        var geometry = ResolveGeometry(context);
        var normal = context.SurfaceNormal;
        if (Dot(ray.Direction, normal) < 0)
        {
            normal = -normal;
        }

        var gratingVector = geometry.GratingVector(ray.Origin);
        var horizontalProjection = Math.Sqrt(
            (gratingVector.X * gratingVector.X) + (gratingVector.Y * gratingVector.Y));
        var period = geometry.GratingPeriodMicrometers / horizontalProjection;
        var wavelength = context.WavelengthNanometers / 1000.0;
        var scaledIncident = ray.Direction * (period * context.RefractiveIndexBefore);
        var gratingShift = gratingVector * (geometry.GratingOrder * wavelength);
        var combined = scaledIncident + gratingShift;
        var tangential = combined - (Dot(combined, normal) * normal);
        var refractiveIndexAfter = context.RefractiveIndexAfter;
        var radicand = (period * period * refractiveIndexAfter * refractiveIndexAfter)
            - Dot(tangential, tangential);
        if (radicand < 0 || !double.IsFinite(radicand))
        {
            return new RealRayInteractionResult(
                ray with { Direction = new Vector3D(double.NaN, double.NaN, double.NaN) },
                reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
        }

        var signedIndexAfter = reflective ? -refractiveIndexAfter : refractiveIndexAfter;
        var normalTerm = normal * (reflective ? -Math.Sqrt(radicand) : Math.Sqrt(radicand));
        var direction = (tangential + normalTerm) / (period * signedIndexAfter);
        return new RealRayInteractionResult(
            ray with { Direction = Normalize(direction) },
            reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
    }

    public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context)
    {
        if (context.Geometry is not IGratingGeometry
            && GrooveFrequencyLinesPerMillimeter is double legacyFrequency)
        {
            var wavelengthMillimeters = context.WavelengthNanometers * 1e-6;
            return ray with
            {
                Angle = ray.Angle + ((Order ?? 1) * wavelengthMillimeters * legacyFrequency)
            };
        }

        var geometry = ResolveGeometry(context);
        var wavelength = context.WavelengthNanometers / 1000.0;
        var radius = geometry.ParaxialRadius;
        var reflective = IsReflective || context.IsReflective;
        if (reflective)
        {
            return ray with
            {
                Angle = -ray.Angle
                    - (2 * context.RefractiveIndexBefore * ray.Height / radius)
                    + (geometry.GratingOrder * wavelength / geometry.GratingPeriodMicrometers)
            };
        }

        var power = (context.RefractiveIndexAfter - context.RefractiveIndexBefore) / radius;
        return ray with
        {
            Angle = (context.RefractiveIndexBefore / context.RefractiveIndexAfter * ray.Angle)
                - (ray.Height * power / context.RefractiveIndexAfter)
                - (geometry.GratingOrder * wavelength
                    / (geometry.GratingPeriodMicrometers * context.RefractiveIndexAfter))
        };
    }

    public IInteractionModel Clone() => GrooveFrequencyLinesPerMillimeter is double frequency
        ? new DiffractiveInteractionModel(frequency, Order ?? 1)
        : new DiffractiveInteractionModel(IsReflective);

    private IGratingGeometry ResolveGeometry(SurfaceInteractionContext context)
    {
        if (context.Geometry is IGratingGeometry grating)
        {
            return grating;
        }

        throw new InvalidOperationException("DiffractiveInteractionModel requires grating geometry.");
    }

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-15 ? new Vector3D(double.NaN, double.NaN, double.NaN) : vector / length;
    }
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

    public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
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
        return new RealRayInteractionResult(
            ray with
            {
                Direction = outgoingLength <= 1e-12 ? ray.Direction : outgoingWaveVector / outgoingLength,
                Intensity = intensity * Profile.Efficiency,
                OpticalPathDifference = ray.OpticalPathDifference - (phase / waveNumber)
            },
            reflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
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
