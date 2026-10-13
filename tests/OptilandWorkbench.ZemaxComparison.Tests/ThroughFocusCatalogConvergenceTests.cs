namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class ThroughFocusCatalogConvergenceTests
{
    [Fact]
    public void RetiredThroughFocusKeyReplaysAndSelectsTheCanonicalContractOnce()
    {
        var canonical = AnalysisComparisonRegistry.Get("Fourier Through Focus MTF");
        Assert.Same(canonical, AnalysisComparisonRegistry.Get("Through Focus MTF"));
        Assert.DoesNotContain(AnalysisComparisonRegistry.Entries, entry => entry.CanonicalAnalysisKey == "Through Focus MTF");
        Assert.Same(canonical, Assert.Single(AnalysisComparisonRegistry.Select(["Through Focus MTF"])));
        Assert.Same(canonical, Assert.Single(AnalysisComparisonRegistry.Select(["Through Focus MTF", "Fourier Through Focus MTF"])));
        Assert.Equal(AnalysisComparisonRegistry.Entries, AnalysisComparisonRegistry.Select([]));
        Assert.Throws<ArgumentException>(() => AnalysisComparisonRegistry.Select(["离焦 MTF"]));
        Assert.Throws<ArgumentException>(() => AnalysisComparisonRegistry.Select(["unknown"]));
    }
}
