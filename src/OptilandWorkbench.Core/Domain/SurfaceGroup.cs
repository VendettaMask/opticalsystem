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
                Renumber();
            }
        };
    }

    public ObservableCollection<OpticalSurface> Items { get; } = new();

    public SurfaceTraceData RecordedTrace { get; private set; } = SurfaceTraceData.Empty;

    public double TotalTrack => Items
        .Where((surface, index) => index != 0 || !ObjectConjugate.IsInfinite(surface))
        .Sum(surface => surface.Thickness);

    public OpticalSurface AddDefaultSurface()
        => InsertDefaultSurfaceCore(Math.Max(0, Items.Count - 1));

    public OpticalSurface InsertDefaultSurface(int surfaceNumber)
    {
        if (surfaceNumber <= 0 || surfaceNumber >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "Insert between the object and image surfaces.");
        }

        return InsertDefaultSurfaceCore(surfaceNumber);
    }

    private OpticalSurface InsertDefaultSurfaceCore(int surfaceNumber)
    {
        var previous = surfaceNumber > 0 ? Items[surfaceNumber - 1] : Items.LastOrDefault();
        var surface = new OpticalSurface
        {
            Label = "Surface",
            Radius = 40,
            Thickness = 5,
            Material = "Air",
            SemiDiameter = Math.Max(5, previous?.SemiDiameter ?? 10)
        };

        surface.InitializeFromLegacyProperties(0);
        Items.Insert(surfaceNumber, surface);
        return surface;
    }

    public void Remove(OpticalSurface? surface)
    {
        if (surface is not null && Items.Contains(surface))
        {
            Items.Remove(surface);
        }
    }

    public void Replace(IEnumerable<OpticalSurface> surfaces)
    {
        ReplaceCore(surfaces, initializeLegacyComposition: false);
    }

    public void ImportLegacySurfaces(IEnumerable<OpticalSurface> surfaces)
    {
        ReplaceCore(surfaces, initializeLegacyComposition: true);
    }

    private void ReplaceCore(
        IEnumerable<OpticalSurface> surfaces,
        bool initializeLegacyComposition)
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

        if (initializeLegacyComposition)
        {
            InitializeLegacyCompositionAndRenumber();
            return;
        }

        Renumber();
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

    public void Renumber()
    {
        var z = 0.0;
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Number = index;
            var existing = Items[index].CoordinateSystem;
            Items[index].CoordinateSystem = new CoordinateSystem(
                new Vector3D(existing.Origin.X, existing.Origin.Y, z),
                existing.RotationXDegrees,
                existing.RotationYDegrees,
                existing.RotationZDegrees);

            if (index != 0 || !ObjectConjugate.IsInfinite(Items[index]))
            {
                z += Items[index].Thickness;
            }
        }
    }

    private void InitializeLegacyCompositionAndRenumber()
    {
        var z = 0.0;
        for (var index = 0; index < Items.Count; index++)
        {
            var surface = Items[index];
            surface.Number = index;
            surface.InitializeFromLegacyProperties(z);
            if (index != 0 || !ObjectConjugate.IsInfinite(surface))
            {
                z += surface.Thickness;
            }
        }
    }
}
