using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Scattering;

namespace OptilandWorkbench.Core.Domain;

public sealed partial class OpticalSurface
{
    internal SurfaceRayTraceStateResult TraceRayState(
        RayState inputRay,
        IMaterial materialBefore,
        IMaterial materialAfter,
        double cumulativePathLength,
        double cumulativeOpticalPathLength)
    {
        var ray = inputRay.Normalize();
        var refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
        var refractiveIndexAfter = materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
        var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
        var distance = Geometry.DistanceToIntersection(localOrigin, localDirection);
        if (distance is null)
        {
            var stopped = ray with { Intensity = 0 };
            return new SurfaceRayTraceStateResult(
                stopped,
                new RayTraceSampleValue(
                    Number,
                    Label,
                    ray.Origin,
                    ray.Direction,
                    0,
                    true,
                    CumulativePathLength: cumulativePathLength,
                    CumulativeOpticalPathLength: cumulativeOpticalPathLength),
                refractiveIndexBefore,
                materialBefore,
                null,
                cumulativePathLength,
                cumulativeOpticalPathLength,
                true);
        }

        var segmentLength = Math.Max(0, distance.Value);
        var segmentOpticalPathLength = Math.Abs(segmentLength * refractiveIndexBefore);
        var nextCumulativePathLength = cumulativePathLength + segmentLength;
        var nextCumulativeOpticalPathLength = cumulativeOpticalPathLength + segmentOpticalPathLength;
        var extinctionCoefficient = materialBefore.ExtinctionCoefficient(ray.WavelengthNanometers);
        var wavelengthMicrometers = ray.WavelengthNanometers / 1000.0;
        var attenuation = extinctionCoefficient <= 0
            ? 1.0
            : Math.Exp((-4.0 * Math.PI * extinctionCoefficient * segmentLength * 1000.0) / wavelengthMicrometers);
        var propagated = Propagate(ray, materialBefore.PropagationModel, segmentLength) with
        {
            OpticalPathDifference = ray.OpticalPathDifference + segmentOpticalPathLength,
            Intensity = ray.Intensity * attenuation
        };
        var localHit = CoordinateSystem.ToLocalPoint(propagated.Origin);
        var vignetted = PhysicalAperture is not null && !PhysicalAperture.Contains(localHit);
        if (vignetted)
        {
            var stopped = propagated with { Intensity = 0 };
            return new SurfaceRayTraceStateResult(
                stopped,
                new RayTraceSampleValue(
                    Number,
                    Label,
                    propagated.Origin,
                    ray.Direction,
                    0,
                    true,
                    segmentLength,
                    segmentOpticalPathLength,
                    nextCumulativePathLength,
                    nextCumulativeOpticalPathLength),
                refractiveIndexBefore,
                materialBefore,
                null,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength,
                true);
        }

        var localNormal = Geometry.SurfaceNormal(localHit);
        var globalNormal = CoordinateSystem.ToGlobalDirection(localNormal);
        var reflective = IsReflective
            || InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: true }
            || InteractionModel is ThinLensInteractionModel { IsReflective: true }
            || InteractionModel is DiffractiveInteractionModel { IsReflective: true }
            || InteractionModel is PhaseInteractionModel { IsReflective: true };
        var context = new SurfaceInteractionStateContext(
            localNormal,
            refractiveIndexBefore,
            refractiveIndexAfter,
            ray.WavelengthNanometers,
            reflective,
            Geometry);
        var localRay = propagated with
        {
            Origin = localHit,
            Direction = CoordinateSystem.ToLocalDirection(propagated.Direction)
        };
        var interaction = Interact(localRay, context);
        var outgoingMaterial = interaction.Kind == RayInteractionKind.Transmitted
            ? materialAfter
            : materialBefore;
        var coated = ApplyCoating(
            interaction.Ray,
            context with { IsReflective = interaction.Kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection });
        var traced = coated with
        {
            Origin = CoordinateSystem.ToGlobalPoint(coated.Origin),
            Direction = CoordinateSystem.ToGlobalDirection(coated.Direction)
        };
        traced = ApplyScattering(traced, globalNormal);

        return new SurfaceRayTraceStateResult(
            traced,
            new RayTraceSampleValue(
                Number,
                Label,
                traced.Origin,
                traced.Direction,
                traced.Intensity,
                false,
                segmentLength,
                segmentOpticalPathLength,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength),
            outgoingMaterial.RefractiveIndex(ray.WavelengthNanometers),
            outgoingMaterial,
            interaction.Kind,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength,
            !traced.CanTrace);
    }

    private static RayState Propagate(RayState ray, IPropagationModel propagation, double distance)
    {
        if (propagation is HomogeneousPropagationModel)
        {
            return ray with { Origin = ray.Origin + (ray.Direction * distance) };
        }

        if (propagation is GrinPropagationModel grin)
        {
            var direction = ray.Direction + new Vector3D(
                -ray.Origin.X * grin.RadialGradient,
                -ray.Origin.Y * grin.RadialGradient,
                0);
            var length = direction.Length;
            direction = length <= 1e-12 ? new Vector3D(0, 0, 1) : direction / length;
            return ray with
            {
                Origin = ray.Origin + (direction * distance),
                Direction = direction,
                IsNormalized = true
            };
        }

        return RayState.FromRealRay(propagation.Propagate(ray.ToRealRay(), distance));
    }

    private RayStateInteractionResult Interact(RayState ray, SurfaceInteractionStateContext context)
    {
        if (InteractionModel is RefractiveReflectiveInteractionModel refractive)
        {
            var normal = context.SurfaceNormal;
            var incoming = ray.Direction;
            if (Dot(incoming, normal) > 0)
            {
                normal = -normal;
            }

            if (refractive.IsReflective || context.IsReflective)
            {
                return new RayStateInteractionResult(
                    ray with { Direction = Normalize(Reflect(incoming, normal)), IsNormalized = true },
                    RayInteractionKind.Reflected);
            }

            var eta = context.RefractiveIndexBefore / Math.Max(1e-9, context.RefractiveIndexAfter);
            var cosI = -Dot(normal, incoming);
            var sinT2 = eta * eta * (1 - (cosI * cosI));
            if (sinT2 > 1)
            {
                return new RayStateInteractionResult(
                    ray with { Direction = Normalize(Reflect(incoming, normal)), IsNormalized = true },
                    RayInteractionKind.TotalInternalReflection);
            }

            var cosT = Math.Sqrt(Math.Max(0, 1 - sinT2));
            var direction = (eta * incoming) + ((eta * cosI - cosT) * normal);
            return new RayStateInteractionResult(
                ray with { Direction = Normalize(direction), IsNormalized = true },
                RayInteractionKind.Transmitted);
        }

        if (InteractionModel is ThinLensInteractionModel thinLens)
        {
            var isReflective = thinLens.IsReflective || context.IsReflective;
            var indexAfter = isReflective
                ? -context.RefractiveIndexBefore
                : context.RefractiveIndexAfter;
            var inputSlopeX = ray.Direction.X / ray.Direction.Z;
            var inputSlopeY = ray.Direction.Y / ray.Direction.Z;
            var outputSlopeX = ((context.RefractiveIndexBefore * inputSlopeX) - (ray.Origin.X / thinLens.FocalLength))
                / indexAfter;
            var outputSlopeY = ((context.RefractiveIndexBefore * inputSlopeY) - (ray.Origin.Y / thinLens.FocalLength))
                / indexAfter;
            var opd = ray.OpticalPathDifference
                - (((ray.Origin.X * ray.Origin.X) + (ray.Origin.Y * ray.Origin.Y)) / (2 * thinLens.FocalLength));
            return new RayStateInteractionResult(
                ray with
                {
                    Direction = new Vector3D(outputSlopeX, outputSlopeY, 1),
                    OpticalPathDifference = opd,
                    IsNormalized = false
                },
                isReflective ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
        }

        var fallback = InteractionModel.Interact(ray.ToRealRay(), context.ToPublic());
        return new RayStateInteractionResult(RayState.FromRealRay(fallback.Ray), fallback.Kind);
    }

    private RayState ApplyCoating(RayState ray, SurfaceInteractionStateContext context)
    {
        return CoatingModel switch
        {
            NoneCoatingModel => ray,
            SimpleCoatingModel simple => ray with
            {
                Intensity = ray.Intensity * (context.IsReflective ? simple.Reflectance : simple.Transmittance)
            },
            ThinFilmStackCoating thinFilm => ray with
            {
                Intensity = ray.Intensity * thinFilm.EstimateTransmission(context.WavelengthNanometers)
            },
            _ => RayState.FromRealRay(CoatingModel.Apply(ray.ToRealRay(), context.ToPublic()))
        };
    }

    private RayState ApplyScattering(RayState ray, Vector3D normal)
    {
        if (ScatteringModel is null)
        {
            return ray;
        }

        if (ScatteringModel is LambertianScatteringModel lambertian)
        {
            return ray with { Intensity = ray.Intensity * (1 - lambertian.ScatterFraction) };
        }

        if (ScatteringModel is MeasuredBsdfScatteringModel measured)
        {
            var sum = 0.0;
            for (var index = 0; index < measured.Samples.Count; index++)
            {
                sum += measured.Samples[index].Value;
            }

            var loss = measured.Samples.Count == 0
                ? 0
                : Math.Clamp(sum / measured.Samples.Count, 0, 1);
            return ray with { Intensity = ray.Intensity * (1 - loss) };
        }

        return RayState.FromRealRay(ScatteringModel.Scatter(ray.ToRealRay(), normal));
    }

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Vector3D Reflect(Vector3D direction, Vector3D normal) =>
        direction - (2 * Dot(direction, normal) * normal);


    internal readonly record struct SurfaceInteractionStateContext(
        Vector3D SurfaceNormal,
        double RefractiveIndexBefore,
        double RefractiveIndexAfter,
        double WavelengthNanometers,
        bool IsReflective,
        Geometries.IGeometry? Geometry)
    {
        public SurfaceInteractionContext ToPublic() => new(
            SurfaceNormal,
            RefractiveIndexBefore,
            RefractiveIndexAfter,
            WavelengthNanometers,
            IsReflective,
            Geometry);
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-12 ? new Vector3D(0, 0, 1) : vector / length;
    }
}

internal readonly record struct RayStateInteractionResult(RayState Ray, RayInteractionKind Kind);

internal readonly record struct SurfaceRayTraceStateResult(
    RayState Ray,
    RayTraceSampleValue Sample,
    double OutgoingRefractiveIndex,
    IMaterial OutgoingMaterial,
    RayInteractionKind? InteractionKind,
    double CumulativePathLength,
    double CumulativeOpticalPathLength,
    bool StopTracing);
