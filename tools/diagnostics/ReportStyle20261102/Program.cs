using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Tests;

namespace ReportStyle;
public static class SampleApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
internal static class Program
{
    private sealed record SourceImage(string File, int Width, int Height, string Sha256);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task Main(string[] args)
    {
        if (args.Length > 2 && args[2] is "--package-only" or "--word-only") { await ReportPackage.Rebuild(args[1], args[2] == "--word-only"); return; }
        var source = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var sourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SampleApplication));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.Apply(UiLocalization.Chinese);
            DisplayTypography.Configure(new AppSettings { Theme = "Light", Language = "zh-CN" });
            using var application = WorkbenchApplication.Create();
            await application.Documents.OpenAsync(source);
            using var snapshot = application.CaptureReportSnapshot();
            var workspace = snapshot.Workspace;
            var document = workspace.Documents.GetSnapshot();
            var excluded = new[] { "Image Simulation", "Geometric Image Analysis", "Geometric Bitmap Image Analysis",
                "Light Source Analysis", "Partially Coherent Image Analysis", "Extended Diffraction Image Analysis" };
            var keys = workspace.Analyses.AnalysisNames.Select(workspace.Analyses.CanonicalKey)
                .Where(MarkdownReportExporter.IsSupportedAnalysis).Distinct().Except(excluded).ToArray();
            await Save("selection.json", new { Included = keys.Select(key => new { Key = key, Title = WorkbenchAnalysisCatalog.DisplayName(key) }),
                Excluded = excluded.Select(key => new { Key = key, Title = WorkbenchAnalysisCatalog.DisplayName(key) }) });
            await Save("snapshot.json", new { Source = source, Sha256 = sourceHash, snapshot.CapturedAt, Document = document,
                Fields = workspace.Prescription.GetFields(), Wavelengths = workspace.Prescription.GetWavelengths(),
                FieldDefinition = workspace.Prescription.GetSystemSettings().FieldDefinition });
            Console.WriteLine($"SELECTED {keys.Length} analyses + 2 layouts; excluded {excluded.Length} expensive image/source analyses");
            var host = new Border { Width = 1200, Height = 900, Opacity = 0 };
            var window = new Window { Width = 900, Height = 700, Content = host };
            window.Show(); Dispatcher.UIThread.RunJobs();
            var renderer = new ReportImageRenderer(host);
            var report = new DesignReportPdfModel(document.Name, Path.GetFileName(source), snapshot.CapturedAt)
            {
                FieldLabel = workspace.Prescription.GetSystemSettings().FieldDefinition == "Angle" ? "视场角 / °" : "视场坐标",
                Fields = string.Join(" / ", workspace.Prescription.GetFields().Select(field => $"({field.X}, {field.Y})")),
                Wavelengths = string.Join(" / ", workspace.Prescription.GetWavelengths().Select(wave => wave.Nanometers)) + " nm"
            };
            report.Configurations.Add((1, document));
            var details = new DesignReportContent();
            var outcomes = new List<object>(); var clock = Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            try
            {
                var index = 0;
                foreach (var dimension in new[] { SceneDimension.TwoDimensional, SceneDimension.ThreeDimensional })
                {
                    index++;
                    var title = dimension == SceneDimension.TwoDimensional ? "二维布局" : "三维布局";
                    Console.WriteLine($"[{clock.Elapsed:hh\\:mm\\:ss}] {index}/{keys.Length + 2} {title}");
                    var scene = await Task.Run(() => workspace.Visualization.BuildSceneAsync(dimension, timeout.Token));
                    var files = await Render(new(null, scene, document, snapshot.CapturedAt, title), $"item-{index:000}");
                    var layoutSection = new DesignReportPdfSection(1, title) { CoverImage = dimension == SceneDimension.TwoDimensional, Conditions = "长度单位：mm · 标准布局视角" };
                    layoutSection.Images.AddRange(files.Select(image => image.File)); report.Sections.Add(layoutSection);
                    var item = new { Index = index, Key = dimension.ToString(), Title = title, Status = "Success", HasPlot = true, Images = files };
                    outcomes.Add(item); await Save($"item-{index:000}.json", item);
                }
                foreach (var key in keys)
                {
                    index++;
                    var title = WorkbenchAnalysisCatalog.DisplayName(key);
                    Console.WriteLine($"[{clock.Elapsed:hh\\:mm\\:ss}] {index}/{keys.Length + 2} {title}");
                    var parameters = workspace.Analyses.MergeSettings(key, null);
                    var descriptors = workspace.Analyses.GetParameters(key, parameters);
                    var section = new DesignReportPdfSection(1, title); report.Sections.Add(section);
                    details.Heading(title + " 计算记录", 3);
                    details.Table(["参数", "值"], descriptors.Select(parameter => new[] { parameter.DisplayName + " (" + parameter.Key + ")", parameters.GetValueOrDefault(parameter.Key, parameter.DefaultValue) }));
                    try
                    {
                        var result = await Task.Run(() => workspace.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, parameters), timeout.Token));
                        var view = result.View; var hasPlot = AnalysisPanel.HasReportPlot(result);
                        section.ApplyAnalysis(view, descriptors, parameters, hasPlot);
                        details.Paragraph("计算来源：" + result.ExecutorId + "；请求指纹：" + result.RequestFingerprint);
                        details.Table(["项目", "值"], view.Rows.Select(row => new[] { row.Metric, row.Value }));
                        if (section.Table is { } numericTable) details.Table(numericTable.Columns, numericTable.Rows);
                        if (!string.IsNullOrWhiteSpace(view.ReportText)) details.Code(view.ReportText, "text");
                        var images = new List<SourceImage>(); string? renderError = null;
                        if (view.Outcome == AnalysisOutcome.Success && hasPlot)
                        {
                            try { images = await Render(new(result, null, result.SourceDocument ?? document, snapshot.CapturedAt, title), $"item-{index:000}"); }
                            catch (Exception error) when (error is not OperationCanceledException) { renderError = error.Message; }
                        }
                        section.Images.AddRange(images.Select(image => image.File));
                        if (view.Outcome != AnalysisOutcome.Success || renderError is not null) section.Failure = renderError ?? view.OutcomeReason ?? view.Outcome.ToString();
                        var item = new { Index = index, Key = key, Title = title, Status = view.Outcome.ToString(), view.OutcomeReason,
                            HasPlot = hasPlot, RenderError = renderError, Images = images, view.Rows, view.Table, view.ReportText,
                            PresentationKind = view.PresentationKind.ToString(), result.Provenance, Parameters = parameters,
                            ParameterLabels = descriptors.Select(p => new { p.Key, p.DisplayName, p.DefaultValue, p.ChoiceLabels }),
                            Conditions = descriptors.Where(p => new[] { "Field", "Wave", "Sample", "Sampling", "Surface", "Reference" }
                                .Any(part => p.Key.Contains(part, StringComparison.OrdinalIgnoreCase))).Take(5)
                                .Select(p => DesignReportPdfModel.Condition(p, parameters)),
                            Metrics = DesignReportPdfModel.KeyMetrics(view), PaneCount = view.PlotPanes.Count };
                        outcomes.Add(new { index, key, title, Status = view.Outcome.ToString(), RenderError = renderError });
                        await Save($"item-{index:000}.json", item);
                        if (renderError is not null) Console.WriteLine($"RENDER FAILED {title}: {renderError}");
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        var item = new { Index = index, Key = key, Title = title, Status = "Failed", OutcomeReason = error.Message, Parameters = parameters };
                        outcomes.Add(item); await Save($"item-{index:000}.json", item);
                        section.Failure = error.Message; Console.WriteLine($"FAILED {title}: {error.Message}");
                    }
                    await Save("progress.json", new { Completed = index, Total = keys.Length + 2, ElapsedSeconds = clock.Elapsed.TotalSeconds });
                }
                var after = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
                if (after != sourceHash) throw new InvalidOperationException("Source lens changed");
                await Save("generation.json", new { Count = index, SourceUnchanged = true, ElapsedSeconds = clock.Elapsed.TotalSeconds, Outcomes = outcomes });
                Console.WriteLine($"COMPLETE {index} items in {clock.Elapsed}");
                var content = new DesignReportContent(); content.Heading(document.Name + " 光学设计分析报告", 1);
                var reports = Path.Combine(Directory.GetParent(output)!.FullName, "Reports"); Directory.CreateDirectory(reports);
                var artifacts = new List<object>();
                foreach (var format in new[] { DesignReportFormat.Html, DesignReportFormat.Pdf, DesignReportFormat.Word, DesignReportFormat.Markdown })
                {
                    var extension = format switch { DesignReportFormat.Html => ".html", DesignReportFormat.Pdf => ".pdf", DesignReportFormat.Word => ".docx", _ => ".md" };
                    var path = Path.Combine(reports, "Double Gauss 28 degree field_full_report" + extension);
                    var images = Path.Combine(output, "source-images");
                    if (format == DesignReportFormat.Markdown)
                    {
                        var assets = Path.Combine(reports, "report_assets"); Directory.CreateDirectory(assets);
                        foreach (var file in Directory.GetFiles(images, "*.png")) File.Copy(file, Path.Combine(assets, Path.GetFileName(file)), true);
                        await File.WriteAllTextAsync(Path.Combine(assets, "company-logo.svg"), DesignReportHtmlWriter.Resource("Assets.Brand.CompanyLogo.svg"));
                        await File.WriteAllTextAsync(path, DesignReportMarkdownWriter.Write(report, details, "report_assets"));
                    }
                    else
                    {
                        using var stream = File.Create(path);
                        if (format == DesignReportFormat.Html) DesignReportHtmlWriter.Write(stream, content, images, timeout.Token, report, details);
                        else if (format == DesignReportFormat.Word) DesignReportWordWriter.Write(stream, content, images, timeout.Token, report);
                        else DesignReportPdfWriter.Write(stream, content, images, timeout.Token, report);
                    }
                    var bytes = await File.ReadAllBytesAsync(path);
                    artifacts.Add(new { Format = format.ToString(), Path = path, Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                    Console.WriteLine($"WROTE {format} {bytes.Length} bytes {path}");
                }
                await Save("artifacts.json", artifacts);
            }
            finally { window.Close(); }

            async Task<List<SourceImage>> Render(ReportImageRequest request, string stem)
            {
                var imageDirectory = Path.Combine(output, "source-images"); Directory.CreateDirectory(imageDirectory);
                var files = await renderer.WriteAsync(request, imageDirectory, stem, timeout.Token);
                var images = new List<SourceImage>();
                foreach (var file in files)
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(imageDirectory, file), timeout.Token);
                    using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
                    if (bitmap.Width != 2404 || bitmap.Height != 1804) throw new InvalidOperationException($"Unexpected source size {bitmap.Width}x{bitmap.Height}");
                    var border = bitmap.GetPixel(0, 0);
                    for (var x = 0; x < bitmap.Width; x++)
                        if (bitmap.GetPixel(x, 0) != border || bitmap.GetPixel(x, bitmap.Height - 1) != border) throw new InvalidOperationException("Incomplete horizontal border");
                    for (var y = 0; y < bitmap.Height; y++)
                        if (bitmap.GetPixel(0, y) != border || bitmap.GetPixel(bitmap.Width - 1, y) != border) throw new InvalidOperationException("Incomplete vertical border");
                    images.Add(new SourceImage(file, bitmap.Width, bitmap.Height, Convert.ToHexString(SHA256.HashData(bytes))));
                }
                return images;
            }
            async Task Save(string file, object value) => await File.WriteAllTextAsync(Path.Combine(output, file), JsonSerializer.Serialize(value, Json));
        }, CancellationToken.None);
    }
}
