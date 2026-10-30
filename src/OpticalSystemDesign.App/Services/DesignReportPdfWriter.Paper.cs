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
        private const float FigureTop = 140;

        internal void PaperReport(DesignReportPdfModel report, string images, bool includeAppendix)
        {
            StartPage(false);
            Text("光学分析报告", 24, true, 5);
            Text(report.Name, 16, false, 16, color: new SKColor(54, 69, 88));
            Text($"{report.CapturedAt:yyyy-MM-dd HH:mm}  ·  {report.FileName}", 9.5f, false, 20,
                color: new SKColor(99, 113, 134));
            foreach (var configuration in report.Configurations)
            {
                var source = configuration.Document;
                Text(report.Configurations.Count == 1 ? "系统概览" : $"配置 {configuration.Number} · 系统概览", 14, true, 10, keepNext: true);
                Metrics([
                    ["有效焦距", DesignReportPdfModel.Number(source.EffectiveFocalLength) + " mm"],
                    ["F 数", DesignReportPdfModel.Number(source.FNumber)],
                    ["系统总长", DesignReportPdfModel.Number(source.TotalTrack) + " mm"],
                    ["入瞳直径", DesignReportPdfModel.Number(source.EntrancePupilDiameter) + " mm"]]);
            }
            Text(report.FieldLabel + "：" + report.Fields, 10, false, 8);
            Text("波长：" + report.Wavelengths, 10, false, 18);
            Text($"分析项目：{report.Sections.Count} 项 · 完成：{report.Sections.Count(section => section.Failure is null)} 项"
                + $" · 未完成：{report.Sections.Count(section => section.Failure is not null)} 项", 10, false, 10);
            Text("各图与结果来自同一工程快照。数值采用便于阅读的精度显示，计算精度保持。", 9, false, 8,
                color: new SKColor(99, 113, 134));
            Contents(report, includeAppendix);
            var sectionNumber = 0;
            long imageBytes = 0;
            foreach (var section in report.Sections)
            {
                token.ThrowIfCancellationRequested();
                sectionNumber++;
                var pageCount = Math.Max(1, section.Images.Count);
                for (var index = 0; index < pageCount; index++)
                {
                    Section(section.Images.Count == 0 && section.Landscape);
                    if (index == 0)
                    {
                        SectionStartPages.Add(_pages);
                        _canvas!.DrawNamedDestinationAnnotation(new SKPoint(Margin, _y), $"report-section-{sectionNumber}");
                    }
                    Text($"{sectionNumber:D2}  /  配置 {section.Configuration}"
                        + (pageCount > 1 ? $"  ·  分图 {index + 1}/{pageCount}" : ""), 9, false, 5,
                        color: new SKColor(54, 84, 113));
                    Text(section.Title, 18, true, 10, keepNext: true);
                    if (!string.IsNullOrWhiteSpace(section.Conditions))
                        Text(section.Conditions, 9, false, 12, color: new SKColor(99, 113, 134));
                    if (section.Failure is { } failure)
                    {
                        Text("未完成：" + failure, 10.5f, false, 12, color: new SKColor(155, 53, 47));
                        continue;
                    }
                    if (section.Images.Count > 0)
                    {
                        using var source = DesignReportImage.Open(images, section.Images[index]);
                        imageBytes += source.Length;
                        if (imageBytes > OpticalSystemDesign.Application.Services.BoundedApplicationFile.MaximumExportBytes)
                            throw new InvalidDataException("报告图片超过大小上限。");
                        using var data = SKData.Create(source);
                        using var image = DesignReportImage.Decode(data);
                        var caption = $"图 {sectionNumber}" + (pageCount > 1 ? $"-{index + 1}" : "") + "  " + section.Title;
                        UniformPicture(image, caption, 10);
                    }
                    else if (section.Table is { } table) Table(table);
                    Metrics(section.Metrics);
                    if (section.Images.Count == 0 && section.Table is null && section.Metrics.Count == 0)
                        Text("此项未提供图形或数值表。", 10, false, 10);
                }
            }
        }

        private void Contents(DesignReportPdfModel report, bool includeAppendix)
        {
            if (report.Sections.Count == 0 && !includeAppendix) return;
            Text("目录", 14, true, 10, keepNext: true);
            using var font = new SKFont(typeface, 10);
            using var ink = new SKPaint { Color = new SKColor(47, 75, 66), IsAntialias = true };
            using var line = new SKPaint { Color = new SKColor(208, 220, 214), StrokeWidth = .5f };
            var lineHeight = LineHeight(font);
            var multipleConfigurations = report.Configurations.Count > 1;
            for (var index = 0; index < report.Sections.Count + (includeAppendix ? 1 : 0); index++)
            {
                token.ThrowIfCancellationRequested();
                var appendix = index == report.Sections.Count;
                var title = appendix ? "附录 详细计算记录" : $"{index + 1:D2}  "
                    + (multipleConfigurations ? $"配置 {report.Sections[index].Configuration} · " : "") + report.Sections[index].Title;
                var lines = Wrap(title, font, Width - 56);
                var height = lines.Count * lineHeight + 8;
                if (_y + height > Bottom)
                {
                    StartPage(false);
                    Text("目录（续）", 14, true, 10, keepNext: true);
                }
                var top = _y;
                for (var row = 0; row < lines.Count; row++)
                    Draw(lines[row], Margin, top + row * lineHeight, lineHeight, font, ink);
                var page = appendix ? appendixPage : contentsPages?.ElementAtOrDefault(index);
                var label = page is > 0 ? page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0000";
                Draw(label, Margin + Width - font.MeasureText(label), top, lineHeight, font, ink);
                var start = Margin + font.MeasureText(lines[0]) + 10;
                var end = Margin + Width - 44;
                if (start < end) _canvas!.DrawLine(start, top + lineHeight / 2, end, top + lineHeight / 2, line);
                _canvas!.DrawLinkDestinationAnnotation(new SKRect(Margin, top, Margin + Width, top + height),
                    appendix ? "report-appendix" : $"report-section-{index + 1}");
                _y += height;
            }
        }

        private void UniformPicture(SKImage image, string caption, float after)
        {
            // Move the complete frame to the next page when necessary. Metadata
            // and metrics must never determine the figure's scale or arrangement.
            if (PageWidth > PageHeight) StartPage(false);
            using var font = new SKFont(typeface, 9);
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

        private void Metrics(IReadOnlyList<string[]> metrics)
        {
            if (metrics.Count == 0) return;
            using var font = new SKFont(typeface, 10);
            using var paint = new SKPaint { Color = new SKColor(54, 69, 88), IsAntialias = true };
            using var fill = new SKPaint { Color = new SKColor(247, 250, 248) };
            for (var index = 0; index < metrics.Count; index += 2)
            {
                var cells = metrics.Skip(index).Take(2)
                    .Select(metric => Wrap(metric[0] + "：" + metric[1], font, Width / 2 - 22)).ToArray();
                var lineHeight = LineHeight(font);
                var height = cells.Max(lines => lines.Count) * lineHeight + 10;
                Reserve(height);
                _canvas!.DrawRect(new SKRect(Margin, _y, Margin + Width, _y + height), fill);
                for (var column = 0; column < cells.Length; column++)
                    for (var line = 0; line < cells[column].Count; line++)
                        Draw(cells[column][line], Margin + column * Width / 2 + 10, _y + 5 + line * lineHeight,
                            lineHeight, font, paint);
                _y += height;
            }
            _y += 8;
        }
    }
}
