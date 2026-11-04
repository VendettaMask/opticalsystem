using OpticalSystemDesign.App.Controls;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

internal static partial class DesignReportPdfWriter
{
    private sealed partial class Writer
    {
        // A single physical frame for the original sheet on every PDF figure page.
        // Include its external 1-DIP border; preserve the source aspect and pixels.
        private const float FigureWidth = 595.28f - 72;
        private const float FigureHeight = FigureWidth
            * (float)(UniformDiagramPage.SheetHeight + 2) / (float)(UniformDiagramPage.SheetWidth + 2);
        private const float FigureTop = 176;

        internal void PaperReport(DesignReportPdfModel report, string images, bool includeAppendix)
        {
            var layout = DesignReportPresentation.Create(report);
            long imageBytes = 0;
            var figure = 0;
            foreach (var page in layout.Pages)
            {
                token.ThrowIfCancellationRequested();
                StartPage(false);
                if (page.SectionNumber > 0 && layout.SectionPages[page.SectionNumber - 1] == page.Number)
                {
                    SectionStartPages.Add(_pages);
                    _canvas!.DrawNamedDestinationAnnotation(new SKPoint(Margin, _y), $"report-section-{page.SectionNumber}");
                }
                if (page.Cover) Text("OPTICAL DESIGN & ANALYSIS", 8, false, 10, color: new SKColor(82, 109, 130));
                Text((page.SectionNumber > 0 ? $"{page.SectionNumber:D2}  " : "") + page.Title,
                    page.Cover ? 25 : 20, true, 12, color: new SKColor(32, 43, 54));
                if (!page.Cover && page.Subtitle.Length > 0)
                    Text(DesignReportHtmlWriter.ShortConditions(page.Subtitle), 8.5f, false, 14, color: new SKColor(82, 96, 110));
                foreach (var block in page.Content.Blocks)
                {
                    switch (block)
                    {
                        case ReportParagraph { Text: "OPTICAL DESIGN & ANALYSIS" }: break;
                        case ReportParagraph paragraph:
                            Text(paragraph.Text, paragraph.Style == ReportTextStyle.Subtitle ? 11 : 8.5f,
                                false, 10, color: paragraph.Style == ReportTextStyle.Warning ? new SKColor(141, 88, 61) : new SKColor(82, 96, 110));
                            break;
                        case ReportTable table: Table(table); break;
                        case ReportDirectoryEntry entry: DirectoryEntry(entry); break;
                        case ReportPicture picture:
                            using (var source = DesignReportImage.Open(images, picture.FileName))
                            {
                                imageBytes += source.Length;
                                if (imageBytes > OpticalSystemDesign.Application.Services.BoundedApplicationFile.MaximumExportBytes)
                                    throw new InvalidDataException("报告图片超过大小上限。");
                                using var data = SKData.Create(source); using var image = DesignReportImage.Decode(data);
                                if (page.Cover) _y = Math.Max(_y, 200);
                                UniformPicture(image, page.Cover ? picture.Title : $"图 {++figure}　{picture.Title}", 14);
                            }
                            break;
                    }
                }
                if (includeAppendix && report.Sections.Count > 0 && page == layout.Pages.Last(candidate => candidate.Content.Blocks.Any(block => block is ReportDirectoryEntry)))
                    DirectoryEntry(new("附录 详细计算记录", report.Sections.Count + 1, 0));
            }
        }

        private void DirectoryEntry(ReportDirectoryEntry entry)
        {
            using var font = typography.Font(ProductFontRole.Document, 9.5f);
            using var ink = new SKPaint { Color = new SKColor(61, 84, 101), IsAntialias = true };
            using var rule = new SKPaint { Color = new SKColor(218, 225, 231), StrokeWidth = .4f };
            var title = $"{entry.SectionNumber:D2}　{entry.Title}";
            var lines = Wrap(title, font, Width - 40);
            var lineHeight = LineHeight(font); var height = lines.Count * lineHeight + 6;
            Reserve(height);
            for (var index = 0; index < lines.Count; index++) Draw(lines[index], Margin, _y + index * lineHeight, lineHeight, font, ink);
            var appendix = context is not null && entry.SectionNumber > context.Sections.Count;
            var page = appendix ? appendixPage ?? 0 : contentsPages?.ElementAtOrDefault(entry.SectionNumber - 1) ?? entry.Page;
            var label = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Draw(label, Margin + Width - typography.Measure(label, font), _y, lineHeight, font, ink);
            _canvas!.DrawLine(Margin, _y + height - 2, Margin + Width, _y + height - 2, rule);
            _canvas.DrawLinkDestinationAnnotation(new SKRect(Margin, _y, Margin + Width, _y + height), appendix ? "report-appendix" : $"report-section-{entry.SectionNumber}");
            _y += height;
        }

        private void UniformPicture(SKImage image, string caption, float after)
        {
            // Move the complete frame to the next page when necessary. Metadata
            // and metrics must never determine the figure's scale or arrangement.
            if (PageWidth > PageHeight) StartPage(false);
            using var font = typography.Font(ProductFontRole.Document, 9);
            var captionHeight = Math.Min(2, Wrap(caption, font, FigureWidth).Count) * LineHeight(font) + 18;
            if (Math.Max(FigureTop, _y) + FigureHeight + captionHeight > Bottom) StartPage(false);
            _y = Math.Max(FigureTop, _y);
            var scale = Math.Min(FigureWidth / image.Width, FigureHeight / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var left = (PageWidth - width) / 2;
            var top = _y + (FigureHeight - height) / 2;
            _canvas!.DrawImage(image, new SKRect(left, top, left + width, top + height),
                new SKSamplingOptions(SKFilterMode.Linear));
            _y += FigureHeight + 8;
            Text(caption, 9, false, after, centered: true, color: new SKColor(99, 113, 134));
        }

    }
}
