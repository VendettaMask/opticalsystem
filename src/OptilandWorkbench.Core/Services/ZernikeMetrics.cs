using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public enum ZernikeBasisKind { Fringe, Standard, Annular }

public sealed record ZernikeFitResult(IReadOnlyList<ZernikeCoefficient> Coefficients,
    double RmsZero, WavefrontStatistics Chief, WavefrontStatistics Centroid,
    double RmsFitError, double MaximumFitError)
{
    public double Value(int term) => term switch
    {
        -8 => Centroid.PeakToValley,
        -7 => Chief.PeakToValley,
        -6 => RmsZero,
        -5 => Chief.Rms,
        -4 => Centroid.Rms,
        -3 => Centroid.Rms * Centroid.Rms,
        -2 => Math.Exp(-Math.Pow(2 * Math.PI * Centroid.Rms, 2)),
        -1 => RmsFitError,
        0 => MaximumFitError,
        _ when term > 0 && term <= Coefficients.Count => Coefficients[term - 1].Value,
        _ => throw new ArgumentOutOfRangeException(nameof(term))
    };
}

/// <summary>Uniform-pupil wavefront fitting through the formal ray, OPD and Zernike engines.</summary>
public static class ZernikeMetrics
{
    public static ZernikeFitResult Evaluate(Optic optic, int wave, int field, int sampling,
        ZernikeBasisKind kind, int maximumTerm, double obscuration = 0, int vertex = 0)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        Validate(kind, maximumTerm, obscuration);
        if (wave < 1 || wave > optic.Wavelengths.Count) throw new ArgumentOutOfRangeException(nameof(wave), "ZERN 需要正波长编号。");
        if (field < 1 || field > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(field), "ZERN 需要正视场编号。");
        if (sampling is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(sampling), "ZERN Samp 支持 1..5（32..512 点/边）。");
        if (vertex != 0) throw new NotSupportedException("ZERN 当前仅支持 Vertex=0；顶点参考倾斜约定尚未核实。");
        var grid = 32 << (sampling - 1);
        // Bound allocations before tracing; the fitter enforces its actual valid-sample bound too.
        if ((long)grid * grid * maximumTerm > ZernikeFitEngine.MaximumFitMatrixValues
            || (long)grid * grid * maximumTerm * maximumTerm > ZernikeFitEngine.MaximumFitWork)
            throw new ArgumentOutOfRangeException(nameof(sampling), "Zernike 拟合矩阵超过共享资源上限。");
        var point = optic.Fields[field - 1];
        var coordinates = FieldCoordinates.Normalize(optic.Fields, point.X, point.Y);
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, coordinates, optic.Wavelengths[wave - 1],
            grid, aimAtStop: optic.RayAimingEnabled, zemaxCentered: true);
        return Fit(wavefront.Samples, kind, maximumTerm, obscuration);
    }

    public static ZernikeFitResult Fit(IReadOnlyList<WavefrontSample> samples, ZernikeBasisKind kind,
        int maximumTerm, double obscuration = 0, bool requireFullRank = true)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        Validate(kind, maximumTerm, obscuration);
        foreach (var s in samples)
            if (!double.IsFinite(s.Intensity) || s.Intensity < 0 || (s.Intensity > 0 && (!double.IsFinite(s.OpdWaves)
                || !double.IsFinite(s.NormalizedPupilX) || !double.IsFinite(s.NormalizedPupilY))))
                throw new InvalidOperationException("Zernike 有效样本必须有限，强度必须为有限非负值。");
        var valid = samples.Where(s => s.Intensity > 0 && (kind != ZernikeBasisKind.Annular
            || s.NormalizedPupilX * s.NormalizedPupilX + s.NormalizedPupilY * s.NormalizedPupilY >= obscuration * obscuration - 1e-12)).ToArray();
        if (valid.Length == 0) throw new InvalidOperationException("Zernike 拟合没有有效波前样本。");
        var coefficients = kind switch
        {
            ZernikeBasisKind.Fringe => ZernikeFitEngine.FitFringe(valid, maximumTerm, requireFullRank),
            ZernikeBasisKind.Standard => ZernikeFitEngine.FitStandard(valid, maximumTerm, requireFullRank),
            _ => ZernikeFitEngine.FitAnnular(valid, maximumTerm, obscuration, requireFullRank)
        };
        var pupil = valid.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var chief = WavefrontStatistics.Measure(valid, pupil, WavefrontReferenceKind.ChiefRay);
        var centroid = WavefrontStatistics.Measure(valid, pupil, WavefrontReferenceKind.Centroid);
        var residuals = new double[valid.Length];
        for (var i = 0; i < valid.Length; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var s = valid[i];
            var fit = kind switch
            {
                ZernikeBasisKind.Fringe => ZernikeFitEngine.Evaluate(coefficients, s.NormalizedPupilX, s.NormalizedPupilY),
                ZernikeBasisKind.Standard => ZernikeFitEngine.EvaluateStandard(coefficients, s.NormalizedPupilX, s.NormalizedPupilY),
                _ => ZernikeFitEngine.EvaluateAnnular(coefficients, s.NormalizedPupilX, s.NormalizedPupilY, obscuration)
            };
            residuals[i] = s.OpdWaves - fit;
        }
        var result = new ZernikeFitResult(coefficients, Math.Sqrt(valid.Average(s => s.OpdWaves * s.OpdWaves)),
            chief, centroid, Math.Sqrt(residuals.Average(v => v * v)), residuals.Max(Math.Abs));
        if (Enumerable.Range(-8, 9).Any(term => !double.IsFinite(result.Value(term))))
            throw new InvalidOperationException("Zernike 统计包含非有限值。");
        return result;
    }

    private static void Validate(ZernikeBasisKind kind, int terms, double obscuration)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (terms < 1 || terms > (kind == ZernikeBasisKind.Fringe ? 37 : 231)) throw new ArgumentOutOfRangeException(nameof(terms));
        if (!double.IsFinite(obscuration) || (kind == ZernikeBasisKind.Annular && (obscuration < 0 || obscuration > .95)))
            throw new ArgumentOutOfRangeException(nameof(obscuration));
    }
}
