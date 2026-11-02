using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

internal static partial class DesignReportHtmlWriter
{
    private static void WritePaper(Stream output, DesignReportPdfModel report, string images, DesignReportContent? details, CancellationToken token)
    {
        var layout = DesignReportPresentation.Create(report);
        var template = Resource("Assets.Reports.EngineeringReport.html");
        var logo = "<img class=\"brand-mark\" alt=\"S.T.A.R.Labs\" src=\"data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Resource("Assets.Brand.CompanyLogo.svg"))) + "\">";
        var navigation = new StringBuilder(); var sheets = new StringBuilder();
        var embedded = new Dictionary<string, string>(StringComparer.Ordinal); long imageBytes = 0;
        var figure = 0;
        foreach (var page in layout.Pages)
        {
            token.ThrowIfCancellationRequested();
            navigation.Append($"<a href=\"#page-{page.Number:D2}\" data-page=\"{page.Number}\"><span class=\"outline-number\">{page.Number:D2}</span>{Encode(page.Title.Replace('\n', ' '))}</a>");
            sheets.Append($"<section class=\"document-page {(page.Cover ? "cover" : page.Content.Blocks.Any(block => block is ReportPicture) ? "" : "table-page")}\" id=\"page-{page.Number:D2}\" data-page=\"{page.Number}\" tabindex=\"-1\" aria-label=\"第{page.Number}页\"><header class=\"page-header\">{logo}<div class=\"header-meta\"><strong>{Encode(report.Name)} / 光学设计分析</strong>CONFIGURATION {page.Configuration}</div></header><div class=\"page-body\">");
            if (page.SectionNumber > 0 && layout.SectionPages[page.SectionNumber - 1] == page.Number)
                sheets.Append($"<a id=\"section-{page.SectionNumber:D3}\"></a>");
            if (page.Cover)
                sheets.Append($"<div class=\"cover-title-row\"><div><div class=\"page-kicker\">OPTICAL DESIGN &amp; ANALYSIS</div><h1 class=\"cover-title\">{Encode(page.Title).Replace("\n", "<br>", StringComparison.Ordinal)}</h1></div><div class=\"cover-index\">TECHNICAL<br>REPORT<br>{report.CapturedAt:yyyy / MM}</div></div>");
            else
                sheets.Append($"<div class=\"analysis-intro\"><h1 class=\"page-title\">{(page.SectionNumber > 0 ? $"<span>{page.SectionNumber:D2}</span>" : "")}{Encode(page.Title)}</h1><p class=\"page-description\">{(page.SectionNumber > 0 ? "配置 " + page.Configuration : Encode(page.Subtitle))}</p>{(page.SectionNumber > 0 ? "<div class=\"condition-row\"><div>" + Encode(ShortConditions(page.Subtitle)) + "</div></div>" : "")}</div>");
            var directoryPage = page.Content.Blocks.Any(block => block is ReportDirectoryEntry);
            if (directoryPage) sheets.Append("<div class=\"full-toc\">");
            foreach (var block in page.Content.Blocks)
            {
                if (page.Cover && block is ReportParagraph { Text: "OPTICAL DESIGN & ANALYSIS" }) continue;
                WriteBlock(sheets, block, page.Cover);
            }
            if (directoryPage) sheets.Append("</div>");
            sheets.Append($"</div><footer class=\"page-footer\"><div><strong>Optical System Design</strong>　·　计算快照 {report.CapturedAt:yyyy.MM.dd}</div><span class=\"page-number\">{page.Number:D2} / {layout.Pages.Count:D2}</span></footer></section>");
        }
        var raw = new StringBuilder();
        if (details is not null) foreach (var block in details.Blocks) WriteBlock(raw, block, rawRecord: true);
        // HTML parsing normalizes newlines before CSP hashes are checked, including on Windows.
        var script = Regex.Match(template, @"<script>([\s\S]*?)</script>").Groups[1].Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var values = new Dictionary<string, string>
        {
            ["DOCUMENT_TITLE"] = Encode(report.Name + " 光学设计分析报告"), ["REPORT_TITLE"] = Encode(report.Name + " 光学设计分析报告"),
            ["REPORT_NAME"] = Encode(report.Name), ["SNAPSHOT"] = Encode($"计算快照 {report.CapturedAt:yyyy.MM.dd HH:mm}"),
            ["CONFIGURATION_NOTE"] = report.Configurations.Count + " 个配置 · 光学设计分析", ["PAGE_COUNT"] = layout.Pages.Count.ToString(CultureInfo.InvariantCulture),
            ["OUTLINE_NOTE"] = $"{layout.Pages.Count} 页 · {report.Sections.Count} 个分析项目<br>{report.CapturedAt:yyyy.MM.dd}",
            ["TOOLBAR_LOGO"] = logo, ["NAVIGATION"] = navigation.ToString(), ["PAGES"] = sheets.ToString(), ["RAW_RECORDS"] = raw.ToString(),
            ["SCRIPT_HASH"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))
        };
        var html = Regex.Replace(template, @"@@([A-Z_]+)@@", match => values[match.Groups[1].Value]);
        if (Encoding.UTF8.GetByteCount(html) > BoundedApplicationFile.MaximumExportBytes) throw new InvalidDataException("报告超过大小上限。");
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 8192, leaveOpen: true);
        writer.Write(html); writer.Flush(); token.ThrowIfCancellationRequested();

        void WriteBlock(StringBuilder html, DesignReportBlock block, bool cover = false, bool rawRecord = false)
        {
            token.ThrowIfCancellationRequested();
            switch (block)
            {
                case ReportHeading heading: html.Append($"<h3 class=\"record-heading\">{Encode(heading.Text)}</h3>"); break;
                case ReportParagraph paragraph:
                    html.Append($"<p class=\"{(cover && paragraph.Style == ReportTextStyle.Subtitle ? "cover-subtitle" : "scope-row")}{(paragraph.Style == ReportTextStyle.Warning ? " unavailable-box" : "")}\">{Encode(paragraph.Text).Replace("\n", "<br>", StringComparison.Ordinal)}</p>"); break;
                case ReportCode code: html.Append("<pre>" + Encode(code.Text) + "</pre>"); break;
                case ReportDirectoryEntry entry:
                    html.Append($"<a class=\"toc-entry\" href=\"#page-{entry.Page:D2}\"><span class=\"toc-no\">{entry.SectionNumber:D2}</span><span>{Encode(entry.Title)}</span><span class=\"leader\"></span><span class=\"toc-page\">{entry.Page:D2}</span></a>"); break;
                case ReportTable table:
                    html.Append("<div class=\"table-container\"><table class=\"report-table full-table\"><colgroup>");
                    foreach (var weight in DesignReportImage.ColumnWeights(table)) html.Append("<col style=\"width:" + (weight * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%\">");
                    html.Append("</colgroup><thead><tr>");
                    foreach (var column in table.Columns) html.Append("<th scope=\"col\">" + Encode(column) + "</th>");
                    html.Append("</tr></thead><tbody>");
                    foreach (var row in table.Rows)
                    {
                        token.ThrowIfCancellationRequested(); html.Append("<tr>");
                        for (var column = 0; column < row.Length; column++)
                            html.Append(column == table.FormulaColumn ? "<td class=\"formula-cell\">" + Regex.Replace(Encode(row[column]), @"\^(\d+)", "<sup>$1</sup>") + "</td>" : "<td" + (double.TryParse(row[column], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? " class=\"numeric-cell\"" : "") + ">" + Encode(row[column]).Replace("\n", "<br>", StringComparison.Ordinal) + "</td>");
                        html.Append("</tr>");
                    }
                    html.Append("</tbody></table></div>"); break;
                case ReportPicture picture:
                    if (!embedded.TryGetValue(picture.FileName, out var base64))
                    {
                        using var source = DesignReportImage.Open(images, picture.FileName); imageBytes += source.Length;
                        if (imageBytes > BoundedApplicationFile.MaximumExportBytes) throw new InvalidDataException("报告图片超过大小上限。");
                        using var data = SKData.Create(source); using var decoded = DesignReportImage.Decode(data);
                        base64 = Convert.ToBase64String(data.AsSpan()); embedded.Add(picture.FileName, base64);
                    }
                    var id = "figure-" + ++figure;
                    html.Append($"<figure class=\"source-figure{(cover ? " cover-figure" : "")}\"><button type=\"button\" class=\"figure-open\" data-figure=\"{id}\" aria-label=\"查看{Encode(picture.Title)}原图\"><img id=\"{id}\" data-title=\"{Encode(picture.Title)}\" src=\"data:image/png;base64,{base64}\" alt=\"{Encode(picture.Title)}\"></button><figcaption><span>{(cover ? "" : "图 " + (figure - (primaryCover() ? 1 : 0)) + "　")}{Encode(picture.Title)}</span><button type=\"button\" class=\"plain-button\" data-figure=\"{id}\">查看原图 ↗</button></figcaption></figure>"); break;
            }
        }
        bool primaryCover() => layout.Pages[0].Content.Blocks.Any(block => block is ReportPicture);
    }

    internal static string ShortConditions(string conditions) => conditions.Length <= 180 ? conditions : conditions[..180] + "…";
    internal static string Resource(string name)
    {
        using var stream = typeof(DesignReportHtmlWriter).Assembly.GetManifestResourceStream("OpticalSystemDesign.App." + name)
            ?? throw new InvalidOperationException("报告资源不可用。");
        using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd();
    }
}
