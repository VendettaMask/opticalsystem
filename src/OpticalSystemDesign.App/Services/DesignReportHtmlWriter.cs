using System.Net;
using System.Text;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

/// <summary>Self-contained offline report; all user text is encoded and all images are embedded.</summary>
internal static class DesignReportHtmlWriter
{
    internal static void Write(Stream output, DesignReportContent content, string images, CancellationToken token)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(false), 8192, leaveOpen: true);
        var title = content.Blocks.OfType<ReportHeading>().FirstOrDefault()?.Text ?? "光学设计报告";
        var subtitle = content.Blocks.OfType<ReportParagraph>().FirstOrDefault(paragraph => paragraph.Style == ReportTextStyle.Subtitle)?.Text;
        if (!string.IsNullOrWhiteSpace(subtitle)) title = subtitle + " - " + title;
        var headings = content.Blocks.OfType<ReportHeading>().Where(heading => heading.Level == 2).ToArray();
        writer.Write("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        writer.Write("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">");
        writer.Write("<title>" + Encode(title) + "</title><style>" + Style + "</style></head><body><div class=\"report-shell\">");
        writer.Write("<aside><div class=\"brand\">");
        using (var logo = typeof(DesignReportHtmlWriter).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Brand.CompanyLogo.svg"))
        {
            if (logo is not null)
            {
                using var bytes = new MemoryStream();
                logo.CopyTo(bytes);
                writer.Write("<img alt=\"S.T.A.R.Labs\" src=\"data:image/svg+xml;base64," + Convert.ToBase64String(bytes.ToArray()) + "\">");
            }
        }
        writer.Write("<span>OPTICAL SYSTEM DESIGN</span></div><nav aria-label=\"报告目录\"><p class=\"nav-label\">报告目录</p>");
        for (var i = 0; i < headings.Length; i++)
            writer.Write($"<a href=\"#section-{i + 1:D3}\">{Encode(headings[i].Text)}</a>");
        writer.Write("</nav><p class=\"edition\">光学设计报告<br>离线文档 · 内嵌图片</p></aside><main><header class=\"document-header\">OPTICAL SYSTEM DESIGN <span>DESIGN REPORT</span></header>");
        var section = 0; var figure = 0; var appendix = false;
        long imageBytes = 0;
        foreach (var block in content.Blocks)
        {
            token.ThrowIfCancellationRequested();
            switch (block)
            {
                case ReportHeading heading:
                    var id = heading.Level == 2 ? $" id=\"section-{++section:D3}\"" : "";
                    var css = heading.NewPage ? " class=\"section-start\"" : "";
                    writer.Write($"<h{heading.Level}{id}{css}>{Encode(heading.Text)}</h{heading.Level}>");
                    break;
                case ReportParagraph paragraph:
                    writer.Write($"<p class=\"text-{paragraph.Style.ToString().ToLowerInvariant()}\">{Encode(paragraph.Text).Replace("\n", "<br>", StringComparison.Ordinal)}</p>");
                    break;
                case ReportContents contents:
                    writer.Write("<section class=\"print-contents\"><h2>目录</h2><ol>");
                    foreach (var entry in contents.Entries)
                        writer.Write($"<li><a href=\"#section-{Array.IndexOf(headings, entry) + 1:D3}\">{Encode(entry.Text)}</a></li>");
                    writer.Write("</ol></section>");
                    break;
                case ReportAppendixStart:
                    writer.Write("<details class=\"appendix\" open><summary>完整参数与计算记录</summary><div>");
                    appendix = true;
                    break;
                case ReportCode code:
                    writer.Write($"<div class=\"code-label\">{Encode(code.Language.ToUpperInvariant())}</div><pre><code>{Encode(code.Text)}</code></pre>");
                    break;
                case ReportTable table:
                    writer.Write("<div class=\"table-scroll\"><table" + (table.Columns.Length == 2 ? " class=\"table-pair\"" : "") + "><thead><tr>");
                    foreach (var column in table.Columns) writer.Write("<th scope=\"col\">" + Encode(column) + "</th>");
                    writer.Write("</tr></thead><tbody>");
                    foreach (var row in table.Rows)
                    {
                        token.ThrowIfCancellationRequested();
                        writer.Write("<tr>");
                        foreach (var cell in row) writer.Write("<td>" + Encode(cell).Replace("\n", "<br>", StringComparison.Ordinal) + "</td>");
                        writer.Write("</tr>");
                    }
                    writer.Write("</tbody></table></div>");
                    break;
                case ReportPicture picture:
                    using (var source = DesignReportImage.Open(images, picture.FileName))
                    {
                        imageBytes += source.Length;
                        if (imageBytes > BoundedApplicationFile.MaximumExportBytes) throw new InvalidDataException("报告图片超过大小上限。");
                        using var data = SKData.Create(source);
                        using var image = DesignReportImage.Decode(data);
                        writer.Write("<figure><img loading=\"eager\" alt=\"" + Encode(picture.Title) + "\" src=\"data:image/png;base64,");
                        writer.Write(Convert.ToBase64String(data.AsSpan()));
                        writer.Write($"\"><figcaption>图 {++figure}　{Encode(picture.Title)}</figcaption></figure>");
                    }
                    break;
            }
        }
        if (appendix) writer.Write("</div></details>");
        writer.Write("<footer>Optical System Design · 光学设计报告</footer></main></div></body></html>");
        token.ThrowIfCancellationRequested();
        writer.Flush();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private const string Style = """
        :root{color-scheme:light;--ink:#172333;--muted:#637186;--accent:#235c96;--navy:#183b60;--line:#d9e1ea;--paper:#fff;--wash:#f4f7fb}
        *{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:28px}body{margin:0;background:#edf2f7;color:var(--ink);font:15px/1.75 "Segoe UI","Microsoft YaHei","PingFang SC",sans-serif}
        .report-shell{display:grid;grid-template-columns:220px minmax(0,960px);max-width:1260px;gap:28px;margin:36px auto;padding:0 24px}
        aside{position:sticky;top:32px;align-self:start;max-height:calc(100vh - 64px);overflow:auto;padding:8px 0}.brand img{display:block;width:155px;height:auto;margin-bottom:12px}.brand span,.nav-label,.edition{font-size:10px;letter-spacing:1.1px;color:var(--muted)}
        nav{margin-top:36px;border-top:1px solid var(--line);padding-top:18px}nav a{display:block;color:var(--ink);text-decoration:none;border-left:2px solid transparent;padding:7px 10px;font-size:13px;line-height:1.5}nav a:hover,nav a:focus-visible{border-color:var(--accent);background:#e3ebf5;color:var(--accent)}.edition{line-height:1.8;letter-spacing:.3px;margin-top:32px}
        .print-contents{display:none}main{min-width:0;background:var(--paper);padding:44px 48px 32px;box-shadow:0 8px 36px #1e385a0b;border:1px solid #e2e8f0}.document-header{display:flex;justify-content:space-between;gap:20px;color:var(--muted);font-size:10px;letter-spacing:1.6px;padding-bottom:20px;border-bottom:1px solid var(--line)}
        h1,h2,h3{color:#000;line-height:1.35;overflow-wrap:anywhere}h1{font-size:34px;font-weight:700;margin:32px 0 12px;letter-spacing:.3px}h2{font-size:23px;margin:44px 0 18px}h3{font-size:18px;margin:30px 0 16px}.text-subtitle{font-size:20px;color:#000;margin:0 0 20px}.text-muted{font-size:12px;color:var(--muted)}.text-warning{color:#9b352f;font-weight:600}p{margin:12px 0 20px;overflow-wrap:anywhere}
        .table-scroll{overflow-x:auto;margin:20px 0 28px}table{width:100%;border-collapse:collapse;font-size:12px;font-variant-numeric:tabular-nums;color:#2d3431}th,td{border:1px solid #d9e3de;padding:10px 12px;vertical-align:middle;text-align:left;overflow-wrap:anywhere}th{background:#eaf1ef;color:#2f4b42;font-weight:600}tbody tr:nth-child(even){background:#f7faf8}thead{display:table-header-group}.table-pair{table-layout:fixed}.table-pair th:first-child,.table-pair td:first-child{width:28%}
        figure{margin:26px 0 32px;break-inside:avoid}figure img{display:block;max-width:100%;height:auto;margin:auto}figcaption{font-size:12px;color:var(--muted);text-align:center;margin-top:12px;line-height:1.5}
        pre{white-space:pre-wrap;overflow-wrap:anywhere;tab-size:4;margin:0 0 24px;padding:18px;background:var(--wash);font:11px/1.65 "Cascadia Mono",Consolas,monospace;border:1px solid var(--line)}.code-label{font-size:10px;letter-spacing:1px;color:var(--muted);margin:18px 0 7px}.appendix{margin-top:22px}summary{cursor:pointer;font-size:13px;color:var(--accent);padding:12px 0}.appendix h3{font-size:16px}.appendix>div{padding-top:10px}footer{margin-top:40px;border-top:1px solid var(--line);padding-top:18px;color:var(--muted);font-size:10px;letter-spacing:.5px}
        @media(max-width:1000px){.report-shell{display:block;margin:16px auto;padding:0 16px;max-width:900px}aside{position:static;max-height:none;padding:12px 4px 20px}.brand img{width:140px}.brand span,.edition{display:none}nav{margin-top:18px;padding-top:12px}nav a{display:inline-block;padding:6px 10px}main{padding:32px}.document-header span{display:none}h1{font-size:29px}}
        @media(max-width:560px){body{font-size:14px}.report-shell{padding:0 8px}main{padding:24px 18px}h1{font-size:26px}h2{font-size:21px}.text-subtitle{font-size:17px}table{min-width:460px}.table-pair{min-width:0}th,td{padding:8px}nav a{font-size:12px}}
        @page{size:A4;margin:18mm}@media print{.print-contents{display:block}.print-contents a{color:#000;text-decoration:none}body{background:white;font-size:10pt}.report-shell{display:block;margin:0;padding:0;max-width:none}aside{display:none}main{box-shadow:none;border:0;padding:0}.document-header{font-size:8pt}h1{font-size:25pt}h2{font-size:17pt;break-after:avoid}h3{font-size:13pt;break-after:avoid}.section-start{break-before:page}.table-scroll{overflow:visible}table{font-size:8.5pt;min-width:0}th,td{padding:6pt}th{background:#eaf1ef!important;color:#2f4b42!important}tr{break-inside:avoid}figure{break-inside:avoid}pre{font-size:8pt;padding:10pt;white-space:pre-wrap}details>div{display:block!important}summary{display:none}footer{font-size:8pt}*{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
        """;
}
