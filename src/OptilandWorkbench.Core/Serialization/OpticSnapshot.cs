namespace OptilandWorkbench.Core.Serialization;

public sealed record OpticSnapshot(
    int SchemaVersion,
    string Name,
    ApertureSnapshot? Aperture,
    string? BackendName,
    List<FieldPointSnapshot> Fields,
    List<WavelengthSnapshot> Wavelengths,
    List<SurfaceSnapshot> Surfaces);

public sealed record ApertureSnapshot(
    string Kind,
    double Value);

public sealed record FieldPointSnapshot(
    string Label,
    double XAngleDegrees,
    double YAngleDegrees,
    double Weight);

public sealed record WavelengthSnapshot(
    string Label,
    double Nanometers,
    double Weight,
    bool IsPrimary);

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
