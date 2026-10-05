using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateHuygensPsfMetric(Optic optic, MeritOperandDefinition definition)
    {
        var strehl = CanonicalType(definition.Type) == "STRH";
        var wave = ZemaxIntegerParameter(definition, strehl ? 1 : 0, definition.Wavelength);
        var field = strehl ? IntegerDataParameter(definition, 0, definition.Field)
            : ZemaxIntegerParameter(definition, 1, definition.Field);
        var polarization = IntegerDataParameter(definition, strehl ? 1 : 0, 0);
        var allConfigurations = IntegerDataParameter(definition, strehl ? 2 : 3, 0);
        var pupil = strehl ? ZemaxIntegerParameter(definition, 0, 1) : IntegerDataParameter(definition, 1, 1);
        var image = strehl ? pupil : IntegerDataParameter(definition, 2, 1);
        if (polarization != 0)
            throw new NotSupportedException("惠更斯 PSF 操作数当前仅支持 Pol=0，尚未提供完整偏振模式。");
        if (allConfigurations != 0)
            throw new NotSupportedException("惠更斯 PSF 操作数当前仅支持 All Conf=0，多配置相干合成尚未实现。");
        var result = HuygensPsfMetrics.Evaluate(optic, wave, field, pupil, image, strehl);
        return strehl ? result.PeakStrehl
            : CanonicalType(definition.Type) == "CEHX" ? result.CentroidX : result.CentroidY;
    }
}
