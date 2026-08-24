using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Multiconfig;

public sealed record MultiConfigurationLinkOverride(
    int ConfigurationIndex,
    int SurfaceNumber,
    string Property);

public sealed class MultiConfiguration
{
    private readonly HashSet<(int Config, int Surface, string Property)> _brokenLinks = new();

    public MultiConfiguration(Optic baseOptic)
        : this(new[] { baseOptic })
    {
    }

    public MultiConfiguration(
        IEnumerable<Optic> configurations,
        IEnumerable<MultiConfigurationLinkOverride>? brokenLinks = null)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        Configurations.AddRange(configurations.Select(configuration =>
            Optic.FromSnapshot(configuration.ToSnapshot())));
        if (Configurations.Count == 0)
        {
            throw new ArgumentException("At least one optical configuration is required.", nameof(configurations));
        }

        if (brokenLinks is null)
        {
            InferBrokenLinks();
        }
        else
        {
            foreach (var link in brokenLinks)
            {
                ValidateLink(link);
                _brokenLinks.Add((
                    link.ConfigurationIndex,
                    link.SurfaceNumber,
                    NormalizeProperty(link.Property)));
            }
        }
    }

    public List<Optic> Configurations { get; } = new();

    public IReadOnlyList<MultiConfigurationLinkOverride> BrokenLinks => _brokenLinks
        .OrderBy(link => link.Config)
        .ThenBy(link => link.Surface)
        .ThenBy(link => link.Property, StringComparer.Ordinal)
        .Select(link => new MultiConfigurationLinkOverride(link.Config, link.Surface, link.Property))
        .ToArray();

    public int AddConfiguration(int sourceConfigIndex = 0)
    {
        if (sourceConfigIndex < 0 || sourceConfigIndex >= Configurations.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceConfigIndex));
        }

        var source = Configurations[sourceConfigIndex];
        Configurations.Add(Optic.FromSnapshot(source.ToSnapshot()));
        var addedIndex = Configurations.Count - 1;
        foreach (var link in _brokenLinks.Where(link => link.Config == sourceConfigIndex).ToArray())
        {
            _brokenLinks.Add((addedIndex, link.Surface, link.Property));
        }

        return addedIndex;
    }

    public int AddSurfaceBeforeImage()
    {
        ValidateCompatibleSurfaceStructures();
        var insertedSurfaceNumber = Math.Max(0, Configurations[0].SurfaceGroup.Items.Count - 1);
        foreach (var configuration in Configurations)
        {
            configuration.Pickups.InsertSurface(insertedSurfaceNumber);
            configuration.SurfaceGroup.AddDefaultSurface();
        }

        RemapBrokenLinks(surfaceNumber =>
            surfaceNumber >= insertedSurfaceNumber ? surfaceNumber + 1 : surfaceNumber);
        return insertedSurfaceNumber;
    }

    public void RemoveSurface(int surfaceNumber)
    {
        ValidateCompatibleSurfaceStructures();
        var surfaceCount = Configurations[0].SurfaceGroup.Items.Count;
        if (surfaceCount <= 2 || surfaceNumber <= 0 || surfaceNumber >= surfaceCount - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceNumber),
                "Object and image surfaces cannot be removed, and at least two surfaces are required.");
        }

        foreach (var configuration in Configurations)
        {
            var surface = FindSurface(configuration, surfaceNumber);
            configuration.Pickups.RemoveSurface(surfaceNumber);
            configuration.SurfaceGroup.Remove(surface);
        }

        RemapBrokenLinks(surfaceNumberToMap => surfaceNumberToMap switch
        {
            var value when value == surfaceNumber => null,
            var value when value > surfaceNumber => value - 1,
            var value => value
        });
    }

    public void SetRadius(int configIndex, int surfaceNumber, double value)
    {
        SetProperty(configIndex, surfaceNumber, "radius", value);
    }

    public void SetThickness(int configIndex, int surfaceNumber, double value)
    {
        SetProperty(configIndex, surfaceNumber, "thickness", value);
    }

    public void SetProperty(int configIndex, int surfaceNumber, string property, double value)
    {
        var normalizedProperty = NormalizeProperty(property);
        var surface = Configurations[configIndex].SurfaceGroup.Items.First(item => item.Number == surfaceNumber);
        if (configIndex != 0)
        {
            _brokenLinks.Add((configIndex, surfaceNumber, normalizedProperty));
        }

        switch (normalizedProperty)
        {
            case "radius":
                surface.Radius = value;
                break;
            case "thickness":
                surface.Thickness = value;
                Configurations[configIndex].SurfaceGroup.Renumber();
                break;
            case "conic":
                surface.Conic = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    public void UpdateLinkState(int configIndex, int surfaceNumber, string property)
    {
        if (configIndex <= 0 || configIndex >= Configurations.Count)
        {
            return;
        }

        var normalizedProperty = NormalizeProperty(property);
        var source = FindSurface(Configurations[0], surfaceNumber);
        var target = FindSurface(Configurations[configIndex], surfaceNumber);
        var link = (configIndex, surfaceNumber, normalizedProperty);
        if (PropertyEquals(source, target, normalizedProperty))
        {
            _brokenLinks.Remove(link);
        }
        else
        {
            _brokenLinks.Add(link);
        }
    }

    public void PropagateBaseProperty(int surfaceNumber, string property)
    {
        var normalizedProperty = NormalizeProperty(property);
        var source = FindSurface(Configurations[0], surfaceNumber);
        for (var config = 1; config < Configurations.Count; config++)
        {
            if (_brokenLinks.Contains((config, surfaceNumber, normalizedProperty)))
            {
                continue;
            }

            var target = FindSurface(Configurations[config], surfaceNumber);
            CopyProperty(source, target, normalizedProperty);
            Configurations[config].SurfaceGroup.Renumber();
        }
    }

    public void PropagateBaseLinks()
    {
        ValidateCompatibleSurfaceStructures();
        var baseOptic = Configurations[0];
        for (var config = 1; config < Configurations.Count; config++)
        {
            for (var index = 0; index < baseOptic.SurfaceGroup.Items.Count; index++)
            {
                var source = baseOptic.SurfaceGroup.Items[index];
                var target = Configurations[config].SurfaceGroup.Items[index];
                var surfaceNumber = source.Number;
                if (!_brokenLinks.Contains((config, surfaceNumber, "radius")))
                {
                    target.Radius = source.Radius;
                }

                if (!_brokenLinks.Contains((config, surfaceNumber, "conic")))
                {
                    target.Conic = source.Conic;
                }

                if (index < baseOptic.SurfaceGroup.Items.Count - 1 && !_brokenLinks.Contains((config, surfaceNumber, "thickness")))
                {
                    target.Thickness = source.Thickness;
                }

                if (!_brokenLinks.Contains((config, surfaceNumber, "material")))
                {
                    CopyMaterial(source, target);
                }
            }

            Configurations[config].SurfaceGroup.Renumber();
        }
    }

    private static OpticalSurface FindSurface(Optic optic, int surfaceNumber) =>
        optic.SurfaceGroup.Items.SingleOrDefault(surface => surface.Number == surfaceNumber)
        ?? throw new InvalidOperationException($"Configuration does not contain surface {surfaceNumber}.");

    private void ValidateCompatibleSurfaceStructures()
    {
        var expectedNumbers = Configurations[0].SurfaceGroup.Items
            .Select(surface => surface.Number)
            .ToArray();
        for (var configIndex = 1; configIndex < Configurations.Count; configIndex++)
        {
            var actualNumbers = Configurations[configIndex].SurfaceGroup.Items
                .Select(surface => surface.Number)
                .ToArray();
            if (!expectedNumbers.SequenceEqual(actualNumbers))
            {
                throw new InvalidOperationException(
                    $"Configuration {configIndex} has an incompatible surface structure.");
            }
        }
    }

    private void RemapBrokenLinks(Func<int, int?> mapSurfaceNumber)
    {
        var remapped = _brokenLinks
            .Select(link => (Link: link, Surface: mapSurfaceNumber(link.Surface)))
            .Where(item => item.Surface.HasValue)
            .Select(item => (item.Link.Config, item.Surface!.Value, item.Link.Property))
            .ToArray();
        _brokenLinks.Clear();
        foreach (var link in remapped)
        {
            _brokenLinks.Add(link);
        }
    }

    private static void CopyProperty(OpticalSurface source, OpticalSurface target, string property)
    {
        switch (property)
        {
            case "radius":
                target.Radius = source.Radius;
                break;
            case "thickness":
                target.Thickness = source.Thickness;
                break;
            case "conic":
                target.Conic = source.Conic;
                break;
            case "material":
                CopyMaterial(source, target);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }

    private static void CopyMaterial(OpticalSurface source, OpticalSurface target)
    {
        target.MaterialAfter = source.MaterialAfter.Clone();
        target.IsReflective = source.IsReflective;
        target.Material = source.Material;
    }

    private static bool PropertyEquals(OpticalSurface source, OpticalSurface target, string property) => property switch
    {
        "radius" => source.Radius.Equals(target.Radius),
        "thickness" => source.Thickness.Equals(target.Thickness),
        "conic" => source.Conic.Equals(target.Conic),
        "material" => source.Material.Equals(target.Material, StringComparison.OrdinalIgnoreCase)
            && source.MaterialAfter.Name.Equals(target.MaterialAfter.Name, StringComparison.OrdinalIgnoreCase)
            && source.IsReflective == target.IsReflective,
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };

    private static string NormalizeProperty(string property)
    {
        var normalized = property?.Trim().ToLowerInvariant();
        return normalized is "radius" or "thickness" or "conic" or "material"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(property));
    }

    private void ValidateLink(MultiConfigurationLinkOverride link)
    {
        if (link.ConfigurationIndex <= 0 || link.ConfigurationIndex >= Configurations.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(link), "The linked configuration index is invalid.");
        }

        _ = FindSurface(Configurations[0], link.SurfaceNumber);
        _ = FindSurface(Configurations[link.ConfigurationIndex], link.SurfaceNumber);
        _ = NormalizeProperty(link.Property);
    }

    private void InferBrokenLinks()
    {
        var baseOptic = Configurations[0];
        for (var configIndex = 1; configIndex < Configurations.Count; configIndex++)
        {
            foreach (var target in Configurations[configIndex].SurfaceGroup.Items)
            {
                var source = baseOptic.SurfaceGroup.Items.FirstOrDefault(
                    surface => surface.Number == target.Number);
                if (source is null)
                {
                    continue;
                }

                foreach (var property in new[] { "radius", "thickness", "conic", "material" })
                {
                    if (!PropertyEquals(source, target, property))
                    {
                        _brokenLinks.Add((configIndex, target.Number, property));
                    }
                }
            }
        }
    }
}
