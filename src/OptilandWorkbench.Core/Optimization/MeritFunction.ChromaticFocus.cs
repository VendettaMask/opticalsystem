using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateChromaticFocus(Optic optic, MeritOperandDefinition definition)
    {
        var code = CanonicalType(definition.Type);
        var second = RequireWavelength(optic, ZemaxIntegerParameter(definition, 1, definition.Wavelength)).Micrometers;
        if (code == "LONA")
            return ChromaticFocusMetrics.LongitudinalAberration(optic, second, FiniteParameter(definition, 0, definition.Hx));
        var first = RequireWavelength(optic, ZemaxIntegerParameter(definition, 0, definition.Surface)).Micrometers;
        if (code == "LACL") return ChromaticFocusMetrics.LateralColor(optic, first, second);
        var zone = FiniteParameter(definition, 0, definition.Hx);
        return code == "AXCL"
            ? ChromaticFocusMetrics.AxialColor(optic, first, second, zone)
            : ChromaticFocusMetrics.Spherochromatism(optic, first, second, zone);
    }
}
