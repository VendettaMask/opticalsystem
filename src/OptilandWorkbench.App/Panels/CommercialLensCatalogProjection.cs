using System.Globalization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;

namespace OptilandWorkbench.App.Panels;

internal sealed record CommercialLensCatalogFilter(
    string? Vendor = null,
    string? Query = null,
    bool UseEffectiveFocalLength = false,
    double EffectiveFocalLengthMinimum = double.NegativeInfinity,
    double EffectiveFocalLengthMaximum = double.PositiveInfinity,
    bool UseEntrancePupilDiameter = false,
    double EntrancePupilDiameterMinimum = double.NegativeInfinity,
    double EntrancePupilDiameterMaximum = double.PositiveInfinity,
    string? ShapeCode = null,
    string? SurfaceType = null,
    string? ElementCount = null);

internal sealed record CommercialLensCatalogFilterResult(
    IReadOnlyList<CommercialLensRow> VisibleRows,
    int FilteredCount,
    int TotalCount,
    int VendorCount)
{
    public string CountText => FilteredCount > VisibleRows.Count
        ? $"显示前 {VisibleRows.Count} / {FilteredCount} 个匹配 · 共 {TotalCount} 项 · {VendorCount} 家厂商"
        : $"{VisibleRows.Count} / {TotalCount} 项 · {VendorCount} 家厂商";
}

internal static class CommercialLensCatalogProjection
{
    public const int MaximumVisibleRows = 500;

    public static CommercialLensCatalogFilterResult Filter(
        IEnumerable<CommercialLensEntryDto> catalog,
        CommercialLensCatalogFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(filter);

        var entries = catalog as IReadOnlyList<CommercialLensEntryDto> ?? catalog.ToArray();
        var vendor = filter.Vendor?.Trim();
        var query = filter.Query?.Trim() ?? string.Empty;
        var shapeCode = filter.ShapeCode?.Trim();
        var surfaceType = filter.SurfaceType?.Trim();
        var elementCount = filter.ElementCount?.Trim();
        var filtered = entries
            .Where(entry => string.IsNullOrEmpty(vendor)
                || vendor == "全部厂商"
                || entry.Manufacturer.Equals(vendor, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrEmpty(query)
                || entry.PartNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.LensType.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(entry => !filter.UseEffectiveFocalLength
                || entry.EffectiveFocalLength >= filter.EffectiveFocalLengthMinimum
                && entry.EffectiveFocalLength <= filter.EffectiveFocalLengthMaximum)
            .Where(entry => !filter.UseEntrancePupilDiameter
                || entry.EntrancePupilDiameter >= filter.EntrancePupilDiameterMinimum
                && entry.EntrancePupilDiameter <= filter.EntrancePupilDiameterMaximum)
            .Where(entry => string.IsNullOrEmpty(shapeCode)
                || shapeCode == "全部形状"
                || entry.ShapeCode.Equals(shapeCode, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrEmpty(surfaceType)
                || surfaceType == "全部曲面"
                || entry.SurfaceType.Equals(surfaceType, StringComparison.OrdinalIgnoreCase))
            .Where(entry => elementCount switch
            {
                "1" => entry.ElementCount == 1,
                "2" => entry.ElementCount == 2,
                "3+" => entry.ElementCount >= 3,
                _ => true
            })
            .ToArray();

        var visible = filtered
            .Take(MaximumVisibleRows)
            .Select(entry => new CommercialLensRow(entry))
            .ToArray();
        var vendorCount = entries
            .Select(entry => entry.Manufacturer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new CommercialLensCatalogFilterResult(
            visible,
            filtered.Length,
            entries.Count,
            vendorCount);
    }
}

internal sealed record CommercialLensRow(CommercialLensEntryDto Entry)
{
    public string Manufacturer => Entry.Manufacturer;

    public string PartNumber => Entry.PartNumber;

    public string Name => Entry.Name;

    public string EffectiveFocalLength => Number(Entry.EffectiveFocalLength);

    public string EntrancePupilDiameter => Number(Entry.EntrancePupilDiameter);

    public string Classification => $"{Entry.ShapeCode}/{Entry.SurfaceType}";

    public int ElementCount => Entry.ElementCount;

    public string ModelAvailability => string.IsNullOrWhiteSpace(Entry.NativePath) ? "仅目录" : "可载入";

    private static string Number(double value) =>
        double.IsFinite(value) && value != 0
            ? NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture)
            : "—";
}
