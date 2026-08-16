using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

public static class StockLensCatalogPolicy
{
    private static readonly IReadOnlyDictionary<string, string> Catalogs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["THORLABS"] = "Thorlabs",
            ["EDMUND OPTICS"] = "Edmund Optics",
            ["DAHENG OPTICS"] = "Daheng Optics",
            ["NEWPORT CORP"] = "Newport",
            ["SIGMA KOKI"] = "Sigma Koki"
        };

    public static IReadOnlyList<string> Manufacturers { get; } = Catalogs.Values.ToArray();

    public static bool IncludesCatalog(string catalogKey) => Catalogs.ContainsKey(catalogKey);

    public static bool IncludesManufacturer(string manufacturer) =>
        Manufacturers.Contains(manufacturer, StringComparer.OrdinalIgnoreCase);
}

public static class StockLensMatcher
{
    public static IReadOnlyList<StockLensMatchResultDto> Match(
        IEnumerable<CommercialLensEntryDto> catalog,
        StockLensMatchRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);
        if (!double.IsFinite(request.TargetEffectiveFocalLength)
            || Math.Abs(request.TargetEffectiveFocalLength) <= 1e-12
            || !double.IsFinite(request.TargetEntrancePupilDiameter)
            || request.TargetEntrancePupilDiameter <= 0)
        {
            return Array.Empty<StockLensMatchResultDto>();
        }

        var manufacturers = request.Manufacturers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eflTolerance = Math.Max(0, request.EffectiveFocalLengthTolerancePercent);
        var epdTolerance = Math.Max(0, request.EntrancePupilDiameterTolerancePercent);
        var targetEflMagnitude = Math.Abs(request.TargetEffectiveFocalLength);
        var maximumResults = Math.Clamp(request.MaximumResults, 1, 100);

        return catalog
            .Where(entry => StockLensCatalogPolicy.IncludesManufacturer(entry.Manufacturer))
            .Where(entry => manufacturers.Count == 0 || manufacturers.Contains(entry.Manufacturer))
            .Where(entry => double.IsFinite(entry.EffectiveFocalLength)
                && Math.Abs(entry.EffectiveFocalLength) > 1e-12)
            .Where(entry => double.IsFinite(entry.EntrancePupilDiameter)
                && entry.EntrancePupilDiameter > 0)
            .Select(entry =>
            {
                var eflDeviation = 100
                    * Math.Abs(Math.Abs(entry.EffectiveFocalLength) - targetEflMagnitude)
                    / targetEflMagnitude;
                var epdDeviation = 100
                    * Math.Abs(entry.EntrancePupilDiameter - request.TargetEntrancePupilDiameter)
                    / request.TargetEntrancePupilDiameter;
                var directionMatches = !request.MatchPowerDirection
                    || Math.Sign(entry.EffectiveFocalLength) == Math.Sign(request.TargetEffectiveFocalLength);
                var shapeMatches = !request.MatchShape
                    || string.IsNullOrWhiteSpace(request.TargetShapeCode)
                    || request.TargetShapeCode == "?"
                    || entry.ShapeCode.Equals(request.TargetShapeCode, StringComparison.OrdinalIgnoreCase);
                var normalizedEfl = eflTolerance <= 0
                    ? (eflDeviation <= 1e-12 ? 0 : double.PositiveInfinity)
                    : eflDeviation / eflTolerance;
                var normalizedEpd = epdTolerance <= 0
                    ? (epdDeviation <= 1e-12 ? 0 : double.PositiveInfinity)
                    : epdDeviation / epdTolerance;
                return new StockLensMatchResultDto(
                    entry,
                    eflDeviation,
                    epdDeviation,
                    Math.Sqrt((normalizedEfl * normalizedEfl) + (normalizedEpd * normalizedEpd)),
                    directionMatches,
                    shapeMatches);
            })
            .Where(result => result.EffectiveFocalLengthDeviationPercent <= eflTolerance)
            .Where(result => result.EntrancePupilDiameterDeviationPercent <= epdTolerance)
            .Where(result => result.DirectionMatches && result.ShapeMatches)
            .OrderBy(result => result.NormalizedScore)
            .ThenBy(result => result.EffectiveFocalLengthDeviationPercent)
            .ThenBy(result => result.EntrancePupilDiameterDeviationPercent)
            .ThenBy(result => result.Entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Entry.PartNumber, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }
}
