using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    private sealed record OptimizationBinding(int SurfaceNumber, bool IsCurvature = false,
        int Layer = 0, CoatingLayerParameter Parameter = CoatingLayerParameter.Multiplier,
        int ConfigurationIndex = -1, MultiConfigurationOperand? Operand = null, string? GradientParameter = null)
    {
        public string Property => GradientParameter is not null ? "material" : Operand?.Property ?? (IsCurvature ? "radius" : "thickness");
    }
    public OptimizerResult OptimizeSurfaceRadius(OpticalSurface surface, string optimizerName, int maxIterations)
    {
        if (IsMultiConfigurationPickupTarget(surface.Number, "radius") || CurrentOptic.Pickups.RadiusPickups.Any(pickup => pickup.TargetSurface == surface.Number))
            throw new InvalidOperationException("拾取半径不能独立优化，请优化源表面或先改为变量。");
        OpticCapabilityPreflight.EnsureSupported(CurrentOptic, OpticCapabilityOperation.Optimization, optimizerName);
        var initialDocument = CaptureDocument();
        try
        {
            var initialRadius = surface.Radius;
            _multiConfiguration.Configurations[_activeConfigurationIndex] = CurrentOptic;
            _multiConfiguration.DetachPropertyLink(_activeConfigurationIndex, surface.Number, "radius");
            var bindings = new[] { new OptimizationBinding(surface.Number, true, ConfigurationIndex: _activeConfigurationIndex) };
            void Changed() => SynchronizeOptimizationState(_multiConfiguration, bindings, _activeConfigurationIndex);
            var problem = CurrentOptic.CreateOptimizationProblem();
            problem.AddVariable(SurfaceCurvatureParameter.Create(surface, Changed));
            using var evaluationScope = ConfigureOptimizationEvaluation(problem, bindings,
                MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic));

            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            CurrentOptic.Pickups.ApplyAll();
            SynchronizeMultiConfigurationProperty(surface, "radius");
            DetachActiveConfigurationAfterOptimization();
            _undoRedo.Capture(initialDocument);
            SetStatus($"{DisplayOptimizerMessage(result.Message)}。半径 {NumericDisplayFormatter.Format(initialRadius)} -> {NumericDisplayFormatter.Format(surface.Radius)}。");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch
        {
            ReplaceDocumentState(initialDocument);
            throw;
        }
    }

    private IReadOnlyList<OptimizationBinding> MarkedOptimizationBindings()
    {
        var bindings = _multiConfiguration.OperandVariables.Select(variable => new OptimizationBinding(
            variable.Operand.SurfaceNumber, variable.Operand.Kind == MultiConfigurationOperandKind.Curvature,
            ConfigurationIndex: variable.ConfigurationIndex, Operand: variable.Operand)).ToList();
        var lastSurfaceNumber = Surfaces.Count == 0 ? -1 : Surfaces[^1].Number;
        foreach (var surface in Surfaces.Where(s => s.Number > 0 && s.Number < lastSurfaceNumber))
        {
            foreach (var curvature in new[] { true, false })
            {
                if (!(curvature ? surface.RadiusVariable : surface.ThicknessVariable)) continue;
                if (curvature ? CurrentOptic.Pickups.RadiusPickups.Any(p => p.TargetSurface == surface.Number)
                    : CurrentOptic.Pickups.ThicknessPickups.Any(p => p.TargetSurface == surface.Number)) continue;
                var property = curvature ? "radius" : "thickness";
                if (IsMultiConfigurationPickupTarget(surface.Number, property)) continue;
                if (!bindings.Any(b => b.ConfigurationIndex == _activeConfigurationIndex && b.SurfaceNumber == surface.Number && b.Property == property))
                    bindings.Add(new(surface.Number, curvature, ConfigurationIndex: _activeConfigurationIndex));
            }
        }
        bindings.AddRange(Surfaces.Where(s => s.Number > 0 && s.CoatingModel is CoherentMultilayerCoating)
            .SelectMany(s => ((CoherentMultilayerCoating)s.CoatingModel).Layers.SelectMany((l, index) =>
                Enum.GetValues<CoatingLayerParameter>().Where(l.Adjustment.IsVariable)
                    .Select(kind => new OptimizationBinding(s.Number, Layer: index + 1, Parameter: kind, ConfigurationIndex: _activeConfigurationIndex)))));
        bindings.AddRange(Surfaces.Where(s => s.Number > 0 && s.Number < lastSurfaceNumber && s.MaterialAfter is GradientIndexMaterial)
            .SelectMany(s => ((GradientIndexMaterial)s.MaterialAfter).Variables.Keys.Order(StringComparer.Ordinal)
                .Select(key => new OptimizationBinding(s.Number, ConfigurationIndex: _activeConfigurationIndex, GradientParameter: key))));
        return bindings;
    }

    public IReadOnlyList<OptimizationVariableResultDto> GetMarkedOptimizationVariables() => MarkedOptimizationBindings().Select(binding =>
    {
        var optic = binding.ConfigurationIndex == _activeConfigurationIndex ? CurrentOptic : _multiConfiguration.Configurations[binding.ConfigurationIndex];
        var surface = optic.SurfaceGroup.Items.Single(s => s.Number == binding.SurfaceNumber);
        var kind = binding.GradientParameter is not null ? OptimizationVariableKind.GradientIndexCoefficient : binding.Layer > 0 ? binding.Parameter switch
        {
            CoatingLayerParameter.Multiplier => OptimizationVariableKind.CoatingMultiplier,
            CoatingLayerParameter.IndexOffset => OptimizationVariableKind.CoatingIndexOffset,
            _ => OptimizationVariableKind.CoatingExtinctionOffset
        } : binding.Operand?.Kind switch
        {
            MultiConfigurationOperandKind.Curvature => OptimizationVariableKind.Curvature,
            MultiConfigurationOperandKind.Conic => OptimizationVariableKind.Conic,
            MultiConfigurationOperandKind.SemiDiameter => OptimizationVariableKind.SemiDiameter,
            _ => binding.IsCurvature ? OptimizationVariableKind.Radius : OptimizationVariableKind.Thickness
        };
        var value = binding.GradientParameter is { } parameter ? GradientIndexParameters.Read(surface, parameter) : binding.Layer > 0 ? CoatingLayerMetrics.Layer(surface, binding.Layer).Adjustment.Value(binding.Parameter)
            : binding.Operand?.Read(optic) ?? (binding.IsCurvature ? surface.Radius : surface.Thickness);
        var label = binding.GradientParameter is { } key ? $"GRIN {key}" : binding.Layer > 0 ? $"膜层 {binding.Layer} {binding.Parameter}" : binding.Operand is { } row
            ? MultiConfigurationOperand.Code(row.Kind) : binding.IsCurvature ? "半径" : "厚度";
        return new OptimizationVariableResultDto(surface.Number, kind, $"配置 {binding.ConfigurationIndex + 1} 表面 {surface.Number} {label}",
            value, value, binding.ConfigurationIndex, binding.Layer, binding.GradientParameter);
    }).ToArray();

    public OptimizerResult OptimizeMarkedVariables(string optimizerName, int maxIterations)
    {
        var bindings = MarkedOptimizationBindings();
        if (bindings.Count == 0) throw new InvalidOperationException("请先设置至少一个优化变量（表面、GRIN 系数、物理膜层或多配置单元格）。");
        var initialDocument = CaptureDocument();
        try
        {
            _multiConfiguration.Configurations[_activeConfigurationIndex] = CurrentOptic;
            MultiConfigurationVariable.Validate(_multiConfiguration.OperandVariables, _multiConfiguration.OperandRows, _multiConfiguration.Configurations);
            foreach (var configuration in bindings.Select(b => b.ConfigurationIndex).Distinct())
                OpticCapabilityPreflight.EnsureSupported(_multiConfiguration.Configurations[configuration], OpticCapabilityOperation.Optimization, optimizerName);
            foreach (var binding in bindings.Where(b => b.Layer == 0))
                _multiConfiguration.DetachPropertyLink(binding.ConfigurationIndex, binding.SurfaceNumber, binding.Property);
            void Changed() => SynchronizeOptimizationState(_multiConfiguration, bindings, _activeConfigurationIndex);
            var problem = CurrentOptic.CreateOptimizationProblem();
            foreach (var binding in bindings)
            {
                var optic = _multiConfiguration.Configurations[binding.ConfigurationIndex];
                var surface = optic.SurfaceGroup.Items.Single(s => s.Number == binding.SurfaceNumber);
                if (binding.Operand is { } row)
                    problem.AddVariable(new MultiConfigurationVariable(binding.ConfigurationIndex, row).Create(optic, Changed));
                else if (binding.Layer > 0)
                    problem.AddVariable(CoatingLayerMetrics.CreateVariable(optic, surface, binding.Layer, binding.Parameter));
                else if (binding.GradientParameter is { } parameter)
                {
                    if (GradientIndexParameters.ScalarParaxialVariableDisabledReason(
                        GradientIndexProfileData.Kind(GradientIndexParameters.Require(surface).Profile), parameter) is { } reason)
                        throw new NotSupportedException(reason);
                    problem.AddVariable(GradientIndexParameters.CreateVariable(optic, surface, parameter, Changed));
                }
                else if (binding.IsCurvature)
                    problem.AddVariable(SurfaceCurvatureParameter.Create(surface, Changed));
                else
                {
                    var initial = surface.Thickness;
                    var lower = Math.Min(.001, initial);
                    var upper = Math.Max(initial + 10, Math.Max(1, initial * 3));
                    problem.AddVariable(new DelegateVariable($"配置 {binding.ConfigurationIndex + 1} 表面 {surface.Number} 厚度",
                        () => surface.Thickness, value => { surface.Thickness = value; Changed(); }, lower, upper,
                        Math.Max(.05, Math.Abs(initial) * .05), new UnitRangeScaler(lower, upper)));
                }
            }
            var operands = CurrentOptic.MeritFunctionOperands.Select(operand => operand.Clone()).ToArray();
            if (!operands.Any(operand => operand.Enabled && MeritFunctionCatalog.CanonicalType(operand.Type) is not ("BLNK" or "DMFS")))
                operands = MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic).ToArray();
            using var evaluationScope = ConfigureOptimizationEvaluation(problem, bindings, operands);
            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            Changed();
            DetachActiveConfigurationAfterOptimization();
            _undoRedo.Capture(initialDocument);
            SetStatus($"{DisplayOptimizerMessage(result.Message)}。{problem.Variables.Count} 个变量，评价函数 {NumericDisplayFormatter.Format(result.InitialMerit)} -> {NumericDisplayFormatter.Format(result.FinalMerit)}。");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch { ReplaceDocumentState(initialDocument); throw; }
    }

    private void DetachActiveConfigurationAfterOptimization() =>
        _multiConfiguration.Configurations[_activeConfigurationIndex] = Optic.FromSnapshot(CurrentOptic.ToSnapshot());

    private static void SynchronizeOptimizationState(MultiConfiguration configurations, IReadOnlyList<OptimizationBinding> bindings, int activeIndex)
    {
        foreach (var binding in bindings.Where(b => b.GradientParameter is not null
            && (b.ConfigurationIndex < 0 ? activeIndex : b.ConfigurationIndex) == 0))
            configurations.PropagateBaseProperty(binding.SurfaceNumber, "material");
        if (configurations.OperandPickups.Count > 0) { configurations.ApplyOperandPickups(); return; }
        foreach (var optic in configurations.Configurations) { optic.Pickups.ApplyAll(); optic.SurfaceGroup.Renumber(); }
        foreach (var binding in bindings.Where(b => b.Layer == 0 && (b.ConfigurationIndex < 0 ? activeIndex : b.ConfigurationIndex) == 0))
        {
            configurations.PropagateBaseProperty(binding.SurfaceNumber, binding.Property);
            var optic = configurations.Configurations[0];
            var targets = binding.Property switch
            {
                "radius" => optic.Pickups.RadiusPickups.Select(p => p.TargetSurface),
                "thickness" => optic.Pickups.ThicknessPickups.Select(p => p.TargetSurface),
                "semiDiameter" => optic.Pickups.SemiDiameterPickups.Select(p => p.TargetSurface),
                _ => Enumerable.Empty<int>()
            };
            foreach (var target in targets.Distinct()) configurations.PropagateBaseProperty(target, binding.Property);
        }
        foreach (var optic in configurations.Configurations) { optic.Pickups.ApplyAll(); optic.SurfaceGroup.Renumber(); }
    }

    private IDisposable ConfigureOptimizationEvaluation(OptimizationProblem problem,
        IReadOnlyList<OptimizationBinding> bindings,
        IReadOnlyList<MeritOperandDefinition> operands)
    {
        SyncActiveConfigurationFromCurrent();
        _multiConfiguration.Configurations[_activeConfigurationIndex] = CurrentOptic;
        CurrentOptic.Pickups.ApplyAll();
        var definitions = operands.Select(operand => operand.Clone()).ToArray();
        foreach (var operand in MeritFunctionCatalog.CreateOperands(CreateMeritConfigurationContext(), definitions))
            problem.AddOperand(operand);

        var snapshots = _multiConfiguration.Configurations.Select(optic => optic.ToSnapshot()).ToArray();
        var brokenLinks = _multiConfiguration.BrokenLinks;
        var operandRows = _multiConfiguration.OperandRows.ToArray();
        var operandVariables = _multiConfiguration.OperandVariables.ToArray();
        var operandPickups = _multiConfiguration.OperandPickups.ToArray();
        var activeIndex = _activeConfigurationIndex;
        var optics = new ThreadLocal<MultiConfiguration>(() => new MultiConfiguration(
            snapshots.Select(Optic.FromSnapshot), brokenLinks, operandRows, operandVariables, operandPickups));
        problem.SetIndependentValueEvaluator(values =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (values.Count != bindings.Count)
                throw new ArgumentException("优化变量数量与表面绑定数量不一致。", nameof(values));
            var configurations = optics.Value!;
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var optic = configurations.Configurations[binding.ConfigurationIndex < 0 ? activeIndex : binding.ConfigurationIndex];
                var surface = optic.SurfaceGroup.Items.First(item => item.Number == binding.SurfaceNumber);
                if (binding.Operand is { } row)
                    MultiConfigurationVariable.Write(row, optic, values[index]);
                else if (binding.Layer > 0)
                    CoatingLayerMetrics.Write(optic, surface, binding.Layer, binding.Parameter, values[index]);
                else if (binding.GradientParameter is { } parameter)
                    GradientIndexParameters.Write(optic, surface, parameter, values[index]);
                else if (binding.IsCurvature)
                    SurfaceCurvatureParameter.Write(surface, values[index]);
                else
                    surface.Thickness = values[index];
            }

            SynchronizeOptimizationState(configurations, bindings, activeIndex);
            return MeritFunctionCatalog.EvaluateOptimizationValues(
                new MeritConfigurationContext(configurations.Configurations, activeIndex, configurations.OperandRows), definitions);
        });
        return optics;
    }
}
