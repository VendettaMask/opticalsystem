using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Differentiation;

public static partial class DifferentiableOpticalMetrics
{
    /// <summary>Explicit CPU research continuation through a coincident glass/air/glass/air triplet after the stop.
    /// Uses shared Core signed intersections/refraction and implicit derivatives. This is not a physical acceptance trace.
    /// Ordinary EvaluateAtFields and all formal validation retain their original forward-path rules.</summary>
    public static DifferentiableOpticalResult EvaluateNullElementContinuation(Optic optic,
        IReadOnlyList<OpticalDesignParameter> parameters, IReadOnlyList<(double X, double Y)> fields,
        IReadOnlyList<PupilSample> pupils, int nullFront, int nullBack, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(optic); token.ThrowIfCancellationRequested();
        EnsureSupported(optic, physicalStop: true);
        var surfaces = optic.SurfaceGroup.Items; var stop = surfaces.ToList().FindIndex(s => s.IsStop);
        if (nullFront <= stop || nullFront < 2 || nullBack != nullFront + 1 || nullBack >= surfaces.Count - 1)
            throw new ArgumentException("Research continuation requires two consecutive null faces after the physical stop.");
        var previous = surfaces[nullFront - 1]; var front = surfaces[nullFront]; var back = surfaces[nullBack];
        if (previous.CoordinateSystem.Origin.Z != front.CoordinateSystem.Origin.Z || front.CoordinateSystem.Origin.Z != back.CoordinateSystem.Origin.Z
            || previous.MaterialAfter.Name != "Air" || front.MaterialBefore.Name != "Air" || back.MaterialAfter.Name != "Air"
            || previous.MaterialBefore.Name != front.MaterialAfter.Name || front.MaterialAfter.Name != back.MaterialBefore.Name)
            throw new ArgumentException("Research continuation requires the original coincident null-element media and zero axial separations.");
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        var result = EvaluateCore(optic, parameters, fields, pupils, token, false, true, nullFront, nullBack);
        return result with { Execution = DesignComputeExecution.Cpu with { Backend = "cpu-null-element-continuation/1" } };
    }
}
