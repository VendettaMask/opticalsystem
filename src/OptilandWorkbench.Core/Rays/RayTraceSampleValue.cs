using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Rays;

public readonly record struct RayTraceSampleValue(
    int SurfaceNumber,
    string SurfaceLabel,
    Vector3D Position,
    Vector3D Direction,
    double Intensity,
    bool Vignetted,
    double SegmentLength = 0,
    double SegmentOpticalPathLength = 0,
    double CumulativePathLength = 0,
    double CumulativeOpticalPathLength = 0,
    double OpticalPathDifference = 0)
{
    public RayTraceSample ToRayTraceSample() => new(
        SurfaceNumber,
        SurfaceLabel,
        Position,
        Direction,
        Intensity,
        Vignetted,
        SegmentLength,
        SegmentOpticalPathLength,
        CumulativePathLength,
        CumulativeOpticalPathLength,
        OpticalPathDifference);

    public static RayTraceSampleValue FromRayTraceSample(RayTraceSample sample) => new(
        sample.SurfaceNumber,
        sample.SurfaceLabel,
        sample.Position,
        sample.Direction,
        sample.Intensity,
        sample.Vignetted,
        sample.SegmentLength,
        sample.SegmentOpticalPathLength,
        sample.CumulativePathLength,
        sample.CumulativeOpticalPathLength,
        sample.OpticalPathDifference);
}
