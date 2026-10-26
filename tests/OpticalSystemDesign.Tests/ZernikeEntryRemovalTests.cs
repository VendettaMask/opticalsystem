using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ZernikeEntryRemovalTests
{
    [Fact]
    public void DesktopCatalogAndSearchKeepOnlyExplicitZernikeAnalyses()
    {
        using var application = WorkbenchApplication.Create();
        Assert.DoesNotContain("Zernike 系数", application.Analyses.AnalysisNames);
        Assert.DoesNotContain(WorkbenchAnalysisCatalog.Descriptors, descriptor => descriptor.CanonicalKey == "Zernike");
        Assert.DoesNotContain(WorkbenchAnalysisCatalog.AllRibbonCommands, command => command.Id == "analysis-zernike");
        Assert.False(WorkbenchAnalysisCatalog.TryGetDescriptor("Zernike", out _));
        Assert.False(WorkbenchAnalysisCatalog.TryGetDescriptor("Zernike 系数", out _));
        foreach (var (key, kind) in new[]
        {
            ("Zernike Fringe", AnalysisPresentationKind.ZernikeFringe),
            ("Zernike Standard", AnalysisPresentationKind.ZernikeStandard),
            ("Zernike Annular", AnalysisPresentationKind.ZernikeAnnular)
        })
        {
            Assert.True(WorkbenchAnalysisCatalog.TryGetDescriptor(key, out var descriptor));
            Assert.Equal(kind, descriptor.PresentationKind);
            Assert.Contains(descriptor.DisplayName, application.Analyses.AnalysisNames);
            Assert.Contains(descriptor.RibbonCommand!, WorkbenchAnalysisCatalog.AllRibbonCommands);
        }
        Assert.Contains("波前图", application.Analyses.AnalysisNames);
        Assert.Contains("Zernike系数 vs. 视场", application.Analyses.AnalysisNames);
    }

    [Theory]
    [InlineData("Zernike")]
    [InlineData("Zernike 系数")]
    public async Task RetiredKeysCannotReopenTheHeatmapThroughSavedRequests(string key)
    {
        using var application = WorkbenchApplication.Create();
        Assert.False(WorkbenchAnalysisCatalog.IsAvailableInMode(key, OpticalWorkbenchMode.Sequential));
        Assert.False(WorkbenchAnalysisCatalog.IsAvailableInMode(key, OpticalWorkbenchMode.NonSequential));
        Assert.Throws<InvalidOperationException>(() => application.Analyses.GetParameters(key));
        Assert.Throws<InvalidOperationException>(() => application.Analyses.MergeSettings(key, null));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => application.Analyses.RunAsync(
            new(Guid.NewGuid(), 1, key, new Dictionary<string, string>())));
        Assert.Contains("Fringe、Standard 或 Annular", error.Message);
    }
}
