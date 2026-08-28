using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Domain;

public sealed partial class OpticalSurface
{
    internal SurfaceRayTraceStateResult TraceRayState(
        RayState inputRay,
        IMaterial materialBefore,
        IMaterial materialAfter,
        double cumulativePathLength,
        double cumulativeOpticalPathLength,
        bool ignorePhysicalAperture = false)
    {
        OpticCapabilityPreflight.EnsureSurfaceSupported(this, OpticCapabilityOperation.RayTrace);
        var ray = inputRay.Normalize();
        var refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
        var refractiveIndexAfter = materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
        var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
        var intersection = Geometry.DistanceToIntersection(localOrigin, localDirection);
        if (!intersection.IsHit)
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

        var segmentLength = Math.Max(0, intersection.Distance);
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
        var vignetted = !ignorePhysicalAperture
            && PhysicalAperture is not null
            && !PhysicalAperture.Contains(localHit);
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
        var reflective = IsReflective;
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
                nextCumulativeOpticalPathLength,
                InteractionKind: interaction.Kind),
            outgoingMaterial.RefractiveIndex(ray.WavelengthNanometers),
            outgoingMaterial,
            interaction.Kind,
            nextCumulativePathLength,
            nextCumulativeOpticalPathLength,
            !traced.CanTrace);
    }

    private static RayState Propagate(RayState ray, IPropagationModel propagation, double distance)
    {
        return RayState.FromRealRay(propagation.Propagate(ray.ToRealRay(), distance));
    }

    private RayStateInteractionResult Interact(RayState ray, SurfaceInteractionStateContext context)
    {
        var result = InteractionModel.Interact(ray.ToRealRay(), context.ToPublic());
        return new RayStateInteractionResult(RayState.FromRealRay(result.Ray), result.Kind);
    }

    private RayState ApplyCoating(RayState ray, SurfaceInteractionStateContext context)
    {
        return RayState.FromRealRay(CoatingModel.Apply(ray.ToRealRay(), context.ToPublic()));
    }

    private RayState ApplyScattering(RayState ray, Vector3D normal)
    {
        return ScatteringModel is null
            ? ray
            : RayState.FromRealRay(ScatteringModel.Scatter(ray.ToRealRay(), normal));
    }


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
