using Avalonia.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UserGuideMathematicsTests
{
    [Fact]
    public async Task EveryActualAnalysisHasAnExistingCalculationChapterAndEveryFormulaHasOutlines()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var panel = new UserGuidePanel([]);
            var chapters = panel.ChaptersForPdf(UserGuidePdfScope.CompleteGuide);
            foreach (var analysis in WorkbenchAnalysisCatalog.Descriptors)
            {
                var id = UserGuidePanel.CalculationChapterForAnalysis(analysis.CanonicalKey);
                Assert.True(id is not null, "Missing calculation coverage: " + analysis.CanonicalKey);
                Assert.Contains(chapters, c => c.Id == id);
            }
            var used = chapters.SelectMany(c => c.Sections).Where(s => s.Formula is not null).Select(s => s.Formula!).ToHashSet();
            Assert.Equal(UserGuideMath.All.Keys.Order(), used.Order());
            foreach (var id in used)
            {
                var artwork = UserGuideMath.Load(id);
                Assert.NotEmpty(artwork.Outlines);
                Assert.True(artwork.Width > 0 && artwork.Height > 0);
                Assert.True(artwork.Width * 9.5 / 1000 < 490, "Formula too wide for legible PDF: " + id);
                using var surface = SKSurface.Create(new SKImageInfo(900, 500));
                surface.Canvas.Clear(SKColors.White);
                UserGuideMath.Draw(surface.Canvas, artwork, 4, 4, 21, SKColors.Black);
                using var image = surface.Snapshot();
                using var pixels = SKBitmap.FromImage(image);
                Assert.Contains(pixels.Pixels, color => color.Red < 80 && color.Green < 80 && color.Blue < 80);
            }
            foreach (var section in chapters.SelectMany(c => c.Sections).Where(s => s.RelatedChapter is not null))
                Assert.Contains(chapters, c => c.Id == section.RelatedChapter);
            WriteAudit("coverage.json", new
            {
                analysisCount = WorkbenchAnalysisCatalog.Descriptors.Count,
                calculationChapters = panel.CalculationChapterCount,
                formulas = used.Count,
                coverage = WorkbenchAnalysisCatalog.Descriptors.ToDictionary(a => a.CanonicalKey, a => UserGuidePanel.CalculationChapterForAnalysis(a.CanonicalKey))
            });
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task CalculationPdfContainsEveryChapterFormulaAndScreenshot(string language)
    {
        var output = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_PDF_DIR");
        var temporary = output is null;
        output ??= Path.Combine(Path.GetTempPath(), "guide-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
            await session.Dispatch(() =>
            {
                var panel = new UserGuidePanel([]);
                var chapters = panel.ChaptersForPdf(UserGuidePdfScope.Calculations);
                var path = Path.Combine(output, $"Optical-System-Design-Calculations-{language}.pdf");
                var result = UserGuidePdfExporter.Export(path, chapters, language);
                WriteAudit(language + ".json", result);
                Assert.Equal(27, result.Chapters);
                Assert.Equal(chapters.Sum(c => c.Sections.Count(s => s.Formula is not null)), result.Formulas);
                Assert.True(result.Screenshots > 5);
                Assert.True(result.Pages >= 27);
                Assert.True(new FileInfo(path).Length > 100_000);
                Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 5));
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
                Assert.Throws<OperationCanceledException>(() => UserGuidePdfExporter.Export(path, chapters, language, cancellation.Token));
                Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                using var during = new CancellationTokenSource();
                Assert.Throws<OperationCanceledException>(() => UserGuidePdfExporter.Export(path, chapters, language, during.Token,
                    page => { if (page == 3) during.Cancel(); }));
                Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            }, CancellationToken.None);
        }
        finally { if (temporary) Directory.Delete(output, true); }
    }

    [Fact]
    public async Task CompleteAndSingleChapterExportsAlsoWorkOnTheBackgroundWorker()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "guide-pdf-scopes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
            await session.Dispatch(async () =>
            {
                var panel = new UserGuidePanel([]);
                var all = panel.ChaptersForPdf(UserGuidePdfScope.CompleteGuide);
                var single = all.Single(c => c.Id == "calculation-paraxial");
                var one = await Task.Run(() => UserGuidePdfExporter.Export(Path.Combine(temporary, "chapter.pdf"), [single], UiLocalization.Chinese));
                Assert.Equal(1, one.Chapters); Assert.Equal(single.Sections.Count(s => s.Formula is not null), one.Formulas);
                var complete = await Task.Run(() => UserGuidePdfExporter.Export(Path.Combine(temporary, "complete.pdf"), all, UiLocalization.Chinese));
                Assert.Equal(all.Count, complete.Chapters);
                Assert.Equal(all.Sum(c => c.Sections.Count(s => s.Formula is not null)), complete.Formulas);
                Assert.True(complete.Screenshots > one.Screenshots);
                WriteAudit("all-scopes.json", new { single = one, complete });
            }, CancellationToken.None);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static void WriteAudit(string name, object data)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GUIDE_PDF_AUDIT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
