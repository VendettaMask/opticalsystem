namespace OptilandWorkbench.Core.Multiconfig;

/// <summary>Affine pickup in editor coordinates; bindings survive row and surface renumbering.</summary>
public sealed record MultiConfigurationPickup(
    int ConfigurationIndex,
    MultiConfigurationOperand Operand,
    int SourceConfigurationIndex,
    MultiConfigurationOperand SourceOperand,
    double Scale = 1,
    double Offset = 0)
{
    public static void Validate(IReadOnlyList<MultiConfigurationPickup>? pickups,
        IReadOnlyList<MultiConfigurationOperand> rows, IReadOnlyList<Optic> configurations,
        IReadOnlyList<MultiConfigurationVariable>? variables = null)
    {
        if (pickups is null) return;
        if (pickups.Count > 65536) throw new ArgumentException("多配置拾取数量不能超过 65536。");
        var targets = new HashSet<(int, MultiConfigurationOperand)>();
        var rowNumbers = rows.Select((row, index) => (row, index)).ToDictionary(p => p.row, p => p.index);
        foreach (var pickup in pickups)
        {
            if (pickup is null || pickup.Operand is null || pickup.SourceOperand is null
                || !rowNumbers.TryGetValue(pickup.Operand, out var targetRow)
                || !rowNumbers.TryGetValue(pickup.SourceOperand, out var sourceRow)
                || pickup.ConfigurationIndex < 0 || pickup.ConfigurationIndex >= configurations.Count
                || pickup.SourceConfigurationIndex < 0 || pickup.SourceConfigurationIndex > pickup.ConfigurationIndex
                || sourceRow > targetRow || (sourceRow == targetRow && pickup.SourceConfigurationIndex == pickup.ConfigurationIndex)
                || !double.IsFinite(pickup.Scale) || !double.IsFinite(pickup.Offset))
                throw new ArgumentException("拾取源的行号和配置号必须不晚于目标，不能引用自身；比例和偏移必须有限。");
            if (!targets.Add((pickup.ConfigurationIndex, pickup.Operand))) throw new ArgumentException("同一单元格不能定义多个拾取。");
            if (variables?.Any(v => v.ConfigurationIndex == pickup.ConfigurationIndex && v.Operand == pickup.Operand) == true)
                throw new ArgumentException("多配置拾取目标不能同时是独立变量。");
            MultiConfigurationVariable.EnsureEditable(pickup.Operand, configurations[pickup.ConfigurationIndex]);
            var optic = configurations[pickup.ConfigurationIndex];
            if (pickup.Operand.Kind == MultiConfigurationOperandKind.Thickness && optic.Solves.KeepImageAtBackFocus
                && pickup.Operand.SurfaceNumber == optic.SurfaceGroup.Items[^1].Number)
                throw new InvalidOperationException("像面厚度正由后焦距求解控制，不能同时设置多配置拾取。");
            pickup.SourceOperand.Read(configurations[pickup.SourceConfigurationIndex]);
        }
    }
}
