using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UnifiedFontManagementTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    [InlineData("Lumen")]
    public async Task AllPortableRolesUseTheSharedPolicyAndBundledGlyphFallbacks(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UnifiedFontManagementTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings()); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            try
            {
                var typography = new DocumentTypography();
                var expected = theme == "Isekai" ? "Almendra" : ProductFonts.DocumentFamilyName;
                Assert.Equal(expected, typography.Primary(ProductFontRole.Document));
                Assert.Equal(expected, typography.Primary(ProductFontRole.Engineering));
                Assert.Equal(ProductFonts.MathFamilyName, typography.Primary(ProductFontRole.Math));
                foreach (var role in new[] { ProductFontRole.Document, ProductFontRole.Engineering, ProductFontRole.Code, ProductFontRole.Math })
                foreach (var rune in "Optical 0123456789 中文 λρ√∞± µm".EnumerateRunes().Where(r => !Rune.IsWhiteSpace(r)))
                    Assert.NotEqual((ushort)0, typography.TypefaceFor(role, rune.Value).GetGlyph(rune.Value));
                foreach (var face in typography.Assets)
                {
                    Assert.NotEmpty(face.Bytes);
                    Assert.Contains("Open Font License", ProductFonts.License(face), StringComparison.OrdinalIgnoreCase);
                }
                // Export workers must not read dispatcher-owned Avalonia properties.
                var worker = await Task.Run(() => new DocumentTypography().Primary(ProductFontRole.Document));
                Assert.Equal(expected, worker);
                using var font = typography.Font(ProductFontRole.Document, 14);
                using var surface = SKSurface.Create(new SKImageInfo(900, 110)); surface.Canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
                typography.Draw(surface.Canvas, "Optical focal length 50.000 mm · 波长 λ = 0.5876 µm", 20, 60, font, paint);
                Capture(surface, theme + "-glyphs.png");
            }
            finally { ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Isekai")]
    public async Task PdfWordHtmlAndGuideEmbedTheRegisteredFontsAndPreserveText(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UnifiedFontManagementTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new AppSettings()); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var directory = EvidenceDirectory(); Directory.CreateDirectory(directory);
            try
            {
                var expected = new DocumentTypography();
                var content = new DesignReportContent();
                content.Heading("Optical System Design / 字体管理", 1);
                content.Paragraph("Focal length: 50.000 mm; 波长 λ = 0.5876 µm; RMS = 0.012345.");
                content.Code("Field 1    X: 0.000    Y: 12.345", "text");
                content.Table(["Index", "Formula", "Value"], [["Z4", "√3 (2ρ^2-1)", "0.012345"]], formulaColumn: 1);
                using (var stream = File.Create(Path.Combine(directory, theme + ".pdf"))) DesignReportPdfWriter.Write(stream, content, directory, CancellationToken.None);
                using (var stream = File.Create(Path.Combine(directory, theme + ".html"))) DesignReportHtmlWriter.Write(stream, content, directory, CancellationToken.None);
                using (var stream = File.Create(Path.Combine(directory, theme + ".docx"))) DesignReportWordWriter.Write(stream, content, directory, CancellationToken.None);
                var pdf = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(directory, theme + ".pdf")));
                Assert.Contains(theme == "Isekai" ? "Almendra" : "NotoSansCJK", pdf);
                var html = File.ReadAllText(Path.Combine(directory, theme + ".html"));
                Assert.Contains("font-src data:", html); Assert.Contains("var(--osd-document)", html);
                Assert.Contains("base64," + Convert.ToBase64String(ProductFonts.MathAsset.Bytes), html);
                Assert.Contains("50.000", html); Assert.DoesNotContain("Microsoft YaHei", html);
                using var zip = ZipFile.OpenRead(Path.Combine(directory, theme + ".docx"));
                XDocument Xml(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
                var names = Xml("word/fontTable.xml").Root!.Elements(w + "font").Select(f => f.Attribute(w + "name")!.Value).ToArray();
                Assert.Contains(expected.Primary(ProductFontRole.Document), names); Assert.Contains(ProductFonts.MathFamilyName, names);
                var mapping = Xml("word/_rels/fontTable.xml.rels").Descendants(rel + "Relationship").ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);
                var restored = new HashSet<string>();
                foreach (var embed in Xml("word/fontTable.xml").Descendants().Where(e => e.Attribute(w + "fontKey") is not null))
                {
                    using var stream = zip.GetEntry("word/" + mapping[embed.Attribute(r + "id")!.Value])!.Open();
                    using var bytes = new MemoryStream(); stream.CopyTo(bytes); var data = bytes.ToArray();
                    // ECMA-376 uses the reverse of the textual GUID byte order.
                    var key = embed.Attribute(w + "fontKey")!.Value.Trim('{', '}').Replace("-", "", StringComparison.Ordinal);
                    for (var index = 0; index < 32; index++) data[index] ^= Convert.ToByte(key.Substring(30 - 2 * (index % 16), 2), 16);
                    restored.Add(Convert.ToHexString(SHA256.HashData(data)));
                }
                Assert.All(expected.Assets, face => Assert.Contains(Convert.ToHexString(SHA256.HashData(face.Bytes)), restored));
                Assert.Contains("50.000", Xml("word/document.xml").ToString());
                Assert.NotNull(zip.GetEntry("word/fonts/LICENSES.txt"));
                var guide = new UserGuidePanel([]);
                UserGuidePdfExporter.Export(Path.Combine(directory, theme + "-guide.pdf"),
                    guide.ChaptersForPdf(UserGuidePdfScope.CompleteGuide).Take(1).ToArray(), UiLocalization.English);
            }
            finally { ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Fact]
    public void FontFamilyLiteralsAndNativeTypefaceLoadingStayInTheRegistry()
    {
        var root = FindRoot();
        var app = Path.Combine(root, "src", "OpticalSystemDesign.App");
        var allowed = new HashSet<string> { "ProductFonts.cs", "ProductFonts.Registry.cs", "DisplayTypography.cs" };
        foreach (var path in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            if (allowed.Contains(Path.GetFileName(path))) continue;
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch("SKTypeface\\.(From|Default)|SKFontManager\\.Default|new\\s+FontFamily\\s*\\(\\s*\"", source);
        }
        var template = File.ReadAllText(Path.Combine(app, "Assets", "Reports", "EngineeringReport.html"));
        Assert.DoesNotContain("Cambria", template); Assert.DoesNotContain("SimSun", template);
        Assert.DoesNotContain("Microsoft YaHei", template); Assert.Contains("var(--osd-math)", template);
    }

    [Fact]
    public async Task AnOpenPopupFollowsEveryThemesDefaultFontInsteadOfFluentInter()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UnifiedFontManagementTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new AppSettings());
            var app = Avalonia.Application.Current!;
            var button = new Button { Content = "Open" };
            var label = new TextBlock { Text = "Optical" };
            var flyout = new Flyout { Content = label };
            var window = new Window { Width = 500, Height = 300, Content = button };
            try
            {
                window.Show(); flyout.ShowAt(button);
                foreach (var theme in new[] { "Light", "Dark", "Pixel", "Lumen", "Isekai", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme); DisplayTypography.Apply(window);
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Equal(ThemeRegistry.FromSettings(theme).UiFontFamily, label.FontFamily);
                    var faces = label.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                        .Select(run => run.GlyphRun.GlyphTypeface.FamilyName).Distinct().ToArray();
                    Assert.NotEmpty(faces);
                    var expected = theme == "Isekai" ? "Almendra" : theme == "Pixel" ? "Fusion Pixel 10px Mono zh_hans" : "DM Sans";
                    // DM Sans's variable face exposes its named optical-size instance.
                    Assert.All(faces, family => { if (expected == "DM Sans") Assert.StartsWith("DM Sans", family); else Assert.Equal(expected, family); });
                }
            }
            finally { flyout.Hide(); window.Close(); ThemeApplicationService.Apply(app, "Light"); DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Isekai")]
    public async Task ActualEngineeringPngAndPdfUseTheCapturedSharedFamily(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UnifiedFontManagementTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings()); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            try
            {
                using var workbench = WorkbenchApplication.Create("cooke");
                var element = OpticalManufacturingModel.BuildElements(workbench.Prescription.GetSurfaces())[0];
                var sheet = new OpticalDrawingSheet(element, OpticalDrawingPageSize.A4, "OPT-FONT-001",
                    "Optical Element / 测试光学元件", "Designer", "Reviewer", "A", .02, -.01, .03, -.02,
                    100, 100, 1, 1, "2 × 0.16", "AR coating", "0.2 × 45°", "10 nm/cm", "2 × 0.1", "2；2");
                Assert.Empty(sheet.Validate());
                var directory = EvidenceDirectory(); Directory.CreateDirectory(directory);
                var original = sheet;
                File.WriteAllBytes(Path.Combine(directory, theme + "-engineering.png"), OpticalDrawingRenderer.RenderPreview(sheet, 1000));
                var path = Path.Combine(directory, theme + "-engineering.pdf");
                await Task.Run(() => OpticalDrawingRenderer.ExportPdf(path, sheet));
                Assert.Contains(theme == "Isekai" ? "Almendra" : "NotoSansCJK", Encoding.Latin1.GetString(File.ReadAllBytes(path)));
                Assert.Equal(original, sheet);
            }
            finally { ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); DisplayTypography.Configure(new AppSettings()); UiLocalization.Apply(UiLocalization.Chinese); }
        }, CancellationToken.None);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.sln")) || Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root unavailable.");
    }
    private static string EvidenceDirectory() => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_UNIFIED_FONT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "unified-font-tests");
    private static void Capture(SKSurface surface, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_UNIFIED_FONT_DIR"); if (directory is null) return;
        Directory.CreateDirectory(directory); using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, name), data.ToArray());
    }
}
