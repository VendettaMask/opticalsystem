using System.Collections.ObjectModel;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Domain;

public sealed class SurfaceGroup
{
    private bool _suppressRenumber;
    private Optic? _owner;
    private bool _rebuildingCoordinates;

    public SurfaceGroup(Optic? owner = null)
    {
        _owner = owner;
        Items.CollectionChanged += (_, _) =>
        {
            if (!_suppressRenumber)
            {
                Renumber();
            }
        };
    }

    public ObservableCollection<OpticalSurface> Items { get; } = new();
    internal void Rebind(Optic owner) => _owner = owner;

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

    public OpticalSurface InsertCoordinateBreak(int surfaceNumber)
    {
        if (_owner is null || surfaceNumber <= 0 || surfaceNumber >= Items.Count)
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
        SequentialCoordinateEditing.EnsurePlacements(_owner);
        var previous = Items[surfaceNumber - 1];
        var surface = new OpticalSurface
        {
            Label = "Coordinate Break",
            Radius = double.PositiveInfinity,
            Thickness = 0,
            SemiDiameter = previous.SemiDiameter,
            Material = previous.MaterialAfter.Name,
            MaterialBefore = previous.MaterialAfter.Clone(),
            MaterialAfter = previous.MaterialAfter.Clone(),
            Geometry = new Geometries.PlaneGeometry(),
            InteractionModel = new Interactions.CoordinateReferenceInteractionModel(),
            Placement = SequentialSurfacePlacement.From(CoordinateSystem.Global, coordinateBreak: new(0, 0, 0, 0, 0, false))
        };
        RemapCoordinateReferencesForInsert(surfaceNumber);
        Items.Insert(surfaceNumber, surface);
        return surface;
    }

    public void SynchronizeFollowingMedia(int surfaceNumber)
    {
        for (var index = surfaceNumber + 1; index < Items.Count; index++)
        {
            var surface = Items[index];
            surface.MaterialBefore = Items[index - 1].MaterialAfter.Clone();
            if (!surface.IsReflective && surface.InteractionModel is not Interactions.CoordinateReferenceInteractionModel) break;
            surface.MaterialAfter = surface.MaterialBefore.Clone();
        }
    }

    private void RemapCoordinateReferencesForInsert(int number)
    {
        if (_owner is not null) SequentialCoordinateEditing.RemapMeritReferences(_owner, n => n >= number ? n + 1 : n);
        SequentialCoordinateEditing.RemapReferences(Items, n => n >= number ? n + 1 : n);
        if (_owner is not null && _owner.GlobalReferenceSurfaceNumber >= number) _owner.GlobalReferenceSurfaceNumber++;
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
        if (Items.Any(item => item.Placement is not null))
            surface.Placement = SequentialSurfacePlacement.From(CoordinateSystem.Global);
        RemapCoordinateReferencesForInsert(surfaceNumber);
        Items.Insert(surfaceNumber, surface);
        return surface;
    }

    public void Remove(OpticalSurface? surface)
    {
        if (surface is not null && Items.Contains(surface))
        {
            var number = Items.IndexOf(surface);
            SequentialCoordinateEditing.ValidateRemoval(Items, number);
            if (_owner is not null) SequentialCoordinateEditing.RemapMeritReferences(_owner, n => n > number ? n - 1 : n, number);
            SequentialCoordinateEditing.RemapReferences(Items.Where(s => !ReferenceEquals(s, surface)), n => n > number ? n - 1 : n);
            if (_owner is not null && _owner.GlobalReferenceSurfaceNumber > number) _owner.GlobalReferenceSurfaceNumber--;
            Items.Remove(surface);
        }
    }

    public void Replace(IEnumerable<OpticalSurface> surfaces)
    {
        ReplaceCore(surfaces, initializeLegacyComposition: false);
    }

    public void ImportLegacySurfaces(IEnumerable<OpticalSurface> surfaces, MaterialRegistry? materials = null)
    {
        ReplaceCore(surfaces, initializeLegacyComposition: true, materials);
    }

    private void ReplaceCore(
        IEnumerable<OpticalSurface> surfaces,
        bool initializeLegacyComposition, MaterialRegistry? materials = null)
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
            InitializeLegacyCompositionAndRenumber(materials);
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
        if (Items.Any(surface => surface.Placement is not null))
        {
            if (_rebuildingCoordinates) return;
            for (var index = 0; index < Items.Count; index++) Items[index].Number = index;
            _rebuildingCoordinates = true;
            try { SequentialSurfacePlacement.Rebuild(Items, _owner); }
            finally { _rebuildingCoordinates = false; }
            return;
        }
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

    private void InitializeLegacyCompositionAndRenumber(MaterialRegistry? materials = null)
    {
        var z = 0.0;
        for (var index = 0; index < Items.Count; index++)
        {
            var surface = Items[index];
            surface.Number = index;
            surface.InitializeFromLegacyProperties(z, materials);
            if (index != 0 || !ObjectConjugate.IsInfinite(surface))
            {
                z += surface.Thickness;
            }
        }
    }
}
