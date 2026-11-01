using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Phase;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static int Binary3Count(ZemaxSurface surface)
    {
        var n = surface.ExtraData.GetValueOrDefault(1);
        if (n != Math.Truncate(n) || n is < 0 or > 60) throw new InvalidDataException("Binary 3 requires 0..60 coefficient groups.");
        if (surface.ExtraData.Any(p => p.Key > 1 + 4 * n && p.Value != 0)) throw new InvalidDataException("Binary 3 coefficient group is truncated.");
        return (int)n;
    }
    private static double[] Binary3Coefficients(ZemaxSurface surface, int member) => Enumerable.Range(0, Binary3Count(surface))
        .Select(i => surface.ExtraData.GetValueOrDefault(2 + 4 * i + member)).ToArray();
    private static IGeometry CreateBinary3Geometry(ZemaxSurface surface, double radius)
    {
        if (surface.Parameters.Any(p => p.Key is < 0 or > 7 && p.Value != 0) || surface.Parameter(7) is not (0 or 1))
            return CreateOpaqueZemaxGeometry(surface);
        _ = CreateBinary3Phase(surface);
        return new ConcentricZoneGeometry(radius, surface.Conic, Binary3Coefficients(surface, 0), surface.Parameter(0),
            surface.Parameter(1), Binary3Coefficients(surface, 2), surface.Parameter(2), surface.Parameter(7) == 1);
    }
    private static DualZoneRadialPhaseProfile CreateBinary3Phase(ZemaxSurface surface)
    {
        foreach (var index in new[] { 4, 5 })
            if (surface.Parameter(index) != Math.Truncate(surface.Parameter(index)) || Math.Abs(surface.Parameter(index)) > 1_000_000)
                throw new InvalidDataException("Binary 3 diffraction orders must be bounded integers.");
        return new DualZoneRadialPhaseProfile(surface.Parameter(2), surface.Parameter(3), Binary3Coefficients(surface, 1),
            Binary3Coefficients(surface, 3), (int)surface.Parameter(4), (int)surface.Parameter(5));
    }
}
