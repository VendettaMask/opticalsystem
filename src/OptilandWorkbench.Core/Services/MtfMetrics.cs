using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public enum MtfMetricKind { Geometric, Fourier, SquareWave }

public readonly record struct DirectionalMtf(double Tangential, double Sagittal);

/// <summary>Single-frequency measurements using the formal grid MTF computation path.</summary>
public static partial class MtfMetrics
{
    public static DirectionalMtf EvaluateGrid(Optic optic, MtfMetricKind kind,
        int sampling, int wave, int fieldNumber, double frequency,
        bool scaleGeometric = true, FftMtfDataType dataType = FftMtfDataType.Modulation, bool requireValidData = false)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (sampling is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(sampling), "MTF 网格采样 Samp 当前支持 1..5（32..512 点/边）。");
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(wave), "波长编号不存在；0 表示多波长。");
        if (fieldNumber < (kind == MtfMetricKind.Geometric ? 1 : 0) || fieldNumber > optic.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), "视场编号不存在；衍射/方波 MTF 的 0 表示轴上无像差瞳孔。");
        if (!double.IsFinite(frequency) || frequency < 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "空间频率必须为有限非负数。");
        if (!Enum.IsDefined(dataType) || dataType == FftMtfDataType.SquareWave
            || (kind != MtfMetricKind.Fourier && dataType != FftMtfDataType.Modulation))
            throw new ArgumentOutOfRangeException(nameof(dataType));

        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        (double Hx, double Hy) field = fieldNumber == 0 ? (0.0, 0.0)
            : FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var pupilSampling = 32 << (sampling - 1);
        DirectionalMtf value;
        if (kind == MtfMetricKind.Geometric)
        {
            var result = MtfMethodEvaluator.EvaluatePolychromatic(optic, MtfComputationMethod.Geometric,
                field, wavelengths, frequency, new MtfComputationSettings(
                    GeometricRayCount: pupilSampling, Distribution: "uniform",
                    ScaleGeometricByDiffractionLimit: scaleGeometric, RequireValidData: requireValidData));
            value = new(result.Tangential, result.Sagittal);
        }
        else
        {
            var primary = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
            var results = new List<(Wavelength Wavelength, MtfResult Result)>();
            foreach (var wavelength in wavelengths)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                // Zero padding avoids circular pupil autocorrelation. This is the
                // general grid convention, not a captured-file FFT display preset.
                var psf = DiffractionEngine.ComputeFftPsf(optic, field, wavelength,
                    pupilSampling, 2 * pupilSampling, ignoreOpd: fieldNumber == 0,
                    aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary);
                if (requireValidData && !psf.Values.Cast<double>().Any(value => double.IsFinite(value) && value > 0))
                    throw new InvalidOperationException("MTF 没有有效的瞳孔/PSF 数据。");
                results.Add((wavelength, DiffractionEngine.ComputeFftMtf(psf, optic, wavelength)));
            }
            var type = kind == MtfMetricKind.SquareWave ? FftMtfDataType.SquareWave : dataType;
            var response = MtfMethodEvaluator.SamplePolychromaticAtFrequency(results, frequency, type);
            value = new(response.Tangential, response.Sagittal);
        }
        ComputationCancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(value.Tangential) || !double.IsFinite(value.Sagittal))
            throw new InvalidOperationException("MTF 结果不是有限值。");
        return value;
    }


}
