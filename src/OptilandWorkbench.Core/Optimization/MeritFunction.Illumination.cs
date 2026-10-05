using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateIllumination(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var sampling = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Field);
        var polarization = IntegerDataParameter(definition, 1, 0);
        if (sampling is < 5 or > 128)
            throw new ArgumentOutOfRangeException(nameof(definition), "RELI/EFNO 的 Samp 必须在 5..128，每边使用指定数量的像方方向余弦网格点。");
        if (wave <= 0)
            throw new NotSupportedException("RELI/EFNO 需要明确的正波长编号；Wave=0 的原生语义尚未核实。");
        var wavelength = RequireWavelength(optic, wave).Micrometers;
        if (field < 1 || field > optic.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(definition), "RELI/EFNO 需要有效的一起始视场编号。");
        if (polarization is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(definition), "RELI/EFNO 的 Pol 必须为 0 或 1。");
        if (optic.ImageSpaceAfocal)
            throw new NotSupportedException("RELI/EFNO 当前需要有焦像方系统。");
        var selected = optic.Fields[field - 1];
        var normalized = FieldCoordinates.Normalize(optic.Fields, selected.X, selected.Y);
        if (type == "RELI")
            return IlluminationMetrics.RelativeToAxis(optic, normalized, wavelength, sampling,
                polarization == 1, IlluminationSamplingKind.UniformImageCosine);

        // RELI explicitly removes factors. EFNO's native policy has not been captured;
        // never silently choose either policy for a non-identity input.
        if (optic.Fields.Any(item => PupilVignetting.FromField(item) != PupilVignetting.Identity))
            throw new NotSupportedException("EFNO 的非零渐晕因子处理尚待原生核实；请先用 CVIG 清除渐晕因子。");
        return IlluminationMetrics.Evaluate(optic, normalized, wavelength, sampling,
            usePolarization: polarization == 1, sampling: IlluminationSamplingKind.UniformImageCosine).EffectiveFNumber;
    }
}
