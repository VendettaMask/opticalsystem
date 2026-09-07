using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public sealed record RadiusPickup(int SourceSurface, int TargetSurface, double Scale, double Offset);
public sealed record SurfaceValuePickup(int SourceSurface, int TargetSurface, double Scale, double Offset = 0);

public sealed class PickupManager
{
    private Optic _optic;
    private readonly List<RadiusPickup> _radiusPickups = new();
    private readonly List<SurfaceValuePickup> _thicknessPickups = new();
    private readonly List<SurfaceValuePickup> _semiDiameterPickups = new();

    public PickupManager(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<RadiusPickup> RadiusPickups => _radiusPickups;
    public IReadOnlyList<SurfaceValuePickup> ThicknessPickups => _thicknessPickups;
    public IReadOnlyList<SurfaceValuePickup> SemiDiameterPickups => _semiDiameterPickups;

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
        _thicknessPickups.Clear();
        _semiDiameterPickups.Clear();
    }

    public void RemoveRadius(int targetSurface) =>
        _radiusPickups.RemoveAll(pickup => pickup.TargetSurface == targetSurface);

    public void RemoveThickness(int targetSurface) =>
        _thicknessPickups.RemoveAll(pickup => pickup.TargetSurface == targetSurface);

    public void RemoveSemiDiameter(int targetSurface) =>
        _semiDiameterPickups.RemoveAll(pickup => pickup.TargetSurface == targetSurface);

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

    public void SetThicknessPickup(
        int sourceSurface,
        int targetSurface,
        double scaleFactor,
        double offset = 0)
    {
        ValidateValuePickup(sourceSurface, targetSurface, scaleFactor, offset);
        RemoveThickness(targetSurface);
        _thicknessPickups.Add(new SurfaceValuePickup(sourceSurface, targetSurface, scaleFactor, offset));
    }

    public void SetSemiDiameterPickup(int sourceSurface, int targetSurface, double scaleFactor)
    {
        ValidateValuePickup(sourceSurface, targetSurface, scaleFactor, 0);
        RemoveSemiDiameter(targetSurface);
        _semiDiameterPickups.Add(new SurfaceValuePickup(sourceSurface, targetSurface, scaleFactor));
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

        RemapForInsert(_thicknessPickups, surfaceNumber);
        RemapForInsert(_semiDiameterPickups, surfaceNumber);
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

        RemapForRemoval(_thicknessPickups, surfaceNumber);
        RemapForRemoval(_semiDiameterPickups, surfaceNumber);
    }

    public void ApplyAll()
    {
        var surfaces = _optic.SurfaceGroup.Items.ToDictionary(surface => surface.Number);
        ApplyRadiusPickups(surfaces);
        ApplyValuePickups(
            surfaces,
            _thicknessPickups,
            surface => surface.Thickness,
            (surface, value) => surface.Thickness = value,
            "厚度",
            double.IsFinite);
        ApplyValuePickups(
            surfaces,
            _semiDiameterPickups,
            surface => surface.SemiDiameter,
            (surface, value) => surface.SemiDiameter = value,
            "净口径",
            value => double.IsFinite(value) && value >= 0.1);
    }

    private void ApplyRadiusPickups(IReadOnlyDictionary<int, OpticalSurface> surfaces)
    {
        if (_radiusPickups.Count == 0) return;
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

    private static void ApplyValuePickups(
        IReadOnlyDictionary<int, OpticalSurface> surfaces,
        IReadOnlyList<SurfaceValuePickup> pickups,
        Func<OpticalSurface, double> read,
        Action<OpticalSurface, double> write,
        string propertyName,
        Func<double, bool>? valid = null)
    {
        if (pickups.Count == 0) return;
        var links = pickups
            .Where(pickup => surfaces.ContainsKey(pickup.SourceSurface) && surfaces.ContainsKey(pickup.TargetSurface))
            .GroupBy(pickup => pickup.TargetSurface)
            .ToDictionary(group => group.Key, group => group.Last());
        var dependents = links.Values.ToLookup(pickup => pickup.SourceSurface);
        var ready = new Queue<SurfaceValuePickup>(links.Values.Where(pickup => !links.ContainsKey(pickup.SourceSurface)));
        var values = new Dictionary<int, double>();
        while (ready.TryDequeue(out var pickup))
        {
            var sourceValue = values.GetValueOrDefault(pickup.SourceSurface, read(surfaces[pickup.SourceSurface]));
            var value = pickup.Offset + (pickup.Scale * sourceValue);
            if (double.IsNaN(value)
                || (double.IsFinite(sourceValue) && !double.IsFinite(value))
                || (valid is not null && !valid(value)))
                throw new InvalidOperationException($"{propertyName}拾取结果超出可表示范围。");
            values.Add(pickup.TargetSurface, value);
            foreach (var dependent in dependents[pickup.TargetSurface]) ready.Enqueue(dependent);
        }
        if (values.Count != links.Count) throw new InvalidOperationException($"{propertyName}拾取存在循环引用。");
        foreach (var (number, value) in values) write(surfaces[number], value);
    }

    private void ValidateValuePickup(int sourceSurface, int targetSurface, double scaleFactor, double offset)
    {
        if (sourceSurface < 0 || sourceSurface >= targetSurface
            || targetSurface >= _optic.SurfaceGroup.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceSurface), "拾取表面必须在当前表面之前。");
        if (!double.IsFinite(scaleFactor) || !double.IsFinite(offset))
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "比例因子和偏移量必须是有限数值。");
    }

    private static void RemapForInsert(List<SurfaceValuePickup> pickups, int surfaceNumber)
    {
        for (var index = 0; index < pickups.Count; index++)
        {
            var pickup = pickups[index];
            pickups[index] = pickup with
            {
                SourceSurface = pickup.SourceSurface >= surfaceNumber ? pickup.SourceSurface + 1 : pickup.SourceSurface,
                TargetSurface = pickup.TargetSurface >= surfaceNumber ? pickup.TargetSurface + 1 : pickup.TargetSurface
            };
        }
    }

    private static void RemapForRemoval(List<SurfaceValuePickup> pickups, int surfaceNumber)
    {
        pickups.RemoveAll(pickup => pickup.SourceSurface == surfaceNumber || pickup.TargetSurface == surfaceNumber);
        for (var index = 0; index < pickups.Count; index++)
        {
            var pickup = pickups[index];
            pickups[index] = pickup with
            {
                SourceSurface = pickup.SourceSurface > surfaceNumber ? pickup.SourceSurface - 1 : pickup.SourceSurface,
                TargetSurface = pickup.TargetSurface > surfaceNumber ? pickup.TargetSurface - 1 : pickup.TargetSurface
            };
        }
    }
}
