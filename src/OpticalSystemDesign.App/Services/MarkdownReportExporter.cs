using System.Globalization;
using System.Text;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal sealed record ReportExportProgress(int Completed, int Total, string Item);
internal sealed record ReportExportResult(string MarkdownPath, int Succeeded, int Failed);
internal sealed record ReportImageRequest(AnalysisResultDto? Analysis, SceneDto? Scene,
    OpticalDocumentSnapshot Document, DateTimeOffset CapturedAt, string Title);
internal delegate Task<IReadOnlyList<string>> ReportImageWriter(
    ReportImageRequest request, string directory, string stem, CancellationToken cancellationToken);

internal static class MarkdownReportExporter
{
    internal static string SuggestedDirectory(string? sourcePath) => string.IsNullOrWhiteSpace(sourcePath)
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;

    internal static string ReportStem(string name, DateTimeOffset time)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var safe = new string(name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray()).Trim(' ', '.');
        if (string.IsNullOrEmpty(safe)) safe = "OpticalSystemDesign";
        if (safe.Length > 80) safe = safe[..80];
        return $"{safe}_report_{time:yyyyMMdd_HHmmss_fff}";
    }

    public static async Task<ReportExportResult> ExportAsync(DesignReportSnapshot snapshot,
        MarkdownReportSettings settings, string outputDirectory, ReportImageWriter writeImages,
        IProgress<ReportExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        settings = settings.Copy();
        var workspace = snapshot.Workspace;
        var source = snapshot.SourceDocument;
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var name = string.IsNullOrWhiteSpace(source.Path) ? source.Name : Path.GetFileNameWithoutExtension(source.Path);
        var stem = ReportStem(name, snapshot.CapturedAt);
        while (File.Exists(Path.Combine(root, stem + ".md")) || Directory.Exists(Path.Combine(root, stem + "_assets")))
            stem = ReportStem(name, snapshot.CapturedAt) + "_" + Guid.NewGuid().ToString("N")[..8];
        var stage = Path.Combine(root, ".report-export-" + Guid.NewGuid().ToString("N"));
        var images = Path.Combine(stage, "assets");
        var finalImages = Path.Combine(root, stem + "_assets");
        var reportPath = Path.Combine(root, stem + ".md");
        Directory.CreateDirectory(images);
        var publishedImages = false;
        var publishedReport = false;
        var succeeded = 0;
        var failed = 0;
        var completed = 0;
        var navigation = workspace.MultiConfiguration.GetNavigationState();
        var configurations = settings.AllConfigurations && workspace.Modes.CurrentMode == OpticalWorkbenchMode.Sequential
            ? Enumerable.Range(0, navigation.Count).ToArray() : [navigation.ActiveIndex];
        var analyses = settings.AnalysisKeys
            .Where(key => !WorkbenchAnalysisCatalog.IsRetiredAnalysis(key))
            .Select(workspace.Analyses.CanonicalKey).Distinct(StringComparer.Ordinal).ToArray();
        var layoutCount = (settings.Include2DLayout ? 1 : 0) + (settings.Include3DLayout ? 1 : 0);
        var total = configurations.Length * (analyses.Length + layoutCount + 1) + 1;
        var text = new StringBuilder();
        text.AppendLine($"# {Escape(source.Name)} — 光学设计报告\n")
            .AppendLine($"- 导出时间：{snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss.fff zzz}")
            .AppendLine($"- 软件：Optical System Design {typeof(MarkdownReportExporter).Assembly.GetName().Version}")
            .AppendLine($"- 源文件：{Escape(source.Path ?? "未保存工程")}")
            .AppendLine($"- 工程修订：{source.Revision}；包含未保存修改：{(source.IsDirty ? "是" : "否")}")
            .AppendLine("- 数据范围：导出开始时的工程快照；所有计算和截图均来自该快照。\n");
        try
        {
            foreach (var configuration in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workspace.Modes.CurrentMode == OpticalWorkbenchMode.Sequential)
                    workspace.MultiConfiguration.Activate(configuration);
                var document = workspace.Documents.GetSnapshot();
                var prefix = $"c{configuration + 1:D3}";
                text.AppendLine($"## 配置 {configuration + 1}\n");
                progress?.Report(new(completed, total, "镜头结构与系统参数"));
                AppendStructure(text, workspace, document);
                CheckLength();
                completed++;
                foreach (var dimension in new[] { SceneDimension.TwoDimensional, SceneDimension.ThreeDimensional })
                {
                    if (dimension == SceneDimension.TwoDimensional ? !settings.Include2DLayout : !settings.Include3DLayout) continue;
                    var title = dimension == SceneDimension.TwoDimensional ? "二维布局" : "三维布局";
                    await Item(title, async () =>
                    {
                        var scene = await workspace.Visualization.BuildSceneAsync(dimension, cancellationToken).ConfigureAwait(false);
                        await Images(new(null, scene, document, snapshot.CapturedAt, title), $"{prefix}-{dimension}");
                    });
                }
                for (var i = 0; i < analyses.Length; i++)
                {
                    var key = analyses[i];
                    var title = WorkbenchAnalysisCatalog.DisplayName(key);
                    await Item(title, async () =>
                    {
                        settings.AnalysisParameters.TryGetValue(key, out var configured);
                        var merged = workspace.Analyses.MergeSettings(key, configured);
                        var descriptors = workspace.Analyses.GetParameters(key, merged);
                        if (settings.AllConfigurations && descriptors.Any(parameter => parameter.Key == AnalysisConfigurationSelection.Key))
                            merged[AnalysisConfigurationSelection.Key] = AnalysisConfigurationSelection.Current;
                        // Analysis-generated bitmap outputs belong to this report,
                        // never to a previously saved UI output path.
                        if (descriptors.Any(parameter => parameter.Key == "OutputFile" && parameter.Kind == AnalysisParameterKind.File))
                            merged["OutputFile"] = Path.Combine(images, $"{prefix}-output-{i + 1:D3}.png");
                        text.AppendLine("分析设置：\n").AppendLine("| 参数 | 值 |\n| --- | --- |");
                        foreach (var parameter in workspace.Analyses.GetParameters(key, merged))
                            text.AppendLine($"| {Escape(parameter.DisplayName)} ({Escape(parameter.Key)}) | {Escape(parameter.Key == "OutputFile" ? "报告附件目录" : merged.GetValueOrDefault(parameter.Key, parameter.DefaultValue))} |");
                        text.AppendLine();
                        var result = await workspace.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, merged), cancellationToken).ConfigureAwait(false);
                        if (result.View.Outcome != AnalysisOutcome.Success)
                            throw new InvalidOperationException(result.View.OutcomeReason ?? result.View.Outcome.ToString());
                        text.AppendLine($"计算来源：{Escape(result.ExecutorId)}；请求指纹：{Escape(result.RequestFingerprint)}\n");
                        if (result.View.ConfigurationScope is { } scope)
                            text.AppendLine($"分析配置：{string.Join(", ", scope.ConfigurationNumbers)}；显示方式：{scope.Display}。\n");
                        AppendAnalysis(text, result.View);
                        await Images(new(result, null, result.SourceDocument ?? document, snapshot.CapturedAt, title), $"{prefix}-analysis-{i + 1:D3}");
                    });
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(completed, total, "写入 Markdown 报告"));
            text.AppendLine($"## 导出结果\n\n成功项目：{succeeded}；失败或不适用项目：{failed}。\n");
            var stagedReport = Path.Combine(stage, "report.md");
            await BoundedApplicationFile.WriteAllTextAtomicAsync(stagedReport, text.ToString(),
                BoundedApplicationFile.MaximumExportBytes, "Markdown optical design report", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(images, finalImages);
            publishedImages = true;
            File.Move(stagedReport, reportPath, overwrite: false);
            publishedReport = true;
            progress?.Report(new(total, total, "导出完成"));
            return new(reportPath, succeeded, failed);
        }
        finally
        {
            // Only directories created by this invocation can be retired.
            RetireOwned(stage, root);
            if (publishedImages && !publishedReport) RetireOwned(finalImages, root);
        }

        async Task Item(string title, Func<Task> action)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(completed, total, title));
            text.AppendLine($"### {Escape(title)}\n");
            try { await action(); succeeded++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                failed++;
                text.AppendLine($"> 未完成：{Escape(error.Message)}\n");
            }
            completed++;
            CheckLength();
            progress?.Report(new(completed, total, title));
        }

        async Task Images(ReportImageRequest request, string imageStem)
        {
            var files = await writeImages(request, images, imageStem, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0) throw new InvalidOperationException("未生成截图。");
            foreach (var file in files)
                text.AppendLine($"![{Escape(request.Title)}](<{Uri.EscapeDataString(stem + "_assets")}/{Uri.EscapeDataString(Path.GetFileName(file))}>)\n");
        }

        void CheckLength()
        {
            if (text.Length > BoundedApplicationFile.MaximumExportBytes / 4)
                throw new InvalidOperationException("报告超过大小上限，请减少分析项目或配置数量。");
        }
    }

    private static void AppendStructure(StringBuilder text, IWorkbenchApplication workspace, OpticalDocumentSnapshot document)
    {
        text.AppendLine("### 系统参数\n\n| 参数 | 值 |\n| --- | --- |")
            .AppendLine($"| EFL / mm | {Number(document.EffectiveFocalLength)} |")
            .AppendLine($"| F/# | {Number(document.FNumber)} |")
            .AppendLine($"| TTL / mm | {Number(document.TotalTrack)} |")
            .AppendLine($"| 入瞳直径 / mm | {Number(document.EntrancePupilDiameter)} |\n");
        text.AppendLine("### 镜头结构\n\n| 面号 | 名称 | 面型 | 半径 / mm | 厚度 / mm | 材料 | 半口径 / mm | 圆锥系数 | 光阑 |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var surface in workspace.Prescription.GetSurfaces())
            text.AppendLine($"| {surface.Number} | {Escape(surface.Label)} | {Escape(surface.GeometryKind)} | {Number(surface.Radius)} | {Number(surface.Thickness)} | {Escape(surface.Material)} | {Number(surface.SemiDiameter)} | {Number(surface.Conic)} | {(surface.IsStop ? "STOP" : "")} |");
        text.AppendLine("\n完整处方与系统设置：\n");
        // Detailed DTOs retain geometry parameters, solve definitions, coatings
        // and system settings beyond the compact surface table.
        text.AppendLine("```json");
        text.AppendLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            System = workspace.Prescription.GetSystemSettings(),
            Environment = workspace.Prescription.GetEnvironmentSettings(),
            Fields = workspace.Prescription.GetFields(),
            Wavelengths = workspace.Prescription.GetWavelengths(),
            Surfaces = workspace.Prescription.GetSurfaces(),
            NonSequential = workspace.Modes.CurrentMode == OpticalWorkbenchMode.NonSequential ? workspace.NonSequential.GetDocument() : null
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        text.AppendLine("```\n");
    }

    private static void AppendAnalysis(StringBuilder text, AnalysisViewDto view)
    {
        if (!string.IsNullOrWhiteSpace(view.ReportText))
        {
            // A fence longer than any run in source text cannot be closed by source content.
            var fence = new string('`', Math.Max(3, view.ReportText.Split('\n').Select(line => line.TrimStart(' ', '\t').TakeWhile(ch => ch == '`').Count() + 1).Max()));
            text.AppendLine(fence + "text").AppendLine(view.ReportText).AppendLine(fence).AppendLine();
        }
        else if (view.Table is { } table)
        {
            text.AppendLine("| " + string.Join(" | ", table.Columns.Select(Escape)) + " |")
                .AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
            foreach (var row in table.Rows) text.AppendLine("| " + string.Join(" | ", row.Select(Escape)) + " |");
            text.AppendLine();
        }
    }

    internal static string Escape(string? value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("|", "&#124;").Replace("[", "&#91;").Replace("]", "&#93;").Replace("\r", "").Replace("\n", "<br>");
    private static string Number(double value) => double.IsNaN(value) ? "不可用" : value.ToString("G12", CultureInfo.InvariantCulture);

    private static void RetireOwned(string path, string root)
    {
        var full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) != root || !Directory.Exists(full)) return;
        try
        {
            // This directory contains only flat generated files and our assets directory.
            foreach (var child in Directory.GetDirectories(full))
            {
                if (Path.GetFileName(child) != "assets") continue;
                foreach (var file in Directory.GetFiles(child)) File.Delete(file);
                Directory.Delete(child);
            }
            foreach (var file in Directory.GetFiles(full)) File.Delete(file);
            Directory.Delete(full);
        }
        catch (IOException) { /* An interrupted cleanup never invalidates the published report. */ }
        catch (UnauthorizedAccessException) { }
    }
}
