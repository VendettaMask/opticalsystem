using System.Globalization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.CoatingDesign.Engine;

/// <summary>Explicit tabulated-nk interchange. Interpolation and optical material evaluation stay in Core.</summary>
public static class MaterialTable
{
    public const string Example = "wavelength_nm,n,k\n400,1.46,0\n550,1.455,0\n700,1.45,0\n";
    public static async Task<string> ReadAsync(string path, CancellationToken token = default) =>
        await BoundedFile.ReadAllTextAsync(path, 4 * 1024 * 1024, "n/k 材料表", token);
    public static MaterialSnapshot Parse(string name, string source, string csv, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source)) throw new ArgumentException("请填写材料名称和数据来源。示例数据请明确标注为用户模型。");
        var lines = csv.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith('#')).ToArray();
        if (lines.Length is < 3 or > 10001) throw new ArgumentException("材料表需要 2–10000 行数据及单位表头。");
        var header = lines[0].TrimStart('\uFEFF').Replace('\t', ',');
        var unit = header switch
        {
            "wavelength_nm,n,k" => AnalysisAxisUnit.Nanometer,
            "wavelength_um,n,k" => AnalysisAxisUnit.Micrometer,
            _ => throw new ArgumentException("第一行须为 wavelength_nm,n,k 或 wavelength_um,n,k；数值使用小数点，n、k 均必填。")
        };
        var x = new double[lines.Length - 1]; var n = new double[x.Length]; var k = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var values = lines[i + 1].Replace('\t', ',').Split(',');
            if (values.Length != 3) throw new ArgumentException($"第 {i + 2} 行需要波长、n、k 三列。");
            double Number(int column)
            {
                if (!double.TryParse(values[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                    throw new ArgumentException($"第 {i + 2} 行包含无效数值。");
                return v;
            }
            x[i] = AnalysisAxisFormatting.Convert(Number(0), unit, AnalysisAxisUnit.Nanometer); n[i] = Number(1); k[i] = Number(2);
            if (!double.IsFinite(x[i]) || x[i] <= 0 || (i > 0 && x[i] <= x[i - 1]) || n[i] <= 0 || k[i] < 0)
                throw new ArgumentException($"第 {i + 2} 行：波长须为正且严格递增，n > 0，k ≥ 0；不自动排序或截断数据。");
        }
        var material = new CatalogGlassMaterial(name.Trim(), "用户实验材料", "tabulated nk", x[0], x[^1],
            refractiveIndexWavelengthsNanometers: x, refractiveIndices: n, extinctionWavelengthsNanometers: x, extinctionCoefficients: k);
        return MaterialLibrary.Capture(material, source.Trim(), x[0], x[^1]) with { Id = id ?? "custom:" + Guid.NewGuid().ToString("N") };
    }
    public static string Export(MaterialSnapshot material)
    {
        if (material.Resolve(new MaterialRegistry()) is not CatalogGlassMaterial c || c.RefractiveIndexWavelengthsNanometers.Count < 2
            || !c.RefractiveIndexWavelengthsNanometers.SequenceEqual(c.ExtinctionWavelengthsNanometers))
            throw new ArgumentException("此材料使用解析色散模型。请复制并提供明确的 n/k 表格，不能把采样近似冒充原始模型。");
        return "wavelength_nm,n,k\n" + string.Join('\n', c.RefractiveIndexWavelengthsNanometers.Select((x, i) =>
            string.Join(',', new[] { x, c.RefractiveIndices[i], c.ExtinctionCoefficients[i] }.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))))) + "\n";
    }
    public static Task WriteAsync(MaterialSnapshot material, string path, CancellationToken token = default) =>
        BoundedFile.WriteAllTextAtomicAsync(path, Export(material), 4 * 1024 * 1024, "n/k 材料表", token);
}
