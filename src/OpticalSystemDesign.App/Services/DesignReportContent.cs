using System.Text;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal abstract record DesignReportBlock;
internal sealed record ReportHeading(string Text, int Level, bool NewPage = false) : DesignReportBlock;
internal enum ReportTextStyle { Normal, Subtitle, Muted, Warning }
internal sealed record ReportParagraph(string Text, ReportTextStyle Style = ReportTextStyle.Normal) : DesignReportBlock;
internal sealed record ReportContents(IReadOnlyList<ReportHeading> Entries) : DesignReportBlock;
internal sealed record ReportAppendixStart : DesignReportBlock;
internal sealed record ReportCode(string Text, string Language) : DesignReportBlock;
internal sealed record ReportTable(string[] Columns, string[][] Rows) : DesignReportBlock;
internal sealed record ReportPicture(string Title, string FileName) : DesignReportBlock;

/// <summary>One calculation result feeds all formats; user text is never parsed as markup.</summary>
internal sealed class DesignReportContent
{
    private long _characters;
    internal List<DesignReportBlock> Blocks { get; } = [];
    internal void Heading(string text, int level, bool newPage = false) => Add(new ReportHeading(text, level, newPage), text.Length);
    internal void Paragraph(string text, ReportTextStyle style = ReportTextStyle.Normal) => Add(new ReportParagraph(text, style), text.Length);
    internal void Append(DesignReportContent content)
    {
        CheckLength(content._characters);
        Blocks.AddRange(content.Blocks);
    }
    internal void Contents(IEnumerable<ReportHeading> headings)
    {
        var entries = headings.Where(heading => heading.Level == 2).ToArray();
        Add(new ReportContents(entries), entries.Sum(heading => (long)heading.Text.Length));
    }
    internal void BeginAppendix() => Add(new ReportAppendixStart(), 0);
    internal void Code(string text, string language) => Add(new ReportCode(text, language), text.Length);
    internal void Picture(string title, string fileName) => Add(new ReportPicture(title, fileName), title.Length + fileName.Length);
    internal void Table(string[] columns, IEnumerable<string[]> rows)
    {
        if (columns.Length == 0) throw new InvalidDataException("报告表格缺少列定义。");
        var values = new List<string[]>();
        Add(new ReportTable(columns, []), columns.Sum(value => (long)value.Length));
        foreach (var row in rows)
        {
            if (row.Length != columns.Length) throw new InvalidDataException("报告表格列数不一致。");
            CheckLength(row.Sum(value => (long)value.Length));
            values.Add(row);
        }
        Blocks[^1] = new ReportTable(columns, values.ToArray());
    }

    private void Add(DesignReportBlock block, long characters)
    {
        CheckLength(characters);
        Blocks.Add(block);
    }

    private void CheckLength(long characters)
    {
        _characters += characters;
        if (_characters > BoundedApplicationFile.MaximumExportBytes / 8 || Blocks.Count > 100_000)
            throw new InvalidOperationException("报告超过大小上限，请减少分析项目或配置数量。");
    }

    internal string Markdown(string imageDirectory)
    {
        var text = new StringBuilder();
        var headingNumber = 0; var figureNumber = 0; var appendix = false;
        var headings = Blocks.OfType<ReportHeading>().Where(heading => heading.Level == 2).ToArray();
        foreach (var block in Blocks)
        {
            switch (block)
            {
                case ReportHeading heading:
                    if (heading.Level == 2) text.AppendLine($"<a id=\"section-{++headingNumber:D3}\"></a>\n");
                    text.Append(new string('#', heading.Level)).Append(' ').AppendLine(Escape(heading.Text)).AppendLine();
                    break;
                case ReportParagraph paragraph:
                    text.AppendLine(paragraph.Style == ReportTextStyle.Subtitle ? $"**{Escape(paragraph.Text)}**" : Escape(paragraph.Text)).AppendLine();
                    break;
                case ReportContents contents:
                    text.AppendLine("## 目录\n");
                    foreach (var entry in contents.Entries)
                        text.AppendLine($"- [{Escape(entry.Text)}](#section-{Array.IndexOf(headings, entry) + 1:D3})");
                    text.AppendLine();
                    break;
                case ReportAppendixStart:
                    text.AppendLine("<details>\n<summary>展开完整参数与计算记录</summary>\n");
                    appendix = true;
                    break;
                case ReportCode code:
                    var fence = new string('`', Math.Max(3, code.Text.Split('\n').Max(line => line.TrimStart(' ', '\t').TakeWhile(c => c == '`').Count() + 1)));
                    text.AppendLine(fence + code.Language).AppendLine(code.Text).AppendLine(fence).AppendLine();
                    break;
                case ReportTable table:
                    Row(table.Columns);
                    text.AppendLine("| " + string.Join(" | ", table.Columns.Select(_ => "---")) + " |");
                    foreach (var row in table.Rows) Row(row);
                    text.AppendLine();
                    break;
                case ReportPicture picture:
                    text.AppendLine($"![{Escape(picture.Title)}](<{Uri.EscapeDataString(imageDirectory)}/{Uri.EscapeDataString(picture.FileName)}>)").AppendLine();
                    text.AppendLine($"*图 {++figureNumber}　{Escape(picture.Title)}*").AppendLine();
                    break;
            }
        }
        if (appendix) text.AppendLine("</details>");
        return text.ToString();
        void Row(IEnumerable<string> cells) => text.AppendLine("| " + string.Join(" | ", cells.Select(Escape)) + " |");
    }

    private static string Escape(string text) => MarkdownReportExporter.Escape(text);
}
