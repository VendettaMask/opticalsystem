using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateConfigurationOperand(MeritConfigurationContext? configurations, MeritOperandDefinition definition)
    {
        if (configurations is null) throw new InvalidOperationException("MCO 操作数需要包含行表的有序配置评价上下文。");
        var row = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var configuration = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (row < 1 || row > configurations.OperandRows.Count) throw new ArgumentOutOfRangeException(nameof(definition), "Op# 必须是从 1 开始的多配置操作数行号，不能用表面号替代。");
        if (configuration < 1 || configuration > configurations.Configurations.Count) throw new ArgumentOutOfRangeException(nameof(definition), "Cfg# 必须是有效的正配置编号。");
        ComputationCancellation.ThrowIfCancellationRequested();
        var value = configurations.OperandRows[row - 1].Read(configurations.Configurations[configuration - 1]);
        return CanonicalType(definition.Type) switch
        {
            "MCOG" => Math.Min(value, definition.Target),
            "MCOL" => Math.Max(value, definition.Target),
            _ => value
        };
    }

    private static double EvaluateZoomThickness(MeritConfigurationContext configurations, MeritOperandDefinition definition)
    {
        var first = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var last = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (first < 1 || last < first)
            throw new ArgumentOutOfRangeException(nameof(definition), "ZTHI 需要从 1 开始的有效表面闭区间；不含物面，终点可等于起点。");
        if (!double.IsFinite(definition.Target) || definition.Target < 0)
            throw new ArgumentOutOfRangeException(nameof(definition), "ZTHI 的允许厚度差必须为非负有限值。");
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var optic in configurations.Configurations)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var value = EvaluateRangeThickness(optic, definition);
            if (!double.IsFinite(value)) throw new InvalidOperationException("ZTHI 配置总厚度不是有限值。");
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return Math.Max(definition.Target, maximum - minimum);
    }
}
