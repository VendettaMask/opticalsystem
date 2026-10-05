using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateGlobalSurfaceData(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var type = CanonicalType(definition.Type);
        if (type == "GLCR")
        {
            var data = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
            if (data is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(definition), "GLCR Data 必须为 1..9，按行排列。");
            var column = GlobalCoordinateMetrics.Axis(optic, surface, (data - 1) % 3);
            return ((data - 1) / 3) switch { 0 => column.X, 1 => column.Y, _ => column.Z };
        }
        var vector = type is "GLCX" or "GLCY" or "GLCZ"
            ? GlobalCoordinateMetrics.Vertex(optic, surface) : GlobalCoordinateMetrics.Axis(optic, surface, 2);
        return type switch { "GLCX" or "GLCA" => vector.X, "GLCY" or "GLCB" => vector.Y, _ => vector.Z };
    }

    private static double EvaluateGlobalRayData(Optic optic, MeritOperandDefinition definition)
    {
        var frame = GlobalCoordinateMetrics.ReferenceFrame(optic);
        var ray = SequentialRayDefinition(optic, definition);
        var sample = RequireRaySample(optic, ray);
        var type = CanonicalType(definition.Type);
        var vector = type is "RAGX" or "RAGY" or "RAGZ"
            ? frame.ToLocalPoint(sample.Position) : UnitVector(frame.ToLocalDirection(sample.Direction));
        return type switch { "RAGX" or "RAGA" => vector.X, "RAGY" or "RAGB" => vector.Y, _ => vector.Z };
    }
}
