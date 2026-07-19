using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Raytrace;

public interface IRayAimer
{
    string Mode { get; }

    Vector3D Aim(FieldPoint field, PupilSample sample, double apertureRadius);
}

public sealed class ParaxialRayAimer : IRayAimer
{
    public string Mode => "paraxial";

    public Vector3D Aim(FieldPoint field, PupilSample sample, double apertureRadius)
    {
        return new Vector3D(sample.X * apertureRadius, sample.Y * apertureRadius, 0);
    }
}

public sealed class IterativeRayAimer : IRayAimer
{
    public string Mode => "iterative";

    public Vector3D Aim(FieldPoint field, PupilSample sample, double apertureRadius)
    {
        return new Vector3D(sample.X * apertureRadius, sample.Y * apertureRadius, 0);
    }
}

public sealed class RobustRayAimer : IRayAimer
{
    public string Mode => "robust";

    public Vector3D Aim(FieldPoint field, PupilSample sample, double apertureRadius)
    {
        return new Vector3D(sample.X * apertureRadius, sample.Y * apertureRadius, 0);
    }
}

public sealed class CachedRayAimer : IRayAimer
{
    private readonly IRayAimer _inner;
    private readonly Dictionary<(double FieldX, double FieldY, double X, double Y, double R), Vector3D> _cache = new();

    public CachedRayAimer(IRayAimer inner)
    {
        _inner = inner;
    }

    public string Mode => $"cached:{_inner.Mode}";

    public Vector3D Aim(FieldPoint field, PupilSample sample, double apertureRadius)
    {
        var key = (field.X, field.Y, sample.X, sample.Y, apertureRadius);
        if (!_cache.TryGetValue(key, out var value))
        {
            value = _inner.Aim(field, sample, apertureRadius);
            _cache[key] = value;
        }

        return value;
    }
}
