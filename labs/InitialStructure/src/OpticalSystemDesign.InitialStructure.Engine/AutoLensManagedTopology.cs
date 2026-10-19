using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>
/// Native fixed-row representation of cementing. A glass/glass interface is followed
/// by one coincident same-medium reference row. The latter has exactly zero power;
/// both profiles and the shared medium are tied, not independently optimized.
/// </summary>
internal static class AutoLensManagedTopology
{
    public static bool IsCemented(InitialStructureSpecification specification, int gap) =>
        specification.FlatStart is { OptimizationMethod: FlatStartOptimizationMethod.AutoLensManagedV1, AutoLens: { } settings }
        && settings.CementedGaps?.Contains(gap) == true;

    public static int AirGapCount(InitialStructureSpecification specification, int elements)
    {
        var gaps = Math.Clamp(elements, 1, 8) - 1;
        return gaps - Enumerable.Range(0, gaps).Count(gap => IsCemented(specification, gap));
    }

    public static void Apply(Optic optic, InitialStructureSpecification specification, int elements)
    {
        if (specification.FlatStart?.OptimizationMethod != FlatStartOptimizationMethod.AutoLensManagedV1
            || specification.FlatStart.AutoLens?.CementedGaps.Count is not > 0) return;
        for (var gap = 0; gap < elements - 1; gap++)
        {
            if (!IsCemented(specification, gap)) continue;
            var back = optic.SurfaceGroup.Items[2 * gap + 2];
            var next = optic.SurfaceGroup.Items[2 * gap + 3];
            back.Thickness = 0;
            back.MaterialAfter = next.MaterialAfter.Clone();
            next.Geometry = back.Geometry.Clone();
        }
        optic.SurfaceGroup.Renumber();
    }

    public static bool IsValid(Optic optic, int gap)
    {
        var back = optic.SurfaceGroup.Items[2 * gap + 2];
        var next = optic.SurfaceGroup.Items[2 * gap + 3];
        return back.Thickness == 0 && back.CoordinateSystem == next.CoordinateSystem
            && ContentFingerprint.Compute(ComponentSnapshotFactory.FromGeometry(back.Geometry)) == ContentFingerprint.Compute(ComponentSnapshotFactory.FromGeometry(next.Geometry))
            && ContentFingerprint.Compute(ComponentSnapshotFactory.FromMaterial(back.MaterialAfter)) == ContentFingerprint.Compute(ComponentSnapshotFactory.FromMaterial(next.MaterialAfter));
    }
}
