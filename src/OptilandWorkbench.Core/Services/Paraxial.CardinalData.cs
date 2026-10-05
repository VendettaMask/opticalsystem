using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public sealed record CardinalPlaneData(
    double ObjectFocalLength, double ImageFocalLength,
    double ObjectFocalPlane, double ImageFocalPlane,
    double ObjectPrincipalPlane, double ImagePrincipalPlane,
    double ObjectAntiPrincipalPlane, double ImageAntiPrincipalPlane,
    double ObjectNodalPlane, double ImageNodalPlane,
    double ObjectAntiNodalPlane, double ImageAntiNodalPlane);

public sealed partial class Paraxial
{
    /// <summary>Signed positions relative to the first and last vertex, respectively.</summary>
    public CardinalPlaneData CardinalPlanes(int startSurface, int endSurface, double wavelengthMicrometers, int orientation)
    {
        EnsureTransmissiveOperandSystem(wavelengthMicrometers);
        if (orientation is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(orientation));
        var first = OperandSurfaceIndex(startSurface);
        var last = OperandSurfaceIndex(endSurface);
        if (first == 0 || last < first)
            throw new ArgumentOutOfRangeException(nameof(startSurface), "CARD 需要从实际光学面开始的正向闭区间。");
        var surfaces = _optic.SurfaceGroup.Items;
        // The current scalar matrix uses vertex radius. An r² asphere term changes that
        // power, and an r term is not differentiable on axis; do not silently ignore either.
        for (var i = first; i <= last; i++)
        {
            var surface = surfaces[i];
            if (surface.Geometry is not (PlaneGeometry or StandardGeometry))
                throw new NotSupportedException("CARD 当前支持标准球面/圆锥、平面及平面理想薄透镜。");
            if (i < last && (!double.IsFinite(surface.Thickness) || surface.Thickness < 0))
                throw new InvalidOperationException("CARD 区间内传播厚度必须为有限非负值。");
            var indexAfter = surface.MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000);
            if (!double.IsFinite(indexAfter) || indexAfter <= 0)
                throw new InvalidOperationException("CARD 区间内介质折射率必须为有限正数。");
            if (i < last && Math.Abs(surfaces[i + 1].CoordinateSystem.Origin.Z
                    - surface.CoordinateSystem.Origin.Z - surface.Thickness) > 1e-9 * Math.Max(1, surface.Thickness))
                throw new NotSupportedException("CARD 当前要求顶点间距与顺序中心厚度一致。");
        }
        var wavelength = wavelengthMicrometers * 1000;
        var objectIndex = surfaces[first - 1].MaterialAfter.RefractiveIndex(wavelength);
        var imageIndex = surfaces[last].MaterialAfter.RefractiveIndex(wavelength);
        if (!double.IsFinite(objectIndex) || objectIndex <= 0 || !double.IsFinite(imageIndex) || imageIndex <= 0)
            throw new InvalidOperationException("CARD 两侧介质折射率必须为有限正数。");
        var m = TraceMatrix(first, last + 1, wavelength, includeTrailingTranslation: false);
        if (!double.IsFinite(m.A) || !double.IsFinite(m.B) || !double.IsFinite(m.C) || !double.IsFinite(m.D)
            || Math.Abs(m.C) <= 1e-15)
            throw new InvalidOperationException("无焦或无效系统没有有限基点位置。");
        var delta = objectIndex / imageIndex;
        // Translate the input/output planes until B=0. Principal planes have
        // transverse magnification ±1; nodal planes have angular magnification ±1.
        return new CardinalPlaneData(delta / m.C, -1 / m.C,
            m.D / m.C, -m.A / m.C,
            (m.D - delta) / m.C, (1 - m.A) / m.C,
            (m.D + delta) / m.C, (-1 - m.A) / m.C,
            (m.D - 1) / m.C, (delta - m.A) / m.C,
            (m.D + 1) / m.C, (-delta - m.A) / m.C);
    }
}
