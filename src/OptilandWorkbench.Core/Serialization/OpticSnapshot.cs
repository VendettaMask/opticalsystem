namespace OptilandWorkbench.Core.Serialization;

public sealed record OpticSnapshot(
    int SchemaVersion,
    string Name,
    ApertureSnapshot? Aperture,
    string? BackendName,
    List<FieldPointSnapshot> Fields,
    List<WavelengthSnapshot> Wavelengths,
    List<SurfaceSnapshot> Surfaces,
    ComponentSnapshot? Apodization = null,
    string FieldDefinition = "Angle",
    bool ObjectSpaceTelecentric = false,
    bool FieldGroupTelecentric = false,
    List<RadiusPickupSnapshot>? RadiusPickups = null,
    SolveSettingsSnapshot? SolveSettings = null,
    List<MeritOperandSnapshot>? MeritOperands = null,
    EnvironmentSnapshot? Environment = null);

public sealed record EnvironmentSnapshot(
    bool MatchRefractiveIndexData = true,
    double TemperatureCelsius = 20.0,
    double PressureAtmospheres = 1.0);

public sealed record MeritOperandSnapshot(
    bool Enabled,
    string Type,
    int Surface,
    int Field,
    int Wavelength,
    double Hx,
    double Hy,
    double Px,
    double Py,
    double Target,
    double Weight,
    string Comment,
    int PupilRings = 3,
    int PupilArms = 6,
    double PupilObscuration = 0,
    string PupilSampling = "hexapolar");

public sealed record ApertureSnapshot(
    string Kind,
    double Value,
    bool ObjectSpaceTelecentric = false);

public sealed record FieldPointSnapshot(
    string Label,
    double XAngleDegrees,
    double YAngleDegrees,
    double Weight,
    double VignetteFactorX = 0,
    double VignetteFactorY = 0);

public sealed record WavelengthSnapshot(
    string Label,
    double Nanometers,
    double Weight,
    bool IsPrimary);

public sealed record RadiusPickupSnapshot(
    int SourceSurface,
    int TargetSurface,
    double Scale,
    double Offset);

public sealed record SolveSettingsSnapshot(
    double DesiredBackFocus,
    bool KeepImageAtBackFocus);

public sealed record SurfaceSnapshot(
    int Number,
    string Label,
    double Radius,
    double Thickness,
    string Material,
    string Coating,
    double SemiDiameter,
    double Conic,
    bool IsStop,
    bool IsReflective = false,
    SurfaceComponentSnapshot? Components = null,
    bool RadiusVariable = false,
    bool ThicknessVariable = false,
    bool SemiDiameterFixed = false,
    CoordinateSystemSnapshot? CoordinateSystem = null);

public sealed record CoordinateSystemSnapshot(
    double OriginX,
    double OriginY,
    double OriginZ,
    double RotationXDegrees,
    double RotationYDegrees,
    double RotationZDegrees);

public sealed record SurfaceComponentSnapshot(
    string GeometryKind,
    string MaterialBefore,
    string MaterialAfter,
    string CoatingKind,
    string InteractionKind,
    string? PhysicalApertureKind,
    string? ScatteringKind,
    ComponentSnapshot? Geometry = null,
    ComponentSnapshot? MaterialBeforeComponent = null,
    ComponentSnapshot? MaterialAfterComponent = null,
    ComponentSnapshot? Coating = null,
    ComponentSnapshot? Interaction = null,
    ComponentSnapshot? PhysicalAperture = null,
    ComponentSnapshot? Scattering = null);
