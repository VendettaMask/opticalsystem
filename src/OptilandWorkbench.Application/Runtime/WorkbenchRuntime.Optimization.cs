using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public OptimizerResult OptimizeSurfaceRadius(OpticalSurface surface, string optimizerName, int maxIterations)
    {
        if (CurrentOptic.Pickups.RadiusPickups.Any(pickup => pickup.TargetSurface == surface.Number))
            throw new InvalidOperationException("拾取半径不能独立优化，请优化源表面或先改为变量。");
        OpticCapabilityPreflight.EnsureSupported(CurrentOptic, OpticCapabilityOperation.Optimization, optimizerName);
        var initialDocument = CaptureDocument();
        try
        {
            var initialRadius = surface.Radius;
            var problem = CurrentOptic.CreateOptimizationProblem();
            problem.AddVariable(SurfaceCurvatureParameter.Create(surface, () => CurrentOptic.Pickups.ApplyAll()));
            using var evaluationScope = ConfigureOptimizationEvaluation(problem,
                new[] { (SurfaceNumber: surface.Number, IsCurvature: true) },
                MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic));

            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            CurrentOptic.Pickups.ApplyAll();
            SynchronizeMultiConfigurationProperty(surface, "radius");
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

    public OptimizerResult OptimizeMarkedVariables(string optimizerName, int maxIterations)
    {
        OpticCapabilityPreflight.EnsureSupported(CurrentOptic, OpticCapabilityOperation.Optimization, optimizerName);
        var lastSurfaceNumber = Surfaces.Count == 0 ? -1 : Surfaces[^1].Number;
        var selected = Surfaces
            .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
            .Where(surface => surface.RadiusVariable || surface.ThicknessVariable)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("请先在镜头数据中设置至少一个半径变量或厚度变量。");

        var initialDocument = CaptureDocument();
        try
        {
            var problem = CurrentOptic.CreateOptimizationProblem();
            var bindings = new List<(int SurfaceNumber, bool IsCurvature)>();
            foreach (var surface in selected)
            {
                if (surface.RadiusVariable && !CurrentOptic.Pickups.RadiusPickups.Any(pickup => pickup.TargetSurface == surface.Number))
                {
                    problem.AddVariable(SurfaceCurvatureParameter.Create(surface, () => CurrentOptic.Pickups.ApplyAll()));
                    bindings.Add((surface.Number, true));
                }

                if (surface.ThicknessVariable
                    && !CurrentOptic.Pickups.ThicknessPickups.Any(pickup => pickup.TargetSurface == surface.Number))
                {
                    var initial = surface.Thickness;
                    // Keep an existing zero/signed spacing in the starting prescription.
                    var lower = Math.Min(0.001, initial);
                    var upper = Math.Max(initial + 10, Math.Max(1, initial * 3));
                    problem.AddVariable(new DelegateVariable(
                        $"表面 {surface.Number} 厚度",
                        () => surface.Thickness,
                        value =>
                        {
                            surface.Thickness = value;
                            CurrentOptic.Pickups.ApplyAll();
                            CurrentOptic.SurfaceGroup.Renumber();
                        },
                        lower, upper,
                        Math.Max(0.05, Math.Abs(initial) * 0.05),
                        new UnitRangeScaler(lower, upper)));
                    bindings.Add((surface.Number, false));
                }
            }

            if (problem.Variables.Count == 0)
                throw new InvalidOperationException("拾取参数不能作为独立变量，请设置源表面为变量。");
            var operands = CurrentOptic.MeritFunctionOperands.Select(operand => operand.Clone()).ToArray();
            if (!operands.Any(operand => operand.Enabled
                && MeritFunctionCatalog.CanonicalType(operand.Type) is not ("BLNK" or "DMFS")))
                operands = MeritFunctionCatalog.CreateDefaultRmsSpot(CurrentOptic).ToArray();
            using var evaluationScope = ConfigureOptimizationEvaluation(problem, bindings, operands);

            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            CurrentOptic.Pickups.ApplyAll();
            CurrentOptic.SurfaceGroup.Renumber();
            foreach (var binding in bindings.Distinct())
                SynchronizeMultiConfigurationProperty(
                    Surfaces.First(item => item.Number == binding.SurfaceNumber),
                    binding.IsCurvature ? "radius" : "thickness");

            _undoRedo.Capture(initialDocument);
            SetStatus($"{DisplayOptimizerMessage(result.Message)}。{problem.Variables.Count} 个变量，评价函数 {NumericDisplayFormatter.Format(result.InitialMerit)} -> {NumericDisplayFormatter.Format(result.FinalMerit)}。");
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

    private IDisposable ConfigureOptimizationEvaluation(OptimizationProblem problem,
        IReadOnlyList<(int SurfaceNumber, bool IsCurvature)> bindings,
        IReadOnlyList<MeritOperandDefinition> operands)
    {
        CurrentOptic.Pickups.ApplyAll();
        var definitions = operands.Select(operand => operand.Clone()).ToArray();
        foreach (var operand in MeritFunctionCatalog.CreateOperands(CurrentOptic, definitions))
            problem.AddOperand(operand);

        var snapshot = CurrentOptic.ToSnapshot();
        var optics = new ThreadLocal<Optic>(() => Optic.FromSnapshot(snapshot));
        problem.SetIndependentValueEvaluator(values =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (values.Count != bindings.Count)
                throw new ArgumentException("优化变量数量与表面绑定数量不一致。", nameof(values));
            var optic = optics.Value!;
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                var surface = optic.SurfaceGroup.Items.First(item => item.Number == binding.SurfaceNumber);
                if (binding.IsCurvature)
                    SurfaceCurvatureParameter.Write(surface, values[index]);
                else
                    surface.Thickness = values[index];
            }

            optic.Pickups.ApplyAll();
            optic.SurfaceGroup.Renumber();
            return MeritFunctionCatalog.EvaluateOptimizationValues(optic, definitions);
        });
        return optics;
    }
}
