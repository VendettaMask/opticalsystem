using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Coordinates;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    public int InsertCoordinateBreak(int surfaceNumber, bool after)
    {
        if (surfaceNumber < 0 || surfaceNumber >= Surfaces.Count) throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        var number = surfaceNumber + (after ? 1 : 0);
        MutateConfigurationOperands(candidate => candidate.InsertCoordinateBreak(number));
        SetStatus("已插入坐标间断。");
        return number;
    }

    public void UpdateCoordinateBreak(int surfaceNumber, CoordinateBreakSettingsDto settings)
    {
        var surface = GetSurfaceByNumber(surfaceNumber);
        var previous = surface.Placement?.Break;
        var operation = new CoordinateBreakParameters(settings.X, settings.Y, settings.TiltX, settings.TiltY, settings.TiltZ, settings.TiltFirst)
        {
            Pickups = previous?.Pickups ?? [],
            ChiefRaySolves = previous?.ChiefRaySolves ?? [],
            Return = settings.ReturnKind == CoordinateReturnKind.None ? null : new(settings.ReturnSurface,
                settings.ReturnKind == CoordinateReturnKind.Orientation ? CoordinateReturnMode.Orientation : CoordinateReturnMode.PositionAndOrientation)
        };
        SequentialCoordinateEditing.ValidateEdit(CurrentOptic, surfaceNumber, operation);
        CaptureCurrentState();
        SequentialCoordinateEditing.EnsurePlacements(CurrentOptic);
        surface.Placement = surface.Placement! with { Break = operation };
        if (settings.Variables is { } variables)
            surface.ShapeVariables = surface.ShapeVariables.Where(v => v is not ("f0" or "f1" or "f2" or "f3" or "f4"))
                .Concat(Enumerable.Range(0, 5).Where(p => variables[p]).Select(p => $"f{p}")).ToArray();
        CurrentOptic.HasImportedSurfaceTiltDecenter = true;
        CurrentOptic.SurfaceGroup.Renumber();
        // The editor explicitly targets the active configuration. Other configurations keep their own operations.
        SyncActiveConfigurationFromCurrent();
        for (var p = 0; p < 5; p++)
            if (_activeConfigurationIndex > 0) _multiConfiguration.UpdateLinkState(_activeConfigurationIndex, surfaceNumber, $"shape:f{p}");
        SynchronizeConfigurationPickups();
        SetStatus("坐标间断已更新。");
        SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
        OpticChanged?.Invoke(this, EventArgs.Empty);
    }
}
