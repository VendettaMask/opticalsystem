using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public enum WavefrontReferenceKind { ChiefRay, Centroid }

public sealed record WavefrontStatistics(double Rms, double PeakToValley, int SampleCount)
{
    /// <summary>Geometric pupil-weighted OPD statistics in waves. Intensity selects valid rays;
    /// it is not applied again as an integration weight.</summary>
    public static WavefrontStatistics Measure(IReadOnlyList<WavefrontSample> wavefront,
        IReadOnlyList<PupilSample> pupil, WavefrontReferenceKind reference)
    {
        if (wavefront.Count != pupil.Count) throw new ArgumentException("波前与瞳孔采样数量不一致。");
        if (!Enum.IsDefined(reference)) throw new ArgumentOutOfRangeException(nameof(reference));
        var samples = new List<(double X, double Y, double Opd, double Weight)>();
        for (var i = 0; i < pupil.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var s = wavefront[i]; var weight = pupil[i].Weight;
            if (!double.IsFinite(weight) || weight < 0 || !double.IsFinite(s.Intensity) || s.Intensity < 0)
                throw new InvalidOperationException("波前样本权重必须为有限非负值。");
            if (weight == 0 || s.Intensity == 0) continue;
            if (!double.IsFinite(s.OpdWaves) || !double.IsFinite(s.NormalizedPupilX) || !double.IsFinite(s.NormalizedPupilY))
                throw new InvalidOperationException("有效波前样本包含非有限值。");
            samples.Add((s.NormalizedPupilX, s.NormalizedPupilY, s.OpdWaves, weight));
        }
        if (samples.Count == 0)
            throw new AnalysisDataUnavailableException("Wavefront statistics", "no valid weighted wavefront samples");

        var weightScale = samples.Max(s => s.Weight);
        var weights = samples.Select(s => s.Weight / weightScale).ToArray();
        var total = weights.Sum();
        var residual = samples.Select(s => s.Opd).ToArray();
        var bases = new List<double[]> { samples.Select(_ => 1.0).ToArray() };
        if (reference == WavefrontReferenceKind.Centroid)
        {
            bases.Add(samples.Select(s => s.X).ToArray());
            bases.Add(samples.Select(s => s.Y).ToArray());
        }
        // Weighted, re-orthogonalized projection onto piston and (optionally) both
        // pupil tilts. Dependent columns do not change the fitted sample values.
        var orthogonal = new List<double[]>();
        foreach (var basis in bases)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var originalNorm = Dot(basis, basis);
            for (var pass = 0; pass < 2; pass++)
                foreach (var q in orthogonal)
                {
                    var coefficient = Dot(basis, q);
                    for (var i = 0; i < basis.Length; i++) basis[i] -= coefficient * q[i];
                }
            var norm = Math.Sqrt(Dot(basis, basis));
            if (norm <= 1e-14 * Math.Sqrt(originalNorm) || norm == 0) continue;
            for (var i = 0; i < basis.Length; i++) basis[i] /= norm;
            var projection = Dot(residual, basis);
            for (var i = 0; i < residual.Length; i++) residual[i] -= projection * basis[i];
            orthogonal.Add(basis);
        }
        var rms = Math.Sqrt(Dot(residual, residual) / total);
        var peakToValley = residual.Max() - residual.Min();
        if (!double.IsFinite(rms) || !double.IsFinite(peakToValley))
            throw new InvalidOperationException("波前统计结果不是有限值。");
        return new(rms, peakToValley, samples.Count);

        double Dot(double[] a, double[] b)
        {
            double sum = 0;
            for (var i = 0; i < weights.Length; i++) sum += weights[i] * a[i] * b[i];
            return sum;
        }
    }
}
