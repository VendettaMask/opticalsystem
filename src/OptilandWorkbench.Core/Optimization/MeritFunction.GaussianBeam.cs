namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateParaxialGaussianBeam(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var wavelength = RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var useX = IntegerDataParameter(definition, 0, 0) != 0;
        var waist = FiniteParameter(definition, 1, .05);
        var position = FiniteParameter(definition, 2, 0);
        var mSquared = FiniteParameter(definition, 3, 1);
        var data = optic.Paraxial.GaussianBeam(surface, wavelength.Micrometers, waist, position, mSquared, useX);
        return CanonicalType(definition.Type) switch
        {
            "GBPD" => data.SemiDivergenceRadians,
            "GBPP" => data.WaistToSurface,
            "GBPR" => double.IsFinite(data.PhaseRadius) ? data.PhaseRadius
                : throw new InvalidOperationException("束腰处相位曲率半径为无穷，不能作为有限评价函数约束。"),
            "GBPS" => data.Size,
            "GBPW" => data.Waist,
            "GBPZ" => data.RayleighRange,
            _ => throw new NotSupportedException(definition.Type)
        };
    }
}
