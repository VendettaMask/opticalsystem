using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateHuygensMtfMetric(Optic optic, MeritOperandDefinition definition)
    {
        if (definition.ZemaxDataParameters is not { Length: >= 5 })
            throw new ArgumentException("惠更斯 MTF 必须提供完整七参数；缺失的 Ima Delta 不能推定为零。");
        var sampling = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Field);
        var frequency = FiniteParameter(definition, 1, definition.SpatialFrequency);
        var polarization = IntegerDataParameter(definition, 2, 0);
        var allConfigurations = IntegerDataParameter(definition, 3, 0);
        var delta = FiniteParameter(definition, 4, 0);
        if (polarization != 0)
            throw new NotSupportedException("惠更斯 MTF 操作数当前仅支持 Pol=0；偏振标量近似不能替代完整偏振 MTF。");
        if (allConfigurations != 0)
            throw new NotSupportedException("惠更斯 MTF 操作数当前仅支持 All Conf=0；多配置合成尚未实现。");
        var value = MtfMetrics.EvaluateHuygens(optic, sampling, wave, field, frequency, delta);
        return CanonicalType(definition.Type)[^1] switch
        {
            'T' => value.Tangential,
            'S' => value.Sagittal,
            'A' => (value.Tangential + value.Sagittal) / 2,
            'N' => Math.Min(value.Tangential, value.Sagittal),
            'X' => Math.Max(value.Tangential, value.Sagittal),
            _ => throw new NotSupportedException(definition.Type)
        };
    }
}
