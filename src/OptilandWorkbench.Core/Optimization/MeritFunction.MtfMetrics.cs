using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateMtfMetric(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var geometric = type.StartsWith("GMT", StringComparison.Ordinal);
        var squareWave = type.StartsWith("MSW", StringComparison.Ordinal);
        var sampling = ZemaxIntegerParameter(definition, 0, 1);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Field);
        var frequency = FiniteParameter(definition, 1, definition.SpatialFrequency);
        var grid = IntegerDataParameter(definition, geometric ? 3 : 2, 1);
        if (grid != 1)
            throw new NotSupportedException("MTF 操作数当前仅支持 Grid=1 网格计算；Grid=0 专用稀疏采样尚未接入。");
        var scale = !geometric || IntegerDataParameter(definition, 2, 0) == 0;
        var supportsDataType = type is "MTFA" or "MTFS" or "MTFT";
        var data = supportsDataType ? IntegerDataParameter(definition, 3, 0) : 0;
        if (data is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(definition), "MTF Data Type 必须为 0..3。");
        var response = MtfMetrics.EvaluateGrid(optic,
            geometric ? MtfMetricKind.Geometric : squareWave ? MtfMetricKind.SquareWave : MtfMetricKind.Fourier,
            sampling, wave, field, frequency, scale, (FftMtfDataType)data);
        // Core phase is in radians; MFE's phase output is in degrees.
        var unitScale = data == 3 ? 180 / Math.PI : 1;
        var tangential = response.Tangential * unitScale;
        var sagittal = response.Sagittal * unitScale;
        return type[^1] switch
        {
            'T' => tangential,
            'S' => sagittal,
            'A' => (tangential + sagittal) / 2,
            'N' => Math.Min(tangential, sagittal),
            'X' => Math.Max(tangential, sagittal),
            _ => throw new NotSupportedException(type)
        };
    }
}
