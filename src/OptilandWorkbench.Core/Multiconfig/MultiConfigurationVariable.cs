using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Multiconfig;

/// <summary>Explicit variable identity; independent of editor row order and active configuration.</summary>
public sealed record MultiConfigurationVariable(int ConfigurationIndex, MultiConfigurationOperand Operand)
{
    public static void Validate(IReadOnlyList<MultiConfigurationVariable>? variables,
        IReadOnlyList<MultiConfigurationOperand> rows, IReadOnlyList<Optic> configurations)
    {
        if (variables is null) return;
        if (variables.Count > 65536 || variables.Distinct().Count() != variables.Count)
            throw new ArgumentException("多配置变量重复或数量超过 65536。");
        foreach (var variable in variables)
        {
            if (variable is null || variable.Operand is null || !rows.Contains(variable.Operand)
                || variable.ConfigurationIndex < 0 || variable.ConfigurationIndex >= configurations.Count)
                throw new ArgumentException("多配置变量必须引用现有行和配置。");
            EnsureEditable(variable.Operand, configurations[variable.ConfigurationIndex]);
        }
    }

    public static void EnsureEditable(MultiConfigurationOperand row, Optic optic)
    {
        var initial = row.Read(optic);
        var pickup = row.Kind switch
        {
            MultiConfigurationOperandKind.Thickness => optic.Pickups.ThicknessPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            MultiConfigurationOperandKind.Curvature => optic.Pickups.RadiusPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            MultiConfigurationOperandKind.SemiDiameter => optic.Pickups.SemiDiameterPickups.Any(p => p.TargetSurface == row.SurfaceNumber),
            _ => false
        };
        if (pickup) throw new InvalidOperationException("该参数由表面拾取控制，不能设为独立变量。");
        if (row.Kind == MultiConfigurationOperandKind.SemiDiameter && initial < 0)
            throw new InvalidOperationException("半口径变量不能为负数。");
    }

    public static void Write(MultiConfigurationOperand row, Optic optic, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var surface = optic.SurfaceGroup.Items.Single(s => s.Number == row.SurfaceNumber);
        switch (row.Kind)
        {
            case MultiConfigurationOperandKind.Thickness: surface.Thickness = value; break;
            case MultiConfigurationOperandKind.Curvature: SurfaceCurvatureParameter.Write(surface, value); break;
            case MultiConfigurationOperandKind.Conic: surface.Conic = value; break;
            case MultiConfigurationOperandKind.SemiDiameter:
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                surface.SemiDiameter = value; surface.SemiDiameterFixed = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(row));
        }
    }

    public IOptimizationVariable Create(Optic optic, Action changed)
    {
        EnsureEditable(Operand, optic);
        var initial = Operand.Read(optic);
        // Local search ranges, not Zemax defaults or manufacturing constraints.
        var extent = Operand.Kind switch
        {
            MultiConfigurationOperandKind.Curvature => Math.Max(.025, 2 * Math.Abs(initial)),
            MultiConfigurationOperandKind.Thickness => Math.Max(10, 2 * Math.Abs(initial)),
            _ => Math.Max(1, 2 * Math.Abs(initial))
        };
        var lower = Operand.Kind switch
        {
            MultiConfigurationOperandKind.Curvature => -extent,
            // OpticalSurface currently enforces this minimum; never propose coordinates it would clamp.
            MultiConfigurationOperandKind.SemiDiameter => .1,
            _ => initial - extent
        };
        var upper = Operand.Kind == MultiConfigurationOperandKind.Curvature ? extent : initial + extent;
        return new DelegateVariable($"配置 {ConfigurationIndex + 1} 表面 {Operand.SurfaceNumber} {MultiConfigurationOperand.Code(Operand.Kind)}",
            () => Operand.Read(optic), value => { Write(Operand, optic, value); changed(); },
            lower, upper, extent * .05, new UnitRangeScaler(lower, upper));
    }
}
