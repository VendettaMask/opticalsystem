using System.Globalization;
using System.Text;
using OptilandWorkbench.ZemaxComparison.Metrics;

namespace OptilandWorkbench.ZemaxComparison.Reporting;

public static class ReportWriter
{
    public static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    public static void Values(string path, MatchedValues values)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("x,y,workbench,zemax,absoluteError,relativeError");
        for (var i = 0; i < values.X.Length; i++)
        {
            var error = Math.Abs(values.Workbench[i] - values.Zemax[i]);
            writer.WriteLine(string.Join(',', new double?[] { values.X[i], values.Y?[i], values.Workbench[i], values.Zemax[i], error,
                values.Zemax[i] == 0 ? null : error / Math.Abs(values.Zemax[i]) }.Select(v => v?.ToString("R", CultureInfo.InvariantCulture) ?? "")));
        }
    }
    public static void Write(string directory, object manifest, IReadOnlyList<AnalysisRun> runs, string language, int exitCode, string? fatalError)
    {
        JsonFiles.Write(Path.Combine(directory, "manifest.json"), manifest);
        var counts = Enum.GetValues<Conclusion>().ToDictionary(c => c.ToString(), c => runs.Count(r => r.Conclusion == c));
        JsonFiles.Write(Path.Combine(directory, "run-summary.json"), new
        {
            ToolVersion = "1.0.0",
            UpdatedUtc = DateTimeOffset.UtcNow,
            ExitCode = exitCode,
            FatalError = fatalError,
            Enumerated = runs.Count,
            WorkbenchExecuted = runs.Count(r => r.WorkbenchStatus == CaptureStatus.Captured),
            ZemaxExecuted = runs.Count(r => r.ZemaxStatus == CaptureStatus.Captured),
            NumericallyCompared = runs.Count(r => r.Metrics.Count > 0 && r.Conclusion is Conclusion.Pass or Conclusion.Close or Conclusion.Difference),
            Counts = counts,
            SupportCounts = Enum.GetValues<SupportStatus>().ToDictionary(s => s.ToString(), s => runs.Count(r => r.Support == s)),
            Runs = runs
        });
        JsonFiles.Write(Path.Combine(directory, "errors", "failed-analyses.json"), runs.Where(r => r.Conclusion == Conclusion.Error).ToArray());
        using (var csv = new StreamWriter(Path.Combine(directory, "analysis-matrix.csv")))
        {
            csv.WriteLine("analysis,zemaxStatus,workbenchStatus,support,conclusion,worstNrmse,reason");
            foreach (var r in runs) csv.WriteLine(string.Join(',', new[] { r.Key, r.ZemaxStatus.ToString(), r.WorkbenchStatus.ToString(), r.Support.ToString(),
                r.Conclusion.ToString(), r.Metrics.Count == 0 ? "" : r.Metrics.Max(m => m.Nrmse).ToString("R", CultureInfo.InvariantCulture), r.Reason }.Select(Csv)));
        }
        var zh = language == "zh-CN";
        var md = new StringBuilder(zh ? "# Zemax / Workbench 数值比较\n\n" : "# Zemax / Workbench numerical comparison\n\n");
        md.AppendLine(zh ? "结论仅适用于 manifest 记录的镜头哈希、CapturedSettings、软件版本和容差配置。图像是数值重绘，原生截图状态单列；截图不计数值通过。"
            : "Conclusions apply only to the recorded source hash, CapturedSettings, software versions and tolerances. Numerical plots are redraws; screenshots are never numeric passes.");
        md.AppendLine($"\nEnumerated: {runs.Count}; Workbench captured: {runs.Count(r => r.WorkbenchStatus == CaptureStatus.Captured)}; Zemax captured: {runs.Count(r => r.ZemaxStatus == CaptureStatus.Captured)}; " + string.Join("; ", counts.Select(p => $"{p.Key}: {p.Value}")));
        if (fatalError is not null) md.AppendLine("\nRun error: " + Escape(fatalError));
        md.AppendLine("\n| Analysis | Zemax | Workbench | Comparison | Worst NRMSE | Reason |\n|---|---|---|---|---:|---|");
        foreach (var r in runs) md.AppendLine($"| [{Escape(r.Key)}](comparisons/{r.Directory}/comparison.json) | {r.ZemaxStatus} | {r.WorkbenchStatus} | {r.Conclusion} | {(r.Metrics.Count == 0 ? "—" : r.Metrics.Max(m => m.Nrmse).ToString("G6", CultureInfo.InvariantCulture))} | {Escape(r.Reason)} |");
        md.AppendLine((zh ? "\n## 环境与输入\n\n```json\n" : "\n## Environment and input\n\n```json\n") + System.Text.Json.JsonSerializer.Serialize(manifest, JsonFiles.Options) + "\n```\n");
        md.AppendLine(zh ? "## 最差差异\n" : "## Worst differences\n");
        foreach (var r in runs.Where(r => r.Metrics.Count > 0).OrderByDescending(r => r.Metrics.Max(m => m.Nrmse)).Take(10))
            md.AppendLine($"- {r.Key}: {r.Conclusion}, NRMSE {r.Metrics.Max(m => m.Nrmse):G6}");
        foreach (var r in runs.Where(r => r.Request is not null))
        {
            md.AppendLine($"\n## {r.Key}\n\n{r.Conclusion}: {Escape(r.Reason)}\n\nNative screenshot: {r.ScreenshotStatus}\n");
            if (r.ScreenshotStatus == "CapturedNativeJpegWithExplicitCfg")
                md.AppendLine($"![Native OpticStudio window](raw/zemax/{r.Directory}/screenshot.jpg)\n");
            md.AppendLine("Settings origin: CapturedSettings; request SHA-256: `" + r.Request!.Fingerprint + "`\n");
            md.AppendLine("```json\n" + System.Text.Json.JsonSerializer.Serialize(r.Request, JsonFiles.Options) + "\n```\n");
            if (r.Tolerances.Count > 0)
                md.AppendLine("Per-quantity tolerances (configuration SHA-256 in manifest):\n\n```json\n" + System.Text.Json.JsonSerializer.Serialize(r.Tolerances, JsonFiles.Options) + "\n```\n");
            foreach (var n in r.Normalizations) md.AppendLine("- " + n);
            if (r.Metrics.Count > 0) md.AppendLine("\n```json\n" + System.Text.Json.JsonSerializer.Serialize(r.Metrics, JsonFiles.Options) + "\n```\n");
            var path = Path.Combine(directory, "comparisons", r.Directory);
            if (Directory.Exists(path)) foreach (var png in Directory.EnumerateFiles(path, "*.png").Order())
                md.AppendLine($"![{Path.GetFileNameWithoutExtension(png)}](comparisons/{r.Directory}/{Path.GetFileName(png)})\n");
        }
        md.AppendLine(zh
            ? "\n## 适用边界\n\n未实现适配器、非等价参考球、模型预检、API/许可证失败和数值差异均单独记录。捕获设置不是通用 Zemax 默认值。本工具不拟合缩放、不搜索对齐方式，也不通过重新归一化改善分数。--fail-on none/error 返回 0 不代表数值一致，必须查看数量、完整状态矩阵和每项原因。"
            : "\n## Limits\n\nUnimplemented adapters, non-equivalent reference spheres, model/preflight failures, API/license failures and numeric differences remain explicit. Configuration choices are validation settings, never universal Zemax defaults. No scale fitting, alignment search or normalization to improve agreement is performed. A successful exit with --fail-on none/error does not imply numerical agreement; consult counts and matrix.");
        File.WriteAllText(Path.Combine(directory, "COMPARISON_REPORT.md"), md.ToString());
    }
    private static string Escape(string s) => s.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
}
