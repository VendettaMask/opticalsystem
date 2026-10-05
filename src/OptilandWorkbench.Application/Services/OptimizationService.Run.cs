using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class OptimizationService
{
    public Task<QuickFocusResultDto> QuickFocusAsync(
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        lock (Gate)
        {
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return QuickFocusWorkerAsync(linked);
    }

    private async Task<QuickFocusResultDto> QuickFocusWorkerAsync(
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                lock (Gate)
                {
                    if (Runtime.Surfaces.Count < 2)
                    {
                        throw new InvalidOperationException(
                            "快速聚焦至少需要一个物方表面和一个像面。");
                    }

                    var summary = FocusMetricEvaluator.Evaluate(Runtime.CurrentOptic);
                    if (!double.IsFinite(summary.BestFocusShift))
                    {
                        throw new InvalidOperationException("快速聚焦未得到有限的焦移结果。");
                    }

                    var focusSurface = Runtime.Surfaces[^2];
                    var initialThickness = focusSurface.Thickness;
                    var finalThickness = Math.Max(
                        0.001,
                        initialThickness + summary.BestFocusShift);
                    var appliedShift = finalThickness - initialThickness;
                    return MutateTransactional(
                        WorkspaceChangeCategory.Optimization,
                        () =>
                        {
                            Runtime.CaptureCurrentState();
                            focusSurface.Thickness = finalThickness;
                            Runtime.CommitSurfaceEdit(
                                focusSurface,
                                nameof(OpticalSurface.Thickness));
                            return new QuickFocusResultDto(
                                focusSurface.Number,
                                initialThickness,
                                appliedShift,
                                finalThickness,
                                summary.BestRmsSpotRadius);
                        },
                        linked.Token);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<OptimizationResultDto> OptimizeSurfaceRadiusAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
        OptimizationLimits.RequireIterationCount(maxIterations);
        CancellationTokenSource linked;
        lock (Gate)
        {
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return OptimizeSurfaceRadiusWorkerAsync(surfaceNumber, optimizerName, maxIterations, linked);
    }

    private async Task<OptimizationResultDto> OptimizeSurfaceRadiusWorkerAsync(
        int surfaceNumber,
        string optimizerName,
        int maxIterations,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                lock (Gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var surface = FindSurface(surfaceNumber)
                        ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
                    var initial = surface.Radius;
                    var result = MutateTransactional(
                        WorkspaceChangeCategory.Optimization,
                        () => Runtime.OptimizeSurfaceRadius(surface, optimizerName, maxIterations),
                        linked.Token);
                    LogOptimizationResult(result);
                    return new OptimizationResultDto(
                        result.Algorithm,
                        BuildOptimizationMessage(result),
                        initial,
                        surface.Radius,
                        result.FinalMerit,
                        result.Iterations,
                        result.AlgorithmVersion,
                        result.StopReason,
                        result.GradientNorm,
                        result.FunctionEvaluations,
                        result.RandomSeed,
                        result.Warnings);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<OptimizationRunResultDto> OptimizeVariablesAsync(
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
        OptimizationLimits.RequireIterationCount(maxIterations);
        CancellationTokenSource linked;
        lock (Gate)
        {
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }

        return OptimizeVariablesWorkerAsync(optimizerName, maxIterations, linked);
    }

    private async Task<OptimizationRunResultDto> OptimizeVariablesWorkerAsync(
        string optimizerName,
        int maxIterations,
        CancellationTokenSource linked)
    {
        using (linked)
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                using var cancellationScope = ComputationCancellation.Push(linked.Token);
                lock (Gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var selected = Runtime.GetMarkedOptimizationVariables();
                    if (selected.Count == 0) throw new InvalidOperationException("请先设置优化变量（表面、GRIN 系数、物理膜层或多配置单元格）。");

                    var result = MutateTransactional(
                        WorkspaceChangeCategory.Optimization,
                        () => Runtime.OptimizeMarkedVariables(optimizerName, maxIterations),
                        linked.Token);
                    LogOptimizationResult(result);
                    var final = Runtime.GetMarkedOptimizationVariables();
                    var variables = selected.Select(variable => variable with
                    {
                        FinalValue = final.Single(item =>
                        item.ConfigurationIndex == variable.ConfigurationIndex && item.SurfaceNumber == variable.SurfaceNumber
                        && item.Kind == variable.Kind && item.Layer == variable.Layer && item.Parameter == variable.Parameter).FinalValue
                    }).ToArray();
                    return new OptimizationRunResultDto(
                        result.Algorithm,
                        BuildOptimizationMessage(result),
                        result.InitialMerit,
                        result.FinalMerit,
                        result.Iterations,
                        variables,
                        result.AlgorithmVersion,
                        result.StopReason,
                        result.GradientNorm,
                        result.FunctionEvaluations,
                        result.RandomSeed,
                        result.Warnings);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    private static string BuildOptimizationMessage(OptimizerResult result)
    {
        var message = WorkbenchRuntime.DisplayOptimizerMessage(result.Message);
        return result.Warnings.Count == 0
            ? message
            : $"{message} 警告：{string.Join("；", result.Warnings)}";
    }

    private static void LogOptimizationResult(OptimizerResult result)
    {
        var gradient = result.GradientNorm?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)
            ?? "n/a";
        var seed = result.RandomSeed?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? "n/a";
        System.Diagnostics.Trace.TraceInformation(
            "Optimization algorithm={0} version={1} stop={2} iterations={3} evaluations={4} gradientNorm={5} randomSeed={6}",
            result.Algorithm,
            result.AlgorithmVersion,
            result.StopReason,
            result.Iterations,
            result.FunctionEvaluations,
            gradient,
            seed);
        foreach (var warning in result.Warnings)
        {
            System.Diagnostics.Trace.TraceWarning("Optimization compatibility warning: {0}", warning);
        }
    }
}
