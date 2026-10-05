using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateSeidelWave(Optic optic, MeritOperandDefinition definition)
    {
        var wavelength = RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var surface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        if (surface < 0 || (surface != 0 && !optic.SurfaceGroup.Items.Any(s => s.Number == surface)))
            throw new ArgumentOutOfRangeException(nameof(definition), "Surf 必须是实际表面编号，0 为全系统总和。");
        var result = SeidelMetrics.Calculate(optic, wavelength.Micrometers);
        var coefficients = surface == 0 ? result.Total : result.Surfaces.Single(s => s.SurfaceNumber == surface).Coefficients;
        var waves = coefficients.InWaves(wavelength.Micrometers / 1000);
        return waves[CanonicalType(definition.Type) switch { "SPHA" => 0, "COMA" => 1, "ASTI" => 2, "FCUR" => 3, _ => throw new NotSupportedException() }];
    }

    private static double EvaluatePetzvalCurvature(Optic optic, MeritOperandDefinition definition) =>
        SeidelMetrics.PetzvalCurvature(optic,
            RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength)).Micrometers);
}
