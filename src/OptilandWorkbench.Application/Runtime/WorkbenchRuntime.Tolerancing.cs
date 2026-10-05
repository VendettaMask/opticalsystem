using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Contracts;
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
            CancellationToken cancellationToken = default,
            int maxDegreeOfParallelism = -1,
            ToleranceAnalysisMode mode = ToleranceAnalysisMode.Sensitivity,
            double inverseValue = 0,
            ToleranceMtfSettingsDto? mtfSettings = null,
            ToleranceCompensationAlgorithm algorithm = ToleranceCompensationAlgorithm.DampedLeastSquares,
            IReadOnlyList<ToleranceCompensatorDto>? additionalCompensators = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSupported(CurrentOptic, OpticCapabilityOperation.Tolerancing);
        surface ??= Surfaces.FirstOrDefault(item => item.Number > 1) ?? Surfaces.FirstOrDefault();
        if (surface is null)
        {
            return TolerancingView.Empty("没有可用于公差分析的表面。");
        }

        var higherIsBetter = criterion == ToleranceCriterion.Mtf;
        mtfSettings = higherIsBetter ? mtfSettings ?? new ToleranceMtfSettingsDto() : null;
        var requestedOperands = operands?.ToArray();
        var configuredOperands = requestedOperands?.Where(item => item.Enabled).ToArray();
        var tolerancing = configuredOperands is { Length: > 0 }
            ? BuildConfiguredTolerancing(configuredOperands, criterion, mtfSettings, algorithm, additionalCompensators)
            : BuildDefaultTolerancing(
                surface.Number,
                radiusSigma,
                thicknessSigma,
                compensationIterations,
                criterion, mtfSettings, algorithm, additionalCompensators);
        if (tolerancing.Perturbations.Count == 0)
        {
            return TolerancingView.Empty("请至少设置一个启用且非零的公差操作数。");
        }

        var sensitivityAnalysis = new SensitivityAnalysis(CurrentOptic, tolerancing);
        var nominalEvaluation = sensitivityAnalysis.EvaluateNominal(compensationIterations, cancellationToken);
        var nominal = nominalEvaluation.Criterion;
        if (!double.IsFinite(nominal))
        {
            return TolerancingView.Empty("名义系统没有可用于公差评价的有效光线。");
        }

        IReadOnlyList<ToleranceOperandDto>? adjustedOperands = null;
        var inverseRows = Array.Empty<TolerancingInverseRow>();
        var inverseTarget = double.NaN;
        if (mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement)
        {
            if (configuredOperands is not { Length: > 0 })
            {
                throw new InvalidOperationException("反向灵敏度需要显式公差操作数表。");
            }

            inverseTarget = mode == ToleranceAnalysisMode.InverseLimit
                ? inverseValue
                : nominal + (higherIsBetter ? -inverseValue : inverseValue);
            var inverseResults = sensitivityAnalysis.RunInverse(
                inverseTarget,
                compensationIterations,
                cancellationToken,
                higherIsBetter && mtfSettings!.SeparateFields
                    ? nominalEvaluation.Fields!.ToDictionary(field => field.FieldNumber,
                        field => mode == ToleranceAnalysisMode.InverseIncrement
                            ? field.Value - inverseValue
                            : mtfSettings.FieldLimits?.FirstOrDefault(limit => limit.FieldNumber == field.FieldNumber)?.Minimum is > 0 and var minimum
                                ? minimum : inverseValue)
                    : null);
            adjustedOperands = ApplyInverseResults(
                requestedOperands ?? configuredOperands,
                inverseResults);
            configuredOperands = adjustedOperands.Where(item => item.Enabled).ToArray();
            tolerancing = BuildConfiguredTolerancing(configuredOperands, criterion, mtfSettings, algorithm, additionalCompensators);
            sensitivityAnalysis = new SensitivityAnalysis(CurrentOptic, tolerancing);
            inverseRows = inverseResults.Select(ToInverseRow).ToArray();
        }

        var sensitivityResults = mode == ToleranceAnalysisMode.SkipSensitivity
            ? Array.Empty<SensitivityResult>()
            : sensitivityAnalysis.Run(compensationIterations, cancellationToken).ToArray();
        var sensitivity = sensitivityResults
                .Select(result => new TolerancingSensitivityRow(
                    result.Perturbation,
                    FormatCriterion(result.DeltaCriterion),
                    FormatCriterion(result.NegativeCriterion),
                    FormatCriterion(result.PositiveCriterion),
                    FormatCriterion(result.WorstCriterion),
                    ToFieldDtos(result.NegativeEvaluation?.Fields), ToFieldDtos(result.PositiveEvaluation?.Fields),
                    ToCompensatorDtos(result.NegativeEvaluation?.Compensators), ToCompensatorDtos(result.PositiveEvaluation?.Compensators)))
                .ToArray();
        var rssEstimatedChange = CalculateSensitivityRss(sensitivityResults, nominal);
        var sensitivityStatistics = sensitivityResults.Length == 0
            ? null
            : new TolerancingSensitivityStatistics(
                FormatCriterion(nominal),
                double.IsFinite(rssEstimatedChange) ? FormatCriterion(rssEstimatedChange) : "失效",
                double.IsFinite(rssEstimatedChange) ? FormatCriterion(higherIsBetter ? Math.Max(0, nominal - rssEstimatedChange) : nominal + rssEstimatedChange) : "失效");
        var requestedTrials = Math.Clamp(trials, 0, 10_000);
        var trialResults = requestedTrials == 0
            ? Array.Empty<TolerancingTrialResult>()
            : new MonteCarlo(CurrentOptic, tolerancing)
                .RunDetailed(
                    requestedTrials,
                    seed,
                    compensationIterations,
                    cancellationToken,
                    workerOptic => configuredOperands is { Length: > 0 }
                        ? BuildConfiguredTolerancingWorker(workerOptic, configuredOperands, criterion, mtfSettings, algorithm, additionalCompensators)
                        : BuildDefaultTolerancingWorker(
                            workerOptic,
                            surface.Number,
                            radiusSigma,
                            thicknessSigma,
                            compensationIterations,
                            criterion, mtfSettings, algorithm, additionalCompensators),
                    maxDegreeOfParallelism: maxDegreeOfParallelism == -1
                        ? Math.Max(1, Environment.ProcessorCount)
                        : maxDegreeOfParallelism);
        var monteCarlo = trialResults
            .Select(result => new TolerancingTrialRow(
                result.Trial + 1,
                FormatCriterion(result.Criterion),
                FormatCriterion(result.CompensatedCriterion),
                FormatCriterion(result.CompensatedCriterion - nominal),
                ToFieldDtos(result.CompensatedFields), ToFieldDtos(result.Fields), ToCompensatorDtos(result.Compensators),
                HasToleranceAcceptance(higherIsBetter, yieldLimit, mtfSettings)
                    ? AcceptanceMargin(result, higherIsBetter, yieldLimit, mtfSettings) >= 0 : null,
                HasToleranceAcceptance(higherIsBetter, yieldLimit, mtfSettings)
                    ? AcceptanceMargin(result, higherIsBetter, yieldLimit, mtfSettings) : null,
                result.CompensatedCriterion))
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
        var yield = HasToleranceAcceptance(higherIsBetter, yieldLimit, mtfSettings) && trialResults.Count > 0
            ? $"{100.0 * monteCarlo.Count(row => row.Passed == true) / trialResults.Count:0.0}%"
            : "未设置";
        var statistics = trialResults.Count == 0
            ? null
            : new TolerancingStatistics(
                FormatCriterion(nominal),
                FormatCriterion(mean),
                FormatCriterion(sigma),
                values.Length > 0 ? FormatCriterion(values[0]) : "失效",
                values.Length > 0 ? FormatCriterion(values[^1]) : "失效",
                values.Length > 0 ? FormatCriterion(Percentile(values, 0.50)) : "失效",
                values.Length > 0 ? FormatCriterion(Percentile(values, 0.90)) : "失效",
                values.Length > 0 ? FormatCriterion(Percentile(values, 0.95)) : "失效",
                yield, values.Length > 0 ? FormatCriterion(Percentile(values, 0.05)) : "失效");

        var fieldStatistics = higherIsBetter
            ? BuildFieldStatistics(nominalEvaluation.Fields!, trialResults, mtfSettings!, yieldLimit)
            : null;
        var inverseDetail = inverseRows.Length == 0
            ? string.Empty
            : $"    反求目标：{FormatCriterion(inverseTarget)}    收紧端点：{inverseRows.Sum(row => CountTightened(row))}";
        SetStatus($"公差分析完成：表面 {surface.Number}，{monteCarlo.Length} 次 Monte Carlo。");
        return new TolerancingView(
            higherIsBetter ? $"{(mtfSettings!.Method == ToleranceMtfMethod.Fourier ? "FFT 衍射" : "几何")} MTF 公差分析（{mtfSettings.Frequency:0.###} cycles/mm，{mtfSettings.Direction}）"
                : $"{(criterion == ToleranceCriterion.RmsWavefront ? "RMS 波前" : "RMS 点列半径")}公差分析",
            sensitivity,
            monteCarlo,
            $"公差数：{tolerancing.Perturbations.Count}    补偿器：{tolerancing.Compensators.Count}    Monte Carlo：{monteCarlo.Length}    失效试验：{invalidTrials}    补偿迭代：{Math.Max(0, compensationIterations)}{inverseDetail}",
            statistics,
            sensitivityStatistics,
            inverseRows,
            adjustedOperands,
            double.IsFinite(inverseTarget) ? FormatCriterion(inverseTarget) : string.Empty,
            mtfSettings, ToFieldDtos(nominalEvaluation.Fields), fieldStatistics, ToCompensatorDtos(nominalEvaluation.Compensators));
    }

    private static bool HasToleranceAcceptance(bool higher, double limit, ToleranceMtfSettingsDto? settings) =>
        limit > 0 || (higher && settings?.SeparateFields == true && settings.FieldLimits?.Any(field => field.Minimum > 0) == true);

    private static double AcceptanceMargin(TolerancingTrialResult trial, bool higher, double limit, ToleranceMtfSettingsDto? settings)
    {
        if (!double.IsFinite(trial.CompensatedCriterion)) return double.NegativeInfinity;
        if (higher && settings?.SeparateFields == true)
        {
            if (trial.CompensatedFields is not { Count: > 0 } fields) return double.NegativeInfinity;
            return fields.Min(field => double.IsFinite(field.Value) && string.IsNullOrEmpty(field.Error)
                ? field.Value - FieldLimit(settings, field.FieldNumber, limit) : double.NegativeInfinity);
        }
        return higher ? trial.CompensatedCriterion - limit : limit - trial.CompensatedCriterion;
    }

    private static double FieldLimit(ToleranceMtfSettingsDto settings, int field, double fallback) =>
        settings.FieldLimits?.FirstOrDefault(item => item.FieldNumber == field)?.Minimum ?? fallback;

    private static IReadOnlyList<ToleranceFieldStatisticsDto> BuildFieldStatistics(
        IReadOnlyList<ToleranceFieldValue> nominal, IReadOnlyList<TolerancingTrialResult> trials,
        ToleranceMtfSettingsDto settings, double commonLimit) => nominal.Select(field =>
        {
            var limit = FieldLimit(settings, field.FieldNumber, commonLimit);
            var values = trials.Select(trial => trial.CompensatedFields?.Where(value => value.FieldNumber == field.FieldNumber).Select(value => (double?)value.Value).FirstOrDefault() ?? double.NaN)
                .Where(double.IsFinite).OrderBy(value => value).ToArray();
            return new ToleranceFieldStatisticsDto(field.FieldNumber, field.Value,
                values.Length == 0 ? double.NaN : values.Average(), values.Length == 0 ? double.NaN : values[0],
                values.Length == 0 ? double.NaN : Percentile(values, 0.05),
                settings.SeparateFields && limit > 0 ? limit : null,
                settings.SeparateFields && limit > 0 && trials.Count > 0 ? 100.0 * values.Count(value => value >= limit) / trials.Count : null);
        }).ToArray();

    private static IReadOnlyList<ToleranceFieldValueDto>? ToFieldDtos(IReadOnlyList<ToleranceFieldValue>? values) =>
        values?.Select(value => new ToleranceFieldValueDto(value.FieldNumber, value.Value, value.Error)).ToArray();
    private static IReadOnlyList<ToleranceCompensatorValueDto>? ToCompensatorDtos(IReadOnlyList<ToleranceCompensatorValue>? values) =>
        values?.Select(value => new ToleranceCompensatorValueDto(value.Name, value.Nominal, value.Value, value.Minimum, value.Maximum)).ToArray();

    private static IReadOnlyList<ToleranceOperandDto> ApplyInverseResults(
        IReadOnlyList<ToleranceOperandDto> operands,
        IReadOnlyList<InverseSensitivityResult> results)
    {
        var resultIndex = 0;
        var adjusted = new ToleranceOperandDto[operands.Count];
        for (var index = 0; index < operands.Count; index++)
        {
            var operand = operands[index];
            if (!operand.Enabled || operand.Kind == ToleranceOperandKind.Compensator)
            {
                adjusted[index] = operand;
                continue;
            }

            if (resultIndex >= results.Count)
            {
                throw new InvalidOperationException("反向灵敏度结果与公差表不一致。");
            }

            var result = results[resultIndex++];
            adjusted[index] = operand with
            {
                Minimum = result.Minimum.AdjustedTolerance,
                Maximum = result.Maximum.AdjustedTolerance
            };
        }

        if (resultIndex != results.Count)
        {
            throw new InvalidOperationException("反向灵敏度返回了多余结果。");
        }

        return adjusted;
    }

    private static TolerancingInverseRow ToInverseRow(InverseSensitivityResult result) => new(
        result.Perturbation,
        ToInverseEndpoint(result.Minimum),
        ToInverseEndpoint(result.Maximum));

    private static TolerancingInverseEndpoint ToInverseEndpoint(
        InverseToleranceEndpointResult endpoint) => new(
            FormatCriterion(endpoint.OriginalTolerance),
            FormatCriterion(endpoint.AdjustedTolerance),
            FormatCriterion(endpoint.Criterion),
            endpoint.Status switch
            {
                InverseToleranceEndpointStatus.UnchangedWithinTarget =>
                    ToleranceInverseEndpointStatus.UnchangedWithinTarget,
                InverseToleranceEndpointStatus.Tightened =>
                    ToleranceInverseEndpointStatus.Tightened,
                InverseToleranceEndpointStatus.ZeroRange =>
                    ToleranceInverseEndpointStatus.ZeroRange,
                _ => ToleranceInverseEndpointStatus.UnsupportedPerturbation
            },
            endpoint.Iterations);

    private static int CountTightened(TolerancingInverseRow row) =>
        (row.Minimum.Status == ToleranceInverseEndpointStatus.Tightened ? 1 : 0)
        + (row.Maximum.Status == ToleranceInverseEndpointStatus.Tightened ? 1 : 0);

    private static double CalculateSensitivityRss(
        IReadOnlyList<SensitivityResult> results,
        double nominal)
    {
        var sumOfMeanSquares = 0.0;
        foreach (var result in results)
        {
            if (double.IsFinite(result.NegativeCriterion)
                && double.IsFinite(result.PositiveCriterion))
            {
                var negativeChange = result.NegativeCriterion - nominal;
                var positiveChange = result.PositiveCriterion - nominal;
                sumOfMeanSquares += ((negativeChange * negativeChange)
                    + (positiveChange * positiveChange)) / 2.0;
                continue;
            }

            if (!double.IsFinite(result.WorstCriterion)) return double.NaN;
            if (double.IsFinite(result.WorstCriterion))
            {
                var change = result.WorstCriterion - nominal;
                sumOfMeanSquares += change * change;
            }
        }

        return Math.Sqrt(sumOfMeanSquares);
    }

    private static string FormatCriterion(double value)
    {
        if (double.IsFinite(value))
        {
            return value.ToString("G12", CultureInfo.InvariantCulture);
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
