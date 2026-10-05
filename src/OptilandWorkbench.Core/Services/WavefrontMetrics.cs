using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

/// <summary>Formal reference-sphere/plane wavefront measurements, without a second optical engine or cache.</summary>
public static class WavefrontMetrics
{
    public static double Evaluate(Optic optic, int wave, double hx, double hy,
        IReadOnlyList<PupilSample> pupil, WavefrontReferenceKind reference,
        bool peakToValley = false, bool requireUnvignetted = false)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(wave), "波长编号不存在；RMS 的 0 表示多波长。");
        if (peakToValley && wave == 0)
            throw new NotSupportedException("峰谷波前当前需要正波长编号；Wave=0 的合成尚未验证。");
        if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 1 || Math.Abs(hy) > 1)
            throw new ArgumentOutOfRangeException(nameof(hx), "归一化视场必须为 -1..1 的有限数。");
        var wavelengths = wave == 0 ? optic.Wavelengths.ToArray() : [optic.Wavelengths[wave - 1]];
        if (wave == 0 && wavelengths.Any(w => !double.IsFinite(w.Weight) || w.Weight < 0))
            throw new InvalidOperationException("多波长权重必须为有限非负值。");
        var scale = wave == 0 ? wavelengths.Max(w => w.Weight) : 1;
        if (scale <= 0) throw new InvalidOperationException("没有正权重波长。");
        var primary = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var coordinates = pupil.Select(p => (p.X, p.Y)).ToArray();
        double weightedVariance = 0, totalWeight = 0;
        foreach (var wavelength in wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var weight = wave == 0 ? wavelength.Weight / scale : 1;
            if (weight == 0) continue;
            var result = WavefrontEngine.GenerateChiefRaySamples(optic, (hx, hy), wavelength,
                coordinates, aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary);
            if (requireUnvignetted && result.VignettedRayCount != 0)
                throw new InvalidOperationException("高斯波前求积需要未渐晕圆瞳；请改用矩形采样操作数。");
            var statistics = WavefrontStatistics.Measure(result.Samples, pupil, reference);
            if (peakToValley) return statistics.PeakToValley;
            weightedVariance += weight * statistics.Rms * statistics.Rms;
            totalWeight += weight;
        }
        var rms = Math.Sqrt(weightedVariance / totalWeight);
        return double.IsFinite(rms) ? rms : throw new InvalidOperationException("RMS 波前结果不是有限值。");
    }
}
