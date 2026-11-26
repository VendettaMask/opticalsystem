using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.Coordinates;

/// <summary>Builds an editable dependency chain without changing the current prescription poses.</summary>
public static class SequentialCoordinateEditing
{
    public static void EnsurePlacements(Optic optic)
    {
        var surfaces = optic.SurfaceGroup.Items;
        if (surfaces.All(s => s.Placement is not null)) return;
        if (surfaces.Any(s => s.Placement is not null))
            throw new NotSupportedException("坐标依赖链不完整，不能编辑。");
        var poses = surfaces.Select(s => s.CoordinateSystem).ToArray();
        for (var i = 0; i < surfaces.Count; i++)
        {
            if (i == 0)
            {
                surfaces[i].Placement = SequentialSurfacePlacement.From(poses[i], Finite(surfaces[i].Thickness));
                continue;
            }
            var afterPrevious = poses[i - 1] with { Origin = poses[i - 1].ToGlobalPoint(new(0, 0, Finite(surfaces[i - 1].Thickness))) };
            surfaces[i].Placement = SequentialSurfacePlacement.From(poses[i].MoveWithFrame(afterPrevious, CoordinateSystem.Global));
        }
    }

    public static void ValidateEdit(Optic optic, int number, CoordinateBreakParameters operation)
    {
        if (number <= 0 || number >= optic.SurfaceGroup.Items.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(number), "只能编辑物面与像面之间的坐标间断。");
        if (optic.SurfaceGroup.Items[number].InteractionModel is not Interactions.CoordinateReferenceInteractionModel)
            throw new InvalidOperationException("所选行不是坐标间断，不能替换实际光学表面。");
        if (Enumerable.Range(0, 5).Any(p => !double.IsFinite(operation.Value(p))))
            throw new ArgumentException("偏心和倾斜必须为有限数值。");
        if (operation.Return is { } frameReturn && (frameReturn.SurfaceNumber < 0 || frameReturn.SurfaceNumber >= number || !Enum.IsDefined(frameReturn.Mode)))
            throw new ArgumentException("坐标返回必须引用前面的表面。");
        var blocked = optic.SurfaceGroup.Items.Skip(number).Select(s => s.Placement?.Break?.EditingRestriction).FirstOrDefault(r => r is not null);
        if (blocked is not null) throw new NotSupportedException(blocked);
    }

    public static void ValidateRemoval(IReadOnlyList<OpticalSurface> surfaces, int number)
    {
        if (surfaces.Any(s => s.Number != number && (s.Placement?.Break?.Return?.SurfaceNumber == number
            || s.Placement?.Break?.Pickups.Any(p => p.SourceSurface == number) == true)))
            throw new InvalidOperationException("该表面正被坐标返回或参数拾取引用，请先修改引用。");
    }

    public static void RemapMeritReferences(Optic optic, Func<int, int> remap, int? removed = null)
    {
        var updates = new List<(MeritOperandDefinition Row, int Slot, int Number)>();
        foreach (var row in optic.MeritFunctionOperands)
        {
            if (!ZemaxOperandRegistry.TryGet(row.Type, out var descriptor)) continue;
            for (var slot = 0; slot < Math.Max(1, row.ZemaxIntegerParameters.Length); slot++)
            {
                if (!descriptor.UsesSlotAs($"Int{slot + 1}", ZemaxOperandParameterValueKind.Surface)
                    && !descriptor.UsesSlotAs($"Int{slot + 1}", ZemaxOperandParameterValueKind.EndSurface)) continue;
                var number = slot < row.ZemaxIntegerParameters.Length ? row.ZemaxIntegerParameters[slot] : row.Surface;
                if (number == 0) continue;
                if (number == removed) throw new InvalidOperationException("该表面正被评价函数引用，请先修改操作数。");
                updates.Add((row, slot, remap(number)));
            }
        }
        foreach (var (row, slot, number) in updates)
        {
            if (slot < row.ZemaxIntegerParameters.Length) row.ZemaxIntegerParameters[slot] = number;
            if (slot == 0) row.Surface = number;
            if (slot == 1) row.Wavelength = number;
        }
    }

    public static void RemapReferences(IEnumerable<OpticalSurface> surfaces, Func<int, int> remap)
    {
        foreach (var surface in surfaces)
        {
            if (surface.Placement?.Break is not { } operation) continue;
            surface.Placement = surface.Placement with
            {
                Break = operation with
                {
                    Return = operation.Return is { } frameReturn ? frameReturn with { SurfaceNumber = remap(frameReturn.SurfaceNumber) } : null,
                    Pickups = operation.Pickups.Select(p => p with { SourceSurface = remap(p.SourceSurface) }).ToArray()
                }
            };
        }
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
