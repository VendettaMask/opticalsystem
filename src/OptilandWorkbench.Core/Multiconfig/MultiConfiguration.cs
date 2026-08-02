using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Core.Multiconfig;

public sealed class MultiConfiguration
{
    private readonly HashSet<(int Config, int Surface, string Property)> _brokenLinks = new();

    public MultiConfiguration(Optic baseOptic)
        : this(new[] { baseOptic })
    {
    }

    public MultiConfiguration(IEnumerable<Optic> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        Configurations.AddRange(configurations.Select(configuration =>
            Optic.FromSnapshot(configuration.ToSnapshot())));
        if (Configurations.Count == 0)
        {
            throw new ArgumentException("At least one optical configuration is required.", nameof(configurations));
        }
    }

    public List<Optic> Configurations { get; } = new();

    public int AddConfiguration(int sourceConfigIndex = 0)
    {
        var source = Configurations[sourceConfigIndex];
        Configurations.Add(Optic.FromSnapshot(source.ToSnapshot()));
        return Configurations.Count - 1;
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
        var surface = Configurations[configIndex].SurfaceGroup.Items.First(item => item.Number == surfaceNumber);
        if (configIndex != 0)
        {
            _brokenLinks.Add((configIndex, surfaceNumber, property));
        }

        switch (property.ToLowerInvariant())
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
        }
    }

    public void PropagateBaseLinks()
    {
        var baseOptic = Configurations[0];
        for (var config = 1; config < Configurations.Count; config++)
        {
            for (var index = 0; index < baseOptic.SurfaceGroup.Items.Count; index++)
            {
                var source = baseOptic.SurfaceGroup.Items[index];
                var target = Configurations[config].SurfaceGroup.Items[index];
                if (!_brokenLinks.Contains((config, index, "radius")))
                {
                    target.Radius = source.Radius;
                }

                if (!_brokenLinks.Contains((config, index, "conic")))
                {
                    target.Conic = source.Conic;
                }

                if (index < baseOptic.SurfaceGroup.Items.Count - 1 && !_brokenLinks.Contains((config, index, "thickness")))
                {
                    target.Thickness = source.Thickness;
                }

                if (!_brokenLinks.Contains((config, index, "material")))
                {
                    target.Material = source.Material;
                }
            }

            Configurations[config].SurfaceGroup.Renumber();
        }
    }
}
