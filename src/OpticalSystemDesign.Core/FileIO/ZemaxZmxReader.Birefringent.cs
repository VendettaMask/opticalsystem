using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static bool IsComputableBirefringent(ZemaxSurface source) => source.Type is "BIRE__IN" or "BIRE_OUT"
        && !source.IsMirror && !source.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)
        && source.Parameter(5) == 0
        && !source.Parameters.Any(p => p.Key is < -1 or > 5 && p.Value != 0)
        && !source.ExtraData.Any(p => p.Value != 0)
        && (source.Type == "BIRE_OUT" || source.Parameter(0) is 0 or 1 && source.Parameter(4) is 0 or 1
            && new Vector3D(source.Parameter(1), source.Parameter(2), source.Parameter(3)).Length > 0);

    private static UniaxialMaterial CreateBirefringentMaterial(Optic optic, ZemaxSurface source, IReadOnlyList<string> catalogs)
    {
        if (!optic.Materials.TryResolve(source.Material, catalogs, out var ordinary)
            || !optic.Materials.TryResolve(source.Material + "-E", catalogs, out var extraordinary))
            throw new NotSupportedException("Birefringent media require both actual ordinary and extraordinary dispersion curves: " + source.Material);
        return new UniaxialMaterial(ordinary, extraordinary, new(source.Parameter(1), source.Parameter(2), source.Parameter(3)),
            source.Parameter(0) == 1, source.Parameter(4) == 1);
    }
}
