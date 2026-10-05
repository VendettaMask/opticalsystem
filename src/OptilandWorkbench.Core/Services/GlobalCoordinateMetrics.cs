using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

/// <summary>Rigid coordinate measurements relative to the selected physical reference surface.</summary>
public static class GlobalCoordinateMetrics
{
    public static CoordinateSystem ReferenceFrame(Optic optic)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var surface = optic.SurfaceGroup.Items.FirstOrDefault(s => s.Number == optic.GlobalReferenceSurfaceNumber)
            ?? throw new InvalidOperationException("全局参考面不存在或为导入时折叠的坐标断点面。");
        ValidateSurface(optic, surface);
        return surface.CoordinateSystem;
    }

    public static Vector3D Vertex(Optic optic, OpticalSurface surface)
    {
        ValidateSurface(optic, surface);
        return ReferenceFrame(optic).ToLocalPoint(surface.CoordinateSystem.Origin);
    }

    public static Vector3D Axis(Optic optic, OpticalSurface surface, int axis)
    {
        ValidateSurface(optic, surface);
        var unit = axis switch
        {
            0 => new Vector3D(1, 0, 0),
            1 => new Vector3D(0, 1, 0),
            2 => new Vector3D(0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };
        return ReferenceFrame(optic).ToLocalDirection(surface.CoordinateSystem.ToGlobalDirection(unit));
    }

    private static void ValidateSurface(Optic optic, OpticalSurface surface)
    {
        if (ReferenceEquals(surface, optic.SurfaceGroup.Items.FirstOrDefault()) && ObjectConjugate.IsInfinite(surface))
            throw new InvalidOperationException("无穷远物面不能作为有限全局坐标测量面或参考面。");
        var frame = surface.CoordinateSystem;
        if (!double.IsFinite(frame.Origin.X) || !double.IsFinite(frame.Origin.Y) || !double.IsFinite(frame.Origin.Z)
            || !double.IsFinite(frame.RotationXDegrees) || !double.IsFinite(frame.RotationYDegrees) || !double.IsFinite(frame.RotationZDegrees))
            throw new InvalidOperationException("表面坐标系包含非有限数值。");
    }
}
