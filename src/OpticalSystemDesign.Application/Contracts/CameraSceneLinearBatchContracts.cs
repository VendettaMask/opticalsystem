using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Application.Contracts;

public enum SceneLinearBatchMode { CurrentLensWavelengths, ArticleSourceShapes, ExplicitSources }
public sealed record SceneLinearSource(string Name, double? WavelengthNanometers = null,
    IReadOnlyList<ScalarSpectrumSample>? RelativeSpectrum = null, double SourceMultiplier = 1,
    int? LensWavelengthNumber = null, double? LensWeight = null, string? Provenance = null);
public sealed record CameraSceneLinearBatchRequest(EmissiveCheckerboardSettings? Chart = null,
    int Width = 128, int Height = 128, double PixelPitchMicrometers = 3, int PixelSamplesPerAxis = 1,
    double FillFactor = 1, int PupilRings = 2, int PupilArms = 4, bool BareInterfaceFresnel = true,
    SceneLinearBatchMode Mode = SceneLinearBatchMode.CurrentLensWavelengths, bool UseLensWeightsAsSourceMultipliers = true,
    double BrightBandRadiance = 1, double DarkBandRadiance = .1, int SpectralQuadratureOrder = 2,
    IReadOnlyList<SceneLinearSource>? Sources = null, int RowsPerStrip = 16, double? PngMaximumIrradiance = null);
public sealed record SceneLinearFrozenTask(SceneLinearSource Source, IReadOnlyList<SceneEmissionNode> Nodes,
    double? ShapeAreaNanometers, double? InputShapeMaximum);
public sealed record CameraSceneLinearBatchPlan(string Format, int SchemaVersion, string Algorithm,
    OpticalDocumentSnapshot SourceDocument, string LensSnapshotJson, string LensSha256,
    CameraSceneLinearBatchRequest Request, IReadOnlyList<SceneLinearFrozenTask> Tasks,
    DirectCameraBatchPlan Work, long Float64Values, bool WithinBudget, string? BudgetFailure)
{
    public const string FormatIdentifier = "OpticalSystemDesign.SceneLinearBatch";
    public const string CurrentAlgorithm = "SceneLinearBatch/1; shared geometric transport; " + SceneEmissionSpectrum.Algorithm;
}
public sealed record SceneLinearFile(string Path, string Kind, long Bytes, string Sha256, int TaskIndex, int? NodeIndex = null);
public sealed record SceneLinearTaskOutput(string PowerPath, string IrradiancePath, string PngPath,
    double TotalPowerWatts, double MaximumIrradiance, DirectCameraSourceStatistics Statistics);
public sealed record SceneLinearPngEncoding(int BitDepth, string ColorType, string TransferFunction,
    double CommonMaximumWattsPerSquareMeter, double OffsetWattsPerSquareMeter, double MaximumQuantizationError,
    string ColorMetadata, int ClippedPixels);
public sealed record CameraSceneLinearBatchManifest(CameraSceneLinearBatchPlan Plan, DetectorRowOrder RowOrder,
    double CollectionAreaSquareMeters, double FirstLensVertexZMillimeters, double SensorPlaneZMillimeters,
    IReadOnlyList<SceneLinearTaskOutput> Outputs, IReadOnlyList<SceneLinearFile> Files, SceneLinearPngEncoding Png,
    long CompletedRays, long SurfaceInteractions);
public sealed record CameraSceneLinearBatchArtifact(string ArchivePath, CameraSceneLinearBatchManifest Manifest);
public sealed record CameraSceneLinearBatchProgress(string Phase, int TaskIndex, int CompletedRows, int Height,
    long CompletedRays, long TotalRays);
public sealed record CameraSceneLinearTaskImage(int TaskIndex, string Name, int Width, int Height, double PixelPitchMicrometers,
    IReadOnlyList<double> PowerWatts, IReadOnlyList<double> IrradianceWattsPerSquareMeter, CameraImage Preview);
