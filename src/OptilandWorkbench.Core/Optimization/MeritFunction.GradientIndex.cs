using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateGradientIndexControl(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var front = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var index = optic.SurfaceGroup.Items.IndexOf(front);
        if (index <= 0 || index >= optic.SurfaceGroup.Items.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(definition), "GRIN 控制面必须为具有下一边界的实际材料入口面。");
        var back = optic.SurfaceGroup.Items[index + 1];
        var number = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (number <= 0 || number > optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(definition), "GRIN 控制需要有效的正波长编号。");
        var wave = ResolveWavelength(optic, number).Nanometers;
        if (type == "DLTN") return GradientIndexControlMetrics.AxialBlank(front, back, wave).Delta;
        if (type is "GRMN" or "GRMX")
        {
            var points = GradientIndexControlMetrics.SixPoints(front, back, wave);
            return type == "GRMN"
                ? BoundaryGreaterThanOrEqual(points.Min(p => p.Index), definition.Target)
                : BoundaryLessThanOrEqual(points.Max(p => p.Index), definition.Target);
        }
        var value = GradientIndexControlMetrics.AtPoint(front, back, type[1] - '0', wave).Index;
        return type[2..] switch
        {
            "GT" => BoundaryGreaterThanOrEqual(value, definition.Target),
            "LT" => BoundaryLessThanOrEqual(value, definition.Target),
            _ => value
        };
    }
}
