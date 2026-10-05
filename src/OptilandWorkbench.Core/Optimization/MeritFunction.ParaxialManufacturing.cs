using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateParaxialRayData(Optic optic, MeritOperandDefinition definition)
    {
        var row = SequentialRayDefinition(optic, definition);
        var wavelength = RequireWavelength(optic, row.Wavelength).Micrometers;
        var field = row.ZemaxDataParameters.Length > 0 || row.Field <= 0
            ? (X: row.Hx, Y: row.Hy) : ResolveNormalizedField(optic, row);
        var sample = optic.Paraxial.TraceNormalizedRay(row.Surface, field.X, field.Y, row.Px, row.Py, wavelength);
        var type = CanonicalType(definition.Type);
        if (type is "PANA" or "PANB" or "PANC")
        {
            var normal = PositiveZNormal(ResolveSurface(optic, row.Surface), sample.Position);
            return type switch { "PANA" => normal.X, "PANB" => normal.Y, _ => normal.Z };
        }
        return type switch
        {
            "PARX" => sample.Position.X,
            "PARY" => sample.Position.Y,
            "PARZ" => sample.Position.Z,
            "PARR" => Math.Sqrt(sample.Position.X * sample.Position.X + sample.Position.Y * sample.Position.Y),
            "PARA" => sample.Direction.X,
            "PARB" => sample.Direction.Y,
            "PARC" => sample.Direction.Z,
            "PATX" => RaySlope(sample.Direction.X, sample.Direction.Z),
            "PATY" => RaySlope(sample.Direction.Y, sample.Direction.Z),
            _ => throw new NotSupportedException(type)
        };
    }

    private static double EvaluateMarginalYni(Optic optic, MeritOperandDefinition definition) =>
        optic.Paraxial.MarginalRayYni(
            ZemaxIntegerParameter(definition, 0, definition.Surface),
            RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength)).Micrometers);

    private static double EvaluateElementFocalLengthInAir(Optic optic, MeritOperandDefinition definition) =>
        optic.Paraxial.ElementEffectiveFocalLengthInAir(
            ZemaxIntegerParameter(definition, 0, definition.Surface),
            RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength)).Micrometers);

    private static SurfaceDiameterMode DiameterMode(MeritOperandDefinition definition)
    {
        var mode = FiniteParameter(definition, 1, definition.Hy);
        return mode switch
        {
            0 => SurfaceDiameterMode.Mechanical,
            1 => SurfaceDiameterMode.Clear,
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "Mode 必须为 0（机械半口径）或 1（净半口径）。")
        };
    }

    private static double EvaluateDiameter(Optic optic, MeritOperandDefinition definition) =>
        SurfaceManufacturingMetrics.Diameter(
            ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)), DiameterMode(definition));

    private static double EvaluateDiameterThicknessExtreme(Optic optic, MeritOperandDefinition definition, bool maximum)
    {
        var mode = DiameterMode(definition);
        if (ZemaxIntegerParameter(definition, 1, definition.Wavelength) < 0)
            throw new ArgumentOutOfRangeException(nameof(definition), "结束表面编号不能为负数。");
        var wavelength = RequireWavelength(optic, 0).Nanometers;
        var values = SurfaceRange(optic, definition, includeEndSurface: true)
            .Where(surface => HasNonUnityThicknessIndex(surface, wavelength))
            .Select(surface => SurfaceManufacturingMetrics.DiameterToThickness(surface, mode)).ToArray();
        if (values.Length == 0) throw new InvalidOperationException("指定范围内没有可计算径厚比的玻璃空间。");
        return maximum ? values.Max() : values.Min();
    }

    private static bool HasNonUnityThicknessIndex(OpticalSurface surface, double wavelengthNanometers)
    {
        if (surface.IsReflective) return false;
        var index = surface.MaterialAfter.RefractiveIndex(wavelengthNanometers);
        if (!double.IsFinite(index) || index <= 0)
            throw new InvalidOperationException("径厚比空间折射率必须为有限正数。");
        return index != 1;
    }

    private static double EvaluateBlankThickness(Optic optic, MeritOperandDefinition definition)
    {
        var front = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        if (!IsGlassSpace(front, RequireWavelength(optic, 0).Nanometers))
            throw new InvalidOperationException("BLTH 需要指定表面之后为玻璃空间。");
        return SurfaceManufacturingMetrics.BlankThickness(front, ResolveNextSurface(optic, front.Number),
            ZemaxIntegerParameter(definition, 1, definition.Wavelength), DiameterMode(definition));
    }
}
