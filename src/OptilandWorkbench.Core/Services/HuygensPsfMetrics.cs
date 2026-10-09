using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public readonly record struct HuygensPsfMeasurement(
    double PeakStrehl, double CentroidX, double CentroidY, double ImageDeltaMillimeters);

/// <summary>Scalar PSF measurements in the current configuration, in image-surface local coordinates.</summary>
public static class HuygensPsfMetrics
{
    public static HuygensPsfMeasurement Evaluate(Optic optic, int wave, int fieldNumber,
        int pupilSamplingCode, int imageSamplingCode, bool strehlSampling = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (optic.ImageSpaceAfocal)
            throw new NotSupportedException("惠更斯 PSF 操作数当前仅支持有焦像空间。");
        if (fieldNumber < 1 || fieldNumber > optic.Fields.Count)
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), "必须指定有效的正视场编号。");
        if (pupilSamplingCode is < 1 or > 4 || imageSamplingCode is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(pupilSamplingCode), "采样代码支持 1..4（32..256），同时受共享直接 PSF 计算预算限制。");
        var pupilSize = 32 << (pupilSamplingCode - 1);
        var imageSize = 32 << (imageSamplingCode - 1);
        AnalysisResourceLimits.ValidateDirectPsfWork(pupilSize, imageSize);
        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        var field = FieldCoordinates.Normalize(optic.Fields, optic.Fields[fieldNumber - 1].X, optic.Fields[fieldNumber - 1].Y);
        var longest = wavelengths.MaxBy(w => w.Nanometers)!;
        var delta = DiffractionEngine.DefaultHuygensImageDeltaMillimeters(optic, field, longest, pupilSize);
        if (strehlSampling) delta /= 2;
        var synthesis = HuygensPsfSynthesis.Compute(optic, field, wavelengths,
            pupilSize, imageSize, delta);
        var psf = synthesis.Psf;
        var idealWeight = synthesis.IdealPeakWeight;
        var energy = 0.0; var xMoment = 0.0; var yMoment = 0.0; var peak = 0.0;
        for (var y = 0; y < imageSize; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < imageSize; x++)
            {
                var intensity = psf.Values[y, x];
                if (!double.IsFinite(intensity) || intensity < 0)
                    throw new InvalidOperationException("惠更斯 PSF 含无效强度。");
                energy += intensity; peak = Math.Max(peak, intensity);
                xMoment += intensity * (x - imageSize / 2);
                yMoment += intensity * (y - imageSize / 2);
            }
        }
        if (!double.IsFinite(energy) || energy <= 0 || !double.IsFinite(idealWeight) || idealWeight <= 0)
            throw new InvalidOperationException("没有可用的惠更斯 PSF 能量。");
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic, field,
            wavelengths.FirstOrDefault(w => w.IsPrimary) ?? wavelengths[0], optic.RayAimingEnabled);
        var globalCentroid = frame.Center + frame.TangentX * (xMoment / energy * delta)
            + frame.TangentY * (yMoment / energy * delta);
        var centroid = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(globalCentroid);
        var strehl = peak / (100 * idealWeight);
        if (!double.IsFinite(strehl) || !double.IsFinite(centroid.X) || !double.IsFinite(centroid.Y))
            throw new InvalidOperationException("惠更斯 PSF 测量不是有限值。");
        return new(strehl, centroid.X, centroid.Y, delta);
    }
}
