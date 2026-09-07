using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Apertures;

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

    public ThicknessSolveDto GetThicknessSolve(int surfaceNumber)
    {
        var surface = GetSurfaceByNumber(surfaceNumber);
        var pickup = CurrentOptic.Pickups.ThicknessPickups
            .LastOrDefault(item => item.TargetSurface == surfaceNumber);
        return pickup is null
            ? new ThicknessSolveDto(surface.ThicknessVariable ? ThicknessSolveKind.Variable : ThicknessSolveKind.Fixed)
            : new ThicknessSolveDto(
                ThicknessSolveKind.Pickup,
                pickup.SourceSurface,
                pickup.Scale,
                pickup.Offset);
    }

    public void SetThicknessSolve(int surfaceNumber, ThicknessSolveUpdateDto update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (surfaceNumber <= 0 || surfaceNumber >= Surfaces.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "当前仅支持物理表面的厚度求解。");
        if (!Enum.IsDefined(update.Kind)) throw new ArgumentOutOfRangeException(nameof(update));
        if (update.Kind == ThicknessSolveKind.Pickup)
        {
            if (update.SourceSurface < 0 || update.SourceSurface >= surfaceNumber)
                throw new ArgumentOutOfRangeException(nameof(update), "拾取表面必须在当前表面之前。");
            if (!double.IsFinite(update.ScaleFactor) || !double.IsFinite(update.Offset))
                throw new ArgumentOutOfRangeException(nameof(update), "比例因子和偏移量必须是有限数值。");
        }

        CaptureCurrentState();
        if (update.Kind == ThicknessSolveKind.Pickup)
            CurrentOptic.Pickups.SetThicknessPickup(
                update.SourceSurface,
                surfaceNumber,
                update.ScaleFactor,
                update.Offset);
        else
            CurrentOptic.Pickups.RemoveThickness(surfaceNumber);
        var surface = GetSurfaceByNumber(surfaceNumber);
        surface.ThicknessVariable = update.Kind == ThicknessSolveKind.Variable;
        CommitSurfaceEdit(surface, nameof(OpticalSurface.Thickness));
    }

    public SemiDiameterSolveDto GetSemiDiameterSolve(int surfaceNumber)
    {
        var surface = GetSurfaceByNumber(surfaceNumber);
        var pickup = CurrentOptic.Pickups.SemiDiameterPickups
            .LastOrDefault(item => item.TargetSurface == surfaceNumber);
        return pickup is null
            ? new SemiDiameterSolveDto(surface.SemiDiameterFixed
                ? SemiDiameterSolveKind.Fixed
                : SemiDiameterSolveKind.Automatic)
            : new SemiDiameterSolveDto(
                SemiDiameterSolveKind.Pickup,
                pickup.SourceSurface,
                pickup.Scale);
    }

    public void SetSemiDiameterSolve(int surfaceNumber, SemiDiameterSolveUpdateDto update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (surfaceNumber < 0 || surfaceNumber >= Surfaces.Count)
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        if (!Enum.IsDefined(update.Kind)) throw new ArgumentOutOfRangeException(nameof(update));
        if (update.Kind == SemiDiameterSolveKind.Pickup)
        {
            if (update.SourceSurface < 0 || update.SourceSurface >= surfaceNumber)
                throw new ArgumentOutOfRangeException(nameof(update), "拾取表面必须在当前表面之前。");
            if (!double.IsFinite(update.ScaleFactor))
                throw new ArgumentOutOfRangeException(nameof(update), "比例因子必须是有限数值。");
        }

        CaptureCurrentState();
        if (update.Kind == SemiDiameterSolveKind.Pickup)
            CurrentOptic.Pickups.SetSemiDiameterPickup(
                update.SourceSurface,
                surfaceNumber,
                update.ScaleFactor);
        else
            CurrentOptic.Pickups.RemoveSemiDiameter(surfaceNumber);
        var surface = GetSurfaceByNumber(surfaceNumber);
        surface.SemiDiameterFixed = update.Kind != SemiDiameterSolveKind.Automatic;
        if (update.Kind == SemiDiameterSolveKind.Automatic && surface.SemiDiameterDefinesPhysicalAperture)
        {
            surface.SemiDiameterDefinesPhysicalAperture = false;
            surface.PhysicalAperture = null;
        }
        else if (update.Kind != SemiDiameterSolveKind.Automatic
            && !surface.IsStop
            && double.IsFinite(surface.Radius)
            && Math.Abs(surface.Radius) > 1e-12
            && surface.PhysicalAperture is null)
        {
            surface.PhysicalAperture = new CircularAperture(surface.SemiDiameter);
            surface.SemiDiameterDefinesPhysicalAperture = true;
        }
        CommitSurfaceEdit(surface, nameof(OpticalSurface.SemiDiameter));
    }
}
