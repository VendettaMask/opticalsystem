using System.Collections.ObjectModel;

namespace OpticalSystemDesign.Application.Contracts;

/// <summary>Immutable, non-localized geometry parameters captured with a prescription row.</summary>
public sealed record SurfaceGeometryDefinitionDto
{
    public SurfaceGeometryDefinitionDto(string kind, IReadOnlyDictionary<string, double> numbers)
    {
        Kind = kind;
        Numbers = new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(numbers));
    }

    public string Kind { get; }
    public IReadOnlyDictionary<string, double> Numbers { get; }

    public bool Equals(SurfaceGeometryDefinitionDto? other) => ReferenceEquals(this, other)
        || other is not null && StringComparer.Ordinal.Equals(Kind, other.Kind)
        && Numbers.Count == other.Numbers.Count
        && Numbers.All(pair => other.Numbers.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind, StringComparer.Ordinal);
        foreach (var pair in Numbers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value);
        }
        return hash.ToHashCode();
    }
}
