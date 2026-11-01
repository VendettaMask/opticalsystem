using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string PassbandQualityProtocol = "passband-margin-quality/1";

    // A zero target deficit must not flatten the physical quality objective. Minimize
    // fractional peak loss, width error and stop-band leakage, with actual margins
    // protected at every retained point. Optical values and derivatives all come from Core.
    private Experiment RefinePassbandQuality(Experiment input, IReadOnlyDictionary<string, IMaterial> materials,
        IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var d = input; var best = input; var t = d.Target; var originalRun = input.Result!.Run;
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        if (indices.Length == 0 || d.Settings.MinimumThicknessNm == d.Settings.MaximumThicknessNm || input.Result.Metrics.FwhmNanometers is null) return input;
        var work = new GeneralCounter(); work.Add(originalRun.GeneralWork);
        var allowance = Math.Min(Math.Max(1, (int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations))),
            Math.Max(0, d.Settings.MaximumIterations - work.RepairIterations - work.ParetoIterations));
        if (allowance == 0) return input;
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId], t.CenterNm, t.AngleDegrees)).ToArray();
        var minimum = Math.Max(t.MinimumNm, t.CenterNm - 2 * t.FwhmNm); var maximum = Math.Min(t.MaximumNm, t.CenterNm + 2 * t.FwhmNm);
        var fixedGrid = GeneralGrid(d, ObjectiveGrid(d).Concat(ThinFilmSpectrum.Grid(minimum, maximum, 1024))
            .Concat(t.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 64))));
        GeneralSystem Read(FilmLayer[] layers, double[] grid, bool derivatives) => GeneralConstraintSystem(d, Solver(d, materials, layers), grid, derivatives, work);
        double[] Grid(FilmLayer[] layers)
        {
            var samples = Read(layers, fixedGrid, false).Samples; var solver = Solver(d, materials, layers); var anchors = new List<double>();
            foreach (var band in new[] { new SpectralBand(minimum, maximum) }.Concat(t.StopBands!))
            {
                var peak = ThinFilmSpectrum.RefineTransmissionPeak(solver, samples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).ToArray(), t.AngleDegrees, t.Polarization);
                work.Spectrum += peak.Evaluations;
                if (fixedGrid.Min(w => Math.Abs(w - peak.Peak.WavelengthNanometers)) > t.FwhmNm * GeneralConstraintInteriorGuard) anchors.Add(peak.Peak.WavelengthNanometers);
            }
            return GeneralGrid(d, fixedGrid.Concat(anchors));
        }
        static bool Physical(GeneralConstraintKey key) => key.Group is 0 or -2 or -3 or -4 or -5 || key.Group >= 6;
        var grid = Grid(best.Layers); var initial = Read(best.Layers, grid, false);
        var peakRow = Array.FindIndex(initial.Keys, k => k.Group == 0); var widthRow = Array.FindIndex(initial.Keys, k => k.Group == -3);
        if (peakRow < 0 || widthRow < 0 || !double.IsFinite(initial.Values[widthRow]))
            return input with { Result = input.Result with { Run = originalRun with
            { Evaluations = originalRun.Evaluations + 2, GeneralWork = work.Snapshot(), PassbandQualityStopReason = "UndefinedGuidedWidth" } } };
        double WidthError(GeneralSystem s) => (s.Values[Array.FindIndex(s.Keys, k => k.Group == -3)] - GeneralConstraintInteriorGuard) * t.FwhmNm + t.FwhmToleranceNm;
        var peakScale = Math.Max(GeneralConstraintInteriorGuard, 1 - t.PeakTransmittance + t.PeakTransmittance * (initial.Values[peakRow] - GeneralConstraintInteriorGuard));
        var widthScale = Math.Max(t.FwhmNm * GeneralConstraintInteriorGuard, Math.Abs(WidthError(initial)));
        var stopReference = initial.Keys.Select((k, i) => (k, i)).Where(x => x.k.Group >= 6 && double.IsFinite(initial.Values[x.i])).ToDictionary(x => x.k.Group, x => initial.Values[x.i]);
        (double Residual, double Factor) Residual(GeneralSystem s, int i) => s.Keys[i].Group switch
        {
            0 => ((1 - t.PeakTransmittance + t.PeakTransmittance * (s.Values[i] - GeneralConstraintInteriorGuard)) / peakScale, t.PeakTransmittance / peakScale),
            -3 => (WidthError(s) / widthScale, t.FwhmNm / widthScale),
            >= 6 when stopReference.ContainsKey(s.Keys[i].Group) => Stop(s, i),
            _ => (0, 0)
        };
        (double Residual, double Factor) Stop(GeneralSystem s, int i)
        {
            var scale = Math.Log(10) * Math.Max(1, t.BlockingOd);
            var loss = Math.Exp(scale * (s.Values[i] - stopReference[s.Keys[i].Group]));
            return (loss, scale * loss);
        }
        double Merit(GeneralSystem s) => Enumerable.Range(0, s.Values.Length).Where(i => s.Keys[i].Group is 0 or -3 || s.Keys[i].Group >= 6)
            .Sum(i => Math.Pow(Residual(s, i).Residual, 2));
        double[] Od(Experiment e) => t.StopBands!.Select(b => MinimumOd(e.Result!.Spectrum, b)).ToArray();
        var history = new List<PassbandQualityTrial>();
        var lambda = .001; long evaluations = 2; var reason = "IterationLimit";
        for (var iteration = 0; iteration < allowance; iteration++)
        {
            token.ThrowIfCancellationRequested(); work.PassbandQualityIterations++;
            grid = Grid(best.Layers); var current = Read(best.Layers, grid, true); evaluations += 2; work.Jacobians++;
            if (!double.IsFinite(WidthError(current))) { reason = "UndefinedGuidedWidth"; break; }
            var phase = indices.Select((i, j) => best.Layers[i].ThicknessNm / units[j]).ToArray();
            var rows = Enumerable.Range(0, current.Keys.Length).Where(i => Physical(current.Keys[i]) && double.IsFinite(current.Values[i])).ToArray();
            var objectives = rows.Where(i => current.Keys[i].Group is 0 or -3 || current.Keys[i].Group >= 6).ToArray();
            var residuals = objectives.Select(i => Residual(current, i).Residual).ToArray(); var objective = new double[objectives.Length, phase.Length];
            for (var i = 0; i < objectives.Length; i++) for (var j = 0; j < phase.Length; j++)
                objective[i, j] = Residual(current, objectives[i]).Factor * current.Derivatives![objectives[i], indices[j]] * units[j];
            var ceilings = rows.Select(i => current.Keys[i].Group switch
            {
                -2 or -3 => (Math.Abs(WidthError(current)) - t.FwhmToleranceNm) / t.FwhmNm + GeneralConstraintInteriorGuard,
                -4 or -5 => Math.Max(GeneralConstraintInteriorGuard, current.Values[i]),
                _ => current.Values[i]
            }).ToArray();
            var normals = new double[rows.Length + 2 * phase.Length, phase.Length]; var upper = new double[normals.GetLength(0)];
            for (var i = 0; i < rows.Length; i++)
            {
                upper[i] = Math.Max(0, ceilings[i] - current.Values[rows[i]]);
                for (var j = 0; j < phase.Length; j++) normals[i, j] = current.Derivatives![rows[i], indices[j]] * units[j];
            }
            for (var j = 0; j < phase.Length; j++)
            {
                normals[rows.Length + 2 * j, j] = 1; normals[rows.Length + 2 * j + 1, j] = -1;
                upper[rows.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                upper[rows.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            var accepted = false; var before = best; var beforeOd = Od(before); var beforeWidth = Math.Abs(before.Result!.Metrics.FwhmNanometers!.Value - t.FwhmNm);
            var bounds = new PassbandQualityBounds(before.Result.Metrics.PeakTransmittance, beforeWidth, beforeOd, AssessGeneral(d, before.Result.Spectrum).Violations);
            var currentMerit = Merit(current);
            for (var attempt = 0; attempt < 6 && !accepted; attempt++)
            {
                var step = InequalityLeastSquaresStep.Solve(objective, residuals, normals, upper, lambda);
                work.InequalitySteps++; work.InequalityIterations += step.ActiveSetIterations; if (!step.Converged) work.InequalityLimits++;
                for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
                {
                    var layers = before.Layers.ToArray();
                    for (var j = 0; j < phase.Length; j++) layers[indices[j]] = layers[indices[j]] with
                    { ThicknessNm = step.Step[j] == 0 ? before.Layers[indices[j]].ThicknessNm
                        : Math.Clamp((phase[j] + fraction * step.Step[j]) * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) };
                    if (layers.SequenceEqual(before.Layers)) break;
                    var trial = Read(layers, grid, false); evaluations++;
                    if (rows.Where((row, i) => trial.Values[row] > ceilings[i]).Any() || !(Merit(trial) < currentMerit)) continue;
                    var certified = Evaluate(d with { Layers = layers, Result = null }, progress: progress, token: token); work.Dense++;
                    var r = certified.Result!; var stop = Od(certified); var error = r.Metrics.FwhmNanometers is { } width ? Math.Abs(width - t.FwhmNm) : double.PositiveInfinity;
                    accepted = (!before.Result.SamplingConverged || r.SamplingConverged) && (!before.Result.Passed || r.Passed)
                        && r.Metrics.PeakTransmittance >= bounds.MinimumPeakTransmittance && error <= bounds.MaximumWidthErrorNm
                        && stop.Zip(beforeOd).All(p => p.First >= p.Second)
                        && WithinGeneralTargetEnvelope(AssessGeneral(d, r.Spectrum), AssessGeneral(d, before.Result.Spectrum))
                        && (r.Metrics.PeakTransmittance > bounds.MinimumPeakTransmittance || error < bounds.MaximumWidthErrorNm || stop.Zip(beforeOd).Any(p => p.First > p.Second));
                    history.Add(new(iteration + 1, layers, r.Metrics, stop, r.SamplingConverged, accepted, bounds));
                    if (!accepted) continue;
                    best = certified; work.PassbandQualityAccepted++; lambda = Math.Max(1e-10, lambda / 3); break;
                }
                if (!accepted) lambda = Math.Min(1e12, lambda * 10);
            }
            progress?.Report(new("通带与截止余量优化", evaluations, best.Result!.Merit));
            if (!accepted) { reason = "NoCertifiedMarginDescent"; break; }
        }
        return best with { Result = best.Result! with { Run = originalRun with
        { Evaluations = originalRun.Evaluations + evaluations, GeneralWork = work.Snapshot(), PassbandQualityStartLayers = d.Layers,
            PassbandQualityTrials = history.ToArray(), PassbandQualityIterationLimit = allowance, PassbandQualityStopReason = reason,
            TargetRefinementIterationLimit = d.Settings.MaximumIterations } } };
    }
}
