using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class PdfPaperReportTests
{
    [Fact]
    public async Task PaperFiguresUsePortraitAndDetailsAreExplicitAndSnapshotSafe()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        var before = application.Documents.GetRecoveryState();
        using var snapshot = application.CaptureReportSnapshot();
        var requests = new List<ReportImageRequest>();
        var settings = new MarkdownReportSettings
        {
            Format = DesignReportFormat.Pdf,
            AnalysisKeys = ["Spot Diagram"],
            Include2DLayout = false,
            Include3DLayout = false
        };
        Assert.False(settings.PdfIncludeDetails);
        var summary = await MarkdownReportExporter.ExportAsync(snapshot, settings, directory.Path, Image);
        Assert.Equal(1, summary.Succeeded); Assert.Equal(0, summary.Failed);
        Assert.True(Assert.Single(requests).ForPrint);
        var summaryPdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(summary.ReportPath));
        Assert.Equal(2, PageCount(summaryPdf));
        var sizes = Regex.Matches(summaryPdf, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.All(sizes.Cast<Match>(), match => Assert.True(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            < double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        settings.PdfIncludeDetails = true;
        Assert.True(settings.Copy().PdfIncludeDetails);
        Assert.True(JsonSerializer.Deserialize<MarkdownReportSettings>(JsonSerializer.Serialize(settings))!.PdfIncludeDetails);
        var detailed = await MarkdownReportExporter.ExportAsync(snapshot, settings, directory.Path, Image);
        var detailedPdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(detailed.ReportPath));
        Assert.True(PageCount(detailedPdf) > PageCount(summaryPdf));
        Assert.Equal(before, application.Documents.GetRecoveryState());

        Task<IReadOnlyList<string>> Image(ReportImageRequest request, string output, string stem, CancellationToken token)
        {
            requests.Add(request);
            using var bitmap = new SKBitmap(320, 200);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var file = stem + ".png";
            using var stream = File.Create(Path.Combine(output, file));
            data.SaveTo(stream);
            return Task.FromResult<IReadOnlyList<string>>([file]);
        }
    }

    [Fact]
    public void OriginalFiguresKeepIdenticalPhysicalSizeDespiteDensityMetadataMetricsAndLegacyLandscape()
    {
        using var directory = new ReportTestDirectory();
        var report = new DesignReportPdfModel("固定源图", "source.staropt", DateTimeOffset.Now);
        for (var index = 0; index < 3; index++)
        {
            var factor = index == 1 ? 2 : 1;
            var file = $"figure-{index}.png";
            using var bitmap = new SKBitmap(1202 * factor, 902 * factor);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(index == 1 ? SKColors.LightBlue : SKColors.White);
            using var pen = new SKPaint { Color = SKColors.DarkBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect(new SKRect(1, 1, bitmap.Width - 1, bitmap.Height - 1), pen);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(directory.Path, file));
            data.SaveTo(stream);
            var section = new DesignReportPdfSection(1, "完整原图 " + index)
            {
                Landscape = index != 0,
                Conditions = index == 2 ? string.Join(" · ", Enumerable.Repeat("Extended conditions 原始分析设置", 70)) : "同一设置"
            };
            section.Images.Add(file);
            section.Metrics.AddRange(Enumerable.Range(0, index * 10).Select(value => new[] { "指标 " + value, "0.1234 mm" }));
            report.Sections.Add(section);
        }
        using var output = new MemoryStream();
        DesignReportPdfWriter.Write(output, new DesignReportContent(), directory.Path, CancellationToken.None, report);
        var pdf = output.ToArray();
        var placements = ImagePlacements(pdf);
        Assert.Equal(3, placements.Count);
        foreach (var (width, height) in placements)
        {
            Assert.InRange(width, 523.27f, 523.29f);
            Assert.InRange(height, 392.66f, 392.69f);
        }
        Assert.Contains("/Width 2404", Encoding.Latin1.GetString(pdf));
        Assert.Contains("/Width 1202", Encoding.Latin1.GetString(pdf));
        Assert.DoesNotContain("/DCTDecode", Encoding.Latin1.GetString(pdf));
    }

    [Theory]
    [InlineData("Seidel Coefficients")]
    [InlineData("Zernike Fringe")]
    [InlineData("Zernike Standard")]
    [InlineData("Zernike Annular")]
    public async Task NumericalReportsUseSelectableTablesWithoutRequiringTextScreenshots(string key)
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        if (key.StartsWith("Zernike", StringComparison.Ordinal))
        {
            var coefficients = await application.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string> { ["ZernikeTerms"] = "37" }));
            var table = Assert.IsType<ReportTable>(DesignReportPdfModel.NumericalTable(coefficients.View));
            Assert.Equal(37, table.Rows.Length);
            Assert.StartsWith("Z1 (", table.Rows[0][0]);
            Assert.StartsWith("Z37 (", table.Rows[^1][0]);
            Assert.All(table.Rows, row => Assert.True(double.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)));
        }
        using var snapshot = application.CaptureReportSnapshot();
        var result = await MarkdownReportExporter.ExportAsync(snapshot,
            new() { Format = DesignReportFormat.Pdf, AnalysisKeys = [key], Include2DLayout = false, Include3DLayout = false },
            directory.Path, (_, _, _, _) => throw new InvalidOperationException("A native table must not require a text screenshot."));
        Assert.Equal(1, result.Succeeded); Assert.Equal(0, result.Failed);
        var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(result.ReportPath));
        Assert.Contains("/ToUnicode", pdf);
        Assert.DoesNotContain("/Subtype /Image", pdf);
        Assert.InRange(PageCount(pdf), 2, 3);
    }

    [Fact]
    public void PaperDisplayPrecisionKeepsSmallValuesAndLeavesNonNumericLabelsIntact()
    {
        Assert.Equal("RMS 0.00123457 mm", DesignReportPdfModel.ReadableValue("RMS 0.00123456789 mm"));
        Assert.Equal("6.12346E-08", DesignReportPdfModel.ReadableValue("0.00000006123456789"));
        Assert.Equal("config-12 / BK7", DesignReportPdfModel.ReadableValue("config-12 / BK7"));
    }

    [Fact]
    public async Task SummaryPrefersOpticalResultsAndOmitsDebugObjectsWhileKeepingFullSourceRows()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var result = await application.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Spot Diagram", new Dictionary<string, string>()));
        var rows = new AnalysisRowDto[]
        {
            new("光线数", "100"), new("ChiefRaySlopeImageSpace", "0.123"),
            new("波长 (µm)", "System.Double[]"), new("RMS 光程差", "0.0001234567"),
            new("波峰到波谷", "0.8"), new("截止频率 (cycles/mm)", "664.201")
        };
        var view = result.View with { Rows = rows };
        var metrics = DesignReportPdfModel.KeyMetrics(view).ToArray();
        Assert.Equal("RMS 光程差", metrics[0][0]);
        Assert.Equal("波峰到波谷", metrics[1][0]);
        Assert.Contains(metrics, metric => metric[0] == "截止频率 (cycles/mm)");
        Assert.DoesNotContain(metrics, metric => metric[0].Contains("ChiefRay", StringComparison.Ordinal)
            || metric[1].Contains("System.", StringComparison.Ordinal));
        Assert.Same(rows, view.Rows);
        var wavelength = application.Analyses.GetParameters("Spot Diagram").First(parameter => parameter.Key == "WavelengthNumber");
        Assert.EndsWith("全部", DesignReportPdfModel.Condition(wavelength, new Dictionary<string, string> { [wavelength.Key] = "0" }));
        Assert.Equal("波长：全部", DesignReportPdfModel.Condition(wavelength, new Dictionary<string, string> { [wavelength.Key] = "所有" }));
    }

    private static int PageCount(string pdf) => Regex.Matches(pdf, @"/Type /Page\b").Count;

    [Fact]
    public void LongContentsLinksEverySectionAndAppendixAcrossMixedPageOrientations()
    {
        using var directory = new ReportTestDirectory();
        using var application = WorkbenchApplication.Create("cooke");
        var report = new DesignReportPdfModel("目录分页检查", "source.staropt", DateTimeOffset.Now);
        var document = application.Documents.GetSnapshot();
        report.Configurations.Add((1, document)); report.Configurations.Add((2, document));
        for (var index = 0; index < 75; index++)
        {
            var section = new DesignReportPdfSection(index % 2 + 1, $"目录检查项目 {index + 1:D2}"
                + (index % 7 == 0 ? "：长标题保留完整名称与全部计算结果，并验证换行之后目录中的实际页码" : ""))
            {
                Landscape = index % 3 == 0,
                Table = new ReportTable(["项目", "结果"], Enumerable.Range(0, index % 5 == 0 ? 65 : 2)
                    .Select(row => new[] { "数据 " + row, "0.123456" }).ToArray())
            };
            report.Sections.Add(section);
        }
        var appendix = new DesignReportContent();
        appendix.Paragraph("附录必须出现在目录中，并跳转到实际的详细记录页。");
        using var output = new MemoryStream();
        DesignReportPdfWriter.Write(output, new(), directory.Path, CancellationToken.None, report, appendix);
        var pdf = Encoding.Latin1.GetString(output.ToArray());
        Assert.Equal(76, Regex.Matches(pdf, @"/Subtype /Link\b").Count);
        Assert.Contains("report-appendix", pdf);
        Assert.Contains("report-section-75", pdf);
        Assert.True(PageCount(pdf) > 78);
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_REPORT_QA_DIR") is { } capture)
        {
            Directory.CreateDirectory(capture);
            File.WriteAllBytes(Path.Combine(capture, "contents-75-sections.pdf"), output.ToArray());
        }
    }

    // Read the actual emitted PDF drawing matrices, rather than a layout helper.
    private static List<(float Width, float Height)> ImagePlacements(byte[] pdf)
    {
        var result = new List<(float, float)>();
        var raw = Encoding.Latin1.GetString(pdf);
        foreach (Match stream in Regex.Matches(raw, @"(?<dictionary><<[^<>]*>>)\s*stream\r?\n"))
        {
            var dictionary = stream.Groups["dictionary"].Value;
            var length = Regex.Match(dictionary, @"/Length\s+(\d+)\b");
            if (!length.Success) continue;
            var count = int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture);
            using var data = new MemoryStream(pdf, stream.Index + stream.Length, count);
            using var decoded = new MemoryStream();
            if (dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                using var inflater = new ZLibStream(data, CompressionMode.Decompress);
                inflater.CopyTo(decoded);
            }
            else data.CopyTo(decoded);
            var commands = Encoding.Latin1.GetString(decoded.ToArray());
            if (!Regex.IsMatch(commands, @"/X\d+ Do")) continue;
            var matrix = Matrix3x2.Identity;
            var saved = new Stack<Matrix3x2>();
            foreach (var line in commands.Split('\n').Select(line => line.Trim()))
            {
                if (line == "q") saved.Push(matrix);
                else if (line == "Q") matrix = saved.Pop();
                else if (line.EndsWith(" cm", StringComparison.Ordinal))
                {
                    var values = line[..^3].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    matrix = new Matrix3x2(values[0], values[1], values[2], values[3], values[4], values[5]) * matrix;
                }
                else if (Regex.IsMatch(line, @"^/X\d+ Do$"))
                    result.Add((Vector2.TransformNormal(Vector2.UnitX, matrix).Length(),
                        Vector2.TransformNormal(Vector2.UnitY, matrix).Length()));
            }
        }
        return result;
    }
}
