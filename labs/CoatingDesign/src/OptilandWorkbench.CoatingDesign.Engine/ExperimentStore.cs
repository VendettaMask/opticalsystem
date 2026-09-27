using System.Globalization;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.CoatingDesign.Engine;

public static class ExperimentStore
{
    public static async Task SaveAsync(Experiment experiment, string path, CancellationToken token = default)
    {
        var snapshot = experiment.Snapshot(); snapshot.Validate();
        CheckResult(snapshot);
        await BoundedFile.WriteAllTextAtomicAsync(path, JsonSerializer.Serialize(snapshot, Experiment.JsonOptions),
            BoundedFile.MaximumOpticalDocumentBytes, "镀膜实验", token);
    }

    public static async Task<Experiment> OpenAsync(string path, CancellationToken token = default)
    {
        var json = await BoundedFile.ReadAllTextAsync(path, BoundedFile.MaximumOpticalDocumentBytes, "镀膜实验", token);
        Experiment d;
        try { d = JsonSerializer.Deserialize<Experiment>(json, Experiment.JsonOptions) ?? throw new InvalidDataException("不是有效的镀膜实验文件。"); }
        catch (JsonException error) { throw new InvalidDataException("实验文件格式损坏或数值类型不正确，请使用有效的 .coating.json 文件。", error); }
        d.Validate(); CheckResult(d);
        // Persisted results are evidence, never a trusted substitute for recalculation.
        return d.Result is null ? d : await Task.Run(() => new DesignService().Evaluate(d, d.Result.Run, token: token), token);
    }

    public static async Task ExportAsync(Experiment experiment, string directory, CancellationToken token = default)
    {
        var d = experiment.Snapshot(); d.Validate(); CheckResult(d);
        if (d.Result is null) throw new InvalidOperationException("请先计算当前膜系，再导出光谱和指标。");
        Directory.CreateDirectory(directory);
        var layers = new StringBuilder("层号,材料,物理厚度_nm,变量\n");
        for (var i = 0; i < d.Layers.Length; i++)
        {
            var l = d.Layers[i]; var name = d.Materials.Single(m => m.Id == l.MaterialId).Name;
            layers.AppendLine($"{i + 1},{Csv(name)},{Number(l.ThicknessNm)},{l.Variable}");
        }
        var spectrum = new StringBuilder("波长_nm,R_fraction,T_fraction,A_fraction,ln_T,OD_raw\n");
        foreach (var s in d.Result.Spectrum)
            spectrum.AppendLine(string.Join(',', new[] { s.WavelengthNanometers, s.Power.Reflectance, s.Power.Transmittance,
                s.Power.Absorptance, s.Power.LogTransmittance, s.Power.OpticalDensity }.Select(Number)));
        var metrics = new StringBuilder("指标,实际值,要求,达标\n");
        foreach (var c in d.Result.Checks) metrics.AppendLine($"{Csv(c.Name)},{(c.Actual.HasValue ? Number(c.Actual.Value) : "")},{Csv(c.Requirement)},{c.Passed}");
        foreach (var (name, content) in new[] { ("layers.csv", layers.ToString()), ("spectrum.csv", spectrum.ToString()), ("metrics.csv", metrics.ToString()),
            ("run.json", JsonSerializer.Serialize(d.Result.Run, Experiment.JsonOptions)) })
            await BoundedFile.WriteAllTextAtomicAsync(Path.Combine(directory, name), content, BoundedFile.MaximumExportBytes, "镀膜结果导出", token);
        await SaveAsync(d, Path.Combine(directory, "experiment.coating.json"), token);
    }

    private static string Number(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
    private static string Csv(string text) => '"' + text.Replace("\"", "\"\"") + '"';
    private static void CheckResult(Experiment d)
    {
        if (d.Result is not null && d.Result.Fingerprint != d.Fingerprint())
            throw new InvalidDataException("结果与当前输入不一致，请重新计算后保存。");
        if (d.Tolerance is not null && d.Tolerance.Fingerprint != d.Fingerprint())
            throw new InvalidDataException("公差结果与当前输入不一致，请重新进行公差复验。");
    }
}
