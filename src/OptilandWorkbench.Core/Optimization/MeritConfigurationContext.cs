using OptilandWorkbench.Core.Multiconfig;

namespace OptilandWorkbench.Core.Optimization;

/// <summary>A stable configuration list for one ordered evaluation. Callers own candidate isolation.</summary>
public sealed class MeritConfigurationContext
{
    public MeritConfigurationContext(IEnumerable<Optic> configurations, int activeConfigurationIndex = 0,
        IReadOnlyList<MultiConfigurationOperand>? operandRows = null)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var items = configurations.ToArray();
        if (items.Length == 0 || items.Any(item => item is null))
            throw new ArgumentException("评价函数需要至少一个非空配置。", nameof(configurations));
        if (activeConfigurationIndex < 0 || activeConfigurationIndex >= items.Length)
            throw new ArgumentOutOfRangeException(nameof(activeConfigurationIndex));
        Configurations = Array.AsReadOnly(items);
        ActiveConfigurationIndex = activeConfigurationIndex;
        OperandRows = Array.AsReadOnly((operandRows ?? []).ToArray());
    }

    public IReadOnlyList<Optic> Configurations { get; }
    public IReadOnlyList<MultiConfigurationOperand> OperandRows { get; }
    public int ActiveConfigurationIndex { get; }
    public Optic ActiveOptic => Configurations[ActiveConfigurationIndex];
}
