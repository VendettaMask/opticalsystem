using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
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
                    if (Connector.Surfaces.Count < 2)
                    {
                        throw new InvalidOperationException(
                            "快速聚焦至少需要一个物方表面和一个像面。");
                    }

                    var summary = FocusMetricEvaluator.Evaluate(Connector.CurrentOptic);
                    if (!double.IsFinite(summary.BestFocusShift))
                    {
                        throw new InvalidOperationException("快速聚焦未得到有限的焦移结果。");
                    }

                    var focusSurface = Connector.Surfaces[^2];
                    var initialThickness = focusSurface.Thickness;
                    var finalThickness = Math.Max(
                        0.001,
                        initialThickness + summary.BestFocusShift);
                    var appliedShift = finalThickness - initialThickness;
                    return Mutate(WorkspaceChangeCategory.Optimization, () =>
                    {
                        Connector.CaptureCurrentState();
                        focusSurface.Thickness = finalThickness;
                        Connector.CommitSurfaceEdit(
                            focusSurface,
                            nameof(OpticalSurface.Thickness));
                        linked.Token.ThrowIfCancellationRequested();
                        return new QuickFocusResultDto(
                            focusSurface.Number,
                            initialThickness,
                            appliedShift,
                            finalThickness,
                            summary.BestRmsSpotRadius);
                    });
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
                    var result = Mutate(
                        WorkspaceChangeCategory.Optimization,
                        () => Connector.OptimizeSurfaceRadius(surface, optimizerName, maxIterations));
                    Workspace.RefreshAutomaticSemiDiameters();
                    linked.Token.ThrowIfCancellationRequested();
                    return new OptimizationResultDto(
                        optimizerName,
                        OpticalWorkspaceModel.DisplayOptimizerMessage(result.Message),
                        initial,
                        surface.Radius,
                        result.FinalMerit,
                        result.Iterations);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }

    public Task<OptimizationRunResultDto> OptimizeVariablesAsync(
        string optimizerName,
        int maxIterations,
        CancellationToken cancellationToken = default)
    {
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
                    var lastSurfaceNumber = Connector.Surfaces.Count == 0
                        ? -1
                        : Connector.Surfaces[^1].Number;
                    var selected = Connector.Surfaces
                        .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
                        .SelectMany(surface => new[]
                        {
                            surface.RadiusVariable
                                ? new OptimizationVariableResultDto(
                                    surface.Number,
                                    OptimizationVariableKind.Radius,
                                    $"表面 {surface.Number} 半径",
                                    surface.Radius,
                                    surface.Radius)
                                : null,
                            surface.ThicknessVariable
                                ? new OptimizationVariableResultDto(
                                    surface.Number,
                                    OptimizationVariableKind.Thickness,
                                    $"表面 {surface.Number} 厚度",
                                    surface.Thickness,
                                    surface.Thickness)
                                : null
                        })
                        .Where(variable => variable is not null)
                        .Cast<OptimizationVariableResultDto>()
                        .ToArray();
                    if (selected.Length == 0)
                    {
                        throw new InvalidOperationException("请先在镜头数据中设置优化变量。");
                    }

                    var result = Mutate(
                        WorkspaceChangeCategory.Optimization,
                        () => Connector.OptimizeMarkedVariables(optimizerName, maxIterations));
                    Workspace.RefreshAutomaticSemiDiameters();
                    var variables = selected.Select(variable =>
                    {
                        var surface = FindSurface(variable.SurfaceNumber)
                            ?? throw new InvalidOperationException($"优化后找不到表面 {variable.SurfaceNumber}。");
                        var finalValue = variable.Kind == OptimizationVariableKind.Radius
                            ? surface.Radius
                            : surface.Thickness;
                        return variable with { FinalValue = finalValue };
                    }).ToArray();
                    linked.Token.ThrowIfCancellationRequested();
                    return new OptimizationRunResultDto(
                        optimizerName,
                        OpticalWorkspaceModel.DisplayOptimizerMessage(result.Message),
                        result.InitialMerit,
                        result.FinalMerit,
                        result.Iterations,
                        variables);
                }
            }, linked.Token).ConfigureAwait(false);
        }
    }
}
