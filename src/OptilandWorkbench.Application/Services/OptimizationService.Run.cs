using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class OptimizationService
{
    public Task<QuickFocusResultDto> QuickFocusAsync(CancellationToken cancellationToken = default) =>
        RunOnSnapshotAsync<QuickFocusResultDto>(worker =>
        {
            if (worker.Surfaces.Count < 2)
                throw new InvalidOperationException("快速聚焦至少需要一个物方表面和一个像面。");
            var summary = FocusMetricEvaluator.Evaluate(worker.CurrentOptic);
            if (!double.IsFinite(summary.BestFocusShift))
                throw new InvalidOperationException("快速聚焦未得到有限的焦移结果。");
            var surface = worker.Surfaces[^2];
            var initial = surface.Thickness;
            var final = Math.Max(.001, initial + summary.BestFocusShift);
            surface.Thickness = final;
            worker.CommitSurfaceEdit(surface, nameof(OpticalSurface.Thickness));
            return () => new(surface.Number, initial, final - initial, final, summary.BestRmsSpotRadius);
        }, cancellationToken);

    public Task<OptimizationResultDto> OptimizeSurfaceRadiusAsync(
        int surfaceNumber, string optimizerName, int maxIterations,
        CancellationToken cancellationToken = default)
    {
        OptimizationLimits.RequireIterationCount(maxIterations);
        return RunOnSnapshotAsync<OptimizationResultDto>(worker =>
        {
            var surface = worker.Surfaces.FirstOrDefault(item => item.Number == surfaceNumber)
                ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
            var initial = surface.Radius;
            var result = worker.OptimizeSurfaceRadius(surface, optimizerName, maxIterations);
            LogOptimizationResult(result);
            return () => new(result.Algorithm, BuildOptimizationMessage(result), initial,
                worker.Surfaces.Single(item => item.Number == surfaceNumber).Radius,
                result.FinalMerit, result.Iterations, result.AlgorithmVersion, result.StopReason,
                result.GradientNorm, result.FunctionEvaluations, result.RandomSeed, result.Warnings);
        }, cancellationToken);
    }

    public Task<OptimizationRunResultDto> OptimizeVariablesAsync(
        string optimizerName, int maxIterations, CancellationToken cancellationToken = default)
    {
        OptimizationLimits.RequireIterationCount(maxIterations);
        return RunOnSnapshotAsync<OptimizationRunResultDto>(worker =>
        {
            var selected = worker.GetMarkedOptimizationVariables();
            if (selected.Count == 0)
                throw new InvalidOperationException("请先设置优化变量（表面、GRIN 系数、物理膜层或多配置单元格）。");
            var result = worker.OptimizeMarkedVariables(optimizerName, maxIterations);
            LogOptimizationResult(result);
            return () =>
            {
                var final = worker.GetMarkedOptimizationVariables();
                var variables = selected.Select(variable => variable with
                {
                    FinalValue = final.Single(item => item.ConfigurationIndex == variable.ConfigurationIndex
                        && item.SurfaceNumber == variable.SurfaceNumber && item.Kind == variable.Kind
                        && item.Layer == variable.Layer && item.Parameter == variable.Parameter).FinalValue
                }).ToArray();
                return new(result.Algorithm, BuildOptimizationMessage(result), result.InitialMerit,
                    result.FinalMerit, result.Iterations, variables, result.AlgorithmVersion,
                    result.StopReason, result.GradientNorm, result.FunctionEvaluations,
                    result.RandomSeed, result.Warnings);
            };
        }, cancellationToken);
    }

    private Task<TResult> RunOnSnapshotAsync<TResult>(
        Func<WorkbenchRuntime, Func<TResult>> compute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedOpticalDocument document;
        CancellationTokenSource linked;
        long revision;
        long generation;
        lock (Gate)
        {
            document = Runtime.CaptureDocument();
            revision = Workspace.Revision;
            generation = Workspace.DocumentGeneration;
            linked = Workspace.LinkDocumentToken(cancellationToken);
        }
        return RunWorkerAsync();

        async Task<TResult> RunWorkerAsync()
        {
            using (linked)
            {
                return await Task.Run(() =>
                {
                    linked.Token.ThrowIfCancellationRequested();
                    using var scope = ComputationCancellation.Push(linked.Token);
                    var worker = WorkbenchRuntime.CreateComputationRuntime(document);
                    var createResult = compute(worker);
                    Workspace.RefreshAutomaticSemiDiameters(worker, linked.Token);
                    var result = createResult();
                    var computed = worker.CaptureDocument();
                    Workspace.MutateTransactional(WorkspaceChangeCategory.Optimization, () =>
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (Workspace.Revision != revision || Workspace.DocumentGeneration != generation)
                            throw new OperationCanceledException("优化期间文档已变化，旧优化结果已取消。", linked.Token);
                        Runtime.CommitComputedDocument(computed, worker.Status);
                    }, linked.Token, refreshAutomaticSemiDiameters: false);
                    return result;
                }, linked.Token).ConfigureAwait(false);
            }
        }
    }

    private static string BuildOptimizationMessage(OptimizerResult result)
    {
        var message = WorkbenchRuntime.DisplayOptimizerMessage(result.Message);
        return result.Warnings.Count == 0 ? message : $"{message} 警告：{string.Join("；", result.Warnings)}";
    }

    private static void LogOptimizationResult(OptimizerResult result)
    {
        var gradient = result.GradientNorm?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        var seed = result.RandomSeed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        System.Diagnostics.Trace.TraceInformation(
            "Optimization algorithm={0} version={1} stop={2} iterations={3} evaluations={4} gradientNorm={5} randomSeed={6}",
            result.Algorithm, result.AlgorithmVersion, result.StopReason, result.Iterations,
            result.FunctionEvaluations, gradient, seed);
        foreach (var warning in result.Warnings)
            System.Diagnostics.Trace.TraceWarning("Optimization compatibility warning: {0}", warning);
    }
}
