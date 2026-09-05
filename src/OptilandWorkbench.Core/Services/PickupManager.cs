namespace OptilandWorkbench.Core.Services;

public sealed record RadiusPickup(int SourceSurface, int TargetSurface, double Scale, double Offset);

public sealed class PickupManager
{
    private Optic _optic;
    private readonly List<RadiusPickup> _radiusPickups = new();

    public PickupManager(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<RadiusPickup> RadiusPickups => _radiusPickups;

    internal void Rebind(Optic optic)
    {
        _optic = optic;
    }

    public void LinkRadius(int sourceSurface, int targetSurface, double scale = -1, double offset = 0)
    {
        _radiusPickups.Add(new RadiusPickup(sourceSurface, targetSurface, scale, offset));
    }

    public void Clear()
    {
        _radiusPickups.Clear();
    }

    public void RemoveRadius(int targetSurface) =>
        _radiusPickups.RemoveAll(pickup => pickup.TargetSurface == targetSurface);

    public void SetCurvaturePickup(int sourceSurface, int targetSurface, double scaleFactor)
    {
        if (sourceSurface < 0 || sourceSurface >= targetSurface
            || targetSurface >= _optic.SurfaceGroup.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceSurface), "拾取表面必须在当前表面之前。");
        // The native format stores R' = scale * R + offset. With zero offset,
        // C' = factor * C is exactly R' = R / factor; zero factor means a plane.
        var radiusScale = scaleFactor == 0 ? 0 : 1 / scaleFactor;
        if (!double.IsFinite(scaleFactor) || !double.IsFinite(radiusScale))
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "比例因子必须是可表示的有限数值。");
        RemoveRadius(targetSurface);
        LinkRadius(sourceSurface, targetSurface, radiusScale, 0);
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
        if (_radiusPickups.Count == 0) return;
        var surfaces = _optic.SurfaceGroup.Items.ToDictionary(surface => surface.Number);
        var links = _radiusPickups
            .Where(pickup => surfaces.ContainsKey(pickup.SourceSurface) && surfaces.ContainsKey(pickup.TargetSurface))
            .GroupBy(pickup => pickup.TargetSurface)
            .ToDictionary(group => group.Key, group => group.Last());
        var dependents = links.Values.ToLookup(pickup => pickup.SourceSurface);
        var ready = new Queue<RadiusPickup>(links.Values.Where(pickup => !links.ContainsKey(pickup.SourceSurface)));
        var values = new Dictionary<int, double>();
        while (ready.TryDequeue(out var pickup))
        {
            var sourceRadius = values.GetValueOrDefault(pickup.SourceSurface, surfaces[pickup.SourceSurface].Radius);
            var radius = pickup.Scale == 0 ? pickup.Offset : sourceRadius * pickup.Scale + pickup.Offset;
            if (double.IsNaN(radius) || (double.IsFinite(sourceRadius) && !double.IsFinite(radius)))
                throw new InvalidOperationException("拾取结果超出可表示的半径范围。");
            values.Add(pickup.TargetSurface, radius);
            foreach (var dependent in dependents[pickup.TargetSurface]) ready.Enqueue(dependent);
        }
        if (values.Count != links.Count) throw new InvalidOperationException("半径拾取存在循环引用。");
        // Resolve the entire dependency graph before touching any surface.
        foreach (var (number, radius) in values) surfaces[number].Radius = radius;
    }
}
