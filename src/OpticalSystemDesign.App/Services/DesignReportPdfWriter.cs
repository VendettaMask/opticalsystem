using System.Globalization;
using System.Text;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

/// <summary>A4 report with selectable embedded-font text, repeating table headers and embedded images.</summary>
internal static partial class DesignReportPdfWriter
{
    internal static void Write(Stream output, DesignReportContent content, string images, CancellationToken token,
        DesignReportPdfModel? paper = null, DesignReportContent? details = null)
    {
        using var fontStream = typeof(DesignReportPdfWriter).Assembly.GetManifestResourceStream("OpticalSystemDesign.App.Assets.Fonts.NotoSansCJKsc-Regular.otf")
            ?? throw new InvalidOperationException("报告字体不可用。");
        using var typeface = SKTypeface.FromStream(fontStream) ?? throw new InvalidOperationException("报告字体不可用。");
        using var pagination = paper is null ? null : new Writer(null, typeface, token);
        if (pagination is not null) Render(pagination, content, images, token, paper, details);
        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata(144f, 101)
        { Title = content.Blocks.OfType<ReportHeading>().FirstOrDefault()?.Text ?? "光学设计报告", Author = "S.T.A.R.Labs" })
            ?? throw new InvalidOperationException("无法创建 PDF 报告。");
        using var writer = new Writer(document, typeface, token, pagination?.SectionStartPages, pagination?.AppendixPage);
        Render(writer, content, images, token, paper, details);
        if (pagination is not null && (!pagination.SectionStartPages.SequenceEqual(writer.SectionStartPages)
            || pagination.AppendixPage != writer.AppendixPage))
            throw new InvalidDataException("目录页码与实际分页不一致。");
        token.ThrowIfCancellationRequested();
        document.Close();
    }

    private static void Render(Writer writer, DesignReportContent content, string images, CancellationToken token,
        DesignReportPdfModel? paper, DesignReportContent? details)
    {
        if (paper is not null)
        {
            writer.PaperReport(paper, images, details is { Blocks.Count: > 0 });
            if (details is null || details.Blocks.Count == 0)
            {
                writer.End();
                return;
            }
            content = details;
            writer.Section(false);
            writer.MarkAppendix();
            writer.Text("附录 详细计算记录", 17, true, 12);
        }
        else writer.StartPage();
        var figure = 0;
        long imageBytes = 0;
        for (var blockIndex = 0; blockIndex < content.Blocks.Count; blockIndex++)
        {
            var block = content.Blocks[blockIndex];
            token.ThrowIfCancellationRequested();
            switch (block)
            {
                case ReportHeading heading:
                    if (heading.NewPage) writer.Section();
                    if (blockIndex + 1 < content.Blocks.Count && content.Blocks[blockIndex + 1] is ReportPicture)
                        writer.KeepFigureHeading();
                    writer.Text(heading.Text, heading.Level == 1 ? 25 : heading.Level == 2 ? 17 : 13, true, 12, keepNext: true);
                    break;
                case ReportParagraph paragraph:
                    writer.Text(paragraph.Text, paragraph.Style == ReportTextStyle.Subtitle ? 16 : paragraph.Style == ReportTextStyle.Muted ? 9 : 10.5f,
                        paragraph.Style == ReportTextStyle.Warning, 12, color: paragraph.Style switch
                        { ReportTextStyle.Muted => new SKColor(99, 113, 134), ReportTextStyle.Warning => new SKColor(155, 53, 47), _ => SKColors.Black });
                    break;
                case ReportContents contents:
                    writer.Text("目录", 13, true, 10, keepNext: true);
                    foreach (var entry in contents.Entries) writer.Text(entry.Text, 10.5f, false, 4, color: new SKColor(35, 92, 150));
                    break;
                case ReportCode code:
                    writer.Text(code.Text, 8, false, 10);
                    break;
                case ReportTable table:
                    writer.Table(table);
                    break;
                case ReportPicture picture:
                    using (var source = DesignReportImage.Open(images, picture.FileName))
                    {
                        imageBytes += source.Length;
                        if (imageBytes > BoundedApplicationFile.MaximumExportBytes) throw new InvalidDataException("报告图片超过大小上限。");
                        using var data = SKData.Create(source);
                        using var image = DesignReportImage.Decode(data);
                        writer.Picture(image, $"图 {++figure}　{picture.Title}");
                    }
                    break;
            }
        }
        writer.End();
    }

    private sealed partial class Writer(SKDocument? document, SKTypeface typeface, CancellationToken token,
        IReadOnlyList<int>? contentsPages = null, int? appendixPage = null) : IDisposable
    {
        private const float Margin = 36, Top = 52;
        private float PageWidth = 595.28f, PageHeight = 841.89f;
        private float Bottom => PageHeight - 42;
        private float Width => PageWidth - 2 * Margin;
        private SKCanvas? _canvas;
        private float _y;
        private int _pages;
        private readonly SKPictureRecorder? _pagination = document is null ? new SKPictureRecorder() : null;
        internal List<int> SectionStartPages { get; } = [];
        internal int? AppendixPage { get; private set; }

        internal void StartPage(bool? landscape = null)
        {
            token.ThrowIfCancellationRequested();
            End();
            if (landscape is { } wide)
            {
                PageWidth = wide ? 841.89f : 595.28f;
                PageHeight = wide ? 595.28f : 841.89f;
            }
            if (++_pages > 1000) throw new InvalidOperationException("报告超过页数上限，请减少分析项目或配置数量。");
            _canvas = document is null ? _pagination!.BeginRecording(new SKRect(0, 0, PageWidth, PageHeight))
                : document.BeginPage(PageWidth, PageHeight);
            _canvas.Clear(SKColors.White);
            using var font = new SKFont(typeface, 8);
            using var paint = new SKPaint { Color = new SKColor(99, 113, 134), IsAntialias = true };
            _canvas.DrawText("Optical System Design", Margin, 27, SKTextAlign.Left, font, paint);
            BrandAssets.DrawCompanyLogo(_canvas, new SKRect(PageWidth - Margin - 82, 15, PageWidth - Margin, 27), SKColors.Black);
            _canvas.DrawText(_pages.ToString(CultureInfo.InvariantCulture), PageWidth / 2, PageHeight - 22, SKTextAlign.Center, font, paint);
            using var line = new SKPaint { Color = new SKColor(217, 225, 234), StrokeWidth = 0.5f };
            _canvas.DrawLine(Margin, 37, PageWidth - Margin, 37, line);
            _y = Top;
        }

        private void Reserve(float height)
        {
            token.ThrowIfCancellationRequested();
            if (_y + height > Bottom) StartPage();
        }

        internal void Section(bool? landscape = null) { if (_y > Top + 1) StartPage(landscape); }
        internal void KeepFigureHeading()
        {
            Reserve(FigureHeight + 74);
        }

        internal void Text(string text, float size, bool bold, float after, bool keepNext = false, SKColor? color = null, bool centered = false)
        {
            using var font = new SKFont(typeface, size) { Embolden = bold };
            var lineHeight = LineHeight(font);
            var lines = Wrap(text, font, Width);
            if (keepNext) Reserve(Math.Min(Bottom - Top, lines.Count * lineHeight + 35));
            using var paint = new SKPaint { Color = color ?? SKColors.Black, IsAntialias = true };
            foreach (var line in lines)
            {
                Reserve(lineHeight);
                Draw(line, centered ? Margin + (Width - font.MeasureText(line)) / 2 : Margin, _y, lineHeight, font, paint);
                _y += lineHeight;
            }
            _y += after;
        }

        internal void Picture(SKImage image, string title)
        {
            UniformPicture(image, title, 16);
        }

        internal void Table(ReportTable table)
        {
            if (table.Columns.Length == 0) return;
            var widths = DesignReportImage.ColumnWeights(table).Select(weight => (float)weight * Width).ToArray();
            using var bodyFont = new SKFont(typeface, 8.5f);
            using var headerFont = new SKFont(typeface, 8.5f) { Embolden = true };
            var lineHeight = Math.Max(LineHeight(bodyFont), LineHeight(headerFont));
            var header = Cells(table.Columns, headerFont);
            var headerHeight = header.Max(lines => lines.Count) * lineHeight + 10;
            if (headerHeight + lineHeight + 10 > Bottom - Top) throw new InvalidDataException("报告表头超过页面高度。");
            Reserve(headerHeight + lineHeight + 10);
            Row(header, 0, header.Max(lines => lines.Count), headerFont, true, false);
            for (var rowIndex = 0; rowIndex < table.Rows.Length; rowIndex++)
            {
                token.ThrowIfCancellationRequested();
                var cells = Cells(table.Rows[rowIndex], bodyFont);
                var count = cells.Max(lines => lines.Count);
                var offset = 0;
                // Ordinary rows stay together. Oversized rows split into readable fragments.
                if (count * lineHeight + 10 <= Bottom - Top - headerHeight && _y + count * lineHeight + 10 > Bottom) NextTablePage();
                while (offset < count)
                {
                    var fit = (int)Math.Floor((Bottom - _y - 10) / lineHeight);
                    if (fit < 1) { NextTablePage(); continue; }
                    var take = Math.Min(fit, count - offset);
                    Row(cells, offset, take, bodyFont, false, rowIndex % 2 != 0);
                    offset += take;
                    if (offset < count) NextTablePage();
                }
            }
            _y += 10;

            List<string>[] Cells(string[] values, SKFont font) => values.Select((value, i) => Wrap(value, font, Math.Max(1, widths[i] - 10))).ToArray();
            void NextTablePage()
            {
                StartPage();
                Row(header, 0, header.Max(lines => lines.Count), headerFont, true, false);
            }
            void Row(List<string>[] cells, int offset, int count, SKFont font, bool isHeader, bool alternate)
            {
                token.ThrowIfCancellationRequested();
                var height = count * lineHeight + 10;
                var x = Margin;
                using var fill = new SKPaint { Color = isHeader ? new SKColor(234, 241, 239) : alternate ? new SKColor(247, 250, 248) : SKColors.White };
                using var border = new SKPaint { Color = new SKColor(217, 227, 222), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };
                using var textPaint = new SKPaint { Color = isHeader ? new SKColor(47, 75, 66) : new SKColor(45, 52, 49), IsAntialias = true };
                for (var column = 0; column < cells.Length; column++)
                {
                    var rect = new SKRect(x, _y, x + widths[column], _y + height);
                    _canvas!.DrawRect(rect, fill); _canvas.DrawRect(rect, border);
                    for (var line = 0; line < count && offset + line < cells[column].Count; line++)
                        Draw(cells[column][offset + line], x + 5, _y + 5 + line * lineHeight, lineHeight, font, textPaint);
                    x += widths[column];
                }
                _y += height;
            }
        }

        private List<string> Wrap(string text, SKFont font, float width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            {
                if (paragraph.Length == 0) { lines.Add(""); continue; }
                var start = 0;
                while (start < paragraph.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var end = start; var lastSpace = -1; var measured = 0f;
                    while (end < paragraph.Length)
                    {
                        var element = StringInfo.GetNextTextElement(paragraph, end);
                        var advance = font.MeasureText(element);
                        if (end > start && measured + advance > width) break;
                        measured += advance;
                        if (char.IsWhiteSpace(paragraph[end])) lastSpace = end;
                        end += element.Length;
                    }
                    if (end < paragraph.Length && lastSpace > start && end - lastSpace < 20) end = lastSpace + 1;
                    lines.Add(paragraph[start..end].TrimEnd());
                    start = end;
                }
            }
            return lines;
        }

        private static float LineHeight(SKFont font) => Math.Max(font.Size * 1.5f, font.Spacing * 1.15f);
        private void Draw(string text, float x, float top, float height, SKFont font, SKPaint paint)
        {
            if (text.Length == 0) return;
            // Retain source UTF-8 clusters so PDF text uses the actual characters,
            // rather than reverse-mapping shared CJK glyphs to compatibility radicals.
            var glyphs = font.GetGlyphs(text);
            var bytes = Encoding.UTF8.GetBytes(text);
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocateTextRun(font, glyphs.Length, 0, 0, bytes.Length);
            run.SetGlyphs(glyphs);
            run.SetText(bytes);
            var index = 0; var offset = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                run.Clusters[index++] = (uint)offset;
                offset += rune.Utf8SequenceLength;
            }
            using var blob = builder.Build();
            _canvas!.DrawText(blob, x, top + (height - font.Metrics.Descent - font.Metrics.Ascent) / 2, paint);
        }
        internal void MarkAppendix()
        {
            AppendixPage = _pages;
            _canvas!.DrawNamedDestinationAnnotation(new SKPoint(Margin, _y), "report-appendix");
        }

        internal void End()
        {
            if (_canvas is null) return;
            if (document is not null) document.EndPage();
            else _pagination!.EndRecording().Dispose();
            _canvas = null;
        }

        public void Dispose() { End(); _pagination?.Dispose(); }
    }
}
