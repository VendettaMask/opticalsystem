using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Core.Serialization;

public static partial class ComponentSnapshotFactory
{
    private static ComponentSnapshot FromGradient5Dispersion(Gradient5Dispersion dispersion)
    {
        var numbers = new Dictionary<string, double>
        {
            ["referenceWavelengthNanometers"] = dispersion.ReferenceWavelengthNanometers,
            ["minimumWavelengthNanometers"] = dispersion.MinimumWavelengthNanometers,
            ["maximumWavelengthNanometers"] = dispersion.MaximumWavelengthNanometers,
            ["kCount"] = dispersion.K[0].Count,
            ["lCount"] = dispersion.L[0].Count
        };
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < dispersion.K[i].Count; j++) numbers.Add($"k{i + 1}_{j + 1}", dispersion.K[i][j]);
            for (var j = 0; j < dispersion.L[i].Count; j++) numbers.Add($"l{i + 1}_{j + 1}", dispersion.L[i][j]);
        }
        return new ComponentSnapshot("gradient5_dispersion", numbers, new());
    }

    private static Gradient5Dispersion ToGradient5Dispersion(ComponentSnapshot snapshot)
    {
        string[] fixedKeys = ["referenceWavelengthNanometers", "minimumWavelengthNanometers", "maximumWavelengthNanometers", "kCount", "lCount"];
        if (snapshot is null || snapshot.Kind != "gradient5_dispersion" || snapshot.Children is { Count: > 0 }
            || snapshot.Text is null || snapshot.Text.Count != 0 || snapshot.Numbers is null
            || fixedKeys.Any(key => !snapshot.Numbers.ContainsKey(key)) || snapshot.Numbers.Values.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Gradient 5 色散组件不完整或含有未知内容。");
        var n = snapshot.Numbers;
        var kCount = Count(n["kCount"]); var lCount = Count(n["lCount"]);
        var required = fixedKeys.ToHashSet(StringComparer.Ordinal);
        for (var i = 1; i <= 3; i++)
        {
            for (var j = 1; j <= kCount; j++) required.Add($"k{i}_{j}");
            for (var j = 1; j <= lCount; j++) required.Add($"l{i}_{j}");
        }
        if (!required.SetEquals(n.Keys)) throw new InvalidDataException("Gradient 5 色散系数缺失或存在未知参数，不能用默认值替换。");
        return new Gradient5Dispersion(n[fixedKeys[0]], n[fixedKeys[1]], n[fixedKeys[2]],
            Rows("k", kCount), Rows("l", lCount));

        IReadOnlyList<IReadOnlyList<double>> Rows(string prefix, int count) =>
            Enumerable.Range(1, 3).Select(i => (IReadOnlyList<double>)Enumerable.Range(1, count).Select(j => n[$"{prefix}{i}_{j}"]).ToArray()).ToArray();
        static int Count(double value) => value == Math.Truncate(value) && value is >= 1 and <= 8
            ? (int)value : throw new InvalidDataException("Gradient 5 色散阶数必须为 1～8 的整数。");
    }
}
