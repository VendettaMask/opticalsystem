using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    internal static TolerancingResultDto RunJointTolerancing(JointToleranceSnapshot snapshot,
        TolerancingRequestDto request, long revision, CancellationToken cancellationToken) =>
        new JointToleranceStudy(snapshot, request, cancellationToken).Run(revision);

    internal static TolerancingTrialRowDto ReplayJointTolerance(JointToleranceSnapshot snapshot,
        TolerancingRequestDto request, int trial, CancellationToken cancellationToken) =>
        new JointToleranceStudy(snapshot, request, cancellationToken).Replay(trial);

    internal static void ValidateJointTolerancing(JointToleranceSnapshot snapshot, TolerancingRequestDto request) =>
        _ = new JointToleranceStudy(snapshot, request, CancellationToken.None);

    private sealed partial class JointToleranceStudy
    {
        private sealed record Configuration(int Number, Optic Optic, Tolerancing Study,
            ToleranceMtfSettingsDto? Mtf, double Limit, ToleranceCompensatorDto[] Definitions);
        private sealed record Outcome(ToleranceConfigurationResultDto[] Before, ToleranceConfigurationResultDto[] After,
            IReadOnlyList<ToleranceCompensatorValueDto>? Adjustments, IReadOnlyList<ToleranceStageValueDto>? Stages);

        private readonly JointToleranceSnapshot _snapshot;
        private readonly TolerancingRequestDto _request;
        private readonly ToleranceConfigurationSettingsDto _settings;
        private readonly int[] _selected;
        private readonly ToleranceOperandDto[] _operands;
        private readonly JointMceBinding[] _mce;
        private readonly IReadOnlyList<MeritOperandDefinition>? _meritDefinitions;
        private readonly IReadOnlyList<MeritOperandDefinition>? _finalMeritDefinitions;
        private readonly WorkbenchRuntime _runtime;
        private readonly CancellationToken _cancellationToken;
        private readonly bool _mtf;
        private readonly int _focusStage;

        public JointToleranceStudy(JointToleranceSnapshot snapshot, TolerancingRequestDto request, CancellationToken token)
        {
            _snapshot = snapshot;
            _request = request;
            _settings = request.ConfigurationSettings ?? throw new ArgumentException("缺少多配置公差设置。");
            _selected = SelectConfigurations(snapshot, _settings);
            _operands = request.Operands?.Where(row => row.Enabled).ToArray() ?? [];
            _mtf = request.Criterion == ToleranceCriterion.Mtf;
            _cancellationToken = token;
            var nominal = snapshot.Configurations.Select(Optic.FromSnapshot).ToArray();
            var active = nominal[snapshot.ActiveConfiguration - 1];
            TolerancingService.ValidateMeritSources(request, active);
            if (TolerancingService.UsesSystemMerit(request))
                _meritDefinitions = active.MeritFunctionOperands.Select(row => row.Clone()).ToArray();
            _finalMeritDefinitions = ResolveMeritDefinitions(request.MeritTable, _meritDefinitions);
            _runtime = new WorkbenchRuntime(nominal[_selected[0] - 1]);
            _mce = BindJointMce(snapshot, nominal, _selected, _settings.MceRows ?? []);
            var lastStage = (request.AdditionalCompensators ?? []).Select(row => row.Stage)
                .Concat(_mce.Where(row => row.Row.Compensator).Select(row => row.Row.Stage)).DefaultIfEmpty(1).Max();
            _focusStage = lastStage > 1 ? lastStage + 1 : 1;
            Validate(nominal);
        }

        private Configuration[] Build(ToleranceOperandDto[] operands)
        {
            var result = new Configuration[_selected.Length];
            for (var i = 0; i < result.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var number = _selected[i];
                var optic = Optic.FromSnapshot(_snapshot.Configurations[number - 1]);
                var limits = ConfigurationLimit(number);
                var mtf = ConfigurationMtf(number);
                var definitions = CompensationDefinitions(number);
                var study = _runtime.BuildConfiguredTolerancingWorker(optic, operands, _request.Criterion,
                    mtf, _request.CompensationAlgorithm, definitions, _finalMeritDefinitions);
                result[i] = new(number, optic, study, mtf, limits, definitions);
            }
            return result;
        }

        private ToleranceCompensatorDto[] CompensationDefinitions(int configuration) =>
            (_request.AdditionalCompensators ?? []).Where(row => row.Configuration == 0 || row.Configuration == configuration).ToArray();

        private double ConfigurationLimit(int configuration) =>
            _settings.Limits?.FirstOrDefault(row => row.Configuration == configuration)?.Limit ?? _request.YieldLimit;

        private ToleranceMtfSettingsDto? ConfigurationMtf(int configuration)
        {
            if (!_mtf) return null;
            var common = _request.MtfSettings ?? new ToleranceMtfSettingsDto();
            var local = _settings.Limits?.FirstOrDefault(row => row.Configuration == configuration);
            return local?.FieldLimits is { } fields ? common with { FieldLimits = fields } : common;
        }

        private Outcome Evaluate(ToleranceOperandDto[] operands, JointMceBinding[] mce,
            int? trial = null, int? endpoint = null, bool maximum = false, double scale = 1)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var configs = Build(operands);
            var shared = configs.Select(config => (config.Optic, config.Study)).ToArray();
            var optics = configs.ToDictionary(config => config.Number, config => config.Optic);
            var localManufacturing = mce.Where(binding => !binding.Row.Compensator).ToArray();
            var sharedCount = shared[0].Study.Perturbations.Count;
            try
            {
                if (trial is { } number)
                    SharedToleranceExecution.ApplyTrial(shared, _request.Seed, number, _cancellationToken, random =>
                    {
                        foreach (var binding in localManufacturing)
                        {
                            var variable = new DelegateVariable(binding.Name, () => 0, value => binding.Apply(optics, value),
                                binding.Row.Minimum, binding.Row.Maximum);
                            new VariableRangePerturbation(binding.Name, variable, binding.Row.Minimum, binding.Row.Maximum,
                                binding.Row.Distribution == ToleranceDistribution.Normal).Apply(shared[0].Optic, random);
                        }
                    });
                else if (endpoint is { } row)
                {
                    if (row < sharedCount) SharedToleranceExecution.ApplyEndpoint(shared, row, maximum, scale);
                    else
                    {
                        var local = localManufacturing[row - sharedCount];
                        local.Apply(optics, (maximum ? local.Row.Maximum : local.Row.Minimum) * scale);
                    }
                }
            }
            catch (Exception error) when (IsEvaluationError(error))
            {
                var invalid = configs.Select(config => Failed(config.Number, error.Message)).ToArray();
                return new(invalid, invalid, null, null);
            }

            var before = configs.Select(Read).ToArray();
            if (_request.CompensationIterations == 0) return new(before, before, null, null);
            var snapshots = new Dictionary<int, ToleranceConfigurationResultDto[]>();
            try
            {
                var joint = BuildJointCompensators(configs, mce, optics);
                var objectives = new Dictionary<int, (int Configuration, Tolerancing Objective)[]>();
                foreach (var definition in _request.StageObjectives ?? [])
                {
                    var members = configs.Select(config => (config.Number, BuildStageObjective(config.Optic, definition, _meritDefinitions))).ToArray();
                    var objective = new Tolerancing();
                    CombineObjectives(objective, members, definition.Criterion == ToleranceCriterion.Mtf);
                    joint.SetStageObjective(definition.Stage, objective);
                    objectives.Add(definition.Stage, members);
                }
                var evaluated = joint.EvaluateCompensated(_request.CompensationIterations, _cancellationToken, stage =>
                    snapshots[stage] = objectives.TryGetValue(stage, out var members)
                        ? members.Select(member => ReadStageObjective(member.Configuration, member.Objective)).ToArray()
                        : configs.Select(Read).ToArray());
                var stages = ToStageDtos(evaluated.Stages, _request.StageObjectives)?
                    .Select(stage => stage with { Configurations = snapshots[stage.Stage] }).ToArray();
                var failedStage = stages?.FirstOrDefault(stage => !double.IsFinite(stage.Criterion)
                    && (stage.Objective is not null || _request.Criterion == ToleranceCriterion.SystemMerit));
                var after = failedStage is null ? configs.Select(Read).ToArray()
                    : configs.Select(config => Failed(config.Number, $"补偿阶段 {failedStage.Stage} 评价失效 {failedStage.Error}")).ToArray();
                return new(before, after, ToCompensatorDtos(evaluated.Compensators), stages);
            }
            catch (Exception error) when (IsEvaluationError(error))
            {
                return new(before, configs.Select(config => Failed(config.Number, error.Message)).ToArray(), null, null);
            }
        }

        private Tolerancing BuildJointCompensators(Configuration[] configurations, JointMceBinding[] mce,
            IReadOnlyDictionary<int, Optic> optics)
        {
            var joint = new Tolerancing { CompensationOptimizer = configurations[0].Study.CompensationOptimizer };
            CombineObjectives(joint, configurations.Select(config => (config.Number, config.Study)).ToArray(), _mtf);

            foreach (var definition in _request.AdditionalCompensators ?? [])
            {
                var variables = configurations.Where(config => definition.Configuration == 0 || config.Number == definition.Configuration)
                    .Select(config => (config.Number, Variable: config.Study.Compensators[Array.IndexOf(config.Definitions, definition)])).ToArray();
                var name = definition.Configuration == 0 ? "共用 " : $"配置 {definition.Configuration} ";
                if (definition.Configuration == 0 && variables.Length > 1)
                {
                    var nominal = variables.Select(item => item.Variable.Value).ToArray();
                    var lower = variables.Select((item, i) => item.Variable.LowerBound - nominal[i]).Max();
                    var upper = variables.Select((item, i) => item.Variable.UpperBound - nominal[i]).Min();
                    if (lower > upper) throw new InvalidOperationException($"共用补偿器 {variables[0].Variable.Name} 的配置行程没有交集。");
                    joint.AddCompensator(new DelegateVariable(name + variables[0].Variable.Name,
                        () => variables[0].Variable.Value - nominal[0], value =>
                        {
                            for (var i = 0; i < variables.Length; i++) variables[i].Variable.Value = nominal[i] + value;
                        }, lower, upper, variables[0].Variable.StepHint), definition.Stage);
                }
                else AddLocal(name, variables[0].Variable, definition.Stage);
            }

            foreach (var config in configurations)
                foreach (var variable in config.Study.Compensators.Skip(config.Definitions.Length))
                    AddLocal($"配置 {config.Number} ", variable, _focusStage);
            foreach (var binding in mce.Where(item => item.Row.Compensator))
                joint.AddCompensator(binding.CreateCompensator(optics), binding.Row.Stage);
            return joint;

            void AddLocal(string prefix, IOptimizationVariable variable, int stage) =>
                joint.AddCompensator(new DelegateVariable(prefix + variable.Name, () => variable.Value, value => variable.Value = value,
                    variable.LowerBound, variable.UpperBound, variable.StepHint), stage);
        }

        private static void CombineObjectives(Tolerancing joint,
            (int Configuration, Tolerancing Objective)[] members, bool higherIsBetter)
        {
            joint.HigherIsBetter = higherIsBetter;
            joint.StrictMerit = members.Any(member => member.Objective.StrictMerit);
            joint.SetCriterionEvaluator(() => higherIsBetter
                ? members.Min(member => member.Objective.Criterion())
                : members.Max(member => member.Objective.Criterion()));
            foreach (var member in members)
            {
                var operands = member.Objective.Operands;
                var weight = operands.Sum(operand => Math.Abs(operand.Weight)) * members.Length;
                foreach (var operand in operands)
                    joint.AddOperand(operand with { Name = $"配置 {member.Configuration} {operand.Name}", Weight = operand.Weight / weight });
            }
        }

        private static ToleranceConfigurationResultDto ReadStageObjective(int configuration, Tolerancing objective)
        {
            var evaluation = objective.Evaluate();
            return new(configuration, evaluation.Criterion, ToFieldDtos(evaluation.Fields), null, null, null,
                Error: double.IsFinite(evaluation.Criterion) ? "" : "阶段评价失效");
        }

        private ToleranceConfigurationResultDto Read(Configuration config)
        {
            try
            {
                var evaluation = config.Study.Evaluate();
                var fields = ToFieldDtos(evaluation.Fields);
                var adjustments = ToCompensatorDtos(evaluation.Compensators);
                if (!double.IsFinite(evaluation.Criterion))
                    return Failed(config.Number, "评价失效") with { Fields = fields, Compensators = adjustments };
                if (!HasToleranceAcceptance(_mtf, config.Limit, config.Mtf))
                    return new(config.Number, evaluation.Criterion, fields, adjustments, null, null);
                if (config.Mtf is { SeparateFields: true } mtf)
                {
                    if (fields is not { Count: > 0 }) return Failed(config.Number, "无有效视场");
                    var worst = fields.Select(field => (field.FieldNumber, Margin: FieldMargin(field, FieldLimit(mtf, field.FieldNumber, config.Limit))))
                        .MinBy(item => item.Margin);
                    return new(config.Number, evaluation.Criterion, fields, adjustments, worst.Margin >= 0, worst.Margin, worst.FieldNumber);
                }
                var margin = Margin(evaluation.Criterion, config.Limit);
                return new(config.Number, evaluation.Criterion, fields, adjustments, margin >= 0, margin);
            }
            catch (Exception error) when (IsEvaluationError(error))
            {
                return Failed(config.Number, error.Message);
            }
        }

        private ToleranceConfigurationResultDto Failed(int configuration, string error) =>
            new(configuration, _mtf ? double.NegativeInfinity : double.PositiveInfinity, null, null,
                false, double.NegativeInfinity, Error: error);
        private static bool IsEvaluationError(Exception error) =>
            error is InvalidOperationException or ArgumentException or ArithmeticException or KeyNotFoundException or NotSupportedException;
        private double Worst(IEnumerable<double> values) => _mtf ? values.Min() : values.Max();
        private double Margin(double value, double target) => _mtf ? value - target : target - value;
        private static double FieldMargin(ToleranceFieldValueDto field, double target) =>
            double.IsFinite(field.Value) && string.IsNullOrEmpty(field.Error) ? field.Value - target : double.NegativeInfinity;
    }
}
