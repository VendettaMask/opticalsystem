namespace OpticalSystemDesign.Application.Contracts;

public enum CoordinateReturnKind { None, Orientation, PositionAndOrientation }
public sealed record CoordinateBreakSettingsDto(double X, double Y, double TiltX, double TiltY, double TiltZ,
    bool TiltFirst, CoordinateReturnKind ReturnKind = CoordinateReturnKind.None, int ReturnSurface = 0,
    IReadOnlyList<bool>? Variables = null);
public sealed record CoordinateReferenceChoiceDto(int Number, string Label);
public sealed record CoordinateBreakEditDto(long Revision, int SurfaceNumber, bool IsCoordinateBreak, bool CanEdit,
    CoordinateBreakSettingsDto Settings, IReadOnlyList<int> ControlledParameters,
    IReadOnlyList<CoordinateReferenceChoiceDto> References, string? Restriction, bool UsesStoredBasis);
