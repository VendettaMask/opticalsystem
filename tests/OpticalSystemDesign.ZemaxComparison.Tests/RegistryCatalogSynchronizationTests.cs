using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class RegistryCatalogSynchronizationTests
{
    [Fact]
    public void EveryCurrentCoreAnalysisHasAnExplicitValidationDisposition()
    {
        Assert.Equal(new AnalysisCatalog(new Optic()).Names.Order(),
            AnalysisComparisonRegistry.Entries.Select(e => e.CanonicalAnalysisKey).Order());
        Assert.Equal(SupportStatus.AdapterNotImplemented, AnalysisComparisonRegistry.Get("Interferogram").Support);
    }

    [Fact]
    public void HistoricalCoreKeysDoNotRestoreRetiredDesktopAnalyses()
    {
        Assert.Equal("Zernike", AnalysisComparisonRegistry.Get("Zernike").CanonicalAnalysisKey);
        Assert.True(WorkbenchAnalysisCatalog.IsRetiredAnalysis("Zernike"));
        Assert.False(WorkbenchAnalysisCatalog.TryGetDescriptor("Zernike", out _));
        Assert.Equal(SupportStatus.Comparable, AnalysisComparisonRegistry.Get("MTF").Support);
    }
}
