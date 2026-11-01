using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineFeasibleGeneralTargets(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations, GeneralCounter work,
        Experiment? validatedStart, IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var originalSolver = Solver(d, materials);
        var source = validatedStart?.Result?.Spectrum ?? GeneralConstraintSystem(d, originalSolver, initialGrid, false, work).Samples;
        var peak = ThinFilmSpectrum.RefineTransmissionPeak(originalSolver,
            source.Where(s => Math.Abs(s.WavelengthNanometers - d.Target.CenterNm) <= 2 * d.Target.FwhmNm).ToArray(), d.Target.AngleDegrees, d.Target.Polarization);
        work.Spectrum += peak.Evaluations;
        var anchors = initialGrid.Min(w => Math.Abs(w - peak.Peak.WavelengthNanometers)) > d.Target.FwhmNm * GeneralConstraintInteriorGuard
            ? new[] { peak.Peak.WavelengthNanometers } : Array.Empty<double>();
        var grid = GeneralGrid(d, initialGrid.Concat(anchors));
        static int Metric(GeneralConstraintKey key) => key.Group switch { 0 => 0, -2 or -3 => 1, -4 or -5 => 2, >= 6 => key.Group - 3, _ => -1 };
        double Interior(int metric) => metric == 0 && d.Target.PeakTransmittance == 1 ? 0 : GeneralConstraintInteriorGuard;
        GeneralSystem Read(FilmLayer[] layers, bool derivatives) => GeneralConstraintSystem(d with { Layers = layers }, Solver(d, materials, layers), grid, derivatives, work);
        var initial = Read(d.Layers, false);
        if (!initial.Keys.Where((k, i) => k.Group is -2 or -3 && double.IsFinite(initial.Values[i])).Any()
            || !initial.Keys.Where((k, i) => k.Group is -4 or -5 && double.IsFinite(initial.Values[i])).Any())
            return RefineProtectedGeneralThickness(d, materials, initialGrid, maximumIterations, work, validatedStart, progress, token);
        CoatingConstraintAssessment Guide(GeneralSystem system)
        {
            var a = AssessGeneral(d, system.Samples); work.BandMetrics++;
            var v = a.Violations.ToArray();
            var centers = Enumerable.Range(0, system.Values.Length).Where(i => Metric(system.Keys[i]) == 2 && double.IsFinite(system.Values[i])).ToArray();
            if (centers.Length > 0) v[2] = Math.Max(0, centers.Max(i => system.Values[i] - Interior(2)));
            return a with { Violations = v };
        }
        var baseline = Guide(initial); var bestGuide = d.Layers; var bestGuideAssessment = baseline;
        var certifiedBest = validatedStart; var formalBaseline = validatedStart is null ? baseline : AssessGeneral(d, validatedStart.Result!.Spectrum);
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, j) => d.Layers[i].ThicknessNm / units[j]).ToArray(); var originalPhase = phase.ToArray();
        FilmLayer[] Layers(double[] values)
        {
            var layers = d.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            { ThicknessNm = values[j] == originalPhase[j] ? d.Layers[indices[j]].ThicknessNm : Math.Clamp(values[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) };
            return layers;
        }
        double[] Ceilings(GeneralSystem start) => baseline.Violations.Select((v, k) => Math.Max(Interior(k),
            start.Values.Where((_, i) => Metric(start.Keys[i]) == k).DefaultIfEmpty(v + Interior(k)).Max())).ToArray();
        var ceilings = Ceilings(initial);
        double Cost(GeneralSystem system) => Enumerable.Range(0, system.Values.Length).Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .Select(i => Math.Max(0, system.Values[i] - Interior(Metric(system.Keys[i])))).Sum(v => v * v);
        bool Protected(GeneralSystem system) => Enumerable.Range(0, system.Values.Length)
            .Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .All(i => system.Values[i] <= ceilings[Metric(system.Keys[i])]) && WithinGeneralTargetEnvelope(Guide(system), baseline);
        var lambda = 1e-3; var period = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)));
        var iterations = 0; long evaluations = 0; var reason = "MaximumIterations";
        for (; iterations < maximumIterations; iterations++)
        {
            token.ThrowIfCancellationRequested();
            if (iterations % period == 0) { work.Passes++; work.ProtectionPasses++; }
            var currentLayers = Layers(phase); var current = Read(currentLayers, true); evaluations++; work.Jacobians++;
            var rows = Enumerable.Range(0, current.Values.Length).Where(i => Metric(current.Keys[i]) >= 0 && double.IsFinite(current.Values[i])).ToArray();
            var r = rows.Select(i => Math.Max(0, current.Values[i] - Interior(Metric(current.Keys[i])))).ToArray();
            var jac = new double[rows.Length, phase.Length];
            for (var i = 0; i < rows.Length; i++) if (r[i] > 0) for (var j = 0; j < phase.Length; j++) jac[i, j] = current.Derivatives![rows[i], indices[j]] * units[j];
            var a = new double[rows.Length + 2 * phase.Length, phase.Length]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < rows.Length; i++)
            {
                b[i] = Math.Max(0, ceilings[Metric(current.Keys[rows[i]])] - current.Values[rows[i]]);
                for (var j = 0; j < phase.Length; j++) a[i, j] = current.Derivatives![rows[i], indices[j]] * units[j];
            }
            for (var j = 0; j < phase.Length; j++)
            {
                a[rows.Length + 2 * j, j] = 1; a[rows.Length + 2 * j + 1, j] = -1;
                b[rows.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                b[rows.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            var accepted = false; var cost = r.Sum(v => v * v);
            for (var attempt = 0; attempt < 6 && !accepted; attempt++)
            {
                var qp = InequalityLeastSquaresStep.Solve(jac, r, a, b, lambda); work.InequalitySteps++; work.InequalityIterations += qp.ActiveSetIterations;
                for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
                {
                    var trialPhase = phase.Zip(qp.Step, (p, s) => p + fraction * s).ToArray();
                    if (trialPhase.SequenceEqual(phase)) break;
                    var trialLayers = Layers(trialPhase); var trial = Read(trialLayers, false); evaluations++;
                    if (!Protected(trial)) { work.ProtectedRejections++; continue; }
                    var assessment = Guide(trial);
                    if (Cost(trial) >= cost) continue;
                    phase = trialPhase; accepted = true; lambda = Math.Max(1e-10, lambda / 3);
                    if (assessment.Merit < bestGuideAssessment.Merit) { bestGuide = trialLayers; bestGuideAssessment = assessment; }
                    break;
                }
                if (!accepted) lambda = Math.Min(1e12, lambda * 10);
            }
            progress?.Report(new("逐项约束膜厚步", evaluations, bestGuideAssessment.Merit));
            var checkpoint = (iterations + 1) % period == 0 || iterations + 1 == maximumIterations || bestGuideAssessment.CoarselyFeasible;
            if (!checkpoint) continue;
            Experiment? certified = null;
            if (validatedStart is not null && !bestGuide.SequenceEqual(certifiedBest!.Layers))
            {
                certified = Evaluate(d with { Layers = bestGuide, Result = null }, progress: progress, token: token); work.Dense++;
                var assessment = AssessGeneral(d, certified.Result!.Spectrum);
                if (WithinGeneralTargetEnvelope(assessment, formalBaseline))
                {
                    if (certified.Result.Passed && !certifiedBest.Result!.Passed || certified.Result.Passed == certifiedBest.Result!.Passed
                        && assessment.Merit < AssessGeneral(d, certifiedBest.Result.Spectrum).Merit) certifiedBest = certified;
                }
                else work.DenseRejections++;
                if (certifiedBest.Result!.Passed) { reason = "ProtectedTargetsSatisfied"; break; }
            }
            if (iterations + 1 == maximumIterations) break;
            var featureSamples = certified?.Result?.Spectrum;
            if (featureSamples is not null)
            {
                var metrics = certified!.Result!.Metrics;
                var features = new[] { metrics.PeakWavelengthNanometers, metrics.LeftHalfMaximumNanometers ?? d.Target.CenterNm, metrics.RightHalfMaximumNanometers ?? d.Target.CenterNm }
                    .Concat(d.Target.StopBands!.Select(band => featureSamples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm)
                        .MaxBy(s => s.Power.LogTransmittance)!.WavelengthNanometers));
                grid = GeneralGrid(d, grid.Concat(features));
            }
            var adaptive = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, bestGuide), grid, d.Target.AngleDegrees, d.Target.Polarization,
                Math.Max(double.Epsilon, Math.Min(d.Target.CenterToleranceNm, d.Target.FwhmToleranceNm) / d.Target.FwhmNm / 16), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
            work.Spectrum += adaptive.Evaluations; work.Refinements++; work.Limit |= adaptive.LimitReached;
            grid = GeneralGrid(d, adaptive.Samples.Select(s => s.WavelengthNanometers));
            initial = Read(d.Layers, false); baseline = Guide(initial); ceilings = Ceilings(initial);
            if (certified is not null && !WithinGeneralTargetEnvelope(AssessGeneral(d, certified.Result!.Spectrum), formalBaseline)) bestGuide = certifiedBest!.Layers;
            bestGuideAssessment = Guide(Read(bestGuide, false));
            for (var j = 0; j < phase.Length; j++) phase[j] = bestGuide[indices[j]].ThicknessNm / units[j];
        }
        var chosen = certifiedBest?.Layers ?? bestGuide;
        return (chosen, certifiedBest?.Result?.Merit ?? bestGuideAssessment.Merit, new(d.Settings.Optimizer, GeneralCoatingProtocol, reason, evaluations, Math.Min(maximumIterations, iterations + 1), d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot(), TargetViolationCeilings = formalBaseline.Violations,
            TargetEnvelopeSampling = validatedStart is null ? "guidance-start" : "dense-start", TargetProtectionPhase = "inequality-targets" });
    }
}
