namespace OpticalSystemDesign.Application.Contracts;

public interface IPerformanceTestService
{
    Task<PerformanceTestResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed record PerformanceTraceTrial(int Rays, int ImageArrivals, long SurfaceVisits, double Seconds)
{
    public double RaysPerSecond => Rays / Seconds;
    public double SurfaceVisitsPerSecond => SurfaceVisits / Seconds;
}

public sealed record PerformanceUpdateTrial(int Updates, double Seconds)
{
    public double UpdatesPerSecond => Updates / Seconds;
}

public sealed record PerformanceTestResult(
    string LensName,
    int ConfigurationNumber,
    int SurfaceCount,
    int FieldNumber,
    double WavelengthMicrometers,
    int LogicalProcessors,
    IReadOnlyList<PerformanceTraceTrial> TraceTrials,
    IReadOnlyList<PerformanceUpdateTrial> UpdateTrials);
