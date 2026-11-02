using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Application.Contracts;

public sealed record CameraSceneEdgeRequest(SfrRegion Region, int BandIndex = 0,
    LinearEdgeOrientation Orientation = LinearEdgeOrientation.NearVertical, int HalfWindowPixels = 24);

/// <summary>Null bands capture only the current lens primary wavelength at unit band radiance. No implicit spectral weighting.</summary>
public sealed record CameraSceneSfrRequest(EmissiveCheckerboardSettings? Chart = null,
    IReadOnlyList<EmissiveCheckerboardBand>? Bands = null, int Width = 96, int Height = 96,
    double PixelPitchMicrometers = 3, int PixelSamplesPerAxis = 2, double FillFactor = 1,
    int PupilRings = 2, int PupilArms = 8, bool BareInterfaceFresnel = true, CameraSceneEdgeRequest? Edge = null,
    bool CaptureLensPrimaryWavelength = false);

/// <summary>Authoritative band-integrated W/pixel. Irradiance is power / collecting area exactly once.</summary>
public sealed record CameraSceneLinearFrame(int Width, int Height, double PixelPitchMicrometers, double FillFactor,
    DetectorRowOrder RowOrder, IReadOnlyList<CameraDetectorBand> Bands,
    long LaunchedRays, long PassedLensRays, long SceneHitRays, long SurfaceInteractions,
    double FirstLensVertexZMillimeters, double SensorPlaneZMillimeters)
{
    public LinearDetectorImage ToLinearImage() => new(new SpectralDetectorPower(Width, Height,
        PixelPitchMicrometers, PixelPitchMicrometers, RowOrder,
        Bands.Select(b => new SpectralDetectorPowerBand(b.WavelengthNanometers, b.PixelPowerWatts)), FillFactor));
}

public sealed record CameraSceneSfrResult(string Format, int SchemaVersion, string Algorithm,
    OpticalDocumentSnapshot SourceDocument, string LensSnapshotJson, string LensSha256,
    CameraSceneSfrRequest Request, CameraSceneLinearFrame Frame, LinearImageEdgeResult? Edge, string? EdgeFailure)
{
    public const string FormatIdentifier = "OpticalSystemDesign.SceneSlantedEdge";
    public const string CurrentAlgorithm = "SceneSlantedEdge/1; geometric inverse transport; " + EmissiveCheckerboard.Algorithm;
}
