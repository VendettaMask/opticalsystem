using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;

namespace OptilandWorkbench.Core.Services;

/// <summary>A first-order ray on the surface's vertex tangent plane, in local coordinates.</summary>
public sealed record ParaxialRaySample(
    int SurfaceNumber,
    Vector3D Position,
    Vector3D Direction,
    Vector3D IncidentDirection,
    double IncidentRefractiveIndex);

public sealed partial class Paraxial
{
    public ParaxialRaySample TraceNormalizedRay(
        int surfaceNumber, double hx, double hy, double px, double py, double wavelengthMicrometers)
    {
        EnsureTransmissiveOperandSystem(wavelengthMicrometers);
        foreach (var coordinate in new[] { hx, hy, px, py })
            if (!double.IsFinite(coordinate) || Math.Abs(coordinate) > 1)
                throw new ArgumentOutOfRangeException(nameof(hx), "归一化近轴光线坐标必须在 [-1, 1] 内。");
        var index = OperandSurfaceIndex(surfaceNumber);
        var surfaces = _optic.SurfaceGroup.Items;
        if (index == 0 && ObjectConjugate.IsInfinite(surfaces[0]))
            throw new InvalidOperationException("无穷远物面没有有限近轴交点。");
        if (!ObjectConjugate.IsInfinite(surfaces[0])
            && Math.Abs(EstimateEntrancePupilLocation(wavelengthMicrometers) - surfaces[0].CoordinateSystem.Origin.Z) <= 1e-15)
            throw new InvalidOperationException("物面与入瞳重合，不能定义指定瞳坐标的近轴光线。");

        ComputationCancellation.ThrowIfCancellationRequested();
        var x = TraceNormalizedPupil(hx, [px], wavelengthMicrometers);
        var y = TraceNormalizedPupil(hy, [py], wavelengthMicrometers);
        var surface = surfaces[index];
        var vertexPlanePoint = new Vector3D(x.Heights[index][0], y.Heights[index][0], surface.CoordinateSystem.Origin.Z);
        var previous = Math.Max(0, index - 1);
        return new ParaxialRaySample(surfaceNumber,
            surface.CoordinateSystem.ToLocalPoint(vertexPlanePoint),
            ParaxialUnitVector(surface.CoordinateSystem.ToLocalDirection(
                new Vector3D(x.Slopes[index][0], y.Slopes[index][0], 1))),
            ParaxialUnitVector(surface.CoordinateSystem.ToLocalDirection(
                new Vector3D(x.Slopes[previous][0], y.Slopes[previous][0], 1))),
            surfaces[previous].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000));
    }

    public double MarginalRayYni(int surfaceNumber, double wavelengthMicrometers)
    {
        EnsureTransmissiveOperandSystem(wavelengthMicrometers);
        var index = OperandSurfaceIndex(surfaceNumber);
        if (index == 0) throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "YNIP 需要实际光学面。");
        var objectSurface = _optic.SurfaceGroup.Items[0];
        if (!ObjectConjugate.IsInfinite(objectSurface)
            && Math.Abs(EstimateEntrancePupilLocation(wavelengthMicrometers) - objectSurface.CoordinateSystem.Origin.Z) <= 1e-15)
            throw new InvalidOperationException("物面与入瞳重合，不能定义近轴边缘光线。");
        var surface = _optic.SurfaceGroup.Items[index];
        var marginal = MarginalRay(wavelengthMicrometers);
        var height = marginal.Heights[index][0];
        var incomingSlope = marginal.Slopes[index - 1][0];
        var indexBefore = _optic.SurfaceGroup.Items[index - 1].MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000);
        var curvature = surface.IsPlane ? 0 : 1 / surface.Radius;
        // In first-order optics the incidence angle is u - (-y/R), in radians.
        return height * indexBefore * (incomingSlope + height * curvature);
    }

    public double ElementEffectiveFocalLengthInAir(int surfaceNumber, double wavelengthMicrometers)
    {
        EnsureTransmissiveOperandSystem(wavelengthMicrometers);
        var index = OperandSurfaceIndex(surfaceNumber);
        var surfaces = _optic.SurfaceGroup.Items;
        if (index == 0 || index + 1 >= surfaces.Count)
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "EFLA 需要相邻两个实际光学面。");
        var front = surfaces[index];
        var back = surfaces[index + 1];
        if (front.InteractionModel is not RefractiveReflectiveInteractionModel
            || back.InteractionModel is not RefractiveReflectiveInteractionModel)
            throw new NotSupportedException("EFLA 当前需要两个折射面，不能把理想薄透镜当作实体单片。");
        if (!double.IsFinite(front.Thickness) || front.Thickness < 0)
            throw new InvalidOperationException("单片中心厚度必须为有限非负数值。");
        var glassIndex = front.MaterialAfter.RefractiveIndex(wavelengthMicrometers * 1000);
        if (!double.IsFinite(glassIndex) || glassIndex <= 0)
            throw new InvalidOperationException("单片折射率必须为有限正数。");
        var matrix = Refract(RayMatrix.Identity, front.Radius, 1, glassIndex);
        matrix = Translate(matrix, front.Thickness);
        matrix = Refract(matrix, back.Radius, glassIndex, 1);
        if (!double.IsFinite(matrix.C) || Math.Abs(matrix.C) <= 1e-15)
            throw new InvalidOperationException("单片空气光焦度为零或无效，没有有限焦距。");
        return -1 / matrix.C;
    }

    private void EnsureTransmissiveOperandSystem(double wavelengthMicrometers)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(wavelengthMicrometers) || wavelengthMicrometers <= 0)
            throw new ArgumentOutOfRangeException(nameof(wavelengthMicrometers));
        EnsureCenteredOperandSystem();
        if (_optic.SurfaceGroup.Items.Any(surface => surface.IsReflective))
            throw new NotSupportedException("本组近轴操作数当前支持共轴透射系统；折叠反射系统尚未验证。");
    }

    private int OperandSurfaceIndex(int surfaceNumber)
    {
        var index = _optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.Number == surfaceNumber);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "找不到指定表面。");
    }

    private static Vector3D ParaxialUnitVector(Vector3D vector)
    {
        var length = vector.Length;
        return double.IsFinite(length) && length > 1e-15 ? vector / length
            : throw new InvalidOperationException("近轴方向不是有限有效向量。");
    }
}
