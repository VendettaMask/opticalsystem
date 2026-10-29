namespace OpticalSystemDesign.Application.Contracts;

public enum CameraSource { ColorChart, ResolutionTarget, DistortionGrid, SiemensStar, ExternalImage }
public enum CameraOptics { CurrentLens, Ideal }
public enum CameraAberration { Geometric, Diffraction }
public enum CameraBayerPattern { RGGB, BGGR, GRBG, GBRG }
public enum CameraSpectralModel { ThreeBand, Continuous }
public enum CameraSpectrumKind { Illuminant, Reflectance }
public enum CameraSceneMode { Uniform, MaterialMap }
public sealed record CameraSceneMaterial(int Id, string Name, string Source, CameraSpectrumCurve? Reflectance = null);
public sealed record CameraMaterialScene(string Format, int SchemaVersion, string Name, string Source,
    int Width, int Height, IReadOnlyList<int> MaterialIds, IReadOnlyList<CameraSceneMaterial> Materials,
    CameraFileOrigin? Origin = null, bool Modified = false)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraMaterialScene";
}
public sealed record CameraMaterialDiagnostics(int Id, string Name, int SampledPixels, double ReflectedIrradianceWattsPerSquareMeter);
public sealed record CameraSpectrumSample(double WavelengthNanometers, double Value);
public sealed record CameraSpectrumCurve(string Name, string Source, CameraSpectrumKind Kind,
    IReadOnlyList<CameraSpectrumSample> Samples, CameraFileOrigin? Origin = null);
public sealed record CameraIntegrationSettings(double MinimumNanometers = 400, double MaximumNanometers = 700,
    double MaximumIntervalNanometers = 100);
public sealed record CameraIntegrationNode(double WavelengthNanometers, double WeightNanometers,
    double IrradianceWattsPerSquareMeterPerNanometer, double Reflectance, double RedQe, double GreenQe, double BlueQe);
public sealed record CameraIntegrationDiagnostics(int Intervals, IReadOnlyList<CameraIntegrationNode> Nodes,
    double IlluminantIrradianceWattsPerSquareMeter, double ReflectedIrradianceWattsPerSquareMeter,
    IReadOnlyList<CameraMaterialDiagnostics>? Materials = null,
    string ReflectanceMeaning = "Uniform material; excludes spatial grayscale modulation and optics");
public sealed record CameraResponseSample(double WavelengthNanometers, double RedQe, double GreenQe, double BlueQe);
public sealed record CameraFileOrigin(string FileName, string Sha256);
public sealed record CameraResponseCurve(string Name, string Source, IReadOnlyList<CameraResponseSample> Samples,
    CameraFileOrigin? Origin = null);
public sealed record CameraSensorIdentity(string Name, string Source, CameraFileOrigin? Origin = null, bool Modified = false,
    CameraCalibrationRecord? Calibration = null);
public sealed record CameraSensorProfile(string Format, int SchemaVersion, string Name, string Source,
    CameraSensorParameters Sensor, CameraResponseCurve? SpectralResponse = null, CameraFileOrigin? Origin = null,
    CameraCalibrationRecord? Calibration = null)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraSensor";
}
public sealed record CameraSensorParameters(double ExposureMilliseconds = 10, double PixelPitchMicrometers = 3,
    double ReferenceIrradiance = 0.02, double RedQe = 0.5, double GreenQe = 0.6, double BlueQe = 0.45,
    double FullWellElectrons = 10000, double ReadNoiseElectrons = 2, double DarkElectronsPerSecond = 0.1,
    double PrnuPercent = 1, double DsnuElectrons = 0.5, double GainDnPerElectron = 0.35,
    int BlackLevel = 64, int BitDepth = 12, bool NoiseEnabled = true, int Seed = 42,
    CameraBayerPattern Pattern = CameraBayerPattern.RGGB);
public sealed record CameraIspParameters(bool Enabled = true, double RedGain = 1, double GreenGain = 1,
    double BlueGain = 1, double Gamma = 2.2, double Denoise = 0, double Sharpen = 0);
public sealed record CameraImagingRequest(CameraSource Source = CameraSource.ColorChart, string SourceFile = "",
    int Width = 128, int Height = 96, CameraOptics Optics = CameraOptics.CurrentLens,
    CameraAberration Aberration = CameraAberration.Diffraction, CameraSensorParameters? Sensor = null,
    CameraIspParameters? Isp = null, CameraResponseCurve? SpectralResponse = null, CameraSensorIdentity? SensorIdentity = null,
    CameraSpectralModel SpectralModel = CameraSpectralModel.ThreeBand, CameraSpectrumCurve? Illuminant = null,
    CameraSpectrumCurve? Reflectance = null, CameraIntegrationSettings? Integration = null,
    CameraSceneMode SceneMode = CameraSceneMode.Uniform, CameraMaterialScene? MaterialScene = null);
public sealed record CameraImage(int Width, int Height, IReadOnlyList<byte> Rgb24);
public sealed record CameraExperimentInputs(string EngineVersion, string? LensSnapshotJson = null,
    byte[]? ExternalImageBytes = null, CameraFileOrigin? ImageOrigin = null,
    string? LensDocumentName = null, long? LensSourceRevision = null);
public sealed record CameraExperiment(string Format, int SchemaVersion, CameraImagingResult B, CameraImagingResult? A = null)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraExperiment";
    public const string CurrentEngineVersion = "CameraLabV5-ExperimentCalibration/1";
}
public sealed record CameraCalibrationFrame(double ExposureMilliseconds, IReadOnlyList<ushort> RawDn);
public sealed record CameraCalibrationCapture(string Format, int SchemaVersion, string Name, string Source,
    string Device, string Conditions, double TemperatureCelsius, int Width, int Height, int BitDepth,
    CameraBayerPattern Pattern, double GainDnPerElectron, IReadOnlyList<CameraCalibrationFrame> DarkFrames,
    IReadOnlyList<CameraCalibrationFrame> FlatFrames, bool Synthetic = false)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraCalibration";
}
public sealed record CameraCalibrationRecord(CameraCalibrationCapture Capture, CameraFileOrigin Origin,
    double BlackLevelDn, double ReadNoiseElectrons, double DarkElectronsPerSecond,
    double DsnuElectrons, double PrnuPercent, string Method);
public sealed record CameraImagingResult(string DocumentName, long SourceRevision, CameraImagingRequest Request,
    CameraImage Source, CameraImage Optical, CameraImage Electrons, CameraImage Raw, CameraImage Noiseless,
    CameraImage Developed, IReadOnlyList<ushort> RawDn, IReadOnlyList<ushort> ReferenceRawDn,
    IReadOnlyList<double> PhotoElectrons, double MeanPhotoElectrons, double RawNoiseRmseDn,
    int SaturatedPixels, string OpticalModel, int OpticalFallbackCount, string ModelScope, CameraIntegrationDiagnostics? Integration = null,
    CameraExperimentInputs? Inputs = null);
public interface ICameraImagingService
{
    Task<CameraSensorProfile> CalibrateSensorAsync(string path, CameraSensorProfile baseline, CancellationToken cancellationToken = default);
    Task SaveExperimentAsync(CameraExperiment experiment, string path, CancellationToken cancellationToken = default);
    Task<CameraExperiment> OpenExperimentAsync(string path, CancellationToken cancellationToken = default);
    Task<CameraImagingResult> RecomputeAsync(CameraImagingResult run, CameraImagingRequest? request = null, CancellationToken cancellationToken = default);
    Task<CameraSensorProfile> ImportSensorAsync(string path, CancellationToken cancellationToken = default);
    Task<CameraResponseCurve> ImportResponseAsync(string path, CancellationToken cancellationToken = default);
    Task<CameraSpectrumCurve> ImportIlluminantAsync(string path, CancellationToken cancellationToken = default);
    Task<CameraSpectrumCurve> ImportReflectanceAsync(string path, CancellationToken cancellationToken = default);
    Task<CameraMaterialScene> ImportMaterialSceneAsync(string path, CancellationToken cancellationToken = default);
    Task SaveMaterialSceneAsync(CameraMaterialScene scene, string path, CancellationToken cancellationToken = default);
    Task SaveSensorAsync(CameraSensorProfile profile, string path, CancellationToken cancellationToken = default);
    Task<CameraImagingResult> SimulateAsync(CameraImagingRequest request, CancellationToken cancellationToken = default);
    Task ExportAsync(CameraImagingResult result, string path, CameraImagingResult? comparison = null,
        CancellationToken cancellationToken = default);
}
