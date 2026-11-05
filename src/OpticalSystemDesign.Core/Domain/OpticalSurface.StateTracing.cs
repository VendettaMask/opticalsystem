using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Domain;

public sealed partial class OpticalSurface
{
    private static readonly PlaneGeometry GridReferencePlane = new();

    // A coplanar Grid Sag display surface in unchanged air can report a
    // vertex-plane sample outside its measured patch. The sag geometry itself
    // stays bounded, including when used for a refracting or reflecting face.
    private bool IsNeutralGridReference(GridSagGeometry grid, IMaterial incoming, IMaterial outgoing) =>
        !IsStop && !IsReflective && Thickness == 0 && PhysicalAperture is null && ScatteringModel is null
        && CoatingModel is OpticalSystemDesign.Core.Coatings.NoneCoatingModel
        && InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: false }
        && (grid.Base.Base.Radius == 0 || double.IsInfinity(grid.Base.Base.Radius))
        && grid.Base.Coefficients.All(c => c == 0)
        && incoming is AirMaterial before && outgoing is AirMaterial after
        && before.PressureAtmospheres == after.PressureAtmospheres;

    internal Vector3D SequentialTraceNormal(Vector3D localPoint) =>
        Geometry is GridSagGeometry grid && !grid.ContainsGridPoint(localPoint)
            && IsNeutralGridReference(grid, MaterialBefore, MaterialAfter)
        ? GridReferencePlane.SurfaceNormal(localPoint)
        : Geometry.SurfaceNormal(localPoint);

    internal SurfaceRayTraceStateResult TraceRayState(
        RayState inputRay,
        IMaterial materialBefore,
        IMaterial materialAfter,
        double cumulativePathLength,
        double cumulativeOpticalPathLength,
        bool ignorePhysicalAperture = false,
        bool stopBeforeInteraction = false,
        bool bypassCoating = false,
        CoordinateSystem? materialBeforeCoordinates = null,
        bool allowVirtualIntersection = false,
        bool ignoreMaterialAbsorption = false,
        Func<RealRay, SurfaceInteractionContext, double>? surfacePhaseOpticalPath = null)
    {
        OpticCapabilityPreflight.EnsureSurfaceSupported(this, OpticCapabilityOperation.RayTrace);
        var ray = inputRay.Normalize();
        var continuesMedium = InteractionModel is CoordinateReferenceInteractionModel;
        if (Geometry is OpticalSystemDesign.Core.Geometries.PolygonGroupGeometry group)
            return group.Trace(this, ray, materialBefore, cumulativePathLength, cumulativeOpticalPathLength, ignoreMaterialAbsorption);
        if (materialBefore is GradientIndexMaterial unlocated)
        {
            if (materialBeforeCoordinates is null)
                throw new NotSupportedException("入射 GRIN 介质需要明确的体坐标系；不能把出口面坐标当作入口坐标。");
            materialBefore = unlocated.At(materialBeforeCoordinates);
        }
        if (materialBefore is UniaxialMaterial unlocatedCrystal)
        {
            if (materialBeforeCoordinates is null) throw new NotSupportedException("Incoming crystal requires its entrance coordinate frame.");
            materialBefore = unlocatedCrystal.At(materialBeforeCoordinates, ray.Direction);
        }
        if (!stopBeforeInteraction && (materialBefore is LocatedUniaxialMaterial || materialAfter is UniaxialMaterial)
            && !continuesMedium && (InteractionModel is not BirefringentInteractionModel || IsReflective))
            throw new NotSupportedException("A crystal medium requires an explicit birefringent entry/exit interface.");
        if (!stopBeforeInteraction && InteractionModel is BirefringentInteractionModel crystalInterface
            && (crystalInterface.IsExit ? materialBefore is not LocatedUniaxialMaterial : materialAfter is not UniaxialMaterial))
            throw new NotSupportedException("Crystal entry/exit is missing its declared crystal medium.");
        if (!stopBeforeInteraction && (materialBefore is LocatedUniaxialMaterial || materialAfter is UniaxialMaterial)
            && !bypassCoating && CoatingModel is not OpticalSystemDesign.Core.Coatings.NoneCoatingModel)
            throw new NotSupportedException("Anisotropic interface coatings require crystal polarization amplitudes.");
        if (ray.PolarizationMatrix is not null && (materialBefore is LocatedUniaxialMaterial || materialAfter is UniaxialMaterial))
            throw new NotSupportedException("Crystal polarization transport is not implemented.");
        if (ray.PolarizationMatrix is not null
            && (materialBefore is LocatedGradientIndexMaterial || materialAfter is GradientIndexMaterial))
            throw new NotSupportedException("GRIN 连续偏振基矢运输尚未接入，不能保留未旋转的偏振矩阵冒充结果。");
        double segmentLength;
        double segmentOpticalPathLength;
        double refractiveIndexBefore;
        Vector3D localHit;
        Vector3D? profileHit = null;
        double? incidenceCosineSquared = null;
        RayState propagated;
        if (materialBefore is LocatedGradientIndexMaterial located)
        {
            var material = located.Material;
            var result = located.LocalPosition is { } localStart
                ? GradientIndexRayIntegrator.TraceToSurfaceFromLocal(material.Profile, located.Coordinates,
                    localStart, ray.Direction, ray.WavelengthNanometers, Geometry, CoordinateSystem,
                    material.MaximumPathLength, material.IntegrationOptions, ComputationCancellation.Current)
                : GradientIndexRayIntegrator.TraceToSurface(material.Profile, located.Coordinates,
                    ray.Origin, ray.Direction, ray.WavelengthNanometers, Geometry, CoordinateSystem,
                    material.MaximumPathLength, material.IntegrationOptions, ComputationCancellation.Current);
            if (result.Termination != GradientIndexTermination.SurfaceReached)
                return Miss(result.End.RefractiveIndex);
            segmentLength = result.End.PathLength;
            segmentOpticalPathLength = result.End.OpticalPathLength;
            refractiveIndexBefore = result.End.RefractiveIndex;
            profileHit = result.ProfileLocalPosition;
            localHit = CoordinateSystem.ToLocalDirection((located.Coordinates.Origin - CoordinateSystem.Origin)
                + located.Coordinates.ToGlobalDirection(result.ProfileLocalPosition));
            propagated = ray with { Origin = result.End.Position, Direction = result.End.Direction, IsNormalized = true };
        }
        else
        {
            refractiveIndexBefore = materialBefore.RefractiveIndex(ray.WavelengthNanometers);
            var localOrigin = CoordinateSystem.ToLocalPoint(ray.Origin);
            var localDirection = CoordinateSystem.ToLocalDirection(ray.Direction);
            var intersection = Intersect(localDirection);
            var signedDistance = intersection.Distance;
            if (!intersection.IsHit && allowVirtualIntersection)
            {
                if (materialBefore.PropagationModel is not HomogeneousPropagationModel)
                    throw new NotSupportedException("Virtual pupil intersections require homogeneous propagation.");
                intersection = Intersect(-localDirection);
                signedDistance = -intersection.Distance;
            }
            if (!intersection.IsHit) return Miss(refractiveIndexBefore, intersection.Status);
            segmentLength = allowVirtualIntersection ? signedDistance : Math.Max(0, intersection.Distance);
            segmentOpticalPathLength = allowVirtualIntersection
                ? segmentLength * refractiveIndexBefore : Math.Abs(segmentLength * refractiveIndexBefore);
            propagated = Propagate(ray, materialBefore.PropagationModel, segmentLength);
            localHit = intersection.Point;
            incidenceCosineSquared = intersection.IncidenceCosineSquared;

            IntersectionResult Intersect(Vector3D direction)
            {
                var hit = Geometry.DistanceToIntersection(localOrigin, direction);
                if (hit.IsHit || Geometry is not GridSagGeometry grid
                    || !IsNeutralGridReference(grid, materialBefore, materialAfter))
                    return hit;
                var plane = GridReferencePlane.DistanceToIntersection(localOrigin, direction);
                return plane.IsHit && !grid.ContainsGridPoint(plane.Point) ? plane : hit;
            }
        }
        if (continuesMedium)
            materialAfter = materialBefore is LocatedGradientIndexMaterial gradientBefore
                ? new LocatedGradientIndexMaterial(gradientBefore.Material, gradientBefore.Coordinates, profileHit)
                : materialBefore;
        var refractiveIndexAfter = continuesMedium ? refractiveIndexBefore
            : materialAfter is GradientIndexMaterial afterGradient
            ? afterGradient.InterfaceRefractiveIndex(localHit, ray.WavelengthNanometers)
            : materialAfter.RefractiveIndex(ray.WavelengthNanometers);
        var nextCumulativePathLength = cumulativePathLength + segmentLength;
        var nextCumulativeOpticalPathLength = cumulativeOpticalPathLength + segmentOpticalPathLength;
        var extinctionCoefficient = materialBefore.ExtinctionCoefficient(ray.WavelengthNanometers);
        var attenuation = ignoreMaterialAbsorption || extinctionCoefficient <= 0
            ? 1.0
            : HomogeneousPowerAttenuation.Evaluate(extinctionCoefficient, ray.WavelengthNanometers, segmentLength);
        propagated = propagated with
        {
            OpticalPathDifference = ray.OpticalPathDifference + segmentOpticalPathLength,
            Intensity = ray.Intensity * attenuation
        };

        SurfaceRayTraceStateResult Miss(double index, IntersectionStatus? status = null) => new(ray with { Intensity = 0 },
            new RayTraceSampleValue(Number, Label, ray.Origin, ray.Direction, 0, true,
                CumulativePathLength: cumulativePathLength, CumulativeOpticalPathLength: cumulativeOpticalPathLength),
            index, materialBefore, null, cumulativePathLength, cumulativeOpticalPathLength, true)
        {
            Failure = status is IntersectionStatus.DomainError or IntersectionStatus.InvalidInput or IntersectionStatus.InvalidNormal
                ? SurfaceTraceFailure.DomainError : SurfaceTraceFailure.IntersectionMiss,
            IntersectionStatus = status
        };
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
                true)
            { Failure = SurfaceTraceFailure.ApertureClipping };
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

        var localNormal = SequentialTraceNormal(localHit);
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
            materialAfter.ExtinctionCoefficient(ray.WavelengthNanometers),
            incidenceCosineSquared,
            surfacePhaseOpticalPath,
            InteractionModel is BirefringentInteractionModel ? new BirefringentInterfaceData(
                materialBefore is LocatedUniaxialMaterial crystalBefore
                    ? crystalBefore.Material.Ellipsoid(ray.WavelengthNanometers, CoordinateSystem.ToLocalDirection(crystalBefore.GlobalAxis))
                    : new(new(0, 0, 1), refractiveIndexBefore, refractiveIndexBefore, false),
                materialAfter is UniaxialMaterial crystalAfter
                    ? crystalAfter.Ellipsoid(ray.WavelengthNanometers, crystalAfter.Axis)
                    : new(new(0, 0, 1), refractiveIndexAfter, refractiveIndexAfter, false)) : null);
        var localRay = propagated with
        {
            Origin = localHit,
            Direction = CoordinateSystem.ToLocalDirection(propagated.Direction)
        };
        var interaction = Interact(localRay, context);
        var coated = bypassCoating ? interaction.Ray : ApplyCoating(interaction.Ray,
            context with { IsReflective = interaction.Kind is RayInteractionKind.Reflected or RayInteractionKind.TotalInternalReflection });
        var outgoingMaterial = interaction.Kind == RayInteractionKind.Transmitted
            ? materialAfter is GradientIndexMaterial gradient ? gradient.At(CoordinateSystem, coated.Origin) : materialAfter
            : materialBefore is LocatedGradientIndexMaterial reflected
                ? new LocatedGradientIndexMaterial(reflected.Material, reflected.Coordinates,
                    coated.Origin == localHit ? profileHit : reflected.Coordinates.ToLocalDirection(
                        (CoordinateSystem.Origin - reflected.Coordinates.Origin) + CoordinateSystem.ToGlobalDirection(coated.Origin)))
                : materialBefore;
        var traced = coated with
        {
            Origin = CoordinateSystem.ToGlobalPoint(coated.Origin),
            Direction = CoordinateSystem.ToGlobalDirection(coated.Direction)
        };
        var hitOrigin = traced.Origin;
        traced = ApplyScattering(traced, globalNormal);
        if (interaction.Kind == RayInteractionKind.Transmitted && materialAfter is UniaxialMaterial transmittedCrystal)
        {
            outgoingMaterial = transmittedCrystal.At(CoordinateSystem, traced.Direction);
            refractiveIndexAfter = outgoingMaterial.RefractiveIndex(ray.WavelengthNanometers);
        }
        else if (outgoingMaterial is LocatedUniaxialMaterial reflectedCrystal)
            outgoingMaterial = new LocatedUniaxialMaterial(reflectedCrystal.Material, reflectedCrystal.GlobalAxis, traced.Direction);
        if (traced.Origin != hitOrigin && outgoingMaterial is LocatedGradientIndexMaterial moved)
            outgoingMaterial = new LocatedGradientIndexMaterial(moved.Material, moved.Coordinates);

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
        double ExtinctionCoefficientAfter,
        double? IncidenceCosineSquared,
        Func<RealRay, SurfaceInteractionContext, double>? SurfacePhaseOpticalPath,
        BirefringentInterfaceData? Birefringent)
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
            ExtinctionCoefficientAfter,
            IncidenceCosineSquared,
            SurfacePhaseOpticalPath,
            Birefringent);
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
    bool StopTracing)
{
    internal SurfaceTraceFailure Failure { get; init; }
    internal IntersectionStatus? IntersectionStatus { get; init; }
}
