using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    private static double ObjectiveMerit(Experiment d, double[] residuals) =>
        d.Settings.DesignProtocolVersion is 2 or 3 or 4 ? residuals.Sum(v => v * v) : Merit(residuals);

    private static string LayerIdentity(FilmLayer[] layers) => string.Join(';', layers.Select(l =>
        FormattableString.Invariant($"{l.MaterialId}:{l.ThicknessNm:R}:{l.Variable}")));

    // TFStudio prototypePositions inspires the alternating, substrate-first role walk.
    // These are bounded integer proposals, not an analytic coupled-cavity synthesis claim.
    private static IEnumerable<FilmLayer[]> NarrowBandPrototypes(Experiment d, Func<string, double, FilmLayer> layer)
    {
        var cavities = d.Target.Structure?.Cavities ?? 1; var emitted = 0;
        for (var mirror = 3; (cavities + 1) * mirror + cavities <= d.Target.MaximumLayers; mirror++)
            foreach (var inner in new[] { mirror, 2 * mirror - 1, 2 * mirror + 1 }.Distinct())
                foreach (var order in new[] { 1, 2 })
                {
                    var counts = Enumerable.Range(0, cavities + 1).Select(i => i == 0 || i == cavities ? mirror : inner).ToArray();
                    if (counts.Sum() + cavities > d.Target.MaximumLayers) continue;
                    var stack = new List<FilmLayer>(); var position = 0;
                    for (var block = 0; block <= cavities; block++)
                    {
                        for (var j = 0; j < counts[block]; j++)
                            stack.Add(layer(++position % 2 == 1 ? d.HighId : d.LowId, 1));
                        if (block < cavities) stack.Add(layer(++position % 2 == 1 ? d.HighId : d.LowId, 2 * order));
                    }
                    stack.Reverse();
                    yield return stack.ToArray();
                    if (++emitted == 256) yield break;
                }
    }

    private static double[] BalancedObjective(Experiment d, CoherentThinFilmSolver solver, double[] grid)
    {
        var t = d.Target;
        var samples = ThinFilmSpectrum.Sample(solver, grid, t.AngleDegrees, t.Polarization);
        var residuals = new List<double>();
        if (t.Kind is DesignKind.Antireflection or DesignKind.HighReflector)
        {
            var scale = Math.Max(.001, t.Kind == DesignKind.Antireflection ? t.Reflectance : 1 - t.Reflectance);
            Group(samples.Select(s => Math.Max(0, t.Kind == DesignKind.Antireflection
                ? s.Power.Reflectance - t.Reflectance : t.Reflectance - s.Power.Reflectance) / scale));
            // A small feasible-side guide keeps a flat one-sided objective useful without altering acceptance.
            var guideScale = .02 / (scale * Math.Sqrt(samples.Length));
            residuals.AddRange(samples.Select(s => guideScale * (t.Kind == DesignKind.Antireflection
                ? s.Power.Reflectance : 1 - s.Power.Reflectance)));
            return residuals.ToArray();
        }
        var center = solver.Evaluate(t.CenterNm, t.AngleDegrees, t.Polarization).Transmittance;
        var halfLeft = solver.Evaluate(t.CenterNm - t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance;
        var halfRight = solver.Evaluate(t.CenterNm + t.FwhmNm / 2, t.AngleDegrees, t.Polarization).Transmittance;
        const double peakScale = .01;
        var widthScale = Math.Max(.0025, .5 * t.PeakTransmittance * t.FwhmToleranceNm / t.FwhmNm);
        residuals.Add(Math.Max(0, t.PeakTransmittance - center) / peakScale);
        residuals.Add((halfLeft - center / 2) / widthScale);
        residuals.Add((halfRight - center / 2) / widthScale);
        var metrics = Metrics(d, samples);
        // Use Core's measured peak and half-height crossings as guidance too;
        // two isolated half-height points do not define the actual passband.
        residuals.Add(Math.Max(0, Math.Abs(metrics.PeakWavelengthNanometers - t.CenterNm) - t.CenterToleranceNm) / t.CenterToleranceNm);
        residuals.Add(metrics.FwhmNanometers is { } width
            ? Math.Max(0, Math.Abs(width - t.FwhmNm) - t.FwhmToleranceNm) / t.FwhmToleranceNm : 100);
        residuals.Add(Math.Max(0, t.PeakTransmittance - metrics.PeakTransmittance) / peakScale);
        foreach (var band in t.StopBands!)
            Group(samples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm)
                .Select(s => Math.Max(0, t.BlockingOd - s.Power.OpticalDensity) / Math.Max(.1, .1 * t.BlockingOd)));
        Group(samples.Where(s => Math.Abs(s.WavelengthNanometers - t.CenterNm) <= t.FwhmNm / 2)
            .Select(s => Math.Max(0, s.Power.Transmittance - center) / peakScale));
        // Checking only the two half-height points admits shoulders and extra peaks outside them.
        Group(samples.Where(s => Math.Abs(s.WavelengthNanometers - t.CenterNm) > t.FwhmNm / 2
                && Math.Abs(s.WavelengthNanometers - t.CenterNm) <= 2 * t.FwhmNm)
            .Select(s => Math.Max(0, s.Power.Transmittance - center / 2) / widthScale));
        return residuals.ToArray();

        void Group(IEnumerable<double> values)
        {
            var rows = values.ToArray();
            if (rows.Length == 0) throw new InvalidOperationException("A required spectral target has no objective samples.");
            // Mean and worst violations each receive half the group's weight, independent of point count.
            residuals.AddRange(rows.Select(v => v / Math.Sqrt(2 * rows.Length)));
            residuals.Add(rows.Max() / Math.Sqrt(2));
        }
    }

    private static IEnumerable<FilmLayer[]> DiverseScreenPool(IReadOnlyList<FilmLayer[]> ranked, int maximum)
    {
        var selected = ranked.Take(Math.Max(1, maximum / 2))
            .Concat(ranked.GroupBy(l => l.Length).Select(g => g.First()))
            .DistinctBy(LayerIdentity).Take(maximum).ToList();
        return selected.Concat(ranked).DistinctBy(LayerIdentity).Take(maximum);
    }
}
