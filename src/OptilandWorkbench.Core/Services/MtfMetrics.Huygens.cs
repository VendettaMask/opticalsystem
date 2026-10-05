using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public static partial class MtfMetrics
{
    /// <summary>Scalar, single-configuration Huygens MTF using the shared PSF and frequency-plot path.</summary>
    public static DirectionalMtf EvaluateHuygens(Optic optic, int sampling, int wave,
        int fieldNumber, double frequency, double imageDeltaMicrometers = 0)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ComputationCancellation.ThrowIfCancellationRequested();
        // 128^4 exceeds the shared direct-PSF work budget. Never silently lower sampling.
        if (sampling is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(sampling), "惠更斯 MTF Samp 当前支持 1..2（瞳孔和像面均为 32/64 点每边），更高采样超过直接 PSF 计算预算。");
        if (optic.ImageSpaceAfocal)
            throw new NotSupportedException("惠更斯 MTF 操作数当前仅支持有焦像空间；无焦图像间隔单位尚未适配。");
        if (fieldNumber < 1 || fieldNumber > optic.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), "惠更斯 MTF 必须指定有效的正视场编号。");
        if (!double.IsFinite(frequency) || frequency < 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "空间频率必须为有限非负数（cycles/mm）。");
        if (!double.IsFinite(imageDeltaMicrometers) || imageDeltaMicrometers < 0)
            throw new ArgumentOutOfRangeException(nameof(imageDeltaMicrometers), "图像间隔必须为有限非负数（µm）；0 使用共享引擎自动间隔。");
        if (imageDeltaMicrometers > 0 && imageDeltaMicrometers / 1000 == 0)
            throw new ArgumentOutOfRangeException(nameof(imageDeltaMicrometers), "图像间隔太小，无法表示为毫米。");
        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        var field = FieldCoordinates.Normalize(optic.Fields,
            optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var size = 32 << (sampling - 1);
        var psf = MtfMethodEvaluator.ComputeHuygensPolychromaticPsf(optic, field, wavelengths,
            new MtfComputationSettings(PupilSampling: size, ImageSize: size,
                PixelPitchMillimeters: imageDeltaMicrometers / 1000,
                UsePolarization: false, UseZemaxHuygensSemantics: true));
        var total = 0.0;
        foreach (var intensity in psf.Values)
        {
            if (!double.IsFinite(intensity) || intensity < 0)
                throw new InvalidOperationException("惠更斯 PSF 含无效强度。");
            total += intensity;
        }
        if (!double.IsFinite(total) || total <= 0)
            throw new InvalidOperationException("没有可用的惠更斯 PSF 能量；请检查光线和孔径。");
        // Reuse the existing captured-analysis convention explicitly. Native MFE
        // numerical equivalence is a separate validation, not implied by this path.
        var result = MtfMethodEvaluator.SampleHuygensMtf(
            DiffractionEngine.ComputePsfMtf(psf, doubleTransformSize: true),
            2 * size, frequency, zemaxCompatible: true);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(result.Tangential) || !double.IsFinite(result.Sagittal))
            throw new InvalidOperationException("惠更斯 MTF 结果不是有限值。");
        return new(result.Tangential, result.Sagittal);
    }
}
