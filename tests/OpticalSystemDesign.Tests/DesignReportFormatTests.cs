using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class DesignReportFormatTests
{
    [Theory]
    [InlineData(DesignReportFormat.Markdown)]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Html)]
    public async Task ReportContainsSystemSummaryWithoutSurfacePrescriptionOrEmptyAppendix(DesignReportFormat format)
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        const string privateLabel = "PRESCRIPTION_ONLY_SURFACE_LABEL";
        var surface = application.Prescription.GetSurfaces()[1];
        application.Prescription.UpdateSurface(surface with { Label = privateLabel });
        var before = application.Documents.GetRecoveryState();
        using var snapshot = application.CaptureReportSnapshot();
        var result = await MarkdownReportExporter.ExportAsync(snapshot,
            new() { Format = format, AnalysisKeys = [], Include2DLayout = false, Include3DLayout = false },
            directory.Path, (_, _, _, _) => throw new InvalidOperationException("No plot was requested."));
        Assert.Equal(0, result.Failed);
        Assert.Equal(before, application.Documents.GetRecoveryState());
        string text;
        if (format == DesignReportFormat.Pdf)
        {
            // The cover and compact summary fit two pages. A serialized prescription
            // would introduce extra pages even when no analyses or layouts were selected.
            var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(result.ReportPath));
            Assert.Equal(2, Regex.Matches(pdf, @"/Type /Page\b").Count);
            return;
        }
        if (format == DesignReportFormat.Word)
        {
            using var zip = ZipFile.OpenRead(result.ReportPath);
            using var stream = zip.GetEntry("word/document.xml")!.Open();
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            text = string.Join("\n", XDocument.Load(stream).Descendants(w + "t").Select(element => element.Value));
        }
        else text = await File.ReadAllTextAsync(result.ReportPath);
        Assert.Contains("系统参数", text);
        Assert.Contains("EFL / mm", text);
        Assert.DoesNotContain(privateLabel, text);
        Assert.DoesNotContain("半径 / mm", text);
        Assert.DoesNotContain("完整处方", text);
        Assert.DoesNotContain("Surfaces", text);
        Assert.DoesNotContain("RadiusSolve", text);
        Assert.DoesNotContain("附录 完整参数与计算记录", text);
    }

    [Theory]
    [InlineData(DesignReportFormat.Word, ".docx")]
    [InlineData(DesignReportFormat.Pdf, ".pdf")]
    [InlineData(DesignReportFormat.Html, ".html")]
    public async Task ExportProducesSelfContainedDocumentsAndPreservesSource(DesignReportFormat format, string extension)
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces()[1];
        const string label = "镜片 [A] <B> & C | λ 中文";
        application.Prescription.UpdateSurface(surface with { Label = label });
        var before = application.Documents.GetRecoveryState();
        using var snapshot = application.CaptureReportSnapshot();
        var options = new MarkdownReportSettings { Format = format, AnalysisKeys = [], Include3DLayout = false };
        var result = await MarkdownReportExporter.ExportAsync(snapshot, options, directory.Path, WriteImage);
        Assert.EndsWith(extension, result.ReportPath);
        Assert.Equal(1, result.Succeeded); Assert.Equal(0, result.Failed);
        Assert.Equal(before, application.Documents.GetRecoveryState());
        Assert.Equal([result.ReportPath], Directory.GetFileSystemEntries(directory.Path));
        if (format == DesignReportFormat.Word)
        {
            using var zip = ZipFile.OpenRead(result.ReportPath);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
            using var documentStream = zip.GetEntry("word/document.xml")!.Open();
            var xml = XDocument.Load(documentStream);
            Assert.DoesNotContain(label, xml.Descendants(w + "t").Select(text => text.Value));
            Assert.True(xml.Descendants(w + "tbl").Count() >= 2);
            Assert.Contains(xml.Descendants(w + "pStyle"), style => (string?)style.Attribute(w + "val") == "Title");
            Assert.Single(zip.Entries, entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
            using var relStream = zip.GetEntry("word/_rels/document.xml.rels")!.Open();
            var relationships = XDocument.Load(relStream);
            foreach (var relationship in relationships.Descendants(r + "Relationship"))
                Assert.NotNull(zip.GetEntry("word/" + (string)relationship.Attribute("Target")!));
            Assert.DoesNotContain(relationships.Descendants(r + "Relationship"), item => (string?)item.Attribute("TargetMode") == "External");
        }
        else if (format == DesignReportFormat.Pdf)
        {
            var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(result.ReportPath));
            Assert.StartsWith("%PDF-", pdf);
            Assert.Contains("/Subtype /Image", pdf);
            Assert.Contains("/ToUnicode", pdf);
            // Skia embeds the bundled CFF font as self-contained Type3 glyph programs.
            Assert.Contains("/Subtype /Type3", pdf);
            Assert.Contains("/CharProcs", pdf);
            Assert.DoesNotContain("/DCTDecode", pdf); // PNG charts must not be degraded by JPEG re-encoding.
        }
        else
        {
            var html = await File.ReadAllTextAsync(result.ReportPath);
            Assert.StartsWith("<!doctype html>", html);
            Assert.DoesNotContain(WebUtility.HtmlEncode(label), html);
            Assert.Contains("data:image/png;base64,", html);
            Assert.Contains("Content-Security-Policy", html);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src=\"http", html, StringComparison.OrdinalIgnoreCase);
        }
        using var repeat = application.CaptureReportSnapshot();
        var again = await MarkdownReportExporter.ExportAsync(repeat, options, directory.Path, WriteImage);
        Assert.NotEqual(result.ReportPath, again.ReportPath);
        Assert.True(File.Exists(result.ReportPath));
    }

    [Theory]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Html)]
    public async Task PackagingFailureCleansOnlyItsOwnFiles(DesignReportFormat format)
    {
        using var directory = new ReportTestDirectory();
        var existing = Path.Combine(directory.Path, "existing.docx");
        await File.WriteAllTextAsync(existing, "keep");
        using var application = WorkbenchApplication.Create("cooke");
        using var snapshot = application.CaptureReportSnapshot();
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => MarkdownReportExporter.ExportAsync(snapshot,
            new() { Format = format, AnalysisKeys = [], Include3DLayout = false }, directory.Path,
            async (_, output, stem, token) =>
            {
                var file = stem + ".png";
                await File.WriteAllTextAsync(Path.Combine(output, file), "invalid image", token);
                return [file];
            }));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.Equal([existing], Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task UnknownFormatIsRejectedBeforeCreatingOutput()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create();
        using var snapshot = application.CaptureReportSnapshot();
        var output = Path.Combine(directory.Path, "absent");
        await Assert.ThrowsAsync<InvalidOperationException>(() => MarkdownReportExporter.ExportAsync(snapshot,
            new() { Format = (DesignReportFormat)999 }, output, WriteImage));
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Markdown)]
    [InlineData(DesignReportFormat.Html)]
    public void FormatSurvivesSettingsCopyAndSerialization(DesignReportFormat format)
    {
        var settings = new MarkdownReportSettings { Format = format };
        Assert.Equal(format, settings.Copy().Format);
        Assert.Equal(format, JsonSerializer.Deserialize<MarkdownReportSettings>(JsonSerializer.Serialize(settings))!.Format);
        Assert.Equal(DesignReportFormat.Markdown, JsonSerializer.Deserialize<MarkdownReportSettings>("{}")!.Format);
    }

    [Fact]
    public async Task ReaderContentPrecedesCompleteAppendixWithoutLosingRawData()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        using var snapshot = application.CaptureReportSnapshot();
        var result = await MarkdownReportExporter.ExportAsync(snapshot,
            new() { AnalysisKeys = ["Spot Diagram"], Include2DLayout = false, Include3DLayout = false }, directory.Path, WriteImage);
        var markdown = await File.ReadAllTextAsync(result.ReportPath);
        var appendix = markdown.IndexOf("## 附录 完整参数与计算记录", StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("![", StringComparison.Ordinal) < appendix);
        Assert.DoesNotContain("```json", markdown);
        Assert.DoesNotContain("完整处方", markdown);
        Assert.Contains("计算来源", markdown[appendix..]);
        Assert.Contains("RayDensity", markdown[appendix..]);
        Assert.Contains("## 目录", markdown);
        Assert.Contains("*图 1", markdown);
        foreach (Match link in Regex.Matches(markdown, @"\]\(#(section-\d+)\)"))
            Assert.Contains("id=\"" + link.Groups[1].Value + "\"", markdown);
    }

    [Fact]
    public void HtmlEncodesUserContentAndKeepsNavigationAndPrintStylesOffline()
    {
        const string attack = "</code><script>alert('x')</script><img src=x onerror=alert(1)>";
        var content = new DesignReportContent();
        content.Heading(attack, 1);
        content.Heading("结构", 2);
        content.Paragraph(attack);
        content.Table(["参数", "值"], [["名称", attack]]);
        content.Heading("附录", 2, true);
        content.BeginAppendix();
        content.Code(attack, "text");
        using var stream = new MemoryStream();
        DesignReportHtmlWriter.Write(stream, content, "", CancellationToken.None);
        var html = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains(WebUtility.HtmlEncode(attack), html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#section-002\"", html);
        Assert.Contains("id=\"section-002\"", html);
        Assert.Contains("@media print", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<details class=\"appendix\" open>", html);
    }

    [Theory]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    public void LongTablesAndOversizedRowsRemainPaginatedAndComplete(DesignReportFormat format)
    {
        var content = new DesignReportContent();
        content.Heading("报告表格换页验证", 1);
        content.Table(["项目", "说明", "数值"], Enumerable.Range(1, 40).Select(i => new[]
            { "项目 " + i, string.Concat(Enumerable.Repeat("长单元格中文内容 ", i == 20 ? 350 : 12)), i.ToString() }));
        content.Paragraph("末行标记 End of report");
        content.Code(new string('x', 600) + " 长行末尾", "text");
        using var stream = new MemoryStream();
        if (format == DesignReportFormat.Word) DesignReportWordWriter.Write(stream, content, "", CancellationToken.None);
        else DesignReportPdfWriter.Write(stream, content, "", CancellationToken.None);
        Assert.True(stream.Length > 1000);
        if (format == DesignReportFormat.Word)
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            using var xmlStream = zip.GetEntry("word/document.xml")!.Open();
            var xml = XDocument.Load(xmlStream);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            Assert.Single(xml.Descendants(w + "tblHeader"));
            Assert.Equal(41, xml.Descendants(w + "tr").Count());
            Assert.Contains("末行标记 End of report", xml.Descendants(w + "t").Select(text => text.Value));
        }
        else
        {
            var bytes = stream.ToArray();
            Assert.True(Regex.Matches(Encoding.Latin1.GetString(bytes), @"/Type /Page\b").Count > 2);
            var commands = PdfStreamCommands(bytes);
            Assert.Contains("/ActualText <FEFF957F>", commands); // 长, not a same-glyph radical.
            Assert.Contains("/ActualText <FEFF884C>", commands); // 行.
        }
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllBytes(Path.Combine(evidence, "table-stress" + (format == DesignReportFormat.Word ? ".docx" : ".pdf")), stream.ToArray());
        }
    }

    private static string PdfStreamCommands(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var result = new StringBuilder();
        foreach (Match match in Regex.Matches(raw, @"<<(?<dictionary>.*?)>>\s*stream\r?\n", RegexOptions.Singleline))
        {
            var length = Regex.Match(match.Groups["dictionary"].Value, @"/Length (\d+)");
            if (!length.Success || !match.Groups["dictionary"].Value.Contains("/FlateDecode", StringComparison.Ordinal)) continue;
            using var compressed = new MemoryStream(pdf, match.Index + match.Length, int.Parse(length.Groups[1].Value));
            using var deflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.Latin1);
            result.AppendLine(reader.ReadToEnd());
        }
        return result.ToString();
    }

    private static Task<IReadOnlyList<string>> WriteImage(ReportImageRequest request, string directory, string stem, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var name = stem + ".png";
        using var surface = SKSurface.Create(new SKImageInfo(320, 200));
        surface.Canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Blue, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        surface.Canvas.DrawCircle(160, 100, 70, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, name));
        data.SaveTo(file);
        return Task.FromResult<IReadOnlyList<string>>([name]);
    }
}
