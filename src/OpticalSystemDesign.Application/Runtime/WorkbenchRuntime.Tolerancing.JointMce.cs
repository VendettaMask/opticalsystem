using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    private sealed record JointMceBinding(ToleranceMceAdjustmentDto Row, double Nominal,
        IReadOnlyList<(int Configuration, int Surface, double Factor)> Effects)
    {
        public string Name => $"{(Row.Compensator ? "CMCO" : "TMCO")} 配置 {Row.Configuration} 行 {Row.Row} 面 {Row.SurfaceNumber}";

        public void Apply(IReadOnlyDictionary<int, Optic> optics, double delta)
        {
            foreach (var (configuration, surface, factor) in Effects)
                if (optics.TryGetValue(configuration, out var optic))
                    SetToleranceThickness(optic, surface, FindSurface(optic, surface).Thickness + factor * delta);
        }

        public IOptimizationVariable CreateCompensator(IReadOnlyDictionary<int, Optic> optics)
        {
            double Get() => FindSurface(optics[Row.Configuration], Row.SurfaceNumber).Thickness;
            return new DelegateVariable(Name, Get, value => Apply(optics, value - Get()),
                Nominal + Row.Minimum, Nominal + Row.Maximum, Math.Max(1e-8, (Row.Maximum - Row.Minimum) / 10));
        }
    }

    private static JointMceBinding[] BindJointMce(JointToleranceSnapshot snapshot, Optic[] optics,
        IReadOnlyList<int> selected, IReadOnlyList<ToleranceMceAdjustmentDto> rows)
    {
        if (rows.Count > 32) throw new ArgumentException("TMCO 和 CMCO 合计最多 32 行。");
        var dependencies = new Dictionary<(int Configuration, int Surface), ((int Configuration, int Surface) Source, double Scale)>();
        var broken = snapshot.BrokenLinks.Where(link => link.Property == "thickness")
            .Select(link => (link.ConfigurationIndex + 1, link.SurfaceNumber)).ToHashSet();
        var externalInputs = new HashSet<(int, int)>();
        var externalOutputs = new HashSet<(int, int)>();
        for (var i = 0; i < optics.Length; i++)
        {
            var config = i + 1;
            foreach (var surface in optics[i].SurfaceGroup.Items)
                if (i > 0 && !broken.Contains((config, surface.Number))
                    && optics[0].SurfaceGroup.Items.Any(item => item.Number == surface.Number))
                    dependencies[(config, surface.Number)] = ((1, surface.Number), 1);
            foreach (var pickup in optics[i].Pickups.ThicknessPickups)
                dependencies[(config, pickup.TargetSurface)] = ((config, pickup.SourceSurface), pickup.Scale);
        }
        foreach (var pickup in snapshot.Pickups)
        {
            var target = (pickup.ConfigurationIndex + 1, pickup.Operand.SurfaceNumber);
            var source = (pickup.SourceConfigurationIndex + 1, pickup.SourceOperand.SurfaceNumber);
            if (pickup.Operand.Kind == MultiConfigurationOperandKind.Thickness)
            {
                if (pickup.SourceOperand.Kind == MultiConfigurationOperandKind.Thickness)
                    dependencies[target] = (source, pickup.Scale);
                else
                {
                    dependencies.Remove(target);
                    externalInputs.Add(target);
                }
            }
            else if (pickup.SourceOperand.Kind == MultiConfigurationOperandKind.Thickness)
                externalOutputs.Add(source);
        }

        var dependents = dependencies.ToLookup(pair => pair.Value.Source);
        var claims = new HashSet<(bool Compensator, int Configuration, int Surface)>();
        var result = new List<JointMceBinding>();
        foreach (var row in rows)
        {
            var label = $"{(row.Compensator ? "CMCO" : "TMCO")} 配置 {row.Configuration} 行 {row.Row}";
            if (!selected.Contains(row.Configuration) || row.Row < 1 || row.Row > snapshot.OperandRows.Length
                || snapshot.OperandRows[row.Row - 1] != new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, row.SurfaceNumber))
                throw new ArgumentException($"{label} 的配置或厚度行引用无效。");
            if (row.SurfaceNumber <= 0 || row.SurfaceNumber >= optics[row.Configuration - 1].SurfaceGroup.Items.Count - 1
                || row.Stage is < 1 or > 8 || !Enum.IsDefined(row.Distribution)
                || !double.IsFinite(row.Minimum) || !double.IsFinite(row.Maximum) || row.Minimum > row.Maximum
                || (row.Compensator && (row.Minimum >= row.Maximum || row.Minimum > 0 || row.Maximum < 0)))
                throw new ArgumentException($"{label} 的表面、上下限、分布或阶段无效。");

            var source = (row.Configuration, row.SurfaceNumber);
            if (dependencies.ContainsKey(source) || externalInputs.Contains(source))
                throw new ArgumentException($"{label} 由拾取或基准配置控制，请选其独立源。");
            var effects = new List<(int Configuration, int Surface, double Factor)>();
            var visited = new HashSet<(int, int)>();
            var queue = new Queue<((int Configuration, int Surface) Cell, double Scale)>();
            queue.Enqueue((source, 1));
            while (queue.TryDequeue(out var item))
            {
                var (configuration, surfaceNumber) = item.Cell;
                if (!visited.Add(item.Cell) || externalOutputs.Contains(item.Cell))
                    throw new ArgumentException($"{label} 含循环或跨参数拾取，无法按厚度传播。");
                var optic = optics[configuration - 1];
                var surface = FindSurface(optic, surfaceNumber);
                if (surfaceNumber <= 0
                    || surfaceNumber >= optic.SurfaceGroup.Items.Count - 1
                    || !double.IsFinite(surface.Thickness + item.Scale * row.Minimum)
                    || !double.IsFinite(surface.Thickness + item.Scale * row.Maximum)
                    || Math.Min(surface.Thickness + item.Scale * row.Minimum, surface.Thickness + item.Scale * row.Maximum) < 0
                    || (row.Compensator && surface.MaterialAfter.RefractiveIndex(587.6) > 1.0001))
                    throw new ArgumentException($"{label} 影响配置 {configuration} 面 {surfaceNumber}，与可调间隔或求解范围冲突。");
                if (!claims.Add((row.Compensator, configuration, surfaceNumber)))
                    throw new ArgumentException($"{label} 与另一个同类操作数重复写入配置 {configuration} 面 {surfaceNumber}。");
                effects.Add((configuration, surfaceNumber, item.Scale));
                foreach (var dependent in dependents[item.Cell])
                    if (dependent.Value.Scale != 0)
                        queue.Enqueue((dependent.Key, dependent.Value.Scale * item.Scale));
            }
            result.Add(new(row, FindSurface(optics[row.Configuration - 1], row.SurfaceNumber).Thickness, effects));
        }
        return result.ToArray();
    }
}
