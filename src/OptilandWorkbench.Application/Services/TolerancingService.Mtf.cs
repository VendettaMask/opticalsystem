using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class TolerancingService
{
    public ToleranceValidationResultDto ValidateStudy(TolerancingRequestDto request)
    {
        try { ValidateRequest(request); return new(true, Array.Empty<string>()); }
        catch (ArgumentException exception) { return new(false, new[] { exception.Message }); }
    }

    private void ValidateMtfAndCompensation(TolerancingRequestDto request)
    {
        lock (Gate)
        {
            var optic = Runtime.CurrentOptic;
            if (!Enum.IsDefined(request.CompensationAlgorithm)) throw new ArgumentException("补偿算法无效。");
            if (request.Criterion == ToleranceCriterion.Mtf)
            {
                var settings = request.MtfSettings ?? new ToleranceMtfSettingsDto();
                if (optic.ImageSpaceAfocal) throw new ArgumentException("MTF 公差目前只支持有焦像空间（cycles/mm）。");
                if (!double.IsFinite(settings.Frequency) || settings.Frequency <= 0 || settings.Frequency > 1_000_000
                    || settings.Sampling is < 1 or > 5 || settings.Wave < 0 || settings.Wave > optic.Wavelengths.Count
                    || optic.Wavelengths.Count == 0 || !Enum.IsDefined(settings.Method) || !Enum.IsDefined(settings.Direction)
                    || optic.Fields.Count == 0 || !optic.Fields.Any(field => field.Weight > 0))
                    throw new ArgumentException("MTF 频率、采样、波长、方向或视场设置无效。");
                if (request.YieldLimit > 1 || (request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement && request.InverseValue > 1))
                    throw new ArgumentException("MTF 限值和下降量必须在 0..1 内。");
                var limits = settings.FieldLimits ?? Array.Empty<ToleranceFieldLimitDto>();
                if (limits.Count > optic.Fields.Count || (!settings.SeparateFields && limits.Count > 0)
                    || limits.Select(limit => limit.FieldNumber).Distinct().Count() != limits.Count
                    || limits.Any(limit => limit.FieldNumber < 1 || limit.FieldNumber > optic.Fields.Count
                        || !double.IsFinite(limit.Minimum) || limit.Minimum is < 0 or > 1))
                    throw new ArgumentException("逐视场 MTF 下限无效、重复或视场不存在。");
            }
            var compensators = request.AdditionalCompensators ?? Array.Empty<ToleranceCompensatorDto>();
            if (compensators.Count > 16 || compensators.Select(item => (item.SurfaceNumber, item.Kind)).Distinct().Count() != compensators.Count)
                throw new ArgumentException("附加补偿器最多 16 项，且不能重复。");
            foreach (var item in compensators)
            {
                if (!Enum.IsDefined(item.Kind) || item.SurfaceNumber <= 0 || item.SurfaceNumber >= optic.SurfaceGroup.Items.Count - 1
                    || !double.IsFinite(item.Minimum) || !double.IsFinite(item.Maximum)
                    || item.Minimum > 0 || item.Maximum < 0 || item.Minimum >= item.Maximum)
                    throw new ArgumentException("补偿表面或范围无效；偏差范围必须包含名义值 0。");
                var surface = optic.SurfaceGroup.Items[item.SurfaceNumber];
                if (item.Kind == ToleranceCompensatorKind.Thickness
                    && (surface.Thickness + item.Minimum < 0 || surface.MaterialAfter.RefractiveIndex(587.6) > 1.0001))
                    throw new ArgumentException("可调间隔补偿必须作用于非负的空气间隔。");
                if (item.Kind == ToleranceCompensatorKind.Thickness
                    && request.Operands?.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator && row.SurfaceNumber == item.SurfaceNumber) == true)
                    throw new ArgumentException("附加间隔补偿与 COMP 重复。");
            }
        }
    }
}
