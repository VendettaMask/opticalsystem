using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed partial class TolerancingPanel
{
    internal ToleranceFileDto BuildStudyFile() => new(3,
        _operands.Select(row => row.ToDto()).ToArray(), (ToleranceCriterion)_criterion.SelectedIndex,
        IntValue(_trials, 20), IntValue(_seed, 1234), IntValue(_compensationIterations, 3),
        DoubleValue(_yieldLimit, 0), _analysisMode, _inverseValue, _distributionOverride,
        _maxDegreeOfParallelism, _worstSensitivityCount, _showMonteCarloTrials,
        _mtfSettings, _compensationAlgorithm, _additionalCompensators);

    internal ToleranceFileDto ParseStudyFile(string json)
    {
        var document = System.Text.Json.JsonSerializer.Deserialize<ToleranceFileDto>(json, ToleranceJsonOptions);
        if (document is null || document.SchemaVersion is < 1 or > 3 || document.Operands is null)
            throw new InvalidDataException("不支持的公差文件版本或操作数表缺失。");
        if (!Enum.IsDefined(document.Mode) || !double.IsFinite(document.InverseValue)
            || document.InverseValue <= 0 || document.MaxDegreeOfParallelism <= 0
            || document.WorstSensitivityCount < 0
            || (document.DistributionOverride.HasValue && !Enum.IsDefined(document.DistributionOverride.Value)))
            throw new InvalidDataException("公差文件包含无效的分析运行设置。");
        var validation = _tolerancing.ValidateStudy(new TolerancingRequestDto(
            document.Operands.FirstOrDefault(row => row.Enabled)?.SurfaceNumber ?? 1, 0, 0,
            document.Trials, document.Seed, document.CompensationIterations, document.Operands,
            document.Criterion, document.YieldLimit, document.MaxDegreeOfParallelism,
            document.Mode, document.InverseValue, document.MtfSettings, document.CompensationAlgorithm, document.AdditionalCompensators));
        if (!validation.IsValid) throw new InvalidDataException(string.Join("；", validation.Messages));
        return document;
    }

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
        IReadOnlyList<ToleranceCompensatorDto>? AdditionalCompensators = null);
}
