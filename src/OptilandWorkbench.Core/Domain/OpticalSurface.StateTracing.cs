using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Services;
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
        bool ignorePhysicalAperture = false,
        bool stopBeforeInteraction = false,
        bool bypassCoating = false,
        CoordinateSystem? materialBeforeCoordinates = null)
    {
        OpticCapabilityPreflight.EnsureSurfaceSupported(this, OpticCapabilityOperation.RayTrace);
        var ray = inputRay.Normalize();
        if (materialBefore is GradientIndexMaterial unlocated)
        {
            if (materialBeforeCoordinates is null)
                throw new NotSupportedException("入射 GRIN 介质需要明确的体坐标系；不能把出口面坐标当作入口坐标。");
            materialBefore = unlocated.At(materialBeforeCoordinates);
        }
        if (ray.PolarizationMatrix is not null
            && (materialBefore is LocatedGradientIndexMaterial || materialAfter is GradientIndexMaterial))
            throw new NotSupportedException("GRIN 连续偏振基矢运输尚未接入，不能保留未旋转的偏振矩阵冒充结果。");
        double segmentLength;
        double segmentOpticalPathLength;
        double refractiveIndexBefore;
        Vector3D localHit;
        RayState propagated;
        if (materialBefore is LocatedGradientIndexMaterial located)
        {
            var material = located.Material;
            var result = GradientIndexRayIntegrator.TraceToSurface(material.Profile, located.Coordinates,
                ray.Origin, ray.Direction, ray.WavelengthNanometers, Geometry, CoordinateSystem,
                material.MaximumPathLength, material.IntegrationOptions, ComputationCancellation.Current);
            if (result.Termination != GradientIndexTermination.SurfaceReached)
                return Miss(result.End.RefractiveIndex);
            segmentLength = result.End.PathLength;
            segmentOpticalPathLength = result.End.OpticalPathLength;
            refractiveIndexBefore = result.End.RefractiveIndex;
            localHit = CoordinateSystem.ToLocalDirection((located.Coordinates.Origin - CoordinateSystem.Origin)
                + located.Coordinates.ToGlobalDirection(result.ProfileLocalPosition));
            propagated = ray with { Origin = result.End.Position, Direction = result.End.Direction, IsNormalized = true };
        }
        else
        {
            refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
            var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
            var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
            var intersection = Geometry.DistanceToIntersection(localOrigin, localDirection);
            if (!intersection.IsHit) return Miss(refractiveIndexBefore);
            segmentLength = Math.Max(0, intersection.Distance);
            segmentOpticalPathLength = Math.Abs(segmentLength * refractiveIndexBefore);
            propagated = Propagate(ray, materialBefore.PropagationModel, segmentLength);
            localHit = CoordinateSystem.ToLocalPoint(propagated.Origin);
        }
        var refractiveIndexAfter = materialAfter is GradientIndexMaterial afterGradient
            ? afterGradient.RefractiveIndex(localHit, ray.WavelengthNanometers)
            : materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var nextCumulativePathLength = cumulativePathLength + segmentLength;
        var nextCumulativeOpticalPathLength = cumulativeOpticalPathLength + segmentOpticalPathLength;
        var extinctionCoefficient = materialBefore.ExtinctionCoefficient(ray.WavelengthNanometers);
        var wavelengthMicrometers = ray.WavelengthNanometers / 1000.0;
        var attenuation = extinctionCoefficient <= 0
            ? 1.0
            : Math.Exp((-4.0 * Math.PI * extinctionCoefficient * segmentLength * 1000.0) / wavelengthMicrometers);
        propagated = propagated with
        {
            OpticalPathDifference = ray.OpticalPathDifference + segmentOpticalPathLength,
            Intensity = ray.Intensity * attenuation
        };

        SurfaceRayTraceStateResult Miss(double index) => new(ray with { Intensity = 0 },
            new RayTraceSampleValue(Number, Label, ray.Origin, ray.Direction, 0, true,
                CumulativePathLength: cumulativePathLength, CumulativeOpticalPathLength: cumulativeOpticalPathLength),
            index, materialBefore, null, cumulativePathLength, cumulativeOpticalPathLength, true);
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
                    propagated.Direction,
                    0,
                    true,
                    segmentLength,
                    segmentOpticalPathLength,
                    nextCumulativePathLength,
                    nextCumulativeOpticalPathLength,
                    RefractiveIndexBefore: refractiveIndexBefore,
                    RefractiveIndexAfter: refractiveIndexAfter),
                refractiveIndexBefore,
                materialBefore,
                null,
                nextCumulativePathLength,
                nextCumulativeOpticalPathLength,
                true);
        }

        if (stopBeforeInteraction)
        {
            // Detector irradiance is incident power: propagate and check the
            // physical aperture, but do not apply the detector's own interaction.
            return new SurfaceRayTraceStateResult(
                propagated,
                new RayTraceSampleValue(Number, Label, propagated.Origin, propagated.Direction,
                    propagated.Intensity, false, segmentLength, segmentOpticalPathLength,
                    nextCumulativePathLength, nextCumulativeOpticalPathLength,
                    IncidentDirection: propagated.Direction,
                    PhaseInclusiveOpticalPathLength: propagated.OpticalPathDifference,
                    RefractiveIndexBefore: refractiveIndexBefore),
                refractiveIndexBefore, materialBefore, null,
                nextCumulativePathLength, nextCumulativeOpticalPathLength, !propagated.CanTrace);
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
            Geometry,
            CoordinateSystem.ToLocalDirection(propagated.Direction),
            extinctionCoefficient,
            materialAfter.ExtinctionCoefficient(ray.WavelengthNanometers));
        var localRay = propagated with
        {
            Origin = localHit,
            Direction = CoordinateSystem.ToLocalDirection(propagated.Direction)
        };
        var interaction = Interact(localRay, context);
        var outgoingMaterial = interaction.Kind == RayInteractionKind.Transmitted
            ? materialAfter is GradientIndexMaterial gradient ? gradient.At(CoordinateSystem) : materialAfter
            : materialBefore;
        var coated = bypassCoating ? interaction.Ray : ApplyCoating(interaction.Ray,
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
                InteractionKind: interaction.Kind,
                IncidentDirection: propagated.Direction,
                PhaseInclusiveOpticalPathLength: traced.OpticalPathDifference,
                RefractiveIndexBefore: refractiveIndexBefore,
                RefractiveIndexAfter: refractiveIndexAfter),
            interaction.Kind == RayInteractionKind.Transmitted ? refractiveIndexAfter : refractiveIndexBefore,
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
        Geometries.IGeometry? Geometry,
        Vector3D IncidentDirection,
        double ExtinctionCoefficientBefore,
        double ExtinctionCoefficientAfter)
    {
        public SurfaceInteractionContext ToPublic() => new(
            SurfaceNormal,
            RefractiveIndexBefore,
            RefractiveIndexAfter,
            WavelengthNanometers,
            IsReflective,
            Geometry,
            IncidentDirection,
            ExtinctionCoefficientBefore,
            ExtinctionCoefficientAfter);
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
