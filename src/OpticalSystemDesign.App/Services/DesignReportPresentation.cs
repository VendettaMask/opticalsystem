using System.Globalization;
using System.Text.RegularExpressions;

namespace OpticalSystemDesign.App.Services;

internal sealed record DesignReportSheet(string Title, string Subtitle, int Number, int SectionNumber,
    int Configuration, bool Cover, DesignReportContent Content);

/// <summary>Shared paper structure for HTML, PDF, Word and Markdown. Only native text/tables paginate here; pictures remain intact.</summary>
internal sealed class DesignReportPresentation
{
    internal const double FigureWidthPoints = 523.28;
    internal const double FigureHeightPoints = FigureWidthPoints * 1804 / 2404;
    internal List<DesignReportSheet> Pages { get; } = [];
    internal List<int> SectionPages { get; } = [];
    internal DesignReportPdfModel Report { get; }

    private DesignReportPresentation(DesignReportPdfModel report) => Report = report;

    internal static DesignReportPresentation Create(DesignReportPdfModel report)
    {
        using var languageScope = UiLocalization.UseLanguage(report.Language);
        var layout = new DesignReportPresentation(report);
        var cover = new DesignReportContent();
        cover.Paragraph("OPTICAL DESIGN & ANALYSIS", ReportTextStyle.Muted);
        cover.Paragraph(Path.GetFileNameWithoutExtension(report.FileName), ReportTextStyle.Subtitle);
        var primaryLayout = report.Sections.FirstOrDefault(section => section.CoverImage && section.Images.Count > 0);
        if (primaryLayout is not null) cover.Picture(primaryLayout.Title, primaryLayout.Images[0]);
        if (report.Configurations.FirstOrDefault().Document is { } source)
            cover.Table([UiLocalization.Text("有效焦距 EFL / mm"), UiLocalization.Text("F 数"), UiLocalization.Text("系统总长 / mm"), UiLocalization.Text("入瞳直径 / mm")],
                [[DesignReportPdfModel.Number(source.EffectiveFocalLength), DesignReportPdfModel.Number(source.FNumber),
                    DesignReportPdfModel.Number(source.TotalTrack), DesignReportPdfModel.Number(source.EntrancePupilDiameter)]]);
        cover.Paragraph(UiLocalization.Template($"计算快照　{report.CapturedAt:yyyy-MM-dd HH:mm}　·　{report.Configurations.Count} 个配置　·　{report.Sections.Count} 个分析项目\n完成：{report.Sections.Count(section => section.Failure is null)} 项；未完成或不适用：{report.Sections.Count(section => section.Failure is not null)} 项。"), ReportTextStyle.Muted);
        cover.Paragraph(report.FieldLabel + "：" + report.Fields + UiLocalization.Text("\n波长：") + report.Wavelengths, ReportTextStyle.Muted);
        if (report.Sections.Count == 0) cover.Paragraph(UiLocalization.Text("系统参数"), ReportTextStyle.Muted);
        layout.Add(report.Name + UiLocalization.Text("\n光学设计分析报告"), "", cover, cover: true);
        var contents = new List<DesignReportContent>();
        for (var index = 0; index < report.Sections.Count; index += 23)
        {
            var body = new DesignReportContent(); contents.Add(body);
            layout.Add(index == 0 ? UiLocalization.Text("目录") : UiLocalization.Text("目录 · 续"), UiLocalization.Template($"{index / 23 + 1} / {(report.Sections.Count + 22) / 23}　·　{report.Sections.Count} 个项目"), body);
        }
        if (report.Sections.Count > 0)
        {
            foreach (var configuration in report.Configurations)
            {
                var document = configuration.Document;
                var body = new DesignReportContent();
                body.Table([UiLocalization.Text("系统参数"), UiLocalization.Text("值")], [
                    [UiLocalization.Text("源文件"), report.FileName], ["EFL / mm", DesignReportPdfModel.Number(document.EffectiveFocalLength)],
                    ["F/#", DesignReportPdfModel.Number(document.FNumber)], ["TTL / mm", DesignReportPdfModel.Number(document.TotalTrack)],
                    [UiLocalization.Text("入瞳直径 / mm"), DesignReportPdfModel.Number(document.EntrancePupilDiameter)],
                    [report.FieldLabel, report.Fields], [UiLocalization.Text("波长"), report.Wavelengths],
                    [UiLocalization.Text("计算时间"), report.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)]
                ]);
                body.Paragraph(UiLocalization.Text("所有图表与数值来自导出开始时的同一工程快照。各分析采用所选设置，参数及原始输出保存在计算记录中。"), ReportTextStyle.Muted);
                layout.Add(UiLocalization.Text("系统概要"), UiLocalization.Text("配置 ") + configuration.Number, body, configuration: configuration.Number);
            }
        }
        for (var index = 0; index < report.Sections.Count; index++)
        {
            var section = report.Sections[index];
            layout.SectionPages.Add(layout.Pages.Count + 1);
            if (section.Failure is { } failure)
            {
                var body = new DesignReportContent(); body.Paragraph(UiLocalization.Text("未完成：") + failure, ReportTextStyle.Warning);
                layout.Add(section.Title, section.Conditions, body, index + 1, section.Configuration);
                continue;
            }
            if (section.Images.Count > 0)
            {
                foreach (var file in section.Images)
                {
                    var body = new DesignReportContent(); body.Picture(section.Title, file);
                    if (section.Metrics.Count > 0) body.Table(section.Metrics.Take(3).Select(pair => pair[0]).ToArray(),
                        [section.Metrics.Take(3).Select(pair => pair[1]).ToArray()]);
                    layout.Add(section.Title, section.Conditions, body, index + 1, section.Configuration);
                }
            }
            else
            {
                var table = section.Table ?? new ReportTable([UiLocalization.Text("项目"), UiLocalization.Text("数值")], section.Metrics.ToArray());
                var chunks = TablePages(table).ToArray();
                for (var part = 0; part < chunks.Length; part++)
                {
                    var body = new DesignReportContent();
                    body.Paragraph(UiLocalization.Template($"数值结果　{part + 1} / {chunks.Length}　·　共 {table.Rows.Length} 行"), ReportTextStyle.Muted);
                    body.Blocks.Add(chunks[part]);
                    if (section.FormulaNote.Length > 0) body.Paragraph(section.FormulaNote, ReportTextStyle.Muted);
                    layout.Add(section.Title + (part == 0 ? "" : UiLocalization.Text(" · 续")), section.Conditions, body, index + 1, section.Configuration);
                }
            }
        }
        for (var index = 0; index < report.Sections.Count; index++)
        {
            var section = report.Sections[index];
            var title = (report.Configurations.Count > 1 ? UiLocalization.Template($"配置 {section.Configuration} · ") : "") + section.Title;
            contents[index / 23].Blocks.Add(new ReportDirectoryEntry(title, index + 1, layout.SectionPages[index]));
        }
        return layout;
    }

    internal DesignReportContent Content()
    {
        var result = new DesignReportContent();
        foreach (var page in Pages)
        {
            result.Blocks.Add(new ReportPageStart(page.Title, page.Subtitle, page.Number, page.SectionNumber, page.Configuration, page.Cover));
            result.Append(page.Content);
        }
        return result;
    }

    private void Add(string title, string subtitle, DesignReportContent body, int section = 0, int configuration = 1, bool cover = false)
    {
        if (Pages.Count >= 1000) throw new InvalidOperationException(UiLocalization.Text("报告超过页数上限，请减少分析项目或配置数量。"));
        Pages.Add(new(title, subtitle, Pages.Count + 1, section, configuration, cover, body));
    }

    internal static IEnumerable<ReportTable> TablePages(ReportTable table)
    {
        var widths = DesignReportImage.ColumnWeights(table).Select(value => value * 70).ToArray();
        var chunk = new List<string[]>(); var height = 0;
        foreach (var row in table.Rows)
        {
            var cost = Math.Max(1, row.Select((value, column) => (int)Math.Ceiling(value.EnumerateRunes().Sum(rune => rune.Value < 128 ? .55 : 1) / Math.Max(3, widths[column] - 2))).Max());
            if (chunk.Count > 0 && height + cost > 17)
            {
                yield return table with { Rows = chunk.ToArray() }; chunk.Clear(); height = 0;
            }
            chunk.Add(row); height += cost;
        }
        if (chunk.Count > 0 || table.Rows.Length == 0) yield return table with { Rows = chunk.ToArray() };
    }

    internal static string ReadableFormula(string value) => Regex.Replace(value, @"\^(\d+)", match =>
        string.Concat(match.Groups[1].Value.Select(character => "⁰¹²³⁴⁵⁶⁷⁸⁹"[character - '0'])));

    internal static IEnumerable<(string Text, bool Superscript)> MathRuns(string expression)
    {
        var position = 0;
        foreach (Match power in Regex.Matches(expression, @"\^(\d+)"))
        {
            if (power.Index > position) yield return (expression[position..power.Index], false);
            yield return (power.Groups[1].Value, true);
            position = power.Index + power.Length;
        }
        if (position < expression.Length) yield return (expression[position..], false);
    }
}
