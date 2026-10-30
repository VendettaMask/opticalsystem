using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Core.Raytrace;

public enum SurfaceTraceFailure
{
    None, ApertureClipping, IntersectionMiss, TotalInternalReflection, InvalidLaunchOrAiming,
    DomainError, Unsupported, Extinguished, Cancelled, RayBudgetExhausted
}

/// <summary>CPU diagnostic of the first failure in nominal propagation. Never changes apertures or records a trace.</summary>
public sealed record SequentialFailureDiagnostic(SurfaceTraceFailure Failure, bool ReachedImage,
    int? SurfaceIndex, string? SurfaceLabel, Vector3D? LocalInterceptMillimeters,
    double? ClearSemiDiameterMillimeters, RayInteractionKind? Interaction, string? Detail);

public sealed partial class SequentialRayTracer
{
    public SequentialFailureDiagnostic DiagnoseFirstFailure(RealRay sourceRay, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sourceRay);
        int? at = null;
        try
        {
            token.ThrowIfCancellationRequested();
            OpticCapabilityPreflight.EnsureSupported(_optic, OpticCapabilityOperation.RayTrace);
            SequentialTraceMeasurement.Record(1);
            if (!sourceRay.CanTrace || !sourceRay.IsAlive)
                return Result(SurfaceTraceFailure.InvalidLaunchOrAiming, detail: "Launch is nonfinite, has no direction or has zero aiming weight.");
            var ray = RayState.FromRealRay(sourceRay.Normalize());
            var material = ResolveMaterial("Air");
            double path = 0, opticalPath = 0;
            for (var index = 0; index < _optic.SurfaceGroup.Items.Count; index++)
            {
                at = index; token.ThrowIfCancellationRequested();
                var surface = _optic.SurfaceGroup.Items[index];
                var trace = TraceSequentialSurface(surface, index, ray, material, path, opticalPath,
                    allowVirtualIntersection: AllowsReferenceBacktrack(_optic.SurfaceGroup.Items, index));
                var failure = trace.Failure;
                if (trace.InteractionKind == RayInteractionKind.TotalInternalReflection) failure = SurfaceTraceFailure.TotalInternalReflection;
                if (failure == SurfaceTraceFailure.None && (!trace.Ray.CanTrace || trace.StopTracing)) failure = SurfaceTraceFailure.Extinguished;
                if (failure != SurfaceTraceFailure.None)
                    return Result(failure, trace.IntersectionStatus is not null || failure == SurfaceTraceFailure.IntersectionMiss ? null
                        : surface.CoordinateSystem.ToLocalPoint(trace.Sample.Position), trace.InteractionKind, trace.IntersectionStatus?.ToString());
                ray = trace.Ray; material = trace.OutgoingMaterial;
                path = trace.CumulativePathLength; opticalPath = trace.CumulativeOpticalPathLength;
            }
            return new(SurfaceTraceFailure.None, _optic.SurfaceGroup.Items.Count > 0, null, null, null, null, null, null);
        }
        catch (OperationCanceledException error) { return Result(SurfaceTraceFailure.Cancelled, detail: error.Message); }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error))
        { return Result(SurfaceTraceFailure.RayBudgetExhausted, detail: error.Message); }
        catch (NotSupportedException error) { return Result(SurfaceTraceFailure.Unsupported, detail: error.Message); }
        catch (Exception error) when (error is ArithmeticException or InvalidOperationException or ArgumentException)
        { return Result(SurfaceTraceFailure.DomainError, detail: error.Message); }

        SequentialFailureDiagnostic Result(SurfaceTraceFailure failure, Vector3D? intercept = null,
            RayInteractionKind? interaction = null, string? detail = null)
        {
            var surface = at is { } i ? _optic.SurfaceGroup.Items[i] : null;
            return new(failure, false, at, surface?.Label, intercept,
                surface?.PhysicalAperture is CircularAperture circle ? circle.Radius : null, interaction, detail);
        }
    }
}
