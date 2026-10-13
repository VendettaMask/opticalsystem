using System.Collections.ObjectModel;

namespace OptilandWorkbench.Application.Contracts;

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
}
