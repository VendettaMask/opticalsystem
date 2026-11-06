using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Tolerancing;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    internal static double? JointAcceptanceMargin(IReadOnlyList<ToleranceConfigurationResultDto> configurations, bool hasAcceptance)
    {
        if (!hasAcceptance) return null;
        if (configurations.Any(config => !double.IsFinite(config.Criterion) || config.AcceptanceMargin == double.NegativeInfinity))
            return double.NegativeInfinity;
        return configurations.Min(config => config.AcceptanceMargin);
    }

    private sealed partial class JointToleranceStudy
    {
        private sealed record Endpoint(double Value, int Iterations);
        private readonly record struct LimitingValue(int Configuration, int? Field, double Margin);

        public TolerancingResultDto Run(long revision)
        {
            var nominal = Evaluate(_operands, _mce);
            var invalid = nominal.After.FirstOrDefault(config => !double.IsFinite(config.Criterion));
            if (invalid is not null)
                throw new InvalidOperationException($"配置 {invalid.Configuration} 的名义系统无法评价：{invalid.Error}");
            var nominalValue = Worst(nominal.After.Select(config => config.Criterion));
            var operands = _operands.ToArray();
            var mce = _mce.ToArray();
            var inverse = _request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement;
            var inverseRows = inverse ? Invert(operands, mce, nominal.After) : null;
            var sensitivity = _request.Mode == ToleranceAnalysisMode.SkipSensitivity ? [] : Sensitivity(operands, mce, nominalValue);

            var trials = new TolerancingTrialRowDto[_request.Trials];
            Parallel.For(0, trials.Length, new ParallelOptions
            {
                CancellationToken = _cancellationToken,
                MaxDegreeOfParallelism = _request.MaxDegreeOfParallelism
            }, index =>
            {
                using var parallelism = ComputationParallelism.SuppressNestedParallelism();
                trials[index] = ToTrial(Evaluate(operands, mce, trial: index), index + 1, nominalValue);
            });

            var perConfiguration = nominal.After.Select(config =>
            {
                var passed = trials.Count(row => row.Configurations!.Any(item => item.Configuration == config.Configuration && item.Passed == true));
                return new ToleranceConfigurationYieldDto(config.Configuration, passed, trials.Length,
                    config.AcceptanceMargin is not null && trials.Length > 0 ? (double)passed / trials.Length : null);
            }).ToArray();
            var adjusted = inverse ? ReplaceEnabled(_request.Operands ?? [], operands) : null;
            var settings = _settings with
            {
                Scope = ToleranceConfigurationScope.Selected, Configurations = _selected,
                MceRows = mce.Select(item => item.Row).ToArray()
            };
            var worstSample = trials.Length > 0
                ? TolerancingService.CaptureJointWorstSample(_snapshot, _request with
                {
                    Operands = adjusted ?? _request.Operands, ConfigurationSettings = settings
                }, trials)
                : null;

            return new TolerancingResultDto($"多配置联合公差（{string.Join(", ", _selected)}）", sensitivity, trials,
                $"配置数 {_selected.Length}，Monte Carlo {trials.Length}，失效 {trials.Count(row => !double.IsFinite(row.CriterionValue!.Value))}",
                Statistics(trials, nominalValue), SourceRevision: revision, InverseRows: inverseRows,
                AdjustedOperands: adjusted, MtfSettings: _mtf ? _request.MtfSettings ?? new() : null,
                NominalCompensators: nominal.Adjustments, NominalStages: nominal.Stages, WorstSample: worstSample,
                NominalConfigurations: nominal.After, ConfigurationYields: perConfiguration,
                AdjustedMceRows: inverse ? settings.MceRows : null);
        }

        public TolerancingTrialRowDto Replay(int trial)
        {
            if (trial < 1 || trial > _request.Trials)
                throw new ArgumentOutOfRangeException(nameof(trial), "样件试验编号超出联合公差范围。");
            using var parallelism = ComputationParallelism.SuppressNestedParallelism();
            return ToTrial(Evaluate(_operands, _mce, trial: trial - 1), trial, double.NaN);
        }

        private TolerancingTrialRowDto ToTrial(Outcome outcome, int trial, double nominal)
        {
            var before = Worst(outcome.Before.Select(config => config.Criterion));
            var after = Worst(outcome.After.Select(config => config.Criterion));
            var hasAcceptance = _selected.Any(number => HasToleranceAcceptance(_mtf, ConfigurationLimit(number), ConfigurationMtf(number)));
            var margin = JointAcceptanceMargin(outcome.After, hasAcceptance);
            return new(trial, FormatCriterion(before), FormatCriterion(after),
                double.IsNaN(nominal) ? "" : FormatCriterion(after - nominal),
                Compensators: outcome.Adjustments, Passed: margin is { } value ? value >= 0 : null,
                AcceptanceMargin: margin, CriterionValue: after, Stages: outcome.Stages,
                Configurations: outcome.After, UncompensatedConfigurations: outcome.Before);
        }

        private static TolerancingStatisticsDto? Statistics(TolerancingTrialRowDto[] trials, double nominal)
        {
            if (trials.Length == 0) return null;
            var values = trials.Select(row => row.CriterionValue!.Value).Where(double.IsFinite).Order().ToArray();
            var average = values.Length == 0 ? double.NaN : values.Average();
            var deviation = values.Length == 0 ? double.NaN : Math.Sqrt(values.Average(value => Math.Pow(value - average, 2)));
            string At(double percentile) => values.Length == 0 ? "失效" : FormatCriterion(Percentile(values, percentile));
            var yield = trials.Any(row => row.Passed.HasValue)
                ? $"{100d * trials.Count(row => row.Passed == true) / trials.Length:0.0}%" : "未设置";
            return new(FormatCriterion(nominal), FormatCriterion(average), FormatCriterion(deviation), At(0), At(1),
                At(.5), At(.9), At(.95), yield, At(.05));
        }

        private TolerancingSensitivityRowDto[] Sensitivity(ToleranceOperandDto[] operands, JointMceBinding[] mce, double nominal)
        {
            return Ranges(operands, mce).Select((range, index) =>
            {
                var lower = Evaluate(operands, mce, endpoint: index);
                var upper = Evaluate(operands, mce, endpoint: index, maximum: true);
                var a = Worst(lower.After.Select(config => config.Criterion));
                var b = Worst(upper.After.Select(config => config.Criterion));
                var worst = Worst([a, b]);
                return new TolerancingSensitivityRowDto(range.Name, FormatCriterion(worst - nominal),
                    FormatCriterion(a), FormatCriterion(b), FormatCriterion(worst),
                    NegativeConfigurations: lower.After, PositiveConfigurations: upper.After,
                    NegativeStages: _request.StageObjectives is { Count: > 0 } ? lower.Stages : null,
                    PositiveStages: _request.StageObjectives is { Count: > 0 } ? upper.Stages : null);
            }).ToArray();
        }

        private TolerancingInverseRowDto[] Invert(ToleranceOperandDto[] operands, JointMceBinding[] mce,
            ToleranceConfigurationResultDto[] nominal)
        {
            var baseline = Limit(nominal, nominal);
            if (baseline.Margin < 0)
                throw new ArgumentException($"配置 {baseline.Configuration} 的名义系统不满足联合反求目标。");

            var ranges = Ranges(operands, mce);
            var low = new Endpoint[ranges.Length];
            var high = new Endpoint[ranges.Length];
            for (var i = 0; i < ranges.Length; i++)
            {
                low[i] = Solve(i, false, ranges[i].Minimum);
                high[i] = Solve(i, true, ranges[i].Maximum);
            }

            var index = 0;
            for (var i = 0; i < operands.Length; i++)
            {
                var operand = operands[i];
                if (operand.Kind == ToleranceOperandKind.Compensator) continue;
                var min = low[index].Value;
                var max = high[index++].Value;
                if (operand.Kind.SurfaceFormModeFor().HasValue)
                {
                    max = Math.Min(-min, max);
                    min = -max;
                }
                operands[i] = operand with { Minimum = min, Maximum = max };
            }
            ConstrainJointIsoInverse(operands);
            for (var i = 0; i < mce.Length; i++)
                if (!mce[i].Row.Compensator)
                {
                    mce[i] = mce[i] with { Row = mce[i].Row with { Minimum = low[index].Value, Maximum = high[index].Value } };
                    index++;
                }

            var adjusted = Ranges(operands, mce);
            return ranges.Select((range, row) => new TolerancingInverseRowDto(range.Name,
                Verify(row, false, range.Minimum, adjusted[row].Minimum, low[row].Iterations),
                Verify(row, true, range.Maximum, adjusted[row].Maximum, high[row].Iterations))).ToArray();

            Endpoint Solve(int row, bool maximum, double value)
            {
                if (Limit(Evaluate(operands, mce, endpoint: row, maximum: maximum).After, nominal).Margin >= 0)
                    return new(value, 0);
                var lo = 0d;
                var hi = 1d;
                const int iterations = 36;
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    var scale = (lo + hi) * .5;
                    var current = Limit(Evaluate(operands, mce, endpoint: row, maximum: maximum, scale: scale).After, nominal);
                    if (current.Margin >= 0) lo = scale;
                    else hi = scale;
                }
                return new(value * lo, iterations);
            }

            TolerancingInverseEndpointDto Verify(int row, bool maximum, double original, double value, int iterations)
            {
                var exact = Evaluate(operands, mce, endpoint: row, maximum: maximum).After;
                var limiting = Limit(exact, nominal);
                if (limiting.Margin < 0)
                    throw new InvalidOperationException($"联合反求 {ranges[row].Name} 的端点复验失败，限制配置 {limiting.Configuration}。");
                var status = value == original ? ToleranceInverseEndpointStatus.UnchangedWithinTarget
                    : value == 0 ? ToleranceInverseEndpointStatus.ZeroRange : ToleranceInverseEndpointStatus.Tightened;
                return new(FormatCriterion(original), FormatCriterion(value), FormatCriterion(Worst(exact.Select(config => config.Criterion))),
                    status, iterations, limiting.Configuration, limiting.Field, limiting.Margin);
            }
        }

        private LimitingValue Limit(ToleranceConfigurationResultDto[] actual, ToleranceConfigurationResultDto[] nominal)
        {
            var increment = _request.Mode == ToleranceAnalysisMode.InverseIncrement;
            return actual.Select((item, index) =>
            {
                if (!double.IsFinite(item.Criterion)) return new LimitingValue(item.Configuration, null, double.NegativeInfinity);
                var limit = ConfigurationLimit(item.Configuration);
                if (ConfigurationMtf(item.Configuration) is { SeparateFields: true } mtf && item.Fields is { Count: > 0 })
                    return item.Fields.Select(field =>
                    {
                        var target = increment
                            ? nominal[index].Fields!.Single(value => value.FieldNumber == field.FieldNumber).Value - _request.InverseValue
                            : FieldLimit(mtf, field.FieldNumber, limit > 0 ? limit : _request.InverseValue);
                        return new LimitingValue(item.Configuration, field.FieldNumber, FieldMargin(field, target));
                    }).MinBy(item => item.Margin);
                var threshold = increment ? nominal[index].Criterion + (_mtf ? -_request.InverseValue : _request.InverseValue)
                    : limit > 0 ? limit : _request.InverseValue;
                return new LimitingValue(item.Configuration, null, Margin(item.Criterion, threshold));
            }).MinBy(item => item.Margin);
        }

        private static (string Name, double Minimum, double Maximum)[] Ranges(ToleranceOperandDto[] operands, JointMceBinding[] mce) =>
            operands.Where(row => row.Kind != ToleranceOperandKind.Compensator)
                .Select(row => (ToleranceOperandName(row), row.Minimum, row.Maximum))
                .Concat(mce.Where(row => !row.Row.Compensator).Select(row => (row.Name, row.Row.Minimum, row.Row.Maximum))).ToArray();

        private static ToleranceOperandDto[] ReplaceEnabled(IReadOnlyList<ToleranceOperandDto> original, ToleranceOperandDto[] enabled)
        {
            var index = 0;
            return original.Select(row => row.Enabled ? enabled[index++] : row).ToArray();
        }
    }
}
