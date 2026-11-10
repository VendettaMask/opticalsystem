using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ReportLocalizationTests
{
    [Fact]
    public async Task AllNativeReportWritersRetainCapturedEnglishWhileTheUiChangesToChinese()
    {
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LOCALIZATION_EVIDENCE");
        var directory = Path.Combine(evidence ?? Path.GetTempPath(), "osd-report-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
            await session.Dispatch(async () =>
            {
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
                UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(UiLocalization.English);
                using var app = WorkbenchApplication.Create("cooke");
                var before = app.Documents.GetRecoveryState();
                var host = new Border();
                var uiLabel = new TextBlock { Text = "保存" };
                var grid = new Grid { Children = { host, uiLabel } };
                var window = new Window { Width = 1600, Height = 1100, Content = grid };
                try
                {
                    window.Show(); PresentationLocalizationTests.Tick(window);
                    var renderer = new ReportImageRenderer(host);
                    foreach (var format in Enum.GetValues<DesignReportFormat>())
                    {
                        UiLocalization.Apply(UiLocalization.English);
                        using var snapshot = app.CaptureReportSnapshot();
                        var result = await MarkdownReportExporter.ExportAsync(snapshot,
                            new() { Format = format, AnalysisKeys = ["MTF"], Include2DLayout = false, Include3DLayout = false },
                            directory, async (request, images, stem, token) =>
                            {
                                Assert.Equal(UiLocalization.English, request.Language);
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    UiLocalization.Apply(UiLocalization.Chinese);
                                    Assert.Equal("保存", uiLabel.Text);
                                });
                                var files = await renderer.WriteAsync(request, images, stem, token);
                                await Dispatcher.UIThread.InvokeAsync(() => Assert.Equal("保存", uiLabel.Text));
                                return files;
                            });
                        Assert.Equal(0, result.Failed);
                        Assert.Equal(UiLocalization.Chinese, UiLocalization.CurrentLanguage);
                    }
                    Assert.Equal(before, app.Documents.GetRecoveryState());
                }
                finally { window.Close(); UiLocalization.Apply(UiLocalization.Chinese); }
            }, CancellationToken.None);
            var html = File.ReadAllText(Directory.GetFiles(directory, "*.html", SearchOption.AllDirectories).Single());
            Assert.Contains("lang=\"en-US\"", html);
            Assert.DoesNotContain("@@", html);
            var script = Regex.Match(html, @"<script>([\s\S]*?)</script>").Groups[1].Value;
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
            Assert.Contains("sha256-" + hash, html);
            Assert.False(PresentationLocalizationTests.Cjk(System.Net.WebUtility.HtmlDecode(html)), "HTML contains untranslated text.");
            var markdown = File.ReadAllText(Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories).Single());
            Assert.False(PresentationLocalizationTests.Cjk(markdown), markdown);
            using var word = ZipFile.OpenRead(Directory.GetFiles(directory, "*.docx", SearchOption.AllDirectories).Single());
            using var content = word.GetEntry("word/document.xml")!.Open();
            var text = string.Join("\n", XDocument.Load(content).Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
            Assert.False(PresentationLocalizationTests.Cjk(text), text);
            Assert.Contains("Optical Design Analysis Report", text);
            Assert.True(new FileInfo(Directory.GetFiles(directory, "*.pdf", SearchOption.AllDirectories).Single()).Length > 1000);
        }
        finally { if (evidence is null && Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
