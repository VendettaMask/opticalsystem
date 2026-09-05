using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public OptimizerResult OptimizeSurfaceRadius(OpticalSurface surface, string optimizerName, int maxIterations)
    {
        if (CurrentOptic.Pickups.RadiusPickups.Any(pickup => pickup.TargetSurface == surface.Number))
            throw new InvalidOperationException("拾取半径不能独立优化，请优化源表面或先改为变量。");
        OpticCapabilityPreflight.EnsureSupported(
            CurrentOptic,
            OpticCapabilityOperation.Optimization,
            optimizerName);
        var initialDocument = CaptureDocument();
        try
        {
            if (surface.IsPlane)
            {
                SetSurfaceRadius(surface, 40);
            }

            var initialRadius = surface.Radius;
            var span = Math.Max(10, Math.Abs(initialRadius) * 1.5);
            var lower = Math.Max(-1_000_000, initialRadius - span);
            var upper = Math.Min(1_000_000, initialRadius + span);
            var problem = CurrentOptic.CreateOptimizationProblem();
            problem.AddVariable(new DelegateVariable(
                $"Surface {surface.Number} radius",
                () => surface.Radius,
                next =>
                {
                    SetSurfaceRadius(surface, next);
                    CurrentOptic.Pickups.ApplyAll();
                },
                lower,
                upper,
                stepHint: Math.Max(0.25, span * 0.1),
                scaler: new UnitRangeScaler(lower, upper)));
            problem.AddOperand(new Operand(
                "RMS spot radius",
                0,
                1,
                () => SpotMetricEvaluator.Evaluate(CurrentOptic).RmsSpotRadius));

            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            SetSurfaceRadius(surface, surface.Radius);
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
        OpticCapabilityPreflight.EnsureSupported(
            CurrentOptic,
            OpticCapabilityOperation.Optimization,
            optimizerName);
        var lastSurfaceNumber = Surfaces.Count == 0 ? -1 : Surfaces[^1].Number;
        var selected = Surfaces
            .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
            .Where(surface => surface.RadiusVariable || surface.ThicknessVariable)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("请先在镜头数据中设置至少一个半径变量或厚度变量。");
        }

        var initialDocument = CaptureDocument();
        try
        {
            var problem = CurrentOptic.CreateOptimizationProblem();
            var variableBindings = new List<(int SurfaceNumber, bool IsRadius)>();
            foreach (var surface in selected)
            {
                if (surface.RadiusVariable && !CurrentOptic.Pickups.RadiusPickups.Any(pickup => pickup.TargetSurface == surface.Number))
                {
                    if (surface.IsPlane)
                    {
                        SetSurfaceRadius(surface, 40);
                    }

                    var initial = surface.Radius;
                    var span = Math.Max(5, Math.Abs(initial) * 0.5);
                    var lower = initial > 0
                        ? Math.Max(0.1, initial - span)
                        : initial - span;
                    var upper = initial < 0
                        ? Math.Min(-0.1, initial + span)
                        : initial + span;
                    problem.AddVariable(new DelegateVariable(
                        $"表面 {surface.Number} 半径",
                        () => surface.Radius,
                        value =>
                        {
                            SetSurfaceRadius(surface, value);
                            CurrentOptic.Pickups.ApplyAll();
                        },
                        Math.Max(-1_000_000, lower),
                        Math.Min(1_000_000, upper),
                        Math.Max(0.1, Math.Abs(initial) * 0.05),
                        new UnitRangeScaler(lower, upper)));
                    variableBindings.Add((surface.Number, true));
                }

                if (surface.ThicknessVariable)
                {
                    var initial = surface.Thickness;
                    var lower = 0.001;
                    var upper = Math.Max(initial + 10, Math.Max(1, initial * 3));
                    problem.AddVariable(new DelegateVariable(
                        $"表面 {surface.Number} 厚度",
                        () => surface.Thickness,
                        value =>
                        {
                            surface.Thickness = value;
                            CurrentOptic.SurfaceGroup.Renumber();
                        },
                        lower,
                        upper,
                        Math.Max(0.05, Math.Abs(initial) * 0.05),
                        new UnitRangeScaler(lower, upper)));
                    variableBindings.Add((surface.Number, false));
                }
            }

            CurrentOptic.Pickups.ApplyAll();
            if (problem.Variables.Count == 0)
                throw new InvalidOperationException("拾取半径不能作为独立变量，请设置源表面为变量。");
            var allMeritOperands = CurrentOptic.MeritFunctionOperands
                .Select(operand => operand.Clone())
                .ToArray();
            var enabledMeritRows = allMeritOperands
                .Select((operand, index) => (operand, index))
                .Where(item => item.operand.Enabled
                    && MeritFunctionCatalog.CanonicalType(item.operand.Type) is not ("BLNK" or "DMFS"))
                .Select(item => item.index)
                .ToArray();
            if (enabledMeritRows.Length == 0)
            {
                problem.AddOperand(new Operand(
                    "RMS spot radius",
                    0,
                    1,
                    EvaluateSpotMerit));
            }
            else
            {
                foreach (var operand in MeritFunctionCatalog.CreateOperands(CurrentOptic, allMeritOperands))
                {
                    problem.AddOperand(operand);
                }
            }

            var evaluationSnapshot = CurrentOptic.ToSnapshot();
            var evaluationOperands = allMeritOperands.Select(operand => operand.Clone()).ToArray();
            using var evaluationOptics = new ThreadLocal<Optic>(() => Optic.FromSnapshot(evaluationSnapshot));
            problem.SetIndependentValueEvaluator(values => EvaluateIndependentValues(
                evaluationOptics.Value!,
                variableBindings,
                evaluationOperands,
                enabledMeritRows,
                values));

            var result = OptimizerCatalog.Create(optimizerName).Optimize(problem, maxIterations);
            CurrentOptic.Pickups.ApplyAll();
            CurrentOptic.SurfaceGroup.Renumber();
            foreach (var binding in variableBindings.Distinct())
            {
                var optimizedSurface = Surfaces.First(item => item.Number == binding.SurfaceNumber);
                SynchronizeMultiConfigurationProperty(
                    optimizedSurface,
                    binding.IsRadius ? "radius" : "thickness");
            }

            _undoRedo.Capture(initialDocument);
            SetStatus($"{DisplayOptimizerMessage(result.Message)}。{problem.Variables.Count} 个变量，评价函数 {NumericDisplayFormatter.Format(result.InitialMerit)} -> {NumericDisplayFormatter.Format(result.FinalMerit)}。");
            SurfaceDataChanged?.Invoke(this, EventArgs.Empty);
            OpticChanged?.Invoke(this, EventArgs.Empty);
            return result;

            double EvaluateSpotMerit()
            {
                try
                {
                    var merit = SpotMetricEvaluator.Evaluate(CurrentOptic).RmsSpotRadius;
                    return double.IsFinite(merit) ? merit : 1_000_000;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return 1_000_000;
                }
            }

            static double[] EvaluateIndependentValues(
                Optic optic,
                IReadOnlyList<(int SurfaceNumber, bool IsRadius)> bindings,
                IReadOnlyList<MeritOperandDefinition> operands,
                IReadOnlyList<int> enabledRows,
                IReadOnlyList<double> values)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var index = 0; index < Math.Min(bindings.Count, values.Count); index++)
                {
                    var binding = bindings[index];
                    var surface = optic.SurfaceGroup.Items.First(item => item.Number == binding.SurfaceNumber);
                    if (binding.IsRadius)
                    {
                        SetSurfaceRadius(surface, values[index]);
                    }
                    else
                    {
                        surface.Thickness = values[index];
                    }
                }

                optic.Pickups.ApplyAll();
                optic.SurfaceGroup.Renumber();
                if (enabledRows.Count == 0)
                {
                    try
                    {
                        var value = SpotMetricEvaluator.Evaluate(optic).RmsSpotRadius;
                        return new[] { double.IsFinite(value) ? value : 1_000_000 };
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return new[] { 1_000_000.0 };
                    }
                }

                var evaluations = MeritFunctionCatalog.EvaluateAll(optic, operands);
                return enabledRows.Select(rowIndex =>
                {
                    var evaluation = evaluations[rowIndex];
                    return string.IsNullOrEmpty(evaluation.Error) && double.IsFinite(evaluation.Value)
                        ? evaluation.Value
                        : 1_000_000;
                }).ToArray();
            }
        }
        catch
        {
            ReplaceDocumentState(initialDocument);
            throw;
        }
    }
}
