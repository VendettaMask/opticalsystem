namespace OptilandWorkbench.Core.Materials;

/// <summary>Shared data contract for immutable GRIN profiles. Contains no tracing formulas.</summary>
public static class GradientIndexProfileData
{
    public static IReadOnlyList<string> Keys(string kind) => Array.AsReadOnly(kind switch
    {
        "gradient1" => new[] { "n0", "nr2", "nr1" },
        "gradient2" => ["n0Squared", "nr2", "nr4", "nr6", "nr8", "nr10", "nr12"],
        "gradient3" => ["n0", "nr2", "nr4", "nr6", "nz1", "nz2", "nz3"],
        "gradient4" => ["n0", "nx1", "nx2", "ny1", "ny2", "nz1", "nz2"],
        "gradient5" => ["n0", "nr2", "nr4", "nz1", "nz2", "nz3", "nz4"],
        _ => throw new ArgumentException("未知的 GRIN 分布类型。", nameof(kind))
    });

    public static string Kind(ISpatialRefractiveIndex profile) => profile switch
    {
        Gradient1IndexProfile => "gradient1",
        Gradient2IndexProfile => "gradient2",
        Gradient3IndexProfile => "gradient3",
        Gradient4IndexProfile => "gradient4",
        Gradient5IndexProfile => "gradient5",
        _ => throw new NotSupportedException("此空间分布没有可编辑的系数映射。")
    };

    public static IReadOnlyDictionary<string, double> Values(ISpatialRefractiveIndex profile)
    {
        double[] values = profile switch
        {
            Gradient1IndexProfile p => [p.BaseIndex, p.RadialQuadratic, p.RadialLinear],
            Gradient2IndexProfile p => new[] { p.BaseIndexSquared }.Concat(p.RadialCoefficients).ToArray(),
            Gradient3IndexProfile p => [p.BaseIndex, p.Radial2, p.Radial4, p.Radial6, p.Axial1, p.Axial2, p.Axial3],
            Gradient4IndexProfile p => [p.BaseIndex, p.Linear.X, p.Quadratic.X, p.Linear.Y, p.Quadratic.Y, p.Linear.Z, p.Quadratic.Z],
            Gradient5IndexProfile p => [p.BaseIndex, p.Radial2, p.Radial4, p.Axial1, p.Axial2, p.Axial3, p.Axial4],
            _ => throw new NotSupportedException("此空间分布没有可编辑的系数映射。")
        };
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(
            Keys(Kind(profile)).Zip(values).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal));
    }

    public static ISpatialRefractiveIndex Create(string kind, IReadOnlyDictionary<string, double> values,
        Gradient5Dispersion? dispersion = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!Keys(kind).ToHashSet(StringComparer.Ordinal).SetEquals(values.Keys)
            || values.Values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("GRIN 系数必须完整、有限且不含未知项。", nameof(values));
        if (kind != "gradient5" && dispersion is not null) throw new ArgumentException("只有 Gradient 5 支持此色散模型。");
        var n = values;
        return kind switch
        {
            "gradient1" => new Gradient1IndexProfile(n["n0"], n["nr2"], n["nr1"]),
            "gradient2" => new Gradient2IndexProfile(n["n0Squared"], n["nr2"], n["nr4"], n["nr6"], n["nr8"], n["nr10"], n["nr12"]),
            "gradient3" => new Gradient3IndexProfile(n["n0"], n["nr2"], n["nr4"], n["nr6"], n["nz1"], n["nz2"], n["nz3"]),
            "gradient4" => new Gradient4IndexProfile(n["n0"], n["nx1"], n["nx2"], n["ny1"], n["ny2"], n["nz1"], n["nz2"]),
            "gradient5" => new Gradient5IndexProfile(n["n0"], n["nr2"], n["nr4"], n["nz1"], n["nz2"], n["nz3"], n["nz4"], dispersion),
            _ => throw new ArgumentException("未知的 GRIN 分布类型。", nameof(kind))
        };
    }
}

public sealed record GradientIndexVariableRange(double Minimum, double Maximum)
{
    internal void Validate(string parameter, double value)
    {
        if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Minimum >= Maximum || !double.IsFinite(Maximum - Minimum) || value < Minimum || value > Maximum
            || parameter is "n0" or "n0Squared" && Minimum <= 0)
            throw new ArgumentException($"GRIN {parameter} 变量需要有限且包含当前值的有效上下限；基准折射率下限必须大于零。");
    }
}
