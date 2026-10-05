using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Core.Services;

/// <summary>Coefficient updates replace immutable materials and invalidate trace state.</summary>
public static class GradientIndexParameters
{
    public static GradientIndexMaterial Require(OpticalSurface surface) => surface.MaterialAfter as GradientIndexMaterial
        ?? throw new NotSupportedException("当前表面后没有 GRIN 材料。");

    public static double Read(OpticalSurface surface, string parameter) => GradientIndexProfileData.Values(Require(surface).Profile)
        .TryGetValue(parameter, out var value) ? value : throw new ArgumentException("未知的 GRIN 系数。");

    public static void Write(Optic optic, OpticalSurface surface, string parameter, double value)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(surface);
        var index = optic.SurfaceGroup.Items.IndexOf(surface);
        if (index < 0) throw new ArgumentException("该表面不属于当前光学系统。", nameof(surface));
        var material = Require(surface);
        var values = GradientIndexProfileData.Values(material.Profile).ToDictionary(p => p.Key, p => p.Value);
        if (!values.ContainsKey(parameter)) throw new ArgumentException("未知的 GRIN 系数。");
        values[parameter] = value;
        var profile = GradientIndexProfileData.Create(GradientIndexProfileData.Kind(material.Profile), values,
            (material.Profile as Gradient5IndexProfile)?.Dispersion);
        var replacement = new GradientIndexMaterial(material.Name, profile, material.MaximumPathLength, material.IntegrationOptions, material.Variables);
        optic.InvalidateRayTraceCache();
        surface.MaterialAfter = replacement;
        if (index + 1 < optic.SurfaceGroup.Items.Count)
            optic.SurfaceGroup.Items[index + 1].MaterialBefore = replacement.Clone();
    }

    /// <summary>Independent variations that leave the scalar paraxial model's structural domain.</summary>
    public static string? ScalarParaxialVariableDisabledReason(string kind, string parameter) =>
        kind == "gradient1" && parameter == "nr1" ? "nr1 的独立变化会使分布在轴上不可微，当前桌面瞄准不支持此变量。"
        : kind == "gradient4" && parameter is "nx1" or "nx2" or "ny1" or "ny2"
            ? "横向系数的独立变化会破坏共轴或旋转对称条件，当前桌面瞄准不支持此变量。" : null;

    public static IOptimizationVariable CreateVariable(Optic optic, OpticalSurface surface, string parameter, Action? changed = null)
    {
        var material = Require(surface);
        if (!material.Variables.TryGetValue(parameter, out var range)) throw new ArgumentException("该 GRIN 系数未标为变量。");
        return new DelegateVariable($"表面 {surface.Number} GRIN {parameter}", () => Read(surface, parameter),
            value => { Write(optic, surface, parameter, value); changed?.Invoke(); }, range.Minimum, range.Maximum,
            Math.Max(double.Epsilon, (range.Maximum - range.Minimum) * .01), new UnitRangeScaler(range.Minimum, range.Maximum));
    }
}
