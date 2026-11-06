using System.Text;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel
{
    private void AppendMtfReport(StringBuilder builder, TolerancingResultDto result)
    {
        if ((ToleranceCriterion)_criterion.SelectedIndex == ToleranceCriterion.SystemMerit)
            builder.AppendLine(OpticalSystemDesign.App.Services.UiLocalization.Text("最终验收使用系统评价函数，按加权残差上限判定。"));
        if (_meritTable is { } finalTable) builder.AppendLine($"  最终评价表 {finalTable.Name}");
        builder.AppendLine($"补偿算法：{_compensationAlgorithm}    附加补偿器：{_additionalCompensators.Count}");
        foreach (var definition in _additionalCompensators)
        {
            var surfaces = definition.Kind.IsElement()
                ? $"{definition.SurfaceNumber}–{definition.EndSurfaceNumber}" : $"{definition.SurfaceNumber}";
            var configuration = result.NominalConfigurations is null ? "" : definition.Configuration == 0 ? "共用 " : $"配置 {definition.Configuration} ";
            builder.AppendLine($"  {configuration}面 {surfaces} {definition.Kind} 阶段 {definition.Stage} 相对名义范围 [{definition.Minimum:G6}, {definition.Maximum:G6}]");
            if (definition.Pivot is { } pivot)
                builder.AppendLine($"  镜组 {surfaces} 首面坐标机械支点 ({Display(pivot.X)}, {Display(pivot.Y)}, {Display(pivot.Z)}) mm");
        }
        AppendCompensators(builder, "名义系统", result.NominalCompensators);
        AppendStages(builder, result.NominalStages);
        AppendConfigurations(builder, "名义", result.NominalConfigurations);
        foreach (var row in result.ConfigurationYields ?? [])
            builder.AppendLine($"  配置 {row.Configuration} 良率 {row.Passed}/{row.Trials} {(row.Yield is { } yield ? yield.ToString("P1") : "未设置")}");
        if (result.ConfigurationYields is { Count: > 0 })
        {
            builder.AppendLine("整机良率按同一次试验的全部配置同时达标统计。");
            foreach (var configuration in result.ConfigurationYields)
            {
                var limit = _configurationSettings?.Limits?.FirstOrDefault(row => row.Configuration == configuration.Configuration);
                var fieldLimits = limit?.FieldLimits ?? result.MtfSettings?.FieldLimits;
                var value = limit?.Limit ?? DoubleValue(_yieldLimit, 0);
                builder.AppendLine($"  配置 {configuration.Configuration} 验收限值 {(value > 0 ? Display(value) : "未设置")}");
                if (result.MtfSettings is { SeparateFields: true })
                    foreach (var field in fieldLimits ?? [])
                        builder.AppendLine($"    视场 {field.FieldNumber} 下限 {Display(field.Minimum)}");
            }
            foreach (var row in _configurationSettings?.MceRows ?? [])
                builder.AppendLine($"  {(row.Compensator ? "CMCO" : "TMCO")} 配置 {row.Configuration} 行 {row.Row} 面 {row.SurfaceNumber} 相对范围 [{Display(row.Minimum)}, {Display(row.Maximum)}]");
        }
        if (result.WorstSample is { } worst)
            builder.AppendLine($"最差样件：试验 {worst.Recorded.Trial}");
        if (result.MtfSettings is not { } settings) return;
        builder.AppendLine($"MTF 频率：{settings.Frequency:G6} cycles/mm    瞳孔：{32 << (settings.Sampling - 1)} × {32 << (settings.Sampling - 1)}    波长编号：{settings.Wave}（0=多波长）");
        builder.AppendLine($"方法：{settings.Method}    方向：{settings.Direction}    验收：{(settings.SeparateFields ? "所有定义视场同时达标" : "按视场权重平均值达标")}");
        builder.AppendLine("MTF 为无量纲 0..1；合格值 ≥ 下限。计算失败计入不合格。");
        AppendFields(builder, "名义", result.NominalFields);
        if (result.FieldStatistics is { Count: > 0 } fields)
        {
            builder.AppendLine("分视场统计：名义 / 均值 / 最小 / P05 / 下限 / 良率（分母包括失败试验）");
            foreach (var field in fields)
                builder.AppendLine($"  视场 {field.FieldNumber}: {Display(field.Nominal)} / {Display(field.Mean)} / {Display(field.Minimum)} / {Display(field.Percentile05)} / {(field.Limit.HasValue ? Display(field.Limit.Value) : "未设置")} / {(field.Yield.HasValue ? $"{field.Yield.Value:0.0}%" : "未计算")}");
        }
        builder.AppendLine("总良率按同一试验同时通过全部限值统计；逐视场良率不能相乘代替总良率。RSS 预计值为局部估算，不替代 Monte Carlo 验收。");
    }

    private static void AppendFields(StringBuilder builder, string label, IReadOnlyList<ToleranceFieldValueDto>? fields)
    {
        foreach (var field in fields ?? Array.Empty<ToleranceFieldValueDto>())
            builder.AppendLine($"  {label}视场 {field.FieldNumber}: {Display(field.Value)}{(string.IsNullOrEmpty(field.Error) ? "" : $"（{field.Error}）")}");
    }
    private static void AppendCompensators(StringBuilder builder, string label, IReadOnlyList<ToleranceCompensatorValueDto>? values)
    {
        foreach (var value in values ?? Array.Empty<ToleranceCompensatorValueDto>())
            builder.AppendLine($"  {label}补偿 {value.Name}: {Display(value.Nominal)} → {Display(value.Value)}，调整 {Display(value.Value - value.Nominal)}，绝对范围 [{Display(value.Minimum)}, {Display(value.Maximum)}]");
    }
    private static void AppendStages(StringBuilder builder, IReadOnlyList<ToleranceStageValueDto>? stages, string label = "")
    {
        foreach (var stage in stages ?? Array.Empty<ToleranceStageValueDto>())
        {
            var criterion = stage.Objective?.Criterion switch
            {
                ToleranceCriterion.RmsSpotRadius => "RMS 点列半径 (mm)",
                ToleranceCriterion.RmsWavefront => "RMS 波前误差",
                ToleranceCriterion.SystemMerit => "系统评价函数",
                ToleranceCriterion.Mtf => $"MTF {stage.Objective.MtfSettings?.Frequency ?? 50:G6} cycles/mm",
                _ => "最终评价准则"
            };
            builder.AppendLine($"  {label}装调阶段 {stage.Stage} {criterion}，评价值 {Display(stage.Criterion)}");
            if (stage.Objective is { Criterion: ToleranceCriterion.Mtf } objective)
            {
                var settings = objective.MtfSettings ?? new();
                builder.AppendLine($"    {settings.Method} / {settings.Direction} / {32 << (settings.Sampling - 1)} × {32 << (settings.Sampling - 1)} / 波长编号 {settings.Wave}");
            }
            if (stage.MeritTableName is { } tableName) builder.AppendLine($"    评价表 {tableName}");
            if (stage.Error.Length > 0) builder.AppendLine($"    {stage.Error}");
            AppendFields(builder, "阶段", stage.Fields);
            AppendCompensators(builder, "阶段", stage.Compensators);
            AppendConfigurations(builder, "阶段", stage.Configurations);
        }
    }
    private static void AppendConfigurations(StringBuilder builder, string label, IReadOnlyList<ToleranceConfigurationResultDto>? configurations)
    {
        foreach (var row in configurations ?? [])
        {
            var status = row.Passed is null ? "" : row.Passed.Value ? "，合格" : "，不合格";
            var field = row.LimitingField is { } number ? $"，限制视场 {number}" : "";
            var margin = row.AcceptanceMargin is { } value ? $"，余量 {Display(value)}" : "";
            builder.AppendLine($"  {label}配置 {row.Configuration} 评价 {Display(row.Criterion)}{status}{field}{margin} {row.Error}");
            AppendFields(builder, $"配置 {row.Configuration}", row.Fields);
            AppendCompensators(builder, $"配置 {row.Configuration}", row.Compensators);
        }
    }

    private static void AppendLimitingConfiguration(StringBuilder builder, string label, TolerancingInverseEndpointDto endpoint)
    {
        if (endpoint.LimitingConfiguration is not { } configuration) return;
        var field = endpoint.LimitingField is { } number ? $"，视场 {number}" : "";
        builder.AppendLine($"  {label}限制配置 {configuration}{field}，余量 {Display(endpoint.AcceptanceMargin ?? double.NaN)}");
    }

    private static string Display(double value) => double.IsFinite(value) ? value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) : "失效 / 无数据";
}
