using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Application.Contracts;

public static class ToleranceSurfaceFormExtensions
{
    public static SurfaceFormMode? SurfaceFormModeFor(this ToleranceOperandKind kind) => kind switch
    {
        ToleranceOperandKind.FringeZernikeIrregularity => SurfaceFormMode.FringeZernike,
        ToleranceOperandKind.StandardZernikeIrregularity => SurfaceFormMode.StandardZernike,
        ToleranceOperandKind.IsoPower => SurfaceFormMode.IsoPower,
        ToleranceOperandKind.IsoIrregularityPv => SurfaceFormMode.IsoIrregularityPv,
        ToleranceOperandKind.IsoRotationalIrregularityPv => SurfaceFormMode.IsoRotationalIrregularityPv,
        ToleranceOperandKind.IsoIrregularityRms => SurfaceFormMode.IsoIrregularityRms,
        _ => null
    };

    public static bool UsesZernikeTerms(this ToleranceOperandKind kind) =>
        kind is ToleranceOperandKind.FringeZernikeIrregularity or ToleranceOperandKind.StandardZernikeIrregularity;

    public static bool UsesIsoStatistics(this ToleranceOperandKind kind) => kind is
        ToleranceOperandKind.IsoIrregularityPv or ToleranceOperandKind.IsoRotationalIrregularityPv
        or ToleranceOperandKind.IsoIrregularityRms;

    public static bool UsesIsoUnits(this ToleranceOperandKind kind) =>
        kind == ToleranceOperandKind.IsoPower || kind.UsesIsoStatistics();
}
