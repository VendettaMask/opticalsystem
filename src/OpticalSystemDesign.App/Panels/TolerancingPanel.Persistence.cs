using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel
{
    internal ToleranceFileDto BuildStudyFile() => new(
        HasBoundMeritTables(_meritTable, _stageObjectives) ? 10
            : UsesSystemMerit((ToleranceCriterion)_criterion.SelectedIndex, _stageObjectives) ? 9
            : _stageObjectives is { Count: > 0 } ? 8
            : _configurationSettings is not null || _additionalCompensators.Any(row => row.Configuration != 0) ? 7
            : _additionalCompensators.Any(row => row.Stage != 1) ? 6
            : _operands.Any(row => row.Kind.SurfaceFormModeFor().HasValue)
            || _additionalCompensators.Any(row => row.Pivot is not null) ? 5
            : _operands.Any(row => ToleranceOperandEditorRow.UsesFringes(row.Kind))
            || _additionalCompensators.Any(row => row.Kind.IsElement()) ? 4 : 3,
        _operands.Select(row => row.ToDto()).ToArray(), (ToleranceCriterion)_criterion.SelectedIndex,
        IntValue(_trials, 20), IntValue(_seed, 1234), IntValue(_compensationIterations, 3),
        DoubleValue(_yieldLimit, 0), _analysisMode, _inverseValue, _distributionOverride,
        _maxDegreeOfParallelism, _worstSensitivityCount, _showMonteCarloTrials,
        _mtfSettings, _compensationAlgorithm, _additionalCompensators, _configurationSettings, _stageObjectives, _meritTable);

    internal ToleranceFileDto ParseStudyFile(string json)
    {
        var document = System.Text.Json.JsonSerializer.Deserialize<ToleranceFileDto>(json, ToleranceJsonOptions);
        if (document is null || document.SchemaVersion is < 1 or > 10 || document.Operands is null)
            throw new InvalidDataException("不支持的公差文件版本或操作数表缺失。");
        if (document.SchemaVersion < 10 && HasBoundMeritTables(document.MeritTable, document.StageObjectives))
            throw new InvalidDataException("独立评价表需要版本 10 公差文件。");
        if (document.SchemaVersion < 9 && UsesSystemMerit(document.Criterion, document.StageObjectives))
            throw new InvalidDataException("系统评价函数需要版本 9 公差文件。");
        if (document.SchemaVersion < 8 && document.StageObjectives is { Count: > 0 })
            throw new InvalidDataException("逐阶段评价需要版本 8 公差文件。");
        if (document.SchemaVersion < 7 && (document.ConfigurationSettings is not null
            || document.AdditionalCompensators?.Any(row => row.Configuration != 0) == true))
            throw new InvalidDataException("多配置公差需要版本 7 公差文件。");
        if (document.SchemaVersion < 6 && document.AdditionalCompensators?.Any(row => row.Stage != 1) == true)
            throw new InvalidDataException("分步装调需要版本 6 公差文件。");
        if (document.SchemaVersion < 5 && (document.Operands.Any(row => row.Kind.SurfaceFormModeFor().HasValue)
            || document.AdditionalCompensators?.Any(row => row.Pivot is not null) == true))
            throw new InvalidDataException("Zernike、ISO 面形公差和机械支点需要版本 5 公差文件。");
        if (document.SchemaVersion < 4 && (document.Operands.Any(row => ToleranceOperandEditorRow.UsesFringes(row.Kind))
            || document.AdditionalCompensators?.Any(row => row.Kind.IsElement()) == true))
            throw new InvalidDataException("条纹公差与镜组补偿需要版本 4 公差文件。");
        if (!Enum.IsDefined(document.Mode) || !double.IsFinite(document.InverseValue)
            || document.InverseValue <= 0 || document.MaxDegreeOfParallelism <= 0
            || document.WorstSensitivityCount < 0
            || (document.DistributionOverride.HasValue && !Enum.IsDefined(document.DistributionOverride.Value)))
            throw new InvalidDataException("公差文件包含无效的分析运行设置。");
        var validation = _tolerancing.ValidateStudy(new TolerancingRequestDto(
            document.Operands.FirstOrDefault(row => row.Enabled)?.SurfaceNumber ?? 1, 0, 0,
            document.Trials, document.Seed, document.CompensationIterations, document.Operands,
            document.Criterion, document.YieldLimit, document.MaxDegreeOfParallelism,
            document.Mode, document.InverseValue, document.MtfSettings, document.CompensationAlgorithm, document.AdditionalCompensators, document.ConfigurationSettings, document.StageObjectives, document.MeritTable));
        if (!validation.IsValid) throw new InvalidDataException(string.Join("；", validation.Messages));
        return document;
    }

    private static bool HasBoundMeritTables(ToleranceMeritTableDto? final, IReadOnlyList<ToleranceStageObjectiveDto>? stages) =>
        final is not null || stages?.Any(stage => stage?.MeritTable is not null) == true;

    private static bool UsesSystemMerit(ToleranceCriterion criterion, IReadOnlyList<ToleranceStageObjectiveDto>? stages) =>
        criterion == ToleranceCriterion.SystemMerit || stages?.Any(stage => stage?.Criterion == ToleranceCriterion.SystemMerit) == true;

    internal sealed record ToleranceFileDto(
        int SchemaVersion,
        IReadOnlyList<ToleranceOperandDto> Operands,
        ToleranceCriterion Criterion,
        int Trials,
        int Seed,
        int CompensationIterations,
        double YieldLimit,
        ToleranceAnalysisMode Mode = ToleranceAnalysisMode.Sensitivity,
        double InverseValue = 0.05,
        ToleranceDistribution? DistributionOverride = null,
        int MaxDegreeOfParallelism = 1,
        int WorstSensitivityCount = 0,
        bool ShowMonteCarloTrials = true,
        ToleranceMtfSettingsDto? MtfSettings = null,
        ToleranceCompensationAlgorithm CompensationAlgorithm = ToleranceCompensationAlgorithm.DampedLeastSquares,
        IReadOnlyList<ToleranceCompensatorDto>? AdditionalCompensators = null,
        ToleranceConfigurationSettingsDto? ConfigurationSettings = null,
        IReadOnlyList<ToleranceStageObjectiveDto>? StageObjectives = null, ToleranceMeritTableDto? MeritTable = null);
}
