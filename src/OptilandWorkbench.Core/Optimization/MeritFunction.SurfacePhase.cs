using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateSurfacePhase(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var wavelength = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths.FirstOrDefault()
            ?? throw new InvalidOperationException("相位计算需要主波长。");
        var quantity = type is "PSLP" or "QSLP" ? SurfacePhaseQuantity.Slope : SurfacePhaseQuantity.Phase;
        if (type is "DPHS" or "QSLP")
            return SurfacePhaseMetrics.Evaluate(surface, quantity, wavelength.Nanometers,
                IntegerDataParameter(definition, 0, 1), IntegerDataParameter(definition, 1, 0),
                type == "QSLP" ? IntegerDataParameter(definition, 2, 0) : 0)
                .Value(ZemaxIntegerParameter(definition, 1, 1));
        if (type == "SPHS" && IntegerDataParameter(definition, 2, 0) != 0)
            throw new NotSupportedException("SPHS 当前支持 Data=0；移除倾斜/光焦度项的数据尚未接通。");
        return SurfacePhaseMetrics.AtPoint(surface, quantity, wavelength.Nanometers,
            ZemaxDataParameter(definition, 0, definition.Hx), ZemaxDataParameter(definition, 1, definition.Hy),
            ZemaxIntegerParameter(definition, 1, 1), IntegerDataParameter(definition, type == "SPHS" ? 3 : 2, 0),
            type == "PSLP" ? IntegerDataParameter(definition, 3, 0) : 0);
    }
}
