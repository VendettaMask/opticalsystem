namespace OptilandWorkbench.Application.Contracts;

public enum AssemblyConjugateKind { Vertex, CurvatureCenter }
public enum AssemblyConjugateStatus { Finite, Infinity, NoReflection, Unsupported, Failed }
public enum AssemblyMeasurementMode { Focused, Infinity }

public sealed record OpticalAssemblyRequest(double WavelengthNanometers = 587.6, bool FromRear = false);
public sealed record OpticalAssemblyRow(int SurfaceNumber, string SurfaceLabel,
    AssemblyConjugateKind Kind, AssemblyConjugateStatus Status, double? PositionMillimeters, string Message);
public sealed record OpticalAssemblyResult(string DocumentName, long SourceRevision,
    OpticalAssemblyRequest Request, IReadOnlyList<OpticalAssemblyRow> Rows);
public sealed record AssemblyReticleRequest(OpticalAssemblyRequest Optics, long ExpectedRevision,
    int SurfaceNumber, AssemblyConjugateKind Kind, double HeadFocalLength = 100,
    double CollimatorFocalLength = 200, double NumericalAperture = 0.02,
    double ReticleLength = 1, double ReticleWidth = 0.02, double FocusOffset = 0, double SensorWidth = 4,
    AssemblyMeasurementMode Mode = AssemblyMeasurementMode.Focused, double PupilDiameter = 4, double InstrumentDistance = 100);
public sealed record AssemblyReticleResult(long SourceRevision, int Size, double SensorWidth,
    IReadOnlyList<double> Pixels, int LaunchedRays, int ReturnedRays, int RecordedRays,
    double? Magnification, double? ImageLength, double? HeadPosition, string Message,
    AssemblyMeasurementMode Mode, double CollimatorPosition, bool MatchesSelectedConjugate);

public interface IOpticalAssemblyService
{
    Task<OpticalAssemblyResult> CalculateAsync(OpticalAssemblyRequest request, CancellationToken cancellationToken = default);
    Task<AssemblyReticleResult> SimulateAsync(AssemblyReticleRequest request, CancellationToken cancellationToken = default);
}
