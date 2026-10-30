using System.Globalization;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal sealed record ReportExportProgress(int Completed, int Total, string Item);
internal sealed record ReportExportResult(string ReportPath, int Succeeded, int Failed);
internal sealed record ReportImageRequest(AnalysisResultDto? Analysis, SceneDto? Scene,
    OpticalDocumentSnapshot Document, DateTimeOffset CapturedAt, string Title,
    bool ForPrint = false, bool Landscape = false);
internal delegate Task<IReadOnlyList<string>> ReportImageWriter(
    ReportImageRequest request, string directory, string stem, CancellationToken cancellationToken);

internal static class MarkdownReportExporter
{
    internal static bool IsSupportedAnalysis(string canonicalKey) =>
        !WorkbenchAnalysisCatalog.IsRetiredAnalysis(canonicalKey) && canonicalKey != "Prescription Report";

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
        var extension = settings.Format switch
        {
            DesignReportFormat.Word => ".docx",
            DesignReportFormat.Pdf => ".pdf",
            DesignReportFormat.Markdown => ".md",
            DesignReportFormat.Html => ".html",
            _ => throw new InvalidOperationException("请选择有效的报告格式。")
        };
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = snapshot.Workspace;
        var source = snapshot.SourceDocument;
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var name = string.IsNullOrWhiteSpace(source.Path) ? source.Name : Path.GetFileNameWithoutExtension(source.Path);
        var stem = ReportStem(name, snapshot.CapturedAt);
        while (File.Exists(Path.Combine(root, stem + extension)) || Directory.Exists(Path.Combine(root, stem + "_assets")))
            stem = ReportStem(name, snapshot.CapturedAt) + "_" + Guid.NewGuid().ToString("N")[..8];
        var stage = Path.Combine(root, ".report-export-" + Guid.NewGuid().ToString("N"));
        var images = Path.Combine(stage, "assets");
        var finalImages = Path.Combine(root, stem + "_assets");
        var reportPath = Path.Combine(root, stem + extension);
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
            .Select(workspace.Analyses.CanonicalKey).Where(IsSupportedAnalysis).Distinct(StringComparer.Ordinal).ToArray();
        var layoutCount = (settings.Include2DLayout ? 1 : 0) + (settings.Include3DLayout ? 1 : 0);
        var total = configurations.Length * (analyses.Length + layoutCount + 1) + 1;
        var text = new DesignReportContent();
        var appendix = new DesignReportContent();
        var outcomes = new List<string[]>();
        var currentConfiguration = 0;
        var pdf = new DesignReportPdfModel(source.Name, source.Path is null ? "未保存工程" : Path.GetFileName(source.Path), snapshot.CapturedAt)
        {
            FieldLabel = string.Equals(workspace.Prescription.GetSystemSettings().FieldDefinition, "Angle", StringComparison.OrdinalIgnoreCase) ? "视场角 / °" : "视场坐标",
            Fields = string.Join("；", workspace.Prescription.GetFields().Select(field => $"({DesignReportPdfModel.Number(field.X)}, {DesignReportPdfModel.Number(field.Y)})")),
            Wavelengths = string.Join(" / ", workspace.Prescription.GetWavelengths().Select(wave => DesignReportPdfModel.Number(wave.Nanometers))) + " nm"
        };
        DesignReportPdfSection? pdfSection = null;
        try
        {
            foreach (var configuration in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workspace.Modes.CurrentMode == OpticalWorkbenchMode.Sequential)
                    workspace.MultiConfiguration.Activate(configuration);
                var document = workspace.Documents.GetSnapshot();
                currentConfiguration = configuration + 1;
                pdf.Configurations.Add((currentConfiguration, document));
                var prefix = $"c{configuration + 1:D3}";
                text.Heading($"配置 {configuration + 1}", 2, newPage: true);
                progress?.Report(new(completed, total, "系统参数概览"));
                AppendSystemSummary(text, document);
                completed++;
                foreach (var dimension in new[] { SceneDimension.TwoDimensional, SceneDimension.ThreeDimensional })
                {
                    if (dimension == SceneDimension.TwoDimensional ? !settings.Include2DLayout : !settings.Include3DLayout) continue;
                    var title = dimension == SceneDimension.TwoDimensional ? "二维布局" : "三维布局";
                    await Item(title, async () =>
                    {
                        var scene = await workspace.Visualization.BuildSceneAsync(dimension, cancellationToken).ConfigureAwait(false);
                        pdfSection!.Conditions = "长度单位：mm · 标准布局视角";
                        pdfSection.Metrics.AddRange(new[]
                        {
                            new[] { "有效焦距 / mm", DesignReportPdfModel.Number(document.EffectiveFocalLength) },
                            new[] { "F 数", DesignReportPdfModel.Number(document.FNumber) },
                            new[] { "系统总长 / mm", DesignReportPdfModel.Number(document.TotalTrack) }
                        });
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
                        appendix.Heading($"配置 {configuration + 1} {title} 计算记录", 3);
                        appendix.Paragraph("分析设置");
                        appendix.Table(["参数", "值"], workspace.Analyses.GetParameters(key, merged).Select(parameter => new[]
                        {
                            $"{parameter.DisplayName} ({parameter.Key})",
                            parameter.Key == "OutputFile" ? "报告附件目录" : merged.GetValueOrDefault(parameter.Key, parameter.DefaultValue)
                        }));
                        var result = await workspace.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, merged), cancellationToken).ConfigureAwait(false);
                        if (result.View.Outcome != AnalysisOutcome.Success)
                            throw new InvalidOperationException(result.View.OutcomeReason ?? result.View.Outcome.ToString());
                        appendix.Paragraph($"计算来源：{result.ExecutorId}；请求指纹：{result.RequestFingerprint}", ReportTextStyle.Muted);
                        if (result.View.ConfigurationScope is { } scope)
                            appendix.Paragraph($"分析配置：{string.Join(", ", scope.ConfigurationNumbers)}；显示方式：{scope.Display}。");
                        var hasPlot = AnalysisPanel.HasReportPlot(result);
                        pdfSection!.Landscape = !hasPlot && result.View.Table?.Columns.Count > 5;
                        pdfSection.Conditions = string.Join(" · ", descriptors.Where(parameter =>
                            new[] { "Field", "Wave", "Sample", "Sampling", "Surface", "Reference" }.Any(part => parameter.Key.Contains(part, StringComparison.OrdinalIgnoreCase)))
                            .Take(5).Select(parameter => DesignReportPdfModel.Condition(parameter, merged)));
                        pdfSection.Metrics.AddRange(DesignReportPdfModel.KeyMetrics(result.View));
                        var detectorWindow = result.View.Rows.Where(row => row.Metric is "探测器窗口来源" or "探测器范围").ToArray();
                        if (detectorWindow.Length > 0)
                            pdfSection.Conditions += " · " + string.Join(" · ", detectorWindow.Select(row => row.Metric + "：" + DesignReportPdfModel.ReadableValue(row.Value)));
                        pdfSection.Table = DesignReportPdfModel.NumericalTable(result.View);
                        if (settings.Format != DesignReportFormat.Pdf || hasPlot)
                            await Images(new(result, null, result.SourceDocument ?? document, snapshot.CapturedAt, title), $"{prefix}-analysis-{i + 1:D3}", hasPlot ? text : appendix);
                        AppendAnalysis(text, appendix, result.View);
                    });
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(completed, total, settings.Format switch
            {
                DesignReportFormat.Word => "写入 Word 报告",
                DesignReportFormat.Pdf => "写入 PDF 报告",
                DesignReportFormat.Html => "写入 HTML 报告",
                _ => "写入 Markdown 报告"
            }));
            text.Heading("导出结果", 2);
            text.Paragraph($"成功项目：{succeeded}；失败或不适用项目：{failed}。");
            if (outcomes.Count > 0) text.Table(["配置", "项目", "执行状态"], outcomes);
            if (appendix.Blocks.Count > 0)
            {
                text.Heading("附录 完整参数与计算记录", 2, newPage: true);
                text.BeginAppendix();
                text.Append(appendix);
            }
            var report = new DesignReportContent();
            report.Heading("光学设计报告", 1);
            report.Paragraph(source.Name, ReportTextStyle.Subtitle);
            report.Paragraph("当前工程的系统概览、布局与所选光学分析。各图表和数据均来自导出开始时的同一工程快照。", ReportTextStyle.Muted);
            report.Heading("报告概览", 2);
            report.Table(["报告信息", "内容"], [
                ["导出时间", snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)],
                ["源文件", source.Path ?? "未保存工程"], ["工程修订", $"{source.Revision}；包含未保存修改：{(source.IsDirty ? "是" : "否")}"],
                ["配置范围", string.Join("、", configurations.Select(value => (value + 1).ToString(CultureInfo.InvariantCulture)))],
                ["所选分析", analyses.Length == 0 ? "仅系统概览与所选布局" : string.Join("、", analyses.Select(WorkbenchAnalysisCatalog.DisplayName))],
                ["执行概况", $"{succeeded} 项完成；{failed} 项失败或不适用"],
                ["软件版本", $"Optical System Design {typeof(MarkdownReportExporter).Assembly.GetName().Version}"]]);
            report.Contents(text.Blocks.OfType<ReportHeading>());
            report.Append(text);
            var stagedReport = Path.Combine(stage, "report" + extension);
            if (settings.Format == DesignReportFormat.Markdown)
                await BoundedApplicationFile.WriteAllTextAtomicAsync(stagedReport, report.Markdown(stem + "_assets"),
                    BoundedApplicationFile.MaximumExportBytes, "Markdown optical design report", cancellationToken).ConfigureAwait(false);
            else
                BoundedApplicationFile.WriteAtomic(stagedReport, BoundedApplicationFile.MaximumExportBytes, "Optical design report", stream =>
                {
                    if (settings.Format == DesignReportFormat.Word) DesignReportWordWriter.Write(stream, report, images, cancellationToken);
                    else if (settings.Format == DesignReportFormat.Html) DesignReportHtmlWriter.Write(stream, report, images, cancellationToken);
                    else DesignReportPdfWriter.Write(stream, report, images, cancellationToken, pdf,
                        settings.PdfIncludeDetails ? appendix : null);
                });
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Format == DesignReportFormat.Markdown)
            {
                Directory.Move(images, finalImages);
                publishedImages = true;
            }
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
            text.Heading(title, 3);
            pdfSection = new(currentConfiguration, title);
            pdf.Sections.Add(pdfSection);
            try { await action(); succeeded++; outcomes.Add([currentConfiguration.ToString(CultureInfo.InvariantCulture), title, "完成"]); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                failed++;
                pdfSection.Failure = error.Message;
                text.Paragraph($"未完成：{error.Message}", ReportTextStyle.Warning);
                outcomes.Add([currentConfiguration.ToString(CultureInfo.InvariantCulture), title, "未完成：" + error.Message]);
            }
            completed++;
            progress?.Report(new(completed, total, title));
        }

        async Task Images(ReportImageRequest request, string imageStem, DesignReportContent? destination = null)
        {
            request = request with { ForPrint = settings.Format == DesignReportFormat.Pdf, Landscape = pdfSection?.Landscape == true };
            var files = await writeImages(request, images, imageStem, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0) throw new InvalidOperationException("未生成截图。");
            foreach (var file in files)
            {
                pdfSection?.Images.Add(Path.GetFileName(file));
                (destination ?? text).Picture(request.Title, Path.GetFileName(file));
            }
        }
    }

    private static void AppendSystemSummary(DesignReportContent text, OpticalDocumentSnapshot document)
    {
        text.Heading("系统参数", 3);
        text.Table(["参数", "值"], [
            ["EFL / mm", Number(document.EffectiveFocalLength)], ["F/#", Number(document.FNumber)],
            ["TTL / mm", Number(document.TotalTrack)], ["入瞳直径 / mm", Number(document.EntrancePupilDiameter)]]);
    }

    private static void AppendAnalysis(DesignReportContent text, DesignReportContent appendix, AnalysisViewDto view)
    {
        if (view.Table is { Columns.Count: > 0 } table)
        {
            if (table.Rows.Count > 80)
            {
                text.Paragraph($"数值表节选：前 12 行，共 {table.Rows.Count} 行。完整数值表见附录。", ReportTextStyle.Muted);
                text.Table(table.Columns.ToArray(), table.Rows.Take(12).Select(row => row.ToArray()));
                appendix.Table(table.Columns.ToArray(), table.Rows.Select(row => row.ToArray()));
            }
            else text.Table(table.Columns.ToArray(), table.Rows.Select(row => row.ToArray()));
        }
        else if (view.Rows.Count > 0)
        {
            text.Table(["指标", "数值"], view.Rows.Take(8).Select(row => new[] { row.Metric, row.Value }));
            if (view.Rows.Count > 8)
            {
                text.Paragraph($"正文显示前 8 项，共 {view.Rows.Count} 项。完整指标见附录。", ReportTextStyle.Muted);
                appendix.Table(["指标", "数值"], view.Rows.Select(row => new[] { row.Metric, row.Value }));
            }
        }
        if (!string.IsNullOrWhiteSpace(view.ReportText))
        {
            appendix.Code(view.ReportText, "text");
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
