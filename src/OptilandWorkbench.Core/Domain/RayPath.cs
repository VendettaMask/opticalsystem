namespace OptilandWorkbench.Core.Domain;

public readonly record struct RayPoint(double Z, double Y);

public sealed record RaySegment(RayPoint Start, RayPoint End);

public sealed record RayPath(
    FieldPoint Field,
    Wavelength Wavelength,
    IReadOnlyList<RaySegment> Segments,
    bool Vignetted);

public sealed record RayTraceResult(IReadOnlyList<RayPath> Paths);
