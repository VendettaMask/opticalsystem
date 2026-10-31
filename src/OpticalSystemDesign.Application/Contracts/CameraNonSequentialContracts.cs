using System.Text.Json.Serialization;

namespace OpticalSystemDesign.Application.Contracts;

public sealed record CameraNscSurfaceBsdf(Guid ObjectId, int ObjectNumber, string Name, NonSequentialSurfaceBsdf Model,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NonSequentialVector3? WorldMaterialAxisX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NonSequentialVector3? WorldMaterialAxisY = null);

public sealed record CameraNscDetectorDefinition(Guid Id, string Name, double WidthMillimeters,
    double HeightMillimeters, int PixelsX, int PixelsY, bool FrontOnly, bool Absorb,
    NonSequentialVector3 WorldOrigin, NonSequentialVector3 WorldXAxis,
    NonSequentialVector3 WorldYAxis, NonSequentialVector3 WorldNormal);

/// <summary>Frozen, band-integrated W/pixel in NSC negative-local-Y-first order. Never a display raster.</summary>
public sealed record CameraNscPowerBand(int WavelengthNumber, double WavelengthNanometers,
    IReadOnlyList<double> PixelPowerWatts);

public sealed record CameraNscDetectorInput(string Format, int SchemaVersion, Guid SessionId,
    string SceneHash, long SourceRevision, DateTimeOffset CreatedUtc, long BranchCount,
    string? TraceConfigurationFingerprint, string? SavedPathFilter, string PathFilter,
    int RandomSeed, NonSequentialSplittingMode SplittingMode,
    CameraNscDetectorDefinition Detector, IReadOnlyList<NonSequentialWavelengthDto> Wavelengths,
    IReadOnlyList<CameraNscPowerBand> Bands, NonSequentialTraceSettings TraceSettings,
    int TracePassCount, IReadOnlyList<Guid> SourceObjectIds, IReadOnlyList<string> Warnings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CameraNscSurfaceBsdf>? SurfaceBsdfs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SurfaceBsdfProtocol = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NonSequentialBsdfStatistics? BsdfStatistics = null)
{
    public const string FormatIdentifier = "OpticalSystemDesign.CameraNscDetector";
}
