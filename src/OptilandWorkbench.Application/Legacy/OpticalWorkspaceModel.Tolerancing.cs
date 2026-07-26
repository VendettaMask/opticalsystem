using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
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

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
public TolerancingView RunTolerancing(
        OpticalSurface? surface,
        double radiusSigma,
        double thicknessSigma,
        int trials,
        int seed,
        int compensationIterations,
        IReadOnlyList<ToleranceOperandDto>? operands = null,
        ToleranceCriterion criterion = ToleranceCriterion.RmsSpotRadius,
        double yieldLimit = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        surface ??= Surfaces.FirstOrDefault(item => item.Number > 1) ?? Surfaces.FirstOrDefault();
        if (surface is null)
        {
            return TolerancingView.Empty("没有可用于公差分析的表面。");
        }

        var configuredOperands = operands?.Where(item => item.Enabled).ToArray();
        var tolerancing = configuredOperands is { Length: > 0 }
            ? BuildConfiguredTolerancing(configuredOperands, criterion)
            : BuildDefaultTolerancing(
                surface.Number,
                radiusSigma,
                thicknessSigma,
                compensationIterations,
                criterion);
        if (tolerancing.Perturbations.Count == 0)
        {
            return TolerancingView.Empty("请至少设置一个启用且非零的公差操作数。");
        }

        var nominal = EvaluateToleranceCriterion(criterion);
        if (!double.IsFinite(nominal))
        {
            return TolerancingView.Empty("名义系统没有可用于公差评价的有效光线。");
        }

        var sensitivity = new SensitivityAnalysis(CurrentOptic, tolerancing)
            .Run(compensationIterations, cancellationToken)
            .Select(result => new TolerancingSensitivityRow(
                result.Perturbation,
                FormatCriterion(result.DeltaCriterion),
                FormatCriterion(result.NegativeCriterion),
                FormatCriterion(result.PositiveCriterion),
                FormatCriterion(result.WorstCriterion)))
            .ToArray();
        var trialResults = new MonteCarlo(CurrentOptic, tolerancing)
            .RunDetailed(
                Math.Clamp(trials, 1, 10_000),
                seed,
                compensationIterations,
                cancellationToken);
        var monteCarlo = trialResults
            .Select(result => new TolerancingTrialRow(
                result.Trial + 1,
                FormatCriterion(result.Criterion),
                FormatCriterion(result.CompensatedCriterion),
                FormatCriterion(result.CompensatedCriterion - nominal)))
            .ToArray();
        var values = trialResults
            .Select(result => result.CompensatedCriterion)
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToArray();
        var invalidTrials = trialResults.Count - values.Length;
        var mean = values.Length > 0 ? values.Average() : double.PositiveInfinity;
        var sigma = values.Length > 0
            ? Math.Sqrt(values.Select(value => Math.Pow(value - mean, 2)).Average())
            : double.PositiveInfinity;
        var yield = yieldLimit > 0
            ? $"{100.0 * values.Count(value => value <= yieldLimit) / trialResults.Count:0.0}%"
            : "未设置";
        var statistics = new TolerancingStatistics(
            NumericDisplayFormatter.Format(nominal),
            FormatCriterion(mean),
            FormatCriterion(sigma),
            values.Length > 0 ? NumericDisplayFormatter.Format(values[0]) : "失效",
            values.Length > 0 ? NumericDisplayFormatter.Format(values[^1]) : "失效",
            values.Length > 0 ? NumericDisplayFormatter.Format(Percentile(values, 0.50)) : "失效",
            values.Length > 0 ? NumericDisplayFormatter.Format(Percentile(values, 0.90)) : "失效",
            values.Length > 0 ? NumericDisplayFormatter.Format(Percentile(values, 0.95)) : "失效",
            yield);

        SetStatus($"公差分析完成：表面 {surface.Number}，{monteCarlo.Length} 次 Monte Carlo。");
        return new TolerancingView(
            $"{(criterion == ToleranceCriterion.RmsWavefront ? "RMS 波前" : "RMS 点列半径")}公差分析",
            sensitivity,
            monteCarlo,
            $"公差数：{tolerancing.Perturbations.Count}    补偿器：{tolerancing.Compensators.Count}    Monte Carlo：{monteCarlo.Length}    失效试验：{invalidTrials}    补偿迭代：{Math.Max(0, compensationIterations)}",
            statistics);
    }

    private static string FormatCriterion(double value)
    {
        if (double.IsFinite(value))
        {
            return NumericDisplayFormatter.Format(value);
        }

        return double.IsNaN(value) ? string.Empty : "失效";
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var fraction = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }
}
