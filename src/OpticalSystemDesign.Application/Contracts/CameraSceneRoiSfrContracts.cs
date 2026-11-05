using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Application.Contracts;

public sealed record CameraSceneRoiSfrRequest(IReadOnlyList<int> TaskIndices, IReadOnlyList<LinearSfrRoi> Regions);
public sealed record SceneLinearTaskSfr(int TaskIndex, string Name, string IrradianceSha256, IReadOnlyList<LinearSfrRoiResult> Regions);
public sealed record CameraSceneRoiSfrReport(string Format, int SchemaVersion, string Algorithm,
    string SourceManifestSha256, string LensSha256, OpticalDocumentSnapshot SourceDocument,
    int Width, int Height, double PixelPitchMicrometers, DetectorRowOrder RowOrder,
    CameraSceneRoiSfrRequest Request, IReadOnlyList<SceneLinearTaskSfr> Tasks)
{
    public const string FormatIdentifier = "OpticalSystemDesign.SceneLinearRoiSfr";
    public const int MaximumMeasurements = 128;
}
public sealed record CameraSceneRoiSfrProgress(int TaskIndex, int CompletedRegions, int TotalMeasurements);

public sealed record SceneRoiMtfComparison(LinearSfrRoiResult Measurement,
    OpticalSystemDesign.Core.Analysis.SceneEdgeMtfReference? Reference, string? ReferenceFailure);
public sealed record SceneTaskMtfComparison(int TaskIndex, string Name, string IrradianceSha256,
    IReadOnlyList<SceneRoiMtfComparison> Regions);
public sealed record CameraSceneMtfReport(string ReferenceAlgorithm, string MeasurementAlgorithm,
    string SourceManifestSha256, string LensSha256, int PupilAxisSamples, IReadOnlyList<SceneTaskMtfComparison> Tasks);
