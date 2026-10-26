namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>Immutable per-operation lookup. W numbers are one-based; zero means unknown.</summary>
public sealed class NonSequentialWavelengthIndex
{
    public const double ToleranceNanometers = 1e-9;
    private readonly double[] _original;
    private readonly (double Nanometers, int Number)[] _sorted;

    public NonSequentialWavelengthIndex(IEnumerable<NonSequentialWavelength> wavelengths)
    {
        ArgumentNullException.ThrowIfNull(wavelengths);
        _original = wavelengths.Select(item => item.Nanometers).ToArray();
        _sorted = _original.Select((value, index) => (Nanometers: value, Number: index + 1))
            .OrderBy(item => item.Nanometers).ToArray();
    }

    /// <summary>First matching document entry, or -1. Display adapters may exclude the tolerance boundary.</summary>
    public int FindIndex(double nanometers, bool includeToleranceBoundary = true)
    {
        for (var index = 0; index < _original.Length; index++)
        {
            var difference = Math.Abs(_original[index] - nanometers);
            if (includeToleranceBoundary ? difference <= ToleranceNanometers : difference < ToleranceNanometers)
                return index;
        }
        return -1;
    }

    public int FindNumber(double nanometers) => FindIndex(nanometers) + 1;

    public int FindNumber(NonSequentialRayBranch branch) => FindNumber(
        branch.WavelengthNanometers > 0 ? branch.WavelengthNanometers
            : branch.Segments.FirstOrDefault()?.WavelengthNanometers ?? 0);

    /// <summary>Retains database reconstruction's nearest-match rule and original W numbering.</summary>
    public int FindNearestNumber(double nanometers)
    {
        if (_sorted.Length == 0 || !double.IsFinite(nanometers)) return 0;
        var low = 0;
        var high = _sorted.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = _sorted[middle].Nanometers.CompareTo(nanometers);
            if (comparison == 0) return _sorted[middle].Number;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        var nearest = low switch
        {
            0 => _sorted[0],
            _ when low == _sorted.Length => _sorted[^1],
            _ => Math.Abs(_sorted[low - 1].Nanometers - nanometers) <= Math.Abs(_sorted[low].Nanometers - nanometers)
                ? _sorted[low - 1] : _sorted[low]
        };
        return Math.Abs(nearest.Nanometers - nanometers) <= ToleranceNanometers ? nearest.Number : 0;
    }
}
