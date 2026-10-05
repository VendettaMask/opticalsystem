using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static readonly string[] GradientOptionKeys =
    ["maximumPathLength", "maximumStep", "minimumStep", "positionTolerance", "directionTolerance",
     "opticalPathTolerance", "relativeTolerance", "surfaceTolerance", "maximumAttempts"];

    private static string[] GradientProfileKeys(string kind)
    {
        try { return GradientIndexProfileData.Keys(kind).ToArray(); }
        catch (ArgumentException error) { throw new InvalidDataException(error.Message, error); }
    }

    private static ComponentSnapshot FromGradientIndex(GradientIndexMaterial material)
    {
        var kind = GradientIndexProfileData.Kind(material.Profile);
        var numbers = GradientIndexProfileData.Values(material.Profile).ToDictionary(p => p.Key, p => p.Value);
        var options = material.IntegrationOptions;
        var values = new[] { material.MaximumPathLength, options.MaximumStep, options.MinimumStep,
            options.PositionTolerance, options.DirectionTolerance, options.OpticalPathTolerance,
            options.RelativeTolerance, options.SurfaceTolerance, options.MaximumAttempts };
        foreach (var pair in GradientOptionKeys.Zip(values)) numbers.Add(pair.First, pair.Second);
        var children = new Dictionary<string, ComponentSnapshot>();
        if (material.Profile is Gradient5IndexProfile { Dispersion: { } dispersion })
            children.Add("dispersion", FromGradient5Dispersion(dispersion));
        if (material.Variables.Count > 0)
        {
            var ranges = new Dictionary<string, double>();
            foreach (var pair in material.Variables.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ranges.Add(pair.Key + "_min", pair.Value.Minimum);
                ranges.Add(pair.Key + "_max", pair.Value.Maximum);
            }
            children.Add("variables", new ComponentSnapshot("gradient_index_variables", ranges, new()));
        }
        return new ComponentSnapshot("gradient_index", numbers, new() { ["name"] = material.Name, ["profile"] = kind }, children.Count == 0 ? null : children);
    }

    internal static GradientIndexMaterial ToGradientIndex(ComponentSnapshot snapshot)
    {
        if (snapshot.Kind != "gradient_index"
            || snapshot.Text is null || snapshot.Numbers is null
            || !snapshot.Text.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["name", "profile"]))
            throw new InvalidDataException("GRIN 材料需要且只接受名称、分布类型和完整数值表。");
        var kind = snapshot.Text["profile"];
        Gradient5Dispersion? dispersion = null;
        Dictionary<string, GradientIndexVariableRange> variables = new(StringComparer.Ordinal);
        if (snapshot.Children is { Count: > 0 })
        {
            foreach (var child in snapshot.Children)
            {
                if (child.Key == "dispersion" && kind == "gradient5") dispersion = ToGradient5Dispersion(child.Value);
                else if (child.Key == "variables") variables = ToGradientVariables(child.Value, kind);
                else throw new InvalidDataException("未知或不适用于当前 GRIN 分布的子组件。");
            }
        }
        var required = GradientProfileKeys(kind).Concat(GradientOptionKeys).ToHashSet(StringComparer.Ordinal);
        if (!required.SetEquals(snapshot.Numbers.Keys) || snapshot.Numbers.Values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("GRIN 材料数值表存在缺失、未知或非有限参数，不能用默认值替换。");
        var n = snapshot.Numbers;
        var attempts = n["maximumAttempts"];
        if (attempts != Math.Truncate(attempts) || attempts is < 1 or > 1_000_000)
            throw new InvalidDataException("GRIN 最大积分次数必须为 1～1000000 的整数。");
        var profile = GradientIndexProfileData.Create(kind, GradientProfileKeys(kind).ToDictionary(key => key, key => n[key]), dispersion);
        return new GradientIndexMaterial(snapshot.Text["name"], profile, n["maximumPathLength"],
            new GradientIndexIntegrationOptions(n["maximumStep"], n["minimumStep"], n["positionTolerance"],
                n["directionTolerance"], n["opticalPathTolerance"], n["relativeTolerance"], n["surfaceTolerance"], (int)attempts), variables);
    }
    private static Dictionary<string, GradientIndexVariableRange> ToGradientVariables(ComponentSnapshot snapshot, string kind)
    {
        if (snapshot is null || snapshot.Kind != "gradient_index_variables" || snapshot.Numbers is null
            || snapshot.Text is null || snapshot.Text.Count != 0 || snapshot.Children is { Count: > 0 }
            || snapshot.Numbers.Count == 0 || snapshot.Numbers.Values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("GRIN 变量范围组件不完整或包含未知内容。");
        var result = new Dictionary<string, GradientIndexVariableRange>(StringComparer.Ordinal);
        foreach (var key in GradientProfileKeys(kind))
        {
            var hasMinimum = snapshot.Numbers.TryGetValue(key + "_min", out var minimum);
            var hasMaximum = snapshot.Numbers.TryGetValue(key + "_max", out var maximum);
            if (hasMinimum != hasMaximum) throw new InvalidDataException("GRIN 变量必须同时保存上下限。");
            if (hasMinimum) result.Add(key, new(minimum, maximum));
        }
        if (snapshot.Numbers.Count != result.Count * 2) throw new InvalidDataException("GRIN 变量引用未知系数。");
        return result;
    }

}
