using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

public sealed class CommercialLensCatalogPanelTests
{
    [Fact]
    public void SelectingVendorImmediatelyFiltersVisibleCatalogRows()
    {
        var result = CommercialLensCatalogProjection.Filter(
            new[]
            {
                Entry("thorlabs", "Thorlabs", "AC254-100-A"),
                Entry("newport", "Newport", "KPX100")
            },
            new CommercialLensCatalogFilter(Vendor: "Newport"));

        var row = Assert.Single(result.VisibleRows);
        Assert.Equal("Newport", row.Manufacturer);
        Assert.Equal(1, result.FilteredCount);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public void CommercialCatalogBindsOnlyFirstVisiblePageAfterBackgroundLoad()
    {
        var result = CommercialLensCatalogProjection.Filter(
            Enumerable.Range(0, 510)
                .Select(index => Entry($"thorlabs-{index}", "Thorlabs", $"AC{index:0000}")),
            new CommercialLensCatalogFilter());

        Assert.Equal(CommercialLensCatalogProjection.MaximumVisibleRows, result.VisibleRows.Count);
        Assert.Equal(510, result.FilteredCount);
        Assert.Equal(510, result.TotalCount);
        Assert.Contains("显示前 500 / 510", result.CountText);
    }

    [Fact]
    public void StockLensMatchingBuildsRankedRowsFromCurrentFirstOrderTarget()
    {
        var request = new StockLensMatchRequestDto(
            TargetEffectiveFocalLength: 25,
            TargetEntrancePupilDiameter: 8,
            Manufacturers: Array.Empty<string>(),
            MaximumResults: 5,
            EffectiveFocalLengthTolerancePercent: 25,
            EntrancePupilDiameterTolerancePercent: 25,
            MatchShape: false,
            TargetShapeCode: "?",
            MatchPowerDirection: true);

        var matches = StockLensMatcher.Match(
            new[]
            {
                Entry("thorlabs", "Thorlabs", "AC254-100-A"),
                Entry("newport", "Newport", "KPX100")
            },
            request);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.True(match.DirectionMatches));
    }

    private static CommercialLensEntryDto Entry(string id, string manufacturer, string partNumber) => new(
        id,
        manufacturer,
        partNumber,
        partNumber,
        "同步库存目录",
        "https://example.com",
        string.Empty,
        "目录镜头",
        "B",
        "S",
        2,
        25,
        10,
        9,
        20,
        0.1,
        486,
        656,
        0,
        0,
        "仅目录",
        null,
        "测试目录头",
        new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
        8);

}
