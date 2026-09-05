using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public RadiusSolveDto GetRadiusSolve(int surfaceNumber)
    {
        var surface = GetSurfaceByNumber(surfaceNumber);
        var pickups = CurrentOptic.Pickups.RadiusPickups.Where(pickup => pickup.TargetSurface == surfaceNumber).ToArray();
        if (pickups.Length == 0)
            return new RadiusSolveDto(surface.RadiusVariable ? RadiusSolveKind.Variable : RadiusSolveKind.Fixed);
        var pickup = pickups[^1];
        var factor = pickup.Scale == 0 ? 0 : 1 / pickup.Scale;
        var editable = pickups.Length == 1 && pickup.Offset == 0 && double.IsFinite(factor)
            && pickup.SourceSurface >= 0 && pickup.SourceSurface < surfaceNumber;
        return new RadiusSolveDto(RadiusSolveKind.Pickup, pickup.SourceSurface, factor, editable);
    }

    public void SetRadiusSolve(int surfaceNumber, RadiusSolveUpdateDto update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (surfaceNumber <= 0 || surfaceNumber >= Surfaces.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "当前仅支持物理表面的半径求解。");
        var surface = GetSurfaceByNumber(surfaceNumber);
        if (surface.Geometry is INonComputableGeometry)
            throw new NotSupportedException("暂不支持此面型的半径求解。");
        if (!Enum.IsDefined(update.Kind)) throw new ArgumentOutOfRangeException(nameof(update));
        if (update.Kind == RadiusSolveKind.Pickup)
        {
            if (update.SourceSurface < 0 || update.SourceSurface >= surfaceNumber)
                throw new ArgumentOutOfRangeException(nameof(update), "拾取表面必须在当前表面之前。");
            if (!double.IsFinite(update.ScaleFactor)
                || (update.ScaleFactor != 0 && !double.IsFinite(1 / update.ScaleFactor)))
                throw new ArgumentOutOfRangeException(nameof(update), "比例因子必须是可表示的有限数值。");
        }
        CaptureCurrentState();
        if (update.Kind == RadiusSolveKind.Pickup)
            CurrentOptic.Pickups.SetCurvaturePickup(update.SourceSurface, surfaceNumber, update.ScaleFactor);
        else
            CurrentOptic.Pickups.RemoveRadius(surfaceNumber);
        surface.RadiusVariable = update.Kind == RadiusSolveKind.Variable;
        CommitSurfaceEdit(surface, nameof(OpticalSurface.Radius));
    }
}
