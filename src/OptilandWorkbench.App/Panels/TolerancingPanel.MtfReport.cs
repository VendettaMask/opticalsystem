using System.Text;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed partial class TolerancingPanel
{
    private void AppendMtfReport(StringBuilder builder, TolerancingResultDto result)
    {
        builder.AppendLine($"补偿算法：{_compensationAlgorithm}    附加补偿器：{_additionalCompensators.Count}");
        foreach (var definition in _additionalCompensators)
            builder.AppendLine($"  面 {definition.SurfaceNumber} {definition.Kind} 相对名义范围 [{definition.Minimum:G6}, {definition.Maximum:G6}]");
        AppendCompensators(builder, "名义系统", result.NominalCompensators);
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
    private static string Display(double value) => double.IsFinite(value) ? value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture) : "失效 / 无数据";
}
