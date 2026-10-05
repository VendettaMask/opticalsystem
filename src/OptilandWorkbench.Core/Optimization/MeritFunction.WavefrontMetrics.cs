using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateWavefrontMetric(Optic optic, MeritOperandDefinition definition)
    {
        var type = CanonicalType(definition.Type);
        var sampling = ZemaxIntegerParameter(definition, 0, definition.PupilRings);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var gaussian = type is "RWCE" or "RWCH" or "MWCE" or "MWCH";
        if (sampling < 1 || sampling > (gaussian ? 32 : 256))
            throw new ArgumentOutOfRangeException(nameof(definition), "高斯环数为 1..32；矩形 Samp 为 1..256。");
        var pupil = gaussian ? ApertureSampler.GenerateGaussianQuadrature(sampling, 6)
            : RayBundleMetrics.RectangularPupil(2 * sampling + 1);
        return WavefrontMetrics.Evaluate(optic, wave,
            FiniteParameter(definition, 0, definition.Hx), FiniteParameter(definition, 1, definition.Hy), pupil,
            type.EndsWith('E') ? WavefrontReferenceKind.Centroid : WavefrontReferenceKind.ChiefRay,
            peakToValley: type.StartsWith('M'), requireUnvignetted: gaussian);
    }
}
