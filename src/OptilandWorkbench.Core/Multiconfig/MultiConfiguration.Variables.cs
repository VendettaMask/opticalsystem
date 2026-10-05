namespace OptilandWorkbench.Core.Multiconfig;

public sealed partial class MultiConfiguration
{
    private readonly List<MultiConfigurationVariable> _operandVariables = new();
    public IReadOnlyList<MultiConfigurationVariable> OperandVariables => _operandVariables.AsReadOnly();

    public void SetOperandVariable(int oneBasedRow, int configurationIndex, bool enabled)
    {
        if (oneBasedRow < 1 || oneBasedRow > _operandRows.Count) throw new ArgumentOutOfRangeException(nameof(oneBasedRow));
        if (configurationIndex < 0 || configurationIndex >= Configurations.Count) throw new ArgumentOutOfRangeException(nameof(configurationIndex));
        var row = _operandRows[oneBasedRow - 1];
        var variable = new MultiConfigurationVariable(configurationIndex, row);
        if (enabled)
        {
            if (IsOperandPickupTarget(configurationIndex, row.SurfaceNumber, row.Property))
                throw new InvalidOperationException("该单元格由多配置拾取控制，请先移除拾取。");
            MultiConfigurationVariable.EnsureEditable(row, Configurations[configurationIndex]);
            if (!_operandVariables.Contains(variable)) _operandVariables.Add(variable);
            DetachPropertyLink(configurationIndex, row.SurfaceNumber, row.Property);
            if (row.Kind == MultiConfigurationOperandKind.SemiDiameter)
                FindSurface(Configurations[configurationIndex], row.SurfaceNumber).SemiDiameterFixed = true;
        }
        else
        {
            _operandVariables.Remove(variable);
            // Disabling the cell variable also clears the equivalent LDE variable.
            var surface = FindSurface(Configurations[configurationIndex], row.SurfaceNumber);
            if (row.Kind == MultiConfigurationOperandKind.Curvature) surface.RadiusVariable = false;
            if (row.Kind == MultiConfigurationOperandKind.Thickness) surface.ThicknessVariable = false;
        }
    }

    public void ClearOperandVariables()
    {
        foreach (var variable in _operandVariables.ToArray())
            SetOperandVariable(_operandRows.IndexOf(variable.Operand) + 1, variable.ConfigurationIndex, false);
    }

    public void DetachPropertyLink(int configurationIndex, int surfaceNumber, string property)
    {
        if (configurationIndex < 0 || configurationIndex >= Configurations.Count) throw new ArgumentOutOfRangeException(nameof(configurationIndex));
        _ = FindSurface(Configurations[configurationIndex], surfaceNumber);
        var normalized = NormalizeProperty(property);
        if (configurationIndex > 0) _brokenLinks.Add((configurationIndex, surfaceNumber, normalized));
    }
}
