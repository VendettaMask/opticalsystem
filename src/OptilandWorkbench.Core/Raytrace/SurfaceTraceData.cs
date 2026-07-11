namespace OptilandWorkbench.Core.Raytrace;

public sealed record SurfaceTraceData(IReadOnlyList<SurfaceTraceRecord> Surfaces)
{
    public static SurfaceTraceData Empty { get; } = new(Array.Empty<SurfaceTraceRecord>());

    public int SurfaceCount => Surfaces.Count;

    public int RayCount => Surfaces.Count == 0 ? 0 : Surfaces[0].RayCount;

    public SurfaceTraceRecord ImageSurface => Surfaces.Count == 0
        ? SurfaceTraceRecord.Empty
        : Surfaces[^1];
}

public sealed record SurfaceTraceRecord(
    int SurfaceNumber,
    string SurfaceLabel,
    IReadOnlyList<double> X,
    IReadOnlyList<double> Y,
    IReadOnlyList<double> Z,
    IReadOnlyList<double> L,
    IReadOnlyList<double> M,
    IReadOnlyList<double> N,
    IReadOnlyList<double> Intensity,
    IReadOnlyList<double> OpticalPathDifference,
    IReadOnlyList<double> CumulativeOpticalPathLength)
{
    public static SurfaceTraceRecord Empty { get; } = new(
        -1,
        string.Empty,
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>(),
        Array.Empty<double>());

    public int RayCount => X.Count;
}
