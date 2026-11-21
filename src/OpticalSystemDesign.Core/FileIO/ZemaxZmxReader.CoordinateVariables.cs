using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static bool SupportsCoordinateVariable(ZemaxDocument document, int sourceNumber) => !document.Surfaces.Any(later => later.Number >= sourceNumber
        && later.ParameterPickups.Values.Any(pickup => pickup.SourceSurface < 0));

    private static void ConfigureCoordinateVariables(ZemaxDocument document, IReadOnlyList<ZemaxSurface> surfaces)
    {
        bool HasVariable(ZemaxSurface surface) => document.Surfaces.First(source => source.Number == surface.Number).VariableParameters.Any(number => number is >= 1 and <= 5)
            || document.ConfigurationOperands.Any(operand => operand.Target == surface.Number && operand.Command == "PRAM" && operand.AuxiliaryIndex is >= 0 and < 5 && operand.SolveCode == 1);
        // Configured surfaces contain evaluated values; pickup declarations belong to the source document.
        bool CanUpdate(ZemaxSurface surface) => SupportsCoordinateVariable(document, surface.Number);
        if (!surfaces.Any(surface => surface.Type == "COORDBRK" && HasVariable(surface) && CanUpdate(surface))) return;
        foreach (var surface in surfaces.Where(surface => surface.Type == "COORDBRK"))
        {
            surface.RetainReferencePlane = true;
            surface.SupportsCoordinateVariables = CanUpdate(surface);
        }
    }

    private static void InstallCoordinateVariablePlacements(Optic optic, IReadOnlyList<ZemaxSurface> sources)
    {
        if (!sources.Any(source => source.SupportsCoordinateVariables)) return;
        var imported = sources.Where(IsImportedSurface).ToArray();
        var surfaces = optic.SurfaceGroup.Items;
        var originalCoordinates = surfaces.Select(surface => surface.CoordinateSystem).ToArray();
        for (var index = 0; index < surfaces.Count; index++)
        {
            var source = imported[index]; var surface = surfaces[index];
            if (index == 0)
            {
                surface.Placement = SequentialSurfacePlacement.From(originalCoordinates[index], double.IsFinite(surface.Thickness) ? surface.Thickness : 0);
                continue;
            }
            var previous = originalCoordinates[index - 1];
            var gap = double.IsFinite(surfaces[index - 1].Thickness) ? surfaces[index - 1].Thickness : 0;
            var afterPrevious = previous with { Origin = previous.ToGlobalPoint(new Vector3D(0, 0, gap)) };
            var incoming = source.Type == "COORDBRK" ? source.IncomingCoordinateSystem! : originalCoordinates[index];
            var relative = incoming.MoveWithFrame(afterPrevious, CoordinateSystem.Global);
            var operation = source.Type == "COORDBRK" ? new CoordinateBreakParameters(source.Parameter(0), source.Parameter(1), source.Parameter(2),
                source.Parameter(3), source.Parameter(4), source.Parameter(5) != 0) : null;
            surface.Placement = SequentialSurfacePlacement.From(relative, coordinateBreak: operation);
            if (source.SupportsCoordinateVariables)
                surface.ShapeVariables = [.. surface.ShapeVariables, .. source.VariableParameters.Where(number => number is >= 1 and <= 5).Select(number => $"f{number - 1}")];
        }
        optic.SurfaceGroup.Renumber();
    }
}
