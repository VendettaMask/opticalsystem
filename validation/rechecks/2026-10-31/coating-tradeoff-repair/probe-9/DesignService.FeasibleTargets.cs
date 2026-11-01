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
        var baseline = Guide(initial); var referenceBaseline = baseline; var referenceGrid = grid;
        var bestGuide = d.Layers; var bestGuideAssessment = baseline;
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
        var attained = referenceBaseline.Violations.Select(v => v == 0).ToArray();
        var formalAttained = formalBaseline.Violations.Select(v => v == 0).ToArray();
        double Bias(GeneralConstraintKey key) => Metric(key) switch
        {
            0 => d.Target.PeakTransmittance == 1 ? 0 : 1e-5 / d.Target.PeakTransmittance,
            1 => d.Target.FwhmToleranceNm / (10 * d.Target.FwhmNm),
            2 => d.Target.CenterToleranceNm / (10 * d.Target.FwhmNm),
            _ => .005 / Math.Max(1, d.Target.BlockingOd)
        };
        var objectiveScales = referenceBaseline.Violations.Select((v, i) => v > 0
            ? Math.Max(v, Bias(initial.Keys.First(k => Metric(k) == i))) : 1).ToArray();
        double Cost(GeneralSystem system) => Enumerable.Range(0, system.Values.Length).Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .Sum(i =>
            {
                var g = Math.Max(0, system.Values[i] + Bias(system.Keys[i])) / objectiveScales[Metric(system.Keys[i])];
                var resolution = Math.Max(GeneralConstraintInteriorGuard, Bias(system.Keys[i]) / objectiveScales[Metric(system.Keys[i])]);
                return g < resolution ? g * g / (2 * resolution) : g - resolution / 2;
            });
        bool Protected(GeneralSystem system) => Enumerable.Range(0, system.Values.Length)
            .Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .All(i => system.Values[i] <= ceilings[Metric(system.Keys[i])]) && WithinGeneralTargetEnvelope(Guide(system), baseline);
        long evaluations = 0;
        var certifiedGuideMerit = bestGuideAssessment.Merit;
        (double[] Phase, GeneralSystem System) Correct(double[] trialPhase, GeneralSystem trial)
        {
            // A linearized feasible tangent can leave a curved nonlinear boundary (Maratos effect).
            // Minimize an elastic feasibility variable, then re-evaluate the actual Core spectrum.
            // The tiny correction margin points inward; no final gate or retained envelope is relaxed.
            for (var correction = 0; correction < 2 && !Protected(trial); correction++)
            {
                var model = Read(Layers(trialPhase), true); evaluations++; work.Jacobians++;
                var guards = Enumerable.Range(0, model.Values.Length).Where(i => Metric(model.Keys[i]) >= 0 && double.IsFinite(model.Values[i])).ToArray();
                var a = new double[guards.Length + 2 * phase.Length + 2, phase.Length + 1]; var bounds = new double[a.GetLength(0)];
                var deficit = guards.Select(i => model.Values[i] - ceilings[Metric(model.Keys[i])] + Interior(Metric(model.Keys[i]))).DefaultIfEmpty(0).Max();
                var elastic = Math.Max(0, deficit);
                for (var i = 0; i < guards.Length; i++)
                {
                    var metric = Metric(model.Keys[guards[i]]);
                    bounds[i] = Math.Max(0, ceilings[metric] - Interior(metric) - model.Values[guards[i]] + elastic);
                    a[i, phase.Length] = -1;
                    for (var j = 0; j < phase.Length; j++) a[i, j] = model.Derivatives![guards[i], indices[j]] * units[j];
                }
                for (var j = 0; j < phase.Length; j++)
                {
                    a[guards.Length + 2 * j, j] = 1; a[guards.Length + 2 * j + 1, j] = -1;
                    bounds[guards.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - trialPhase[j]));
                    bounds[guards.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, trialPhase[j] - d.Settings.MinimumThicknessNm / units[j]));
                }
                a[a.GetLength(0) - 2, phase.Length] = -1; bounds[^2] = elastic;
                a[a.GetLength(0) - 1, phase.Length] = 1;
                var objective = new double[1, phase.Length + 1]; objective[0, phase.Length] = 1;
                var result = InequalityLeastSquaresStep.Solve(objective, [1 + elastic], a, bounds, .001);
                work.InequalitySteps++; work.InequalityIterations += result.ActiveSetIterations; if (!result.Converged) work.InequalityLimits++;
                trialPhase = trialPhase.Select((p, j) => Math.Clamp(p + result.Step[j], d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray();
                trial = Read(Layers(trialPhase), false); evaluations++;
            }
            return (trialPhase, trial);
        }
        var lambda = 1e-3; var period = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)));
        var iterations = 0; var reason = "MaximumIterations";
        for (; iterations < maximumIterations; iterations++)
        {
            token.ThrowIfCancellationRequested();
            if (iterations % period == 0) { work.Passes++; work.ProtectionPasses++; }
            var currentLayers = Layers(phase); var current = Read(currentLayers, true); evaluations++; work.Jacobians++;
            var rows = Enumerable.Range(0, current.Values.Length).Where(i => Metric(current.Keys[i]) >= 0 && double.IsFinite(current.Values[i])).ToArray();
            var protectedRows = rows.Where(i => Metric(current.Keys[i]) >= 0).ToArray();
            var targets = rows.Select(i => Math.Max(0, current.Values[i] + Bias(current.Keys[i])) / objectiveScales[Metric(current.Keys[i])]).ToArray();
            var scales = rows.Select((i, k) => Math.Sqrt(Math.Max(targets[k], Math.Max(GeneralConstraintInteriorGuard,
                Bias(current.Keys[i]) / objectiveScales[Metric(current.Keys[i])])))).ToArray();
            var r = targets.Select((v, i) => v / scales[i]).ToArray();
            var jac = new double[rows.Length, phase.Length];
            for (var i = 0; i < rows.Length; i++) if (r[i] > 0) for (var j = 0; j < phase.Length; j++) jac[i, j] = current.Derivatives![rows[i], indices[j]] * units[j]
                / (objectiveScales[Metric(current.Keys[rows[i]])] * scales[i]);
            var a = new double[protectedRows.Length + 2 * phase.Length, phase.Length]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < protectedRows.Length; i++)
            {
                var slack = Math.Max(0, ceilings[Metric(current.Keys[protectedRows[i]])] - current.Values[protectedRows[i]]);
                b[i] = slack <= GeneralConstraintInteriorGuard / 2 ? 0 : slack;
                for (var j = 0; j < phase.Length; j++) a[i, j] = current.Derivatives![protectedRows[i], indices[j]] * units[j];
            }
            for (var j = 0; j < phase.Length; j++)
            {
                a[protectedRows.Length + 2 * j, j] = 1; a[protectedRows.Length + 2 * j + 1, j] = -1;
                b[protectedRows.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                b[protectedRows.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            var accepted = false; var cost = Cost(current);
            for (var attempt = 0; attempt < 6 && !accepted; attempt++)
            {
                var qp = InequalityLeastSquaresStep.Solve(jac, r, a, b, lambda); work.InequalitySteps++; work.InequalityIterations += qp.ActiveSetIterations;
                if (!qp.Converged) work.InequalityLimits++;
                for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
                {
                    var trialPhase = phase.Select((p, j) => Math.Clamp(p + fraction * qp.Step[j], d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray();
                    if (trialPhase.SequenceEqual(phase)) break;
                    var trialLayers = Layers(trialPhase); var trial = Read(trialLayers, false); evaluations++;
                    if (Cost(trial) >= cost) continue;
                    if (!Protected(trial))
                    {
                        work.ProtectedRejections++;
                        (trialPhase, trial) = Correct(trialPhase, trial); trialLayers = Layers(trialPhase);
                        if (!Protected(trial) || Cost(trial) >= cost) continue;
                    }
                    var ranking = ReferenceEquals(grid, referenceGrid) ? trial : GeneralConstraintSystem(d, Solver(d, materials, trialLayers), referenceGrid, false, work);
                    var assessment = Guide(ranking);
                    if (!WithinGeneralTargetEnvelope(assessment, referenceBaseline) || assessment.Violations.Where((v, i) => attained[i] && v > 0).Any()) continue;
                    phase = trialPhase; accepted = true; lambda = Math.Max(1e-10, lambda / 3);
                    var milestone = assessment.Violations.Where((v, i) => !attained[i] && v == 0).Any();
                    if (milestone || assessment.Merit < bestGuideAssessment.Merit * (1 - GeneralConstraintInteriorGuard))
                    {
                        bestGuide = trialLayers; bestGuideAssessment = assessment;
                        for (var k = 0; k < attained.Length; k++) if (assessment.Violations[k] == 0) { attained[k] = true; ceilings[k] = Interior(k); }
                    }
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
                if (WithinGeneralTargetEnvelope(assessment, formalBaseline) && !assessment.Violations.Where((v, i) => formalAttained[i] && v > 0).Any())
                {
                    var milestone = assessment.Violations.Where((v, i) => !formalAttained[i] && v == 0).Any();
                    if (certified.Result.Passed && !certifiedBest.Result!.Passed || certified.Result.Passed == certifiedBest.Result!.Passed
                        && (milestone || bestGuideAssessment.Merit < certifiedGuideMerit * (1 - GeneralConstraintInteriorGuard)))
                    {
                        certifiedBest = certified; certifiedGuideMerit = bestGuideAssessment.Merit;
                        for (var k = 0; k < formalAttained.Length; k++) formalAttained[k] |= assessment.Violations[k] == 0;
                    }
                }
                else work.DenseRejections++;
                if (certifiedBest.Result!.Passed) { reason = "ProtectedTargetsSatisfied"; break; }
            }
            if (iterations + 1 == maximumIterations) break;
            var featureSamples = bestGuideAssessment.CoarselyFeasible ? certified?.Result?.Spectrum : null;
            if (featureSamples is not null)
            {
                var metrics = certified!.Result!.Metrics;
                var features = new[] { metrics.PeakWavelengthNanometers, metrics.LeftHalfMaximumNanometers ?? d.Target.CenterNm, metrics.RightHalfMaximumNanometers ?? d.Target.CenterNm }
                    .Concat(d.Target.StopBands!.Select(band => featureSamples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm)
                        .MaxBy(s => s.Power.LogTransmittance)!.WavelengthNanometers));
                grid = GeneralGrid(d, grid.Concat(features));
            }
            var adaptive = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, Layers(phase)), grid, d.Target.AngleDegrees, d.Target.Polarization,
                Math.Max(double.Epsilon, Math.Min(d.Target.CenterToleranceNm, d.Target.FwhmToleranceNm) / d.Target.FwhmNm / 16), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
            work.Spectrum += adaptive.Evaluations; work.Refinements++; work.Limit |= adaptive.LimitReached;
            grid = GeneralGrid(d, adaptive.Samples.Select(s => s.WavelengthNanometers));
            initial = Read(d.Layers, false); baseline = Guide(initial); ceilings = Ceilings(initial);
            for (var k = 0; k < attained.Length; k++) if (attained[k]) ceilings[k] = Interior(k);
            // The retained physical rank and the step objective differ. Preserve the accepted iterate
            // across grid updates instead of repeatedly restarting it at an earlier returned candidate.
        }
        var chosen = certifiedBest?.Layers ?? bestGuide;
        return (chosen, certifiedBest?.Result?.Merit ?? bestGuideAssessment.Merit, new(d.Settings.Optimizer, GeneralCoatingProtocol, reason, evaluations, Math.Min(maximumIterations, iterations + 1), d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot(), TargetViolationCeilings = formalBaseline.Violations,
            TargetEnvelopeSampling = validatedStart is null ? "guidance-start" : "dense-start", TargetProtectionPhase = "inequality-targets" });
    }
}
