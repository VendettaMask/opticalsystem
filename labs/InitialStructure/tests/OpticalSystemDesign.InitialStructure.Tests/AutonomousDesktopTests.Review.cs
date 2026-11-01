using Avalonia.Controls;
using Avalonia;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using OpticalSystemDesign.Tests;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed partial class AutonomousDesktopTests
{
    [Fact]
    public async Task M36ThreeLanguageReviewPortfolioReportAndSelectedStructureKeepTheOriginalRecordUnchanged()
    {
        using var files = new OwnedFiles(); using var host = SafeHeadlessUnitTestSession.StartNew(typeof(FlatStartDesktopTests));
        await host.Dispatch(async () =>
        {
            var language = UiLocalization.CurrentLanguage;
            var reportLanguage = UiLocalization.Chinese;
            var portfolioPath = files.Path("portable.design-portfolio.json"); var studyPath = files.Path("record.design-study.json");
            var window = new AutonomousTopologyWindow(new() { RunDirectory = files.Root, InitialPlan = Plan(),
                OpenPicker = (_, kind) => Task.FromResult<string?>(kind switch { "record" => AutonomousDesignReviewTests.Original(),
                    "design-portfolio" => portfolioPath, "design-study" => studyPath, _ => null }),
                SavePicker = (_, kind) => Task.FromResult<string?>(kind switch { "design-portfolio" => portfolioPath,
                    "design-study" => studyPath, "design-report" => files.Path(reportLanguage + ".md"), "optic" => files.Path("selected.staropt"), _ => null }) });
            try
            {
                window.Show();
                using var display = SequentialTraceMeasurement.Begin();
                Click(window, "AutonomousOpenRecord"); await window.PendingOperation;
                var tabs = Find<TabControl>(window, "AutonomousTabs"); tabs.SelectedItem = Find<TabItem>(window, "AutonomousReviewTab");
                window.UpdateLayout(); Assert.NotNull(window.CurrentReview);
                var original = window.CurrentReview!.Study.SourceFingerprint; var plan = ContentFingerprint.Compute(window.CurrentReview.Study.Plan);
                Assert.True(window.CurrentReview.Candidates.Any(c => c.OriginalQualified));
                var decisions = Find<DataGrid>(window, "AutonomousReviewDecisions");
                decisions.SelectedItem = decisions.ItemsSource!.Cast<object>().First(row =>
                    ((DesignReviewDecision)row.GetType().GetProperty("Decision")!.GetValue(row)!).Outcome.StartsWith("Rejected:", StringComparison.Ordinal));
                Assert.Contains("Rejected:", Find<TextBox>(window, "AutonomousReviewDetails").Text!);
                Find<TextBox>(window, "AutonomousReviewName").Text = "Original F8 fixed apertures";
                Click(window, "AutonomousPortfolioAdd"); await window.PendingOperation;
                Click(window, "AutonomousPortfolioSave"); await window.PendingOperation;
                Click(window, "AutonomousReviewData"); await window.PendingOperation;
                Assert.True(File.Exists(portfolioPath)); Assert.True(File.Exists(studyPath));
                Click(window, "AutonomousPortfolioOpen"); await window.PendingOperation;
                var entries = Find<ComboBox>(window, "AutonomousPortfolioEntries"); entries.SelectedIndex = 0;
                Assert.Equal(original, window.CurrentReview!.Study.SourceFingerprint); Assert.Equal(plan, ContentFingerprint.Compute(window.CurrentReview.Study.Plan));
                foreach (var locale in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
                {
                    reportLanguage = locale; UiLocalization.Apply(locale); window.UpdateLayout();
                    Assert.Equal(UiLocalization.Text("记录、方案与报告"), Find<TabItem>(window, "AutonomousReviewTab").Header);
                    Assert.Equal(UiLocalization.Text("导出设计报告"), Find<Button>(window, "AutonomousReviewReport").Content);
                    if (locale == UiLocalization.English)
                        Assert.DoesNotContain(DesignReviewLabels.All, key => key == UiLocalization.Text(key));
                    if (locale == UiLocalization.Japanese)
                        Assert.DoesNotContain(DesignReviewLabels.Items.Where(item => item.Zh != item.Ja), item => UiLocalization.Text(item.Zh) == item.Zh);
                    Click(window, "AutonomousReviewReport"); await window.PendingOperation;
                    Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                    var report = await File.ReadAllTextAsync(files.Path(locale + ".md"));
                    Assert.Contains(UiLocalization.Text("自动初始结构设计报告"), report); Assert.Contains("F/8", report);
                    Assert.Contains(original, report); Assert.DoesNotContain("## 原验收规格", locale == UiLocalization.Chinese ? "" : report);
                    if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_M36_EVIDENCE") is { } evidence)
                    { Directory.CreateDirectory(evidence); File.Copy(files.Path(locale + ".md"), Path.Combine(evidence, "report-" + locale + ".md"), true); }
                    Capture(window, "m36-review-wide-" + locale);
                    Assert.Equal(original, window.CurrentReview!.Study.SourceFingerprint);
                }
                Assert.Equal(0, display.RayCount);
                Click(window, "AutonomousReviewOpenData"); await window.PendingOperation;
                Assert.Equal(original, window.CurrentReview!.Study.SourceFingerprint);
                var grid = Find<DataGrid>(window, "AutonomousReviewCandidates");
                grid.SelectedItem = grid.ItemsSource!.Cast<object>().Single(row => (string)row.GetType().GetProperty("Id")!.GetValue(row)! == "cold:final");
                Click(window, "AutonomousReviewExport"); await window.PendingOperation;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.Equal(0, display.RayCount);
                window.Width = 480; window.UpdateLayout();
                var reportButton = Find<Button>(window, "AutonomousReviewReport"); reportButton.BringIntoView();
                await Task.Delay(30); window.UpdateLayout();
                var page = reportButton.GetVisualAncestors().OfType<ScrollViewer>().Last();
                var pageContent = (Control)page.Content!;
                var position = reportButton.TranslatePoint(default, pageContent)!.Value;
                page.Offset = new Vector(0, Math.Clamp(position.Y - 80, 0, Math.Max(0, page.Extent.Height - page.Viewport.Height)));
                window.UpdateLayout();
                Capture(window, "m36-review-narrow-ja-JP");
                var source = window.CurrentReview!.Study.Preparation!; var before = ContentFingerprint.Compute(source);
                Click(window, "AutonomousReviewStructure"); await window.PendingOperation; await window.StructurePendingLoad;
                Assert.Empty(Find<TextBlock>(window, "AutonomousError").Text!);
                Assert.NotNull(Find<TopologyStructurePlot>(window, "AutonomousStructurePlot").Data);
                Assert.Equal(before, ContentFingerprint.Compute(window.CurrentReview!.Study.Preparation!));
                Assert.InRange(display.RayCount, 1, 50_000);
                Click(window, "AutonomousNew"); await window.PendingOperation;
                tabs.SelectedItem = Find<TabItem>(window, "AutonomousReviewTab"); Assert.Null(window.CurrentReview);
                Assert.Null(window.CurrentCheckpoint);
            }
            finally { window.RequestSafeClose(); await window.PendingOperation; await window.StructurePendingLoad; UiLocalization.Apply(language); }
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(3));
    }
}
