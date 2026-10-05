using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Multiconfig;

public sealed partial class MultiConfiguration
{
    private sealed record Cell(int Configuration, int Surface, MultiConfigurationOperandKind Kind)
    {
        public MultiConfigurationOperand Row => new(Kind, Surface);
    }
    private sealed record CellRule(Cell Target, Cell[] Sources, Func<IReadOnlyDictionary<Cell, double>, double> Calculate, bool BaseLink = false);

    /// <summary>Resolve MCE, surface pickups, base links and image solves in one dependency order.</summary>
    public void ApplyOperandPickups()
    {
        if (_operandPickups.Count == 0) return;
        MultiConfigurationPickup.Validate(_operandPickups, _operandRows, Configurations, _operandVariables);
        var surfaces = Configurations.Select((optic, configuration) => (optic, configuration))
            .SelectMany(p => p.optic.SurfaceGroup.Items.Select(surface => (p.configuration, surface)))
            .ToDictionary(p => (p.configuration, p.surface.Number), p => p.surface);
        var cells = surfaces.SelectMany(p => Enum.GetValues<MultiConfigurationOperandKind>()
            .Select(kind => new Cell(p.Key.configuration, p.Key.Number, kind))).ToArray();
        var values = cells.ToDictionary(cell => cell, cell => ReadStored(surfaces[(cell.Configuration, cell.Surface)], cell.Kind));
        var rules = new Dictionary<Cell, CellRule>();
        var pickups = _operandPickups.ToDictionary(p => new Cell(p.ConfigurationIndex, p.Operand.SurfaceNumber, p.Operand.Kind));
        foreach (var cell in cells)
        {
            var optic = Configurations[cell.Configuration];
            if (pickups.TryGetValue(cell, out var pickup))
            {
                var source = new Cell(pickup.SourceConfigurationIndex, pickup.SourceOperand.SurfaceNumber, pickup.SourceOperand.Kind);
                rules.Add(cell, new(cell, [source], state =>
                {
                    // Use the formal surface coordinate conversion, including a plane at zero curvature.
                    var sourceSurface = surfaces[(source.Configuration, source.Surface)].Clone();
                    WriteStored(sourceSurface, source.Kind, state[source]);
                    var coordinate = source.Kind == MultiConfigurationOperandKind.Curvature
                        ? SurfaceCurvatureParameter.Read(sourceSurface) : state[source];
                    var result = PickupManager.EvaluateValue(coordinate, pickup.Scale, pickup.Offset, "多配置", double.IsFinite);
                    var targetSurface = surfaces[(cell.Configuration, cell.Surface)].Clone();
                    if (cell.Kind == MultiConfigurationOperandKind.Curvature) SurfaceCurvatureParameter.Write(targetSurface, result);
                    else WriteStored(targetSurface, cell.Kind, result);
                    return ReadStored(targetSurface, cell.Kind);
                }));
                continue;
            }
            if (cell.Kind == MultiConfigurationOperandKind.Thickness && cell.Surface == optic.SurfaceGroup.Items[^1].Number && optic.Solves.KeepImageAtBackFocus)
            {
                var all = optic.SurfaceGroup.Items.Select(s => new Cell(cell.Configuration, s.Number, cell.Kind)).ToArray();
                rules.Add(cell, new(cell, all[..^1], state => optic.Solves.CalculateImageThickness(all.Select(c => state[c]).ToArray())));
                continue;
            }
            if (cell.Kind == MultiConfigurationOperandKind.Curvature && optic.Pickups.RadiusPickups.LastOrDefault(p => p.TargetSurface == cell.Surface) is { } radius)
            {
                var source = cell with { Surface = radius.SourceSurface };
                rules.Add(cell, new(cell, [source], state => PickupManager.EvaluateRadius(radius, state[source])));
                continue;
            }
            var valuePickup = cell.Kind switch
            {
                MultiConfigurationOperandKind.Thickness => optic.Pickups.ThicknessPickups.LastOrDefault(p => p.TargetSurface == cell.Surface),
                MultiConfigurationOperandKind.SemiDiameter => optic.Pickups.SemiDiameterPickups.LastOrDefault(p => p.TargetSurface == cell.Surface),
                _ => null
            };
            if (valuePickup is not null)
            {
                var source = cell with { Surface = valuePickup.SourceSurface };
                rules.Add(cell, new(cell, [source], state => PickupManager.EvaluateValue(state[source], valuePickup.Scale, valuePickup.Offset,
                    cell.Row.Property, value => double.IsFinite(value) && (cell.Kind != MultiConfigurationOperandKind.SemiDiameter || value >= .1))));
                continue;
            }
            if (cell.Configuration > 0 && !_brokenLinks.Contains((cell.Configuration, cell.Surface, cell.Row.Property)))
            {
                var source = cell with { Configuration = 0 };
                if (values.ContainsKey(source)) rules.Add(cell, new(cell, [source], state => state[source], true));
            }
        }

        // An automatic semi-diameter is a traced output, not an independent scalar input.
        // Reject that dependency until automatic ray-envelope nodes are part of this graph.
        var visitedSources = new HashSet<Cell>();
        var sourceQueue = new Queue<Cell>(_operandPickups.Select(p => new Cell(p.SourceConfigurationIndex, p.SourceOperand.SurfaceNumber, p.SourceOperand.Kind)));
        while (sourceQueue.TryDequeue(out var source))
        {
            if (!visitedSources.Add(source)) continue;
            if (rules.TryGetValue(source, out var rule))
                foreach (var dependency in rule.Sources) sourceQueue.Enqueue(dependency);
            else if (source.Kind == MultiConfigurationOperandKind.SemiDiameter
                && surfaces.TryGetValue((source.Configuration, source.Surface), out var sourceSurface) && !sourceSurface.SemiDiameterFixed)
                throw new InvalidOperationException("拾取源依赖自动半口径；请先将源半口径设为固定值或变量，再设置拾取。");
        }

        var dependents = rules.Values.SelectMany(rule => rule.Sources.Select(source => (source, rule.Target))).ToLookup(p => p.source, p => p.Target);
        var pending = rules.Values.ToDictionary(rule => rule.Target, rule => rule.Sources.Count(rules.ContainsKey));
        foreach (var source in rules.Values.SelectMany(r => r.Sources))
            if (!values.ContainsKey(source)) throw new InvalidOperationException("拾取引用的表面不存在。");
        var ready = new Queue<Cell>(pending.Where(p => p.Value == 0).Select(p => p.Key));
        var ordered = new List<CellRule>();
        while (ready.TryDequeue(out var cell))
        {
            var rule = rules[cell];
            values[cell] = rule.Calculate(values);
            // Validate every value before mutating any real surface.
            var staged = surfaces[(cell.Configuration, cell.Surface)].Clone();
            WriteStored(staged, cell.Kind, values[cell]);
            ordered.Add(rule);
            foreach (var target in dependents[cell]) if (--pending[target] == 0) ready.Enqueue(target);
        }
        if (ordered.Count != rules.Count) throw new InvalidOperationException("多配置、表面拾取或后焦距求解之间存在循环引用。");
        foreach (var rule in ordered)
        {
            var cell = rule.Target; var surface = surfaces[(cell.Configuration, cell.Surface)];
            WriteStored(surface, cell.Kind, values[cell]);
            if (cell.Kind == MultiConfigurationOperandKind.SemiDiameter)
                surface.SemiDiameterFixed = rule.BaseLink
                    ? surfaces[(rule.Sources[0].Configuration, rule.Sources[0].Surface)].SemiDiameterFixed : true;
        }
        foreach (var optic in Configurations) optic.SurfaceGroup.Renumber();
    }

    private static double ReadStored(OpticalSurface surface, MultiConfigurationOperandKind kind) => kind switch
    {
        MultiConfigurationOperandKind.Curvature => surface.Radius,
        MultiConfigurationOperandKind.Thickness => surface.Thickness,
        MultiConfigurationOperandKind.Conic => surface.Conic,
        MultiConfigurationOperandKind.SemiDiameter => surface.SemiDiameter,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static void WriteStored(OpticalSurface surface, MultiConfigurationOperandKind kind, double value)
    {
        switch (kind)
        {
            case MultiConfigurationOperandKind.Curvature: surface.Radius = value; break;
            case MultiConfigurationOperandKind.Thickness: surface.Thickness = value; break;
            case MultiConfigurationOperandKind.Conic: surface.Conic = value; break;
            case MultiConfigurationOperandKind.SemiDiameter:
                if (!double.IsFinite(value) || value < .1) throw new InvalidOperationException("半口径拾取结果必须不小于 0.1 mm。");
                surface.SemiDiameter = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
