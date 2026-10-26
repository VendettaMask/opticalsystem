using System.Collections.ObjectModel;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Multiconfig;

public sealed class OpticalConfigurationCollection : Collection<Optic>, IReadOnlyList<Optic>
{
    protected override void InsertItem(int index, Optic item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Count >= StarOptProjectStore.MaximumConfigurationCount)
            throw new InvalidOperationException("Configuration count exceeds the native project save limit.");
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, Optic item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }

    public void AddRange(IEnumerable<Optic> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var bounded = items.Take(StarOptProjectStore.MaximumConfigurationCount - Count + 1).ToArray();
        if (bounded.Any(item => item is null) || Count + bounded.Length > StarOptProjectStore.MaximumConfigurationCount)
            throw new ArgumentException("Invalid optical configuration collection.", nameof(items));
        foreach (var item in bounded) Add(item);
    }
}
