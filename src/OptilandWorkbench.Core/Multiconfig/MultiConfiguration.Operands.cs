using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Multiconfig;

public sealed partial class MultiConfiguration
{
    private readonly List<MultiConfigurationOperand> _operandRows = new();
    public IReadOnlyList<MultiConfigurationOperand> OperandRows => _operandRows.AsReadOnly();

    public void ReplaceOperandRows(IReadOnlyList<MultiConfigurationOperand> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var replacement = rows.ToArray();
        MultiConfigurationOperand.ValidateRows(replacement, Configurations);
        var retainedPickups = _operandPickups.Where(p => replacement.Contains(p.Operand)).ToArray();
        MultiConfigurationPickup.Validate(retainedPickups, replacement, Configurations,
            _operandVariables.Where(v => replacement.Contains(v.Operand)).ToArray());
        var updates = new List<(MeritOperandDefinition Definition, int Number)>();
        foreach (var optic in Configurations)
            foreach (var definition in optic.MeritFunctionOperands.Where(definition => !definition.CompatibilityOnly
                && MeritFunctionCatalog.CanonicalType(definition.Type) is "MCOV" or "MCOG" or "MCOL"))
            {
                var number = definition.ZemaxIntegerParameters.Length > 0 ? definition.ZemaxIntegerParameters[0] : definition.Surface;
                if (number <= 0 || number > _operandRows.Count)
                    throw new InvalidOperationException("评价函数含无效的本地 MCO 行引用，请先修正或删除该评价行再调整多配置行表。");
                var next = Array.IndexOf(replacement, _operandRows[number - 1]);
                if (next < 0) throw new InvalidOperationException($"多配置行 {number} 正被评价函数引用，请先移除引用再删除该行。");
                updates.Add((definition, next + 1));
            }
        foreach (var (definition, number) in updates)
        {
            var parameters = definition.ZemaxIntegerParameters;
            definition.ZemaxIntegerParameters = [number, parameters.Length > 1 ? parameters[1] : definition.Wavelength];
            definition.Surface = number;
        }
        _operandVariables.RemoveAll(variable => !replacement.Contains(variable.Operand));
        _operandRows.Clear(); _operandRows.AddRange(replacement);
        _operandPickups.Clear(); _operandPickups.AddRange(retainedPickups);
    }

    public void SetOperandValue(int oneBasedRow, int configurationIndex, double value)
    {
        if (_operandPickups.Count > 0) ExecutePickupMutation(() => SetOperandValueCore(oneBasedRow, configurationIndex, value));
        else SetOperandValueCore(oneBasedRow, configurationIndex, value);
    }

    private void SetOperandValueCore(int oneBasedRow, int configurationIndex, double value)
    {
        if (oneBasedRow < 1 || oneBasedRow > _operandRows.Count) throw new ArgumentOutOfRangeException(nameof(oneBasedRow));
        if (configurationIndex < 0 || configurationIndex >= Configurations.Count) throw new ArgumentOutOfRangeException(nameof(configurationIndex));
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "多配置值必须是有限数值。");
        var row = _operandRows[oneBasedRow - 1];
        var optic = Configurations[configurationIndex]; var surface = FindSurface(optic, row.SurfaceNumber);
        if (row.Kind == MultiConfigurationOperandKind.SemiDiameter && value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "半口径不能为负数。");
        var hasPickup = row.Kind switch
        {
            MultiConfigurationOperandKind.Thickness => optic.Pickups.ThicknessPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            MultiConfigurationOperandKind.Curvature => optic.Pickups.RadiusPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            MultiConfigurationOperandKind.SemiDiameter => optic.Pickups.SemiDiameterPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            _ => false
        };
        if (hasPickup) throw new InvalidOperationException("该参数由表面拾取控制，请编辑源表面或先移除拾取。");
        var storedValue = row.Kind == MultiConfigurationOperandKind.Curvature
            ? value == 0 ? 0 : 1 / value : value;
        if (row.Kind == MultiConfigurationOperandKind.Curvature && value != 0 && !double.IsFinite(storedValue))
            throw new ArgumentOutOfRangeException(nameof(value), "曲率倒数超出有限半径范围。");
        SetProperty(configurationIndex, row.SurfaceNumber, row.Property, storedValue);
        if (_operandPickups.Count == 0) { optic.Pickups.ApplyAll(); optic.SurfaceGroup.Renumber(); }
        if (configurationIndex == 0 && _operandPickups.Count == 0)
        {
            PropagateBaseProperty(row.SurfaceNumber, row.Property);
            var targets = row.Kind switch
            {
                MultiConfigurationOperandKind.Thickness => optic.Pickups.ThicknessPickups.Select(p => p.TargetSurface),
                MultiConfigurationOperandKind.Curvature => optic.Pickups.RadiusPickups.Select(p => p.TargetSurface),
                MultiConfigurationOperandKind.SemiDiameter => optic.Pickups.SemiDiameterPickups.Select(p => p.TargetSurface),
                _ => Enumerable.Empty<int>()
            };
            foreach (var target in targets.Distinct()) PropagateBaseProperty(target, row.Property);
            foreach (var configuration in Configurations) { configuration.Pickups.ApplyAll(); configuration.SurfaceGroup.Renumber(); }
        }
    }

    private void RemapOperandSurfaces(Func<int, int> remap)
    {
        for (var index = 0; index < _operandPickups.Count; index++)
        {
            var pickup = _operandPickups[index];
            _operandPickups[index] = pickup with
            {
                Operand = pickup.Operand with { SurfaceNumber = remap(pickup.Operand.SurfaceNumber) },
                SourceOperand = pickup.SourceOperand with { SurfaceNumber = remap(pickup.SourceOperand.SurfaceNumber) }
            };
        }
        for (var index = 0; index < _operandVariables.Count; index++)
        {
            var variable = _operandVariables[index];
            _operandVariables[index] = variable with { Operand = variable.Operand with { SurfaceNumber = remap(variable.Operand.SurfaceNumber) } };
        }
        for (var index = 0; index < _operandRows.Count; index++)
            _operandRows[index] = _operandRows[index] with { SurfaceNumber = remap(_operandRows[index].SurfaceNumber) };
    }
}
