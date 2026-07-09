using System.Collections.ObjectModel;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;

namespace OptilandWorkbench.Core.Domain;

public sealed class SurfaceGroup
{
    private bool _suppressRenumber;

    public SurfaceGroup()
    {
        Items.CollectionChanged += (_, _) =>
        {
            if (!_suppressRenumber)
            {
                Renumber();
            }
        };
    }

    public ObservableCollection<OpticalSurface> Items { get; } = new();

    public double TotalTrack => Items.Sum(surface => surface.Thickness);

    public OpticalSurface AddDefaultSurface()
    {
        var last = Items.LastOrDefault();
        var surface = new OpticalSurface
        {
            Label = "Surface",
            Radius = 40,
            Thickness = 5,
            Material = last?.Material == "Air" ? "N-BK7" : "Air",
            SemiDiameter = Math.Max(5, last?.SemiDiameter ?? 10)
        };

        Items.Add(surface);
        return surface;
    }

    public void Remove(OpticalSurface? surface)
    {
        if (surface is not null && Items.Contains(surface))
        {
            Items.Remove(surface);
        }
    }

    public void Replace(IEnumerable<OpticalSurface> surfaces, bool syncComposition = true)
    {
        _suppressRenumber = true;
        try
        {
            Items.Clear();
            foreach (var surface in surfaces)
            {
                Items.Add(surface);
            }
        }
        finally
        {
            _suppressRenumber = false;
        }

        Renumber(syncComposition);
    }

    public double ApertureRadius()
    {
        var stop = Items.FirstOrDefault(surface => surface.IsStop);
        if (stop is not null)
        {
            return stop.SemiDiameter;
        }

        return Items.Count == 0 ? 5 : Math.Max(1, Items.Max(surface => surface.SemiDiameter));
    }

    public void Renumber(bool syncComposition = true)
    {
        var z = 0.0;
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Number = index;
            if (syncComposition)
            {
                Items[index].SyncCompositionFromLegacyProperties(z);
            }
            else
            {
                var existing = Items[index].CoordinateSystem;
                Items[index].CoordinateSystem = new CoordinateSystem(
                    new Vector3D(0, 0, z),
                    existing.RotationXDegrees,
                    existing.RotationYDegrees,
                    existing.RotationZDegrees);
            }

            z += Items[index].Thickness;
        }
    }
}
