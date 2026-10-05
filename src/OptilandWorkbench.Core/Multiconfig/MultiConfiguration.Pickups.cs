namespace OptilandWorkbench.Core.Multiconfig;

public sealed partial class MultiConfiguration
{
    private readonly List<MultiConfigurationPickup> _operandPickups = new();
    public IReadOnlyList<MultiConfigurationPickup> OperandPickups => _operandPickups.AsReadOnly();

    private void InitializeOperandPickups(IReadOnlyList<MultiConfigurationPickup>? pickups)
    {
        MultiConfigurationPickup.Validate(pickups, _operandRows, Configurations, _operandVariables);
        _operandPickups.AddRange(pickups ?? []);
        foreach (var pickup in _operandPickups) PinPickupTarget(pickup);
        ApplyOperandPickups();
    }

    public bool IsOperandPickupTarget(int configuration, int surface, string property) => _operandPickups.Any(p =>
        p.ConfigurationIndex == configuration && p.Operand.SurfaceNumber == surface && p.Operand.Property == NormalizeProperty(property));

    public void SetOperandPickup(int row, int configuration, int sourceRow, int sourceConfiguration, double scale = 1, double offset = 0)
    {
        if (row < 1 || row > _operandRows.Count || sourceRow < 1 || sourceRow > _operandRows.Count)
            throw new ArgumentOutOfRangeException(nameof(row));
        var pickup = new MultiConfigurationPickup(configuration, _operandRows[row - 1], sourceConfiguration, _operandRows[sourceRow - 1], scale, offset);
        var replacement = _operandPickups.Where(p => p.ConfigurationIndex != configuration || p.Operand != pickup.Operand).Append(pickup).ToArray();
        var variables = _operandVariables.Where(v => v.ConfigurationIndex != configuration || v.Operand != pickup.Operand).ToArray();
        MultiConfigurationPickup.Validate(replacement, _operandRows, Configurations, variables);
        ExecutePickupMutation(() =>
        {
            _operandVariables.RemoveAll(v => v.ConfigurationIndex == configuration && v.Operand == pickup.Operand);
            _operandPickups.Clear(); _operandPickups.AddRange(replacement);
            PinPickupTarget(pickup);
        });
    }

    public void ClearOperandPickup(int row, int configuration)
    {
        if (row < 1 || row > _operandRows.Count || configuration < 0 || configuration >= Configurations.Count)
            throw new ArgumentOutOfRangeException(nameof(row));
        ExecutePickupMutation(() => _operandPickups.RemoveAll(p => p.ConfigurationIndex == configuration && p.Operand == _operandRows[row - 1]));
    }

    private void PinPickupTarget(MultiConfigurationPickup pickup)
    {
        DetachPropertyLink(pickup.ConfigurationIndex, pickup.Operand.SurfaceNumber, pickup.Operand.Property);
        var surface = FindSurface(Configurations[pickup.ConfigurationIndex], pickup.Operand.SurfaceNumber);
        if (pickup.Operand.Kind == MultiConfigurationOperandKind.Curvature) surface.RadiusVariable = false;
        if (pickup.Operand.Kind == MultiConfigurationOperandKind.Thickness) surface.ThicknessVariable = false;
        if (pickup.Operand.Kind == MultiConfigurationOperandKind.SemiDiameter) surface.SemiDiameterFixed = true;
    }

    private void ExecutePickupMutation(Action action)
    {
        var snapshots = Configurations.Select(optic => optic.ToSnapshot()).ToArray();
        var pickups = _operandPickups.ToArray(); var variables = _operandVariables.ToArray(); var links = _brokenLinks.ToArray();
        try { action(); ApplyOperandPickups(); }
        catch
        {
            for (var index = 0; index < snapshots.Length; index++) Configurations[index].ApplySnapshot(snapshots[index]);
            _operandPickups.Clear(); _operandPickups.AddRange(pickups);
            _operandVariables.Clear(); _operandVariables.AddRange(variables);
            _brokenLinks.Clear(); foreach (var link in links) _brokenLinks.Add(link);
            throw;
        }
    }
}
