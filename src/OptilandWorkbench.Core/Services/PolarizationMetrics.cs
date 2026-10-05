using System.Numerics;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public static class PolarizationMetrics
{
    /// <summary>Principal local-XY phase difference, not a Jones-matrix eigen-retardance.</summary>
    public static double PhaseDifference(ComplexElectricField field)
    {
        var x = field.X.Magnitude; var y = field.Y.Magnitude;
        if (!double.IsFinite(field.SquaredNorm) || !double.IsFinite(x) || !double.IsFinite(y) || x <= 0 || y <= 0)
            throw new InvalidOperationException("Ex/Ey 必须均为非零有限电场；零分量的相位差未定义。");
        var relative = field.X / x * Complex.Conjugate(field.Y / y);
        return Math.Atan2(relative.Imaginary, relative.Real);
    }

    /// <summary>
    /// Gaussian-pupil and spectral weighted RMS of principal Ex/Ey phase at the image.
    /// No piston/mean subtraction or transmitted-power reweighting is applied.
    /// Native wrap, weighting and input-reference conventions still require captures.
    /// </summary>
    public static double RmsRetardance(Optic optic, int wave, double hx, double hy, int rings, JonesInputState input)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(input);
        ComputationCancellation.ThrowIfCancellationRequested();
        if (rings is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(rings), "RRET 高斯环数须为 1..32。");
        if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 1 || Math.Abs(hy) > 1)
            throw new ArgumentOutOfRangeException(nameof(hx), "RRET 归一化视场须在 [-1,1] 内。");
        if (optic.SurfaceGroup.Items.Count < 2) throw new InvalidOperationException("RRET 需要物面和像面。");
        if (optic.SurfaceGroup.Items.Any(s => s.PhysicalAperture is not (null or CircularAperture)))
            throw new NotSupportedException("RRET 高斯求积当前仅支持未渐晕圆瞳；环形及非圆孔径未接入。");
        if (optic.Fields.Any(f => f.VignetteFactorX != 0 || f.VignetteFactorY != 0
            || f.VignetteDecenterX != 0 || f.VignetteDecenterY != 0 || f.VignetteAngleDegrees != 0))
            throw new NotSupportedException("RRET 当前不支持视场渐晕变换；需要未渐晕圆瞳。");
        var wavelengths = MetricWavelengthSelection.Select(optic, wave);
        var pupil = ApertureSampler.GenerateGaussianQuadrature(rings, 6);
        if ((long)wavelengths.Length * pupil.Count > 65536)
            throw new ArgumentException("RRET 光线数超过本次计算预算。");
        var frame = optic.SurfaceGroup.Items[0].CoordinateSystem;
        double sum = 0, weightSum = 0;
        foreach (var wavelength in wavelengths)
        {
            var generate = optic.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(hx, hy,
                wavelength.Micrometers, aimAtStop: optic.RayAimingEnabled);
            foreach (var point in pupil)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var ray = generate(point.X, point.Y);
                // Common path phase cancels in Ex/Ey. Omitting it avoids precision
                // loss for huge optical paths without dropping interface retardance.
                var result = optic.SequentialRayTracer.TracePolarized(ray, input,
                    inputCoordinates: frame, includePropagationPhase: false);
                var phase = PhaseDifference(result.LocalField);
                var weight = wavelength.Weight * point.Weight;
                sum += weight * phase * phase;
                weightSum += weight;
            }
        }
        var rms = Math.Sqrt(sum / weightSum);
        return double.IsFinite(rms) ? rms : throw new InvalidOperationException("RRET 没有有限 RMS 相位差结果。");
    }
}
