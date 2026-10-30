using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Coatings;

public sealed record AdaptiveThinFilmSamples(ThinFilmSample[] Samples, int Evaluations, bool LimitReached, double MaximumObservedChordError);

public static class AdaptiveThinFilmSpectrum
{
    // An observed interpolation-error guide, never an independent passband/acceptance certificate.
    public static AdaptiveThinFilmSamples Sample(CoherentThinFilmSolver solver, IEnumerable<double> anchors, double angle,
        ThinFilmPolarization polarization, double powerTolerance, int maximumSamples)
    {
        if (!double.IsFinite(powerTolerance) || powerTolerance <= 0 || maximumSamples is < 3 or > ThinFilmSpectrum.MaximumSamples)
            throw new ArgumentException("自适应光谱采样参数无效。");
        var points = new SortedDictionary<double, ThinFilmSample>();
        foreach (var w in anchors.Distinct().Order())
        {
            if (points.Count == maximumSamples) throw new ArgumentException("初始光谱锚点超过自适应采样上限。");
            points[w] = new(w, solver.Evaluate(w, angle, polarization));
        }
        if (points.Count < 3) throw new ArgumentException("至少需要三个光谱锚点。");
        var queue = new Queue<(ThinFilmSample A, ThinFilmSample B)>();
        var ordered = points.Values.ToArray();
        for (var i = 1; i < ordered.Length; i++) queue.Enqueue((ordered[i - 1], ordered[i]));
        var maximumError = 0.0;
        while (queue.Count > 0 && points.Count < maximumSamples)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var (a, b) = queue.Dequeue(); var wave = a.WavelengthNanometers + (b.WavelengthNanometers - a.WavelengthNanometers) / 2;
            if (wave <= a.WavelengthNanometers || wave >= b.WavelengthNanometers) continue;
            var m = new ThinFilmSample(wave, solver.Evaluate(wave, angle, polarization)); points[wave] = m;
            var error = Math.Max(Math.Abs(m.Power.Reflectance - (a.Power.Reflectance + b.Power.Reflectance) / 2),
                Math.Max(Math.Abs(m.Power.Transmittance - (a.Power.Transmittance + b.Power.Transmittance) / 2),
                    Math.Abs(m.Power.Absorptance - (a.Power.Absorptance + b.Power.Absorptance) / 2)));
            maximumError = Math.Max(maximumError, error);
            if (error > powerTolerance) { queue.Enqueue((a, m)); queue.Enqueue((m, b)); }
        }
        return new(points.Values.ToArray(), points.Count, queue.Count > 0, maximumError);
    }
}
