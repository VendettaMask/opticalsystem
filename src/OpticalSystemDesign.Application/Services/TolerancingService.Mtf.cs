using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Tolerancing;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class TolerancingService
{
    public ToleranceValidationResultDto ValidateStudy(TolerancingRequestDto request)
    {
        try
        {
            if (request.ConfigurationSettings is { Scope: not ToleranceConfigurationScope.Current })
            {
                JointToleranceSnapshot snapshot;
                lock (Gate) snapshot = Runtime.CaptureJointToleranceSnapshot();
                WorkbenchRuntime.ValidateJointTolerancing(snapshot, request);
            }
            else ValidateRequest(request);
            return new(true, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return new(false, new[] { exception.Message });
        }
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
                ValidateMtfSettings(optic, settings);
                if (request.YieldLimit > 1 || (request.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement && request.InverseValue > 1))
                    throw new ArgumentException("MTF 限值和下降量必须在 0..1 内。");
            }
            foreach (var stage in request.StageObjectives ?? [])
            {
                if (stage.Criterion != ToleranceCriterion.Mtf) continue;
                try { ValidateMtfSettings(optic, stage.MtfSettings ?? new()); }
                catch (ArgumentException error) { throw new ArgumentException($"补偿阶段 {stage.Stage}：{error.Message}", error); }
            }
            var compensators = request.AdditionalCompensators ?? Array.Empty<ToleranceCompensatorDto>();
            if (compensators.Count > 16 || compensators.Select(item => (item.SurfaceNumber, item.Kind)).Distinct().Count() != compensators.Count)
                throw new ArgumentException("附加补偿器最多 16 项，且不能重复。");
            foreach (var item in compensators)
            {
                if (item.Stage is < 1 or > 8)
                    throw new ArgumentException("补偿阶段须在 1–8 之间。");
                if (!Enum.IsDefined(item.Kind) || item.SurfaceNumber <= 0 || item.SurfaceNumber >= optic.SurfaceGroup.Items.Count - 1
                    || !double.IsFinite(item.Minimum) || !double.IsFinite(item.Maximum)
                    || item.Minimum > 0 || item.Maximum < 0 || item.Minimum >= item.Maximum)
                    throw new ArgumentException("补偿表面或范围无效；偏差范围必须包含名义值 0。");
                if (item.Kind.IsElement() && (item.EndSurfaceNumber <= item.SurfaceNumber
                    || item.EndSurfaceNumber >= optic.SurfaceGroup.Items.Count - 1))
                    throw new ArgumentException("镜组补偿需要有效的起止实体面，且至少包含两个面。");
                if (item.Pivot is { } pivot && (!item.Kind.IsElementTilt()
                    || !IsPivotOffset(pivot.X) || !IsPivotOffset(pivot.Y) || !IsPivotOffset(pivot.Z)))
                    throw new ArgumentException("机械支点只能用于镜组倾斜，坐标必须在 ±1000000 mm 内。");
                var surface = optic.SurfaceGroup.Items[item.SurfaceNumber];
                if (item.Kind == ToleranceCompensatorKind.Thickness
                    && (surface.Thickness + item.Minimum < 0 || surface.MaterialAfter.RefractiveIndex(587.6) > 1.0001))
                    throw new ArgumentException("可调间隔补偿必须作用于非负的空气间隔。");
                if (item.Kind == ToleranceCompensatorKind.Thickness
                    && request.Operands?.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator && row.SurfaceNumber == item.SurfaceNumber) == true)
                    throw new ArgumentException("附加间隔补偿与 COMP 重复。");
            }

            var stageCount = compensators.Select(item => item.Stage).Distinct().Count();
            var hasStagedCompensation = compensators.Any(item => item.Stage > 1);
            var hasFocus = request.Operands is not { Count: > 0 }
                || request.Operands.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator);
            if (hasStagedCompensation && hasFocus) stageCount++;
            if (stageCount > 1 && request.CompensationIterations > MonteCarlo.MaximumCompensationIterations / stageCount)
                throw new ArgumentException("补偿阶段数乘以每阶段循环数不得超过 500。");

            var groups = compensators.Where(item => item.Kind.IsElement())
                .Select(item => (First: item.SurfaceNumber, Last: item.EndSurfaceNumber))
                .Distinct().OrderBy(group => group.First).ToArray();
            for (var i = 1; i < groups.Length; i++)
                if (groups[i].First <= groups[i - 1].Last)
                    throw new ArgumentException("不同镜组补偿的表面范围不能重叠。");
            if (compensators.Where(item => item.Pivot is not null)
                .GroupBy(item => (item.SurfaceNumber, item.EndSurfaceNumber))
                .Any(group => group.Select(item => item.Pivot).Distinct().Skip(1).Any()))
                throw new ArgumentException("同一镜组的倾斜补偿必须共用机械支点。");

            foreach (var group in groups)
            {
                if (compensators.Any(item => !item.Kind.IsElement() && item.SurfaceNumber >= group.First
                    && (item.Kind == ToleranceCompensatorKind.Thickness ? item.SurfaceNumber < group.Last : item.SurfaceNumber <= group.Last))
                    || request.Operands?.Any(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator
                        && row.SurfaceNumber >= group.First && row.SurfaceNumber < group.Last) == true)
                    throw new ArgumentException("镜组补偿与组内单面姿态或组内可调间隔冲突。");
            }
        }
    }

    public ToleranceMeritTableDto CaptureMeritTable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (Gate)
        {
            var optic = Runtime.CurrentOptic;
            var table = new ToleranceMeritTableDto(name.Trim(),
                optic.MeritFunctionOperands.Select(MeritOperandSnapshot.FromDefinition).ToArray());
            ValidateMeritSource(optic, table);
            return table;
        }
    }

    internal static bool HasBoundMeritTables(TolerancingRequestDto request) =>
        request.MeritTable is not null || request.StageObjectives?.Any(stage => stage?.MeritTable is not null) == true;

    internal static bool UsesSystemMerit(TolerancingRequestDto request) =>
        request.Criterion == ToleranceCriterion.SystemMerit
            || request.StageObjectives?.Any(stage => stage?.Criterion == ToleranceCriterion.SystemMerit) == true;

    internal static void ValidateMeritSources(TolerancingRequestDto request, Optic active)
    {
        if (request.Criterion == ToleranceCriterion.SystemMerit) ValidateMeritSource(active, request.MeritTable);
        else if (request.MeritTable is not null) throw new ArgumentException("最终验收的绑定表须使用系统评价函数。");

        foreach (var stage in request.StageObjectives ?? [])
        {
            if (stage is not { Criterion: ToleranceCriterion.SystemMerit }) continue;
            try { ValidateMeritSource(active, stage.MeritTable); }
            catch (ArgumentException error) { throw new ArgumentException($"补偿阶段 {stage.Stage}：{error.Message}", error); }
        }
    }

    private static void ValidateMeritSource(Optic active, ToleranceMeritTableDto? table)
    {
        if (table is not null && (string.IsNullOrWhiteSpace(table.Name) || table.Name.Length > 80
            || table.Rows is not { Count: > 0 and <= 2000 } || table.Rows.Any(row => row is null)))
            throw new ArgumentException("绑定评价表须有名称且包含 1–2000 行，名称最多 80 字符。");
        _ = MeritFunctionCatalog.CreateToleranceResiduals(active,
            table?.Rows.Select(row => row.ToDefinition()).ToArray() ?? active.MeritFunctionOperands.ToArray());
    }

    internal static void ValidateStageObjectives(TolerancingRequestDto request, IEnumerable<int> activeStages)
    {
        if (request.StageObjectives is not { Count: > 0 } objectives) return;
        if (request.CompensationIterations <= 0)
            throw new ArgumentException("设置阶段评价前，请启用装调补偿并设置正的循环数。");

        var stages = activeStages.ToHashSet();
        foreach (var objective in objectives)
        {
            if (objective is null || !stages.Remove(objective.Stage))
                throw new ArgumentException($"补偿阶段 {objective?.Stage} 重复或没有可调量。");
            if (!Enum.IsDefined(objective.Criterion)
                || (objective.Criterion != ToleranceCriterion.Mtf && objective.MtfSettings is not null)
                || (objective.Criterion != ToleranceCriterion.SystemMerit && objective.MeritTable is not null)
                || objective.MtfSettings?.FieldLimits is { Count: > 0 })
                throw new ArgumentException($"补偿阶段 {objective.Stage} 的评价设置无效。阶段评价不使用验收限值。");
        }
    }

    private static void ValidateMtfSettings(Optic optic, ToleranceMtfSettingsDto settings)
    {
        if (optic.ImageSpaceAfocal) throw new ArgumentException("MTF 公差目前只支持有焦像空间（cycles/mm）。");
        if (!double.IsFinite(settings.Frequency) || settings.Frequency <= 0 || settings.Frequency > 1_000_000
            || settings.Sampling is < 1 or > 5 || settings.Wave < 0 || settings.Wave > optic.Wavelengths.Count
            || optic.Wavelengths.Count == 0 || !Enum.IsDefined(settings.Method) || !Enum.IsDefined(settings.Direction)
            || optic.Fields.Count == 0 || !optic.Fields.Any(field => field.Weight > 0))
            throw new ArgumentException("MTF 频率、采样、波长、方向或视场设置无效。");
        var limits = settings.FieldLimits ?? Array.Empty<ToleranceFieldLimitDto>();
        if (limits.Count > optic.Fields.Count || (!settings.SeparateFields && limits.Count > 0)
            || limits.Select(limit => limit.FieldNumber).Distinct().Count() != limits.Count
            || limits.Any(limit => limit.FieldNumber < 1 || limit.FieldNumber > optic.Fields.Count
                || !double.IsFinite(limit.Minimum) || limit.Minimum is < 0 or > 1))
            throw new ArgumentException("逐视场 MTF 下限无效、重复或视场不存在。");
    }

    private static bool IsPivotOffset(double millimeters) =>
        double.IsFinite(millimeters) && millimeters is >= -1_000_000 and <= 1_000_000;
}
