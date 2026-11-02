using System.Text;

namespace OpticalSystemDesign.App.Services;

internal static class DesignReportMarkdownWriter
{
    internal static string Write(DesignReportPdfModel report, DesignReportContent details, string imageDirectory)
    {
        var result = new StringBuilder(); var figure = 0;
        result.AppendLine($"![S.T.A.R.Labs](<{Uri.EscapeDataString(imageDirectory)}/company-logo.svg>)\n");
        var layout = DesignReportPresentation.Create(report);
        foreach (var page in layout.Pages)
        {
            // Markdown keeps native semantic text/tables; a viewer chooses its own colors and pagination.
            if (page.SectionNumber > 0 && layout.SectionPages[page.SectionNumber - 1] != page.Number) continue;
            result.AppendLine($"<a id=\"page-{page.Number:D2}\"></a>\n");
            if (page.SectionNumber > 0) result.AppendLine($"<a id=\"section-{page.SectionNumber:D3}\"></a>\n");
            result.Append(page.Cover ? "# " : "## ").AppendLine(Escape((page.SectionNumber == 0 && page.Subtitle.StartsWith("配置 ", StringComparison.Ordinal) ? page.Subtitle + " · " : "") + page.Title.Replace('\n', ' '))).AppendLine();
            if (page.SectionNumber > 0)
            {
                result.AppendLine($"配置 {page.Configuration}　·　{Escape(page.Subtitle)}\n");
                var section = report.Sections[page.SectionNumber - 1];
                if (section.Failure is { } failure) result.AppendLine("**未完成：" + Escape(failure) + "**\n");
                else
                {
                    foreach (var image in section.Images) Picture(image, section.Title);
                    if (section.Images.Count > 0)
                    {
                        if (section.Metrics.Count > 0) Table(new(section.Metrics.Take(3).Select(pair => pair[0]).ToArray(),
                            [section.Metrics.Take(3).Select(pair => pair[1]).ToArray()]));
                    }
                    else if (section.Table is { } table) Table(table);
                    else Table(new(["项目", "数值"], section.Metrics.ToArray()));
                    if (section.FormulaNote.Length > 0) result.AppendLine(Escape(section.FormulaNote)).AppendLine();
                }
            }
            else foreach (var block in page.Content.Blocks)
            {
                switch (block)
                {
                    case ReportDirectoryEntry entry: result.AppendLine($"- [{entry.SectionNumber:D2}　{Escape(entry.Title)}](#section-{entry.SectionNumber:D3})"); break;
                    case ReportTable table: Table(table); break;
                    case ReportParagraph paragraph: result.AppendLine(Escape(paragraph.Text)).AppendLine(); break;
                    // The drawing is already linked in its own chapter, so Markdown doesn't duplicate the cover image.
                }
            }
            result.AppendLine();
        }
        if (details.Blocks.Count > 0)
        {
            result.AppendLine("## 附录 完整参数与计算记录\n");
            var appendix = new DesignReportContent(); appendix.BeginAppendix(); appendix.Append(details);
            result.Append(appendix.Markdown(imageDirectory));
        }
        return result.ToString();

        void Picture(string file, string title)
        {
            result.AppendLine($"![{Escape(title)}](<{Uri.EscapeDataString(imageDirectory)}/{Uri.EscapeDataString(file)}>)\n");
            result.AppendLine($"*图 {++figure}　{Escape(title)}*\n");
        }
        void Table(ReportTable table)
        {
            Row(table.Columns); result.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
            foreach (var row in table.Rows) Row(row.Select((value, column) => column == table.FormulaColumn ? DesignReportPresentation.ReadableFormula(value) : value));
            result.AppendLine();
        }
        void Row(IEnumerable<string> cells) => result.AppendLine("| " + string.Join(" | ", cells.Select(Escape)) + " |");
    }
    private static string Escape(string value) => MarkdownReportExporter.Escape(value);
}
