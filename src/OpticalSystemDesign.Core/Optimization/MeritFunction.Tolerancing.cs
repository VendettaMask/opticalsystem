namespace OpticalSystemDesign.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    /// <summary>Creates signed residuals from the ordered table without turning skipped rows into target errors.</summary>
    public static IReadOnlyList<Operand> CreateToleranceResiduals(Optic optic, IReadOnlyList<MeritOperandDefinition> definitions)
    {
        var context = new MeritConfigurationContext([optic]);
        var rows = new List<Operand>();
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (!definition.Enabled) continue;
            var type = CanonicalType(definition.Type);
            var name = $"{type} 第 {i + 1} 行";
            if (type is "CONF" or "MCOV" or "MCOL" or "MCOG" or "ZTHI")
                throw new ArgumentException($"{name} 显式引用配置。公差中的系统评价函数按配置分别执行，请使用本配置操作数。");
            if (definition.CompatibilityOnly || HasOpaqueZemaxParameters(type))
                throw new ArgumentException($"{name} 是兼容保留行，无法用于公差评价。");
            if (!double.IsFinite(definition.Weight) || !double.IsFinite(definition.Target))
                throw new ArgumentException($"{name} 的目标或权重不是有限数值。");
            if (definition.Weight == 0 || IsSystemStateOperand(type)
                || type is "BLNK" or "DMFS" or "REQS" or "GOTO" or "ENDX" or "OOFF" or "SKIN" or "SKIS" or "USYM")
                continue;
            var row = i;
            var target = type == "EQUA" ? 0 : definition.Target;
            rows.Add(new Operand(name, 0, definition.Weight, () =>
            {
                var results = EvaluateToleranceTable(context, definitions);
                var result = results[row];
                return result.Contribution == 0 ? 0 : result.Value - target;
            }));
        }
        var weight = rows.Sum(row => Math.Abs(row.Weight));
        if (!double.IsFinite(weight) || weight <= 0)
            throw new ArgumentException("系统评价函数需要至少一条非零权重的有效评价操作数。");
        return rows;
    }

    private static IReadOnlyList<MeritOperandEvaluation> EvaluateToleranceTable(
        MeritConfigurationContext context, IReadOnlyList<MeritOperandDefinition> definitions)
    {
        var batch = ActiveEvaluationBatch.Value;
        var tables = batch is null ? null : batch.ToleranceTables ??= new();
        if (tables?.TryGetValue(context, out var cached) == true) return cached;

        var results = EvaluateAll(context, definitions);
        for (var i = 0; i < results.Count; i++)
        {
            var evaluation = results[i];
            if (!string.IsNullOrEmpty(evaluation.Error) || !double.IsFinite(evaluation.Contribution))
                throw new OptimizationEvaluationException($"{CanonicalType(definitions[i].Type)} 第 {i + 1} 行", evaluation.Error);
        }
        tables?.Add(context, results);
        return results;
    }
}
