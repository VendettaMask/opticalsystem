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
    SolveSettingsSnapshot? SolveSettings = null);

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
    SurfaceComponentSnapshot? Components = null);

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
