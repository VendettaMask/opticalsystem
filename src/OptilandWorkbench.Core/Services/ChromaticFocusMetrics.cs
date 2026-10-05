using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Services;

/// <summary>Signed focus and chromatic measurements for centered focal transmission systems.</summary>
public static class ChromaticFocusMetrics
{
    public static double LongitudinalAberration(Optic optic, double wavelengthMicrometers, double zone)
    {
        ValidateSystem(optic, wavelengthMicrometers);
        if (!double.IsFinite(zone) || zone < 0 || zone > 1)
            throw new ArgumentOutOfRangeException(nameof(zone), "Zone 必须在 [0, 1] 内；0 为近轴，正值为真实光线 Py。");
        if (zone == 0)
        {
            var trace = optic.Paraxial.MarginalRay(wavelengthMicrometers);
            return AxisIntercept(trace.Heights[^1][0], trace.Slopes[^1][0]);
        }

        var image = optic.SurfaceGroup.Items[^1];
        var sample = optic.TraceGenericFinalSample(0, 0, 0, zone, wavelengthMicrometers, aimAtStop: optic.RayAimingEnabled);
        if (sample is null || sample.SurfaceNumber != image.Number || sample.Vignetted
            || !double.IsFinite(sample.Intensity) || sample.Intensity <= 0 || sample.InteractionKind is null)
            throw new InvalidOperationException("焦移所需的轴上光线未有效到达像面（失追迹或渐晕）。");
        var point = image.CoordinateSystem.ToLocalPoint(sample.Position);
        var direction = image.CoordinateSystem.ToLocalDirection(sample.Direction);
        if (!double.IsFinite(direction.Z) || direction.Z <= 0)
            throw new InvalidOperationException("焦移所需的光线没有有效前向像空间方向。");
        return AxisIntercept(point.Y, direction.Y / direction.Z);
    }

    public static double AxialColor(Optic optic, double firstWavelengthMicrometers, double secondWavelengthMicrometers, double zone) =>
        LongitudinalAberration(optic, firstWavelengthMicrometers, zone)
        - LongitudinalAberration(optic, secondWavelengthMicrometers, zone);

    public static double Spherochromatism(Optic optic, double firstWavelengthMicrometers, double secondWavelengthMicrometers, double zone) =>
        AxialColor(optic, firstWavelengthMicrometers, secondWavelengthMicrometers, zone)
        - AxialColor(optic, firstWavelengthMicrometers, secondWavelengthMicrometers, 0);

    public static double LateralColor(Optic optic, double firstWavelengthMicrometers, double secondWavelengthMicrometers)
    {
        ValidateSystem(optic, firstWavelengthMicrometers);
        ValidateSystem(optic, secondWavelengthMicrometers);
        var maximumField = FieldCoordinates.MaximumRadius(optic.Fields);
        if (!double.IsFinite(maximumField))
            throw new InvalidOperationException("近轴倍率色差需要有限视场坐标。");
        if (optic.FieldDefinition == FieldDefinitionKind.Angle && maximumField >= 90)
            throw new NotSupportedException("近轴倍率色差需要小于 90 度的角度视场。");
        // The scalar meridional reference is +maximum field magnitude, not a selected editor row.
        var first = optic.Paraxial.TraceNormalizedPupil(1, [0.0], firstWavelengthMicrometers);
        var second = optic.Paraxial.TraceNormalizedPupil(1, [0.0], secondWavelengthMicrometers);
        // Match the signed image-height convention of the formal Lateral Color analysis.
        var value = second.Heights[^1][0] - first.Heights[^1][0];
        return double.IsFinite(value) ? value : throw new InvalidOperationException("近轴主光线像高不是有限数值。");
    }

    private static double AxisIntercept(double height, double slope)
    {
        if (!double.IsFinite(height) || !double.IsFinite(slope) || slope == 0)
            throw new InvalidOperationException("轴上边缘光线没有有限焦点，不能以零焦移替代。");
        var distance = -height / slope;
        return double.IsFinite(distance) ? distance : throw new InvalidOperationException("轴向焦移不是有限数值。");
    }

    private static void ValidateSystem(Optic optic, double wavelengthMicrometers)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Analysis, "Chromatic focus");
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers));
        if (optic.Wavelengths.Count == 0)
            throw new InvalidOperationException("焦移/色差需要显式定义系统波长，不能使用默认波长替代。");
        if (optic.ImageSpaceAfocal)
            throw new NotSupportedException("本组焦移/色差操作数暂不支持无焦像空间的屈光度或角度单位。");
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.Count < 3)
            throw new InvalidOperationException("焦移/色差需要物面、实际光学面和像面。");
        foreach (var surface in surfaces)
        {
            var frame = surface.CoordinateSystem;
            if (!double.IsFinite(frame.Origin.Z) || frame.Origin.X != 0 || frame.Origin.Y != 0
                || frame.RotationXDegrees != 0 || frame.RotationYDegrees != 0 || frame.RotationZDegrees != 0
                || surface.IsReflective || surface.Geometry is not (PlaneGeometry or StandardGeometry)
                || surface.InteractionModel is not (RefractiveReflectiveInteractionModel or ThinLensInteractionModel)
                || surface.MaterialAfter.PropagationModel is not HomogeneousPropagationModel)
                throw new NotSupportedException("本组焦移/色差当前支持共轴、未旋转的透射标准面与平面理想薄透镜；非球面多项式、偏心、反射、GRIN 和相位/衍射系统尚未支持。");
            if (surface.InteractionModel is ThinLensInteractionModel && !surface.IsPlane)
                throw new NotSupportedException("理想薄透镜必须定义在平面上。");
            var index = surface.MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000);
            if (!double.IsFinite(index) || index <= 0)
                throw new InvalidOperationException("焦移/色差计算需要有限正折射率。");
        }
        for (var i = 1; i < surfaces.Count; i++)
        {
            if (surfaces[i].CoordinateSystem.Origin.Z < surfaces[i - 1].CoordinateSystem.Origin.Z)
                throw new NotSupportedException("本组焦移/色差当前不支持逆向表面序列。");
            if (i == 1 && ObjectConjugate.IsInfinite(surfaces[0])) continue;
            var thickness = surfaces[i - 1].Thickness;
            if (!double.IsFinite(thickness) || thickness < 0
                || Math.Abs(surfaces[i].CoordinateSystem.Origin.Z - surfaces[i - 1].CoordinateSystem.Origin.Z - thickness)
                    > 1e-9 * Math.Max(1, thickness))
                throw new NotSupportedException("本组焦移/色差要求顺序中心厚度与顶点间距一致且有限非负。");
        }
        var image = surfaces[^1];
        if (!image.IsPlane || image.InteractionModel is not RefractiveReflectiveInteractionModel
            || image.MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000)
                != surfaces[^2].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000))
            throw new NotSupportedException("本组焦移/色差需要不改变介质的平面像面。");
        var pupilDiameter = optic.Paraxial.EstimateEntrancePupilDiameter();
        if (!double.IsFinite(pupilDiameter) || pupilDiameter <= 0)
            throw new InvalidOperationException("焦移/色差需要有限正入瞳直径。");
        if (!ObjectConjugate.IsInfinite(surfaces[0])
            && Math.Abs(optic.Paraxial.EstimateEntrancePupilLocation(wavelengthMicrometers) - surfaces[0].CoordinateSystem.Origin.Z) <= 1e-15)
            throw new InvalidOperationException("有限物面与入瞳重合，不能定义边缘光线。");
    }
}
