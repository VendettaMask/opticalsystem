using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

public sealed class StockLensMatcherTests
{
    [Fact]
    public void MatchFiltersDirectionToleranceVendorAndRanksByNormalizedDeviation()
    {
        var catalog = new[]
        {
            Entry("best", "Thorlabs", 101, 25),
            Entry("second", "Newport", 110, 24),
            Entry("negative", "Edmund Optics", -100, 25),
            Entry("unsupported", "Anteryon", 100, 25),
            Entry("outside", "Sigma Koki", 140, 25)
        };
        var request = new StockLensMatchRequestDto(
            100,
            25,
            StockLensCatalogPolicy.Manufacturers,
            5,
            25,
            25,
            MatchShape: false,
            TargetShapeCode: "?",
            MatchPowerDirection: true);

        var matches = StockLensMatcher.Match(catalog, request);

        Assert.Equal(new[] { "best", "second" }, matches.Select(match => match.Entry.Id));
        Assert.Equal(1, matches[0].EffectiveFocalLengthDeviationPercent, precision: 8);
        Assert.True(matches[0].NormalizedScore < matches[1].NormalizedScore);
    }

    [Fact]
    public void PolicyContainsOnlyRequestedFiveManufacturers()
    {
        Assert.Equal(
            new[] { "Thorlabs", "Edmund Optics", "Daheng Optics", "Newport", "Sigma Koki" },
            StockLensCatalogPolicy.Manufacturers);
        Assert.True(StockLensCatalogPolicy.IncludesCatalog("DaHeng Optics"));
        Assert.False(StockLensCatalogPolicy.IncludesCatalog("CVI MELLES GRIOT"));
    }

    private static CommercialLensEntryDto Entry(
        string id,
        string manufacturer,
        double effectiveFocalLength,
        double entrancePupilDiameter) => new(
        id,
        manufacturer,
        id,
        id,
        "本机 Zemax Stockcat",
        "https://example.com",
        string.Empty,
        "目录镜头",
        "B",
        "S",
        2,
        effectiveFocalLength,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        "仅目录",
        null,
        "测试",
        DateTimeOffset.MinValue,
        entrancePupilDiameter);
}
