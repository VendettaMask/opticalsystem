using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Multiconfig;

public sealed partial class MultiConfiguration
{
    private sealed record Cell(int Configuration, int Surface, MultiConfigurationOperandKind Kind, string? Parameter = null)
    {
        public MultiConfigurationOperand Row => new(Kind, Surface, Parameter);
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
        var cells = surfaces.SelectMany(p => new[] { MultiConfigurationOperandKind.Thickness, MultiConfigurationOperandKind.Curvature,
                MultiConfigurationOperandKind.Conic, MultiConfigurationOperandKind.SemiDiameter }
            .Select(kind => new Cell(p.Key.configuration, p.Key.Number, kind))
            .Concat(SurfaceShapeParameters.CoefficientNames(p.Value).Select(parameter => new Cell(p.Key.configuration, p.Key.Number, MultiConfigurationOperandKind.ShapeCoefficient, parameter)))
            .Concat(p.Value.MaterialAfter is Materials.GradientIndexMaterial material
                ? Materials.GradientIndexProfileData.Keys(Materials.GradientIndexProfileData.Kind(material.Profile))
                    .Select(parameter => new Cell(p.Key.configuration, p.Key.Number, MultiConfigurationOperandKind.GradientIndexCoefficient, parameter)) : [])
            .Concat(p.Value.MaterialAfter is Materials.ModelGlassMaterial ? Materials.ModelGlassMaterial.ParameterNames
                .Select(parameter => new Cell(p.Key.configuration, p.Key.Number, MultiConfigurationOperandKind.ModelGlassCoefficient, parameter)) : []))
            .Concat(Configurations.SelectMany((_, configuration) => _operandRows.Where(row => row.IsSystemParameter)
                .Select(row => new Cell(configuration, row.SurfaceNumber, row.Kind)))).ToArray();
        var values = cells.ToDictionary(cell => cell, cell => cell.Row.IsSystemParameter
            ? cell.Row.Read(Configurations[cell.Configuration]) : ReadStored(surfaces[(cell.Configuration, cell.Surface)], cell.Kind, cell.Parameter));
        var rules = new Dictionary<Cell, CellRule>();
        var pickups = _operandPickups.ToDictionary(p => new Cell(p.ConfigurationIndex, p.Operand.SurfaceNumber, p.Operand.Kind, p.Operand.Parameter));
        foreach (var cell in cells)
        {
            var optic = Configurations[cell.Configuration];
            if (pickups.TryGetValue(cell, out var pickup))
            {
                var source = new Cell(pickup.SourceConfigurationIndex, pickup.SourceOperand.SurfaceNumber, pickup.SourceOperand.Kind, pickup.SourceOperand.Parameter);
                rules.Add(cell, new(cell, [source], state =>
                {
                    // Use the formal surface coordinate conversion, including a plane at zero curvature.
                    var coordinate = state[source];
                    if (source.Kind == MultiConfigurationOperandKind.Curvature)
                    {
                        var sourceSurface = surfaces[(source.Configuration, source.Surface)].Clone();
                        WriteStored(sourceSurface, source.Kind, state[source], source.Parameter);
                        coordinate = SurfaceCurvatureParameter.Read(sourceSurface);
                    }
                    var result = PickupManager.EvaluateValue(coordinate, pickup.Scale, pickup.Offset, "多配置", double.IsFinite);
                    if (cell.Row.IsSystemParameter)
                    {
                        cell.Row.ValidateSystemValue(optic, result);
                        return result;
                    }
                    var targetSurface = surfaces[(cell.Configuration, cell.Surface)].Clone();
                    if (cell.Kind == MultiConfigurationOperandKind.Curvature) SurfaceCurvatureParameter.Write(targetSurface, result);
                    else WriteStored(targetSurface, cell.Kind, result, cell.Parameter);
                    return ReadStored(targetSurface, cell.Kind, cell.Parameter);
                }));
                continue;
            }
            // System rows are independent per configuration. Only an explicit
            // MCE pickup links them; surface-number base links never apply.
            if (cell.Row.IsSystemParameter) continue;
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
                    cell.Row.Property, value => double.IsFinite(value) && (cell.Kind != MultiConfigurationOperandKind.SemiDiameter || value >= 0))));
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
        var sourceQueue = new Queue<Cell>(_operandPickups.Select(p => new Cell(p.SourceConfigurationIndex, p.SourceOperand.SurfaceNumber, p.SourceOperand.Kind, p.SourceOperand.Parameter)));
        while (sourceQueue.TryDequeue(out var source))
        {
            if (!visitedSources.Add(source)) continue;
            if (rules.TryGetValue(source, out var rule))
                foreach (var dependency in rule.Sources) sourceQueue.Enqueue(dependency);
            else if (source.Kind == MultiConfigurationOperandKind.SemiDiameter
                && surfaces.TryGetValue((source.Configuration, source.Surface), out var sourceSurface)
                && (!sourceSurface.SemiDiameterFixed || sourceSurface.SemiDiameterMaximum))
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
            if (cell.Row.IsSystemParameter) cell.Row.ValidateSystemValue(Configurations[cell.Configuration], values[cell]);
            else
            {
                var staged = surfaces[(cell.Configuration, cell.Surface)].Clone();
                WriteStored(staged, cell.Kind, values[cell], cell.Parameter);
            }
            ordered.Add(rule);
            foreach (var target in dependents[cell]) if (--pending[target] == 0) ready.Enqueue(target);
        }
        if (ordered.Count != rules.Count) throw new InvalidOperationException("多配置、表面拾取或后焦距求解之间存在循环引用。");
        foreach (var rule in ordered)
        {
            var cell = rule.Target;
            if (cell.Row.IsSystemParameter)
            {
                cell.Row.WriteSystemValue(Configurations[cell.Configuration], values[cell]);
                continue;
            }
            var surface = surfaces[(cell.Configuration, cell.Surface)];
            WriteStored(surface, cell.Kind, values[cell], cell.Parameter);
            if (cell.Kind is MultiConfigurationOperandKind.GradientIndexCoefficient or MultiConfigurationOperandKind.ModelGlassCoefficient)
                SynchronizeFollowingMedia(Configurations[cell.Configuration], cell.Surface);
            if (cell.Kind == MultiConfigurationOperandKind.SemiDiameter)
            {
                surface.SemiDiameterFixed = rule.BaseLink
                    ? surfaces[(rule.Sources[0].Configuration, rule.Sources[0].Surface)].SemiDiameterFixed : true;
                surface.SemiDiameterMaximum = rule.BaseLink
                    && surfaces[(rule.Sources[0].Configuration, rule.Sources[0].Surface)].SemiDiameterMaximum;
            }
        }
        foreach (var optic in Configurations) optic.SurfaceGroup.Renumber();
    }

    private static double ReadStored(OpticalSurface surface, MultiConfigurationOperandKind kind, string? parameter) => kind switch
    {
        MultiConfigurationOperandKind.Curvature => surface.Radius,
        MultiConfigurationOperandKind.Thickness => surface.Thickness,
        MultiConfigurationOperandKind.Conic => surface.Conic,
        MultiConfigurationOperandKind.SemiDiameter => surface.SemiDiameter,
        MultiConfigurationOperandKind.ShapeCoefficient => SurfaceShapeParameters.Read(surface, parameter!),
        MultiConfigurationOperandKind.GradientIndexCoefficient => GradientIndexParameters.Read(surface, parameter!),
        MultiConfigurationOperandKind.ModelGlassCoefficient => ModelGlassParameters.Read(surface, parameter!),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static void WriteStored(OpticalSurface surface, MultiConfigurationOperandKind kind, double value, string? parameter)
    {
        switch (kind)
        {
            case MultiConfigurationOperandKind.Curvature: surface.Radius = value; break;
            case MultiConfigurationOperandKind.Thickness: surface.Thickness = value; break;
            case MultiConfigurationOperandKind.Conic: surface.Conic = value; break;
            case MultiConfigurationOperandKind.ShapeCoefficient: SurfaceShapeParameters.Write(surface, parameter!, value); break;
            case MultiConfigurationOperandKind.GradientIndexCoefficient: GradientIndexParameters.WriteStoredValue(surface, parameter!, value); break;
            case MultiConfigurationOperandKind.ModelGlassCoefficient: ModelGlassParameters.WriteStored(surface, parameter!, value); break;
            case MultiConfigurationOperandKind.SemiDiameter:
                if (!double.IsFinite(value) || value < 0) throw new InvalidOperationException("半口径拾取结果必须是非负的有限数值。");
                surface.SemiDiameter = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
