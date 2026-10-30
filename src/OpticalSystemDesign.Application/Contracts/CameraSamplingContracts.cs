namespace OpticalSystemDesign.Application.Contracts;

public enum CameraSamplingAxis { Pupil, Scene, Pixel, Window, Guard }
public enum CameraSamplingComparisonStatus { Compared, AtLimit, Unavailable }
public sealed record CameraSamplingRegion(int X, int Y, int Width = 4, int Height = 4);
public sealed record CameraSamplingSettings(CameraSceneDiffractionSettings Diffraction, CameraPixelSamplingParameters? PixelSampling);
public sealed record CameraSamplingDifference(double MeanAbsolute, double MaximumAbsolute, double Rms,
    double? RelativeL1, double? RelativeRms, double BaselineSum, double RefinedSum, int WorstX, int WorstY);
public sealed record CameraSamplingSnapshot(CameraSamplingSettings Settings, IReadOnlyList<CameraDetectorBand> Bands,
    IReadOnlyList<double> PhotoElectrons, IReadOnlyList<ushort> NoiselessRawDn, CameraBayerPattern LocalBayerPattern,
    CameraSceneDiffractionDiagnostics Diagnostics, double ElapsedSeconds, CameraSamplingExecution? Execution = null);
public sealed record CameraSamplingExecution(long LaunchedRays, long SurfaceInteractions, long AvoidedAuxiliaryRays,
    string Policy = "RegionExactSupport/1", long MaximumRays = 8_388_608, long MaximumSurfaceInteractions = 100_000_000,
    long SourceCandidateVisits = 0, long MaximumCandidateVisits = 65_536);
public sealed record CameraSamplingComparison(CameraSamplingAxis Axis, CameraSamplingComparisonStatus Status,
    CameraSamplingSettings RequestedSettings, string Message, CameraSamplingSnapshot? Refined = null,
    IReadOnlyList<CameraSamplingDifference>? BandDifferences = null, CameraSamplingDifference? ElectronDifference = null,
    CameraSamplingDifference? RawDifference = null, IReadOnlyList<CameraSamplingComparison>? FurtherRefinements = null);
public sealed record CameraSamplingReport(string Format, int SchemaVersion, string SourceRunSha256,
    string SourceEngineVersion, CameraSamplingRegion Region, CameraSamplingSnapshot Baseline,
    IReadOnlyList<CameraSamplingDifference> SavedBandDifferences, CameraSamplingDifference SavedElectronDifference,
    IReadOnlyList<CameraSamplingComparison> Comparisons)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraSamplingReport";
}
