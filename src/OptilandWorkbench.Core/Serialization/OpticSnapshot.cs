namespace OptilandWorkbench.Core.Serialization;

public sealed record OpticSnapshot(
    string Name,
    List<FieldPointSnapshot> Fields,
    List<WavelengthSnapshot> Wavelengths,
    List<SurfaceSnapshot> Surfaces);

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
    bool IsStop);
