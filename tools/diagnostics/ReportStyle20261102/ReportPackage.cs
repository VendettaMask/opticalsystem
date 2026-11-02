using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Application.Services;

namespace ReportStyle;

internal static class ReportPackage
{
    internal static async Task Rebuild(string capture, bool wordOnly = false)
    {
        capture = Path.GetFullPath(capture);
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(capture, "snapshot.json")));
        var source = snapshot.RootElement; var document = source.GetProperty("Document").Deserialize<OpticalDocumentSnapshot>()!;
        var report = new DesignReportPdfModel(document.Name, Path.GetFileName(source.GetProperty("Source").GetString()!), source.GetProperty("CapturedAt").GetDateTimeOffset())
        {
            Fields = string.Join(" / ", source.GetProperty("Fields").EnumerateArray().Select(field => $"({field.GetProperty("X")}, {field.GetProperty("Y")})")),
            Wavelengths = string.Join(" / ", source.GetProperty("Wavelengths").EnumerateArray().Select(wave => DesignReportPdfModel.Number(wave.GetProperty("Nanometers").GetDouble()))) + " nm"
        };
        report.Configurations.Add((1, document)); var details = new DesignReportContent();
        if (source.TryGetProperty("FieldDefinition", out var definition)) report.FieldLabel = definition.GetString() == "Angle" ? "视场角 / °" : "视场坐标";
        else
        {
            var lens = source.GetProperty("Source").GetString()!;
            if (!Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(lens))).Equals(source.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The source lens no longer matches the frozen capture.");
            using var metadata = WorkbenchApplication.Create(); await metadata.Documents.OpenAsync(lens);
            report.FieldLabel = metadata.Prescription.GetSystemSettings().FieldDefinition == "Angle" ? "视场角 / °" : "视场坐标";
        }
        foreach (var file in Directory.GetFiles(capture, "item-*.json").Order(StringComparer.Ordinal))
        {
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file)); var item = json.RootElement;
            var title = item.GetProperty("Title").GetString()!; var key = item.GetProperty("Key").GetString()!;
            var section = new DesignReportPdfSection(1, title) { CoverImage = key == "TwoDimensional" }; report.Sections.Add(section);
            if (item.TryGetProperty("Images", out var images)) section.Images.AddRange(images.EnumerateArray().Select(image => image.GetProperty("File").GetString()!));
            if (item.TryGetProperty("Rows", out var rows))
            {
                var settings = item.GetProperty("Parameters").Deserialize<Dictionary<string, string>>()!;
                var view = new AnalysisViewDto(title, rows.Deserialize<AnalysisRowDto[]>()!, item.GetProperty("ReportText").GetString() ?? "", [], new(), [], 0,
                    item.GetProperty("Table").ValueKind == JsonValueKind.Null ? null : item.GetProperty("Table").Deserialize<AnalysisTableDto>(),
                    Enum.Parse<AnalysisPresentationKind>(item.GetProperty("PresentationKind").GetString()!));
                section.ApplyAnalysis(view, [], settings, item.GetProperty("HasPlot").GetBoolean());
                if (section.Table is { FormulaColumn: 3 } coefficients)
                    section.Table = coefficients with { Rows = coefficients.Rows.Select(row =>
                    {
                        var copy = row.ToArray();
                        copy[3] = ZernikeFitEngine.PolynomialExpression(int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture),
                            view.PresentationKind == AnalysisPresentationKind.ZernikeStandard, view.PresentationKind == AnalysisPresentationKind.ZernikeAnnular,
                            double.Parse(settings.GetValueOrDefault("ObscurationRatio", "0.5"), System.Globalization.CultureInfo.InvariantCulture));
                        return copy;
                    }).ToArray() };
                section.Conditions = string.Join(" · ", item.GetProperty("Conditions").EnumerateArray().Select(value => value.GetString()));
                details.Heading(title + " 计算记录", 3);
                details.Table(["参数", "值"], settings.Select(pair => new[] { pair.Key, pair.Value }));
                if (item.TryGetProperty("Provenance", out var provenance)) details.Paragraph("计算来源：" + provenance.GetProperty("ExecutorId").GetString() + "；请求指纹：" + provenance.GetProperty("RequestFingerprint").GetString());
                details.Table(["项目", "值"], view.Rows.Select(row => new[] { row.Metric, row.Value }));
                if (section.Table is { } table) details.Blocks.Add(table);
                if (view.ReportText.Length > 0) details.Code(view.ReportText, "text");
            }
            if (item.GetProperty("Status").GetString() != "Success") section.Failure = item.GetProperty("OutcomeReason").GetString();
            if (item.TryGetProperty("RenderError", out var error) && error.ValueKind != JsonValueKind.Null) section.Failure = error.GetString();
        }
        var content = new DesignReportContent(); content.Heading(document.Name + " 光学设计分析报告", 1);
        var output = Path.Combine(Directory.GetParent(capture)!.FullName, "Reports"); Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "report_assets"));
        foreach (var file in Directory.GetFiles(Path.Combine(capture, "source-images"), "*.png")) File.Copy(file, Path.Combine(output, "report_assets", Path.GetFileName(file)), true);
        await File.WriteAllTextAsync(Path.Combine(output, "report_assets", "company-logo.svg"), DesignReportHtmlWriter.Resource("Assets.Brand.CompanyLogo.svg"));
        var artifacts = new List<object>();
        if (wordOnly)
        {
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(capture, "artifacts.json")));
            artifacts.AddRange(previous.RootElement.EnumerateArray().Where(item => item.GetProperty("Format").GetString() != "Word").Select(item => (object)item.Clone()));
        }
        foreach (var format in Enum.GetValues<DesignReportFormat>())
        {
            if (wordOnly && format != DesignReportFormat.Word) continue;
            var extension = format switch { DesignReportFormat.Markdown => ".md", DesignReportFormat.Word => ".docx", DesignReportFormat.Html => ".html", _ => ".pdf" };
            var path = Path.Combine(output, "Double Gauss 28 degree field_full_report" + extension);
            var images = Path.Combine(capture, "source-images");
            if (format == DesignReportFormat.Markdown) await File.WriteAllTextAsync(path, DesignReportMarkdownWriter.Write(report, details, "report_assets"));
            else
            {
                using var stream = File.Create(path);
                if (format == DesignReportFormat.Word) DesignReportWordWriter.Write(stream, content, images, CancellationToken.None, report);
                else if (format == DesignReportFormat.Html) DesignReportHtmlWriter.Write(stream, content, images, CancellationToken.None, report, details);
                else DesignReportPdfWriter.Write(stream, content, images, CancellationToken.None, report);
            }
            var bytes = await File.ReadAllBytesAsync(path);
            artifacts.Add(new { Format = format.ToString(), Path = path, Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            Console.WriteLine($"PACKAGED {format} {bytes.Length}");
        }
        await File.WriteAllTextAsync(Path.Combine(capture, "artifacts.json"), JsonSerializer.Serialize(artifacts, new JsonSerializerOptions { WriteIndented = true }));
    }
}
