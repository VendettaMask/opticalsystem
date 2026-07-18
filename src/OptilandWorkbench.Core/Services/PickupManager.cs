namespace OptilandWorkbench.Core.Services;

public sealed record RadiusPickup(int SourceSurface, int TargetSurface, double Scale, double Offset);

public sealed class PickupManager
{
    private readonly Optic _optic;
    private readonly List<RadiusPickup> _radiusPickups = new();

    public PickupManager(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<RadiusPickup> RadiusPickups => _radiusPickups;

    public void LinkRadius(int sourceSurface, int targetSurface, double scale = -1, double offset = 0)
    {
        _radiusPickups.Add(new RadiusPickup(sourceSurface, targetSurface, scale, offset));
    }

    public void Clear()
    {
        _radiusPickups.Clear();
    }

    public void InsertSurface(int surfaceNumber)
    {
        for (var index = 0; index < _radiusPickups.Count; index++)
        {
            var pickup = _radiusPickups[index];
            _radiusPickups[index] = pickup with
            {
                SourceSurface = pickup.SourceSurface >= surfaceNumber
                    ? pickup.SourceSurface + 1
                    : pickup.SourceSurface,
                TargetSurface = pickup.TargetSurface >= surfaceNumber
                    ? pickup.TargetSurface + 1
                    : pickup.TargetSurface
            };
        }
    }

    public void RemoveSurface(int surfaceNumber)
    {
        _radiusPickups.RemoveAll(pickup =>
            pickup.SourceSurface == surfaceNumber || pickup.TargetSurface == surfaceNumber);
        for (var index = 0; index < _radiusPickups.Count; index++)
        {
            var pickup = _radiusPickups[index];
            _radiusPickups[index] = pickup with
            {
                SourceSurface = pickup.SourceSurface > surfaceNumber
                    ? pickup.SourceSurface - 1
                    : pickup.SourceSurface,
                TargetSurface = pickup.TargetSurface > surfaceNumber
                    ? pickup.TargetSurface - 1
                    : pickup.TargetSurface
            };
        }
    }

    public void ApplyAll()
    {
        foreach (var pickup in _radiusPickups)
        {
            var source = _optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == pickup.SourceSurface);
            var target = _optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == pickup.TargetSurface);
            if (source is not null && target is not null)
            {
                target.Radius = (source.Radius * pickup.Scale) + pickup.Offset;
            }
        }
    }
}
