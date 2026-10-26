using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ReportDocumentTitleTests
{
    [Theory]
    [InlineData("Prescription Report", "表面数据报告")]
    [InlineData("System Data Report", "系统数据报告")]
    [InlineData("Classified Data Report", "分类数据报告")]
    [InlineData("First Order", "系统数据摘要")]
    [InlineData("Cardinal Points Data", "基面数据")]
    public void ReportTitlesUseTheChineseDisplayName(string key, string title)
    {
        using var application = WorkbenchApplication.Create();
        using var panels = CreateManager(application);
        panels.ShowAnalysis(key);
        var document = panels.Factory.OpenDocuments().Single(item => item.Id == $"analysis:{key}");
        Assert.Equal(title, document.Title);
        Assert.Equal(key, Assert.IsType<AnalysisPanel>(document.Context).AnalysisKey);
    }

    [Fact]
    public async Task SavedEnglishReportAndCloneTitlesRestoreWithCanonicalChineseSources()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var application = WorkbenchApplication.Create();
            using var panels = CreateManager(application);
            panels.ShowAnalysis("System Data Report");
            panels.CloneActiveAnalysis();
            foreach (var document in panels.Factory.OpenDocuments().Where(document => document.Id.StartsWith("analysis:", StringComparison.Ordinal)))
            {
                document.Title = document.Title.Replace("系统数据报告", "System Data Report", StringComparison.Ordinal);
                panels.Factory.UpdateDescriptor(panels.Factory.Descriptor(document.Id)! with { Title = document.Title });
            }
            await panels.SaveLayoutSlotAsync(1);
            await panels.LoadLayoutSlotAsync(1);
            var reports = panels.Factory.OpenDocuments().Where(document => document.Id.StartsWith("analysis:", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, reports.Length);
            Assert.Contains(reports, document => document.Title == "系统数据报告");
            Assert.Contains(reports, document => document.Title == "系统数据报告（副本）");
            Assert.All(reports, document => Assert.Equal("System Data Report", Assert.IsType<AnalysisPanel>(document.Context).AnalysisKey));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("My report")]
    [InlineData("System Data Report（副本） notes")]
    public void CustomAnalysisTitlesKeepTheirOriginalText(string title)
    {
        using var application = WorkbenchApplication.Create();
        using var panels = CreateManager(application);
        var document = panels.Factory.OpenDocument(new WorkspaceDocumentDescriptor(
            "analysis:custom", WorkspaceDocumentTypes.Analysis, title, "System Data Report"));
        Assert.Equal(title, document.Title);
        Assert.Equal("System Data Report", Assert.IsType<AnalysisPanel>(document.Context).AnalysisKey);
    }

    private static PanelManager CreateManager(WorkbenchApplication application) => new(application, new AppSettings(),
        new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "report-title-" + Guid.NewGuid().ToString("N"))));
}
