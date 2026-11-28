using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class PrescriptionService
{
    public CoordinateBreakEditDto GetCoordinateBreak(int surfaceNumber)
    {
        lock (Gate)
        {
            var surface = FindSurface(surfaceNumber) ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
            var optic = Runtime.CurrentOptic;
            var operation = surface.Placement?.Break;
            var reference = surface.InteractionModel is CoordinateReferenceInteractionModel;
            var restriction = optic.SurfaceGroup.Items.Skip(surfaceNumber).Select(s => s.Placement?.Break?.EditingRestriction).FirstOrDefault(r => r is not null);
            var controlled = Enumerable.Range(0, 5).Where(p => operation?.Pickups.Any(v => v.Parameter == p) == true
                || operation?.ChiefRaySolves.Any(v => v.Parameter == p) == true
                || Runtime.IsMultiConfigurationPickupTarget(surfaceNumber, $"shape:f{p}")
                || surface.BlockedVariableProperties.Contains($"shape:f{p}")).ToArray();
            var settings = new CoordinateBreakSettingsDto(operation?.X ?? 0, operation?.Y ?? 0,
                operation?.TiltX ?? 0, operation?.TiltY ?? 0, operation?.TiltZ ?? 0, operation?.TiltFirst ?? false,
                operation?.Return is not { } frameReturn ? CoordinateReturnKind.None
                    : frameReturn.Mode == CoordinateReturnMode.Orientation ? CoordinateReturnKind.Orientation : CoordinateReturnKind.PositionAndOrientation,
                operation?.Return?.SurfaceNumber ?? 0,
                Enumerable.Range(0, 5).Select(p => surface.ShapeVariables.Contains($"f{p}")).ToArray());
            return new(Workspace.Revision, surfaceNumber, reference,
                reference && surfaceNumber > 0 && surfaceNumber < optic.SurfaceGroup.Items.Count - 1 && restriction is null,
                settings, controlled, optic.SurfaceGroup.Items.Take(surfaceNumber).Select(s => new CoordinateReferenceChoiceDto(s.Number, s.Label)).ToArray(),
                restriction, reference && operation is null);
        }
    }

    public int InsertCoordinateBreak(int surfaceNumber, bool after, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            CheckCoordinateRevision(expectedRevision);
            return Runtime.InsertCoordinateBreak(surfaceNumber, after);
        });

    public void UpdateCoordinateBreak(int surfaceNumber, CoordinateBreakSettingsDto settings, long expectedRevision) =>
        MutateTransactional(WorkspaceChangeCategory.Surface, () =>
        {
            CheckCoordinateRevision(expectedRevision);
            ArgumentNullException.ThrowIfNull(settings);
            var edit = GetCoordinateBreak(surfaceNumber);
            if (!edit.CanEdit) throw new InvalidOperationException(edit.Restriction ?? "所选行不是可编辑的坐标间断。");
            if (!Enum.IsDefined(settings.ReturnKind) || settings.Variables is { Count: not 5 })
                throw new ArgumentException("坐标设置无效。");
            var before = new[] { edit.Settings.X, edit.Settings.Y, edit.Settings.TiltX, edit.Settings.TiltY, edit.Settings.TiltZ };
            var after = new[] { settings.X, settings.Y, settings.TiltX, settings.TiltY, settings.TiltZ };
            foreach (var p in edit.ControlledParameters)
                if (before[p] != after[p] || (settings.Variables?[p] == true && edit.Settings.Variables?[p] != true))
                    throw new InvalidOperationException("该坐标参数由拾取或求解控制，请编辑源参数。");
            Runtime.UpdateCoordinateBreak(surfaceNumber, settings);
        });

    private void CheckCoordinateRevision(long expectedRevision)
    {
        if (expectedRevision != Workspace.Revision)
            throw new InvalidOperationException("工程已变化，请还原并重新编辑坐标设置。");
    }
}
