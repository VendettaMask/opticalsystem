using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Services;

public sealed partial class Paraxial
{
    private bool HasGradientIndex => _optic.SurfaceGroup.Items.Any(s => s.MaterialAfter is GradientIndexMaterial);

    private void ValidateGradientIndexSystem()
    {
        var surfaces = _optic.SurfaceGroup.Items;
        if (surfaces.Count < 3) throw new InvalidOperationException("GRIN 一阶系统需要物面、材料入口及出口。");
        for (var i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            var frame = s.CoordinateSystem;
            if (frame.Origin.X != 0 || frame.Origin.Y != 0 || frame.RotationXDegrees != 0 || frame.RotationYDegrees != 0
                || s.Geometry is not (PlaneGeometry or StandardGeometry)
                || s.InteractionModel is not (RefractiveReflectiveInteractionModel or ThinLensInteractionModel)
                || s.IsReflective || s.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: true }
                || s.InteractionModel is ThinLensInteractionModel { IsReflective: true })
                throw new NotSupportedException("GRIN 标量一阶系统当前要求共轴标准面/平面及透射或理想薄透镜；偏心、倾斜、反射、非球面与相位面尚未接通。");
            if (!double.IsFinite(frame.Origin.Z) || double.IsNaN(s.Radius)
                || s.Geometry is StandardGeometry && s.Radius == 0)
                throw new InvalidOperationException("GRIN 一阶系统的顶点和曲率半径无效。");
            if (!(i == 0 && ObjectConjugate.IsInfinite(s)) && !double.IsFinite(s.Thickness))
                throw new InvalidOperationException("GRIN 一阶系统仅允许物面正无穷厚度。");
            if (i < surfaces.Count - 1 && !(i == 0 && ObjectConjugate.IsInfinite(s)))
            {
                if (!double.IsFinite(s.Thickness) || s.Thickness < 0
                    || Math.Abs(surfaces[i + 1].CoordinateSystem.Origin.Z - frame.Origin.Z - s.Thickness)
                        > 1e-10 * Math.Max(1, s.Thickness))
                    throw new NotSupportedException("GRIN 一阶系统要求有限非负顺序间距，且顶点坐标与中心厚度一致。");
            }
            if (s.MaterialAfter is GradientIndexMaterial gradient)
            {
                if (i == 0 || i == surfaces.Count - 1)
                    throw new NotSupportedException("物方或像面之后无明确出口的 GRIN 尚未接入一阶系统。");
                GradientIndexParaxialTransport.ValidateAxis(gradient.Profile);
                if (gradient.Profile is Gradient4IndexProfile p && p.Quadratic.X != p.Quadratic.Y)
                    throw new NotSupportedException("非旋转对称 GRIN 需要独立的 X/Y 瞳孔和瞄准链，不能使用共享标量近轴结果。");
            }
            else if (s.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
                throw new NotSupportedException("GRIN 一阶系统不能混用入口方向近似或未知传播模型。");
        }
    }

    private static double AxialIndex(IMaterial material, OpticalSurface entrance, double globalZ, double wavelength)
    {
        var n = material is GradientIndexMaterial gradient
            ? gradient.RefractiveIndex(new(0, 0, globalZ - entrance.CoordinateSystem.Origin.Z), wavelength)
            : material.RefractiveIndex(wavelength);
        return double.IsFinite(n) && n > 0 ? n : throw new InvalidOperationException("轴上折射率必须为有限正数。");
    }

    private static RayMatrix AxialPropagation(IMaterial material, OpticalSurface entrance,
        double fromZ, double toZ, double wavelength, bool reverse = false)
    {
        if (material is not GradientIndexMaterial gradient)
            return Translate(RayMatrix.Identity, (reverse ? -1 : 1) * (toZ - fromZ));
        var originZ = entrance.CoordinateSystem.Origin.Z;
        var m = GradientIndexParaxialTransport.Between(gradient, fromZ - originZ, toZ - originZ,
            wavelength, cancellationToken: ComputationCancellation.Current);
        // Reverse rays use z_rev = z_image - z_global, and u_rev = -u_global.
        return new(m.A, reverse ? -m.B : m.B, reverse ? -m.C : m.C, m.D);
    }

    private static RayMatrix Compose(RayMatrix after, RayMatrix before) => new(
        after.A * before.A + after.B * before.C, after.A * before.B + after.B * before.D,
        after.C * before.A + after.D * before.C, after.C * before.B + after.D * before.D);

    private RayMatrix TraceGradientMatrix(int first, int end, double wavelength, bool includeTrailingTranslation)
    {
        var surfaces = _optic.SurfaceGroup.Items;
        var matrix = RayMatrix.Identity;
        var currentIndex = first <= 0 ? 1.0
            : AxialIndex(surfaces[first - 1].MaterialAfter, surfaces[first - 1], surfaces[first].CoordinateSystem.Origin.Z, wavelength);
        for (var i = first; i < end; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var s = surfaces[i];
            var z = s.CoordinateSystem.Origin.Z;
            var nextIndex = AxialIndex(s.MaterialAfter, s, z, wavelength);
            matrix = s.InteractionModel is ThinLensInteractionModel thin
                ? ThinLens(matrix, thin.FocalLength, currentIndex, nextIndex, false)
                : Refract(matrix, s.Radius, currentIndex, nextIndex);
            currentIndex = nextIndex;
            if ((i != 0 || !ObjectConjugate.IsInfinite(s)) && (includeTrailingTranslation || i + 1 < end))
            {
                matrix = Compose(AxialPropagation(s.MaterialAfter, s, z, z + s.Thickness, wavelength), matrix);
                currentIndex = AxialIndex(s.MaterialAfter, s, z + s.Thickness, wavelength);
            }
        }
        return matrix;
    }

    private ParaxialTrace TraceGradientGeneric(IReadOnlyList<double> initialHeights, IReadOnlyList<double> initialSlopes,
        double initialZ, double wavelengthMicrometers, int skip, bool reverse)
    {
        if (initialHeights.Count != initialSlopes.Count || !double.IsFinite(initialZ)
            || initialHeights.Any(v => !double.IsFinite(v)) || initialSlopes.Any(v => !double.IsFinite(v))
            || !double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
            throw new ArgumentException("GRIN 近轴输入需要等长有限高度/斜率数组、有限起点及正波长。");
        var wavelength = wavelengthMicrometers * 1000;
        var surfaces = _optic.SurfaceGroup.Items;
        var y = initialHeights.ToArray();
        var u = initialSlopes.ToArray();
        var z = reverse ? surfaces[^1].CoordinateSystem.Origin.Z - initialZ : initialZ;
        var heights = new List<IReadOnlyList<double>>();
        var slopes = new List<IReadOnlyList<double>>();
        for (var ordinal = Math.Clamp(skip, 0, surfaces.Count); ordinal < surfaces.Count; ordinal++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var i = reverse ? surfaces.Count - 1 - ordinal : ordinal;
            var s = surfaces[i];
            if (i != 0 || reverse && !ObjectConjugate.IsInfinite(s))
            {
                var incomingEntrance = reverse ? s : surfaces[i - 1];
                var outgoingEntrance = reverse ? surfaces[Math.Max(0, i - 1)] : s;
                var targetZ = s.CoordinateSystem.Origin.Z;
                var m = AxialPropagation(incomingEntrance.MaterialAfter, incomingEntrance, z, targetZ, wavelength, reverse);
                if (i != 0)
                {
                    var n1 = AxialIndex(incomingEntrance.MaterialAfter, incomingEntrance, targetZ, wavelength);
                    var n2 = AxialIndex(outgoingEntrance.MaterialAfter, outgoingEntrance, targetZ, wavelength);
                    m = s.InteractionModel is ThinLensInteractionModel thin
                        ? ThinLens(m, thin.FocalLength, n1, n2, false)
                        : Refract(m, reverse ? -s.Radius : s.Radius, n1, n2);
                }
                // A finite object is a real plane reached on return, without a fictitious object interaction.
                for (var ray = 0; ray < y.Length; ray++)
                    (y[ray], u[ray]) = (m.A * y[ray] + m.B * u[ray], m.C * y[ray] + m.D * u[ray]);
                z = targetZ;
            }
            heights.Add(y.ToArray());
            slopes.Add(u.ToArray());
        }
        return new(heights, slopes);
    }
}
