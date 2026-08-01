using System.Collections.ObjectModel;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Raytrace;

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
                Renumber(syncComposition: false);
            }
        };
    }

    public ObservableCollection<OpticalSurface> Items { get; } = new();

    public SurfaceTraceData RecordedTrace { get; private set; } = SurfaceTraceData.Empty;

    public double TotalTrack => Items
        .Where((surface, index) => index != 0 || !ObjectConjugate.IsInfinite(surface))
        .Sum(surface => surface.Thickness);

    public OpticalSurface AddDefaultSurface()
    {
        var imageIndex = Math.Max(0, Items.Count - 1);
        var previous = imageIndex > 0 ? Items[imageIndex - 1] : Items.LastOrDefault();
        var surface = new OpticalSurface
        {
            Label = "Surface",
            Radius = 40,
            Thickness = 5,
            Material = "Air",
            SemiDiameter = Math.Max(5, previous?.SemiDiameter ?? 10)
        };

        surface.SyncCompositionFromLegacyProperties(0);
        Items.Insert(imageIndex, surface);
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

    public void RecordTrace(SurfaceTraceData trace)
    {
        RecordedTrace = trace;
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

            if (index != 0 || !ObjectConjugate.IsInfinite(Items[index]))
            {
                z += Items[index].Thickness;
            }
        }
    }
}
