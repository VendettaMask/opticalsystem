using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string TargetProtectionProtocol = "target-violation-envelope/3";

    private static bool WithinGeneralTargetEnvelope(CoatingConstraintAssessment candidate, CoatingConstraintAssessment baseline) =>
        candidate.Violations.Length == baseline.Violations.Length
        && candidate.Violations.Zip(baseline.Violations).All(p => p.First <= p.Second);

    // Minimize the target residuals while constraining each formal target's violation to its starting level.
    // Targets that already pass remain constrained by their original gate; unfinished targets cannot be sacrificed.
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineProtectedGeneralThickness(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations, GeneralCounter work,
        Experiment? validatedStart, IProgress<DesignProgress>? progress, CancellationToken token, bool unscaledSampledDirection = false)
    {
        var solver = Solver(d, materials);
        var sourceSamples = validatedStart is null ? GeneralConstraintSystem(d, solver, initialGrid, false, work).Samples
            : SearchPeakReferenceSamples(d, solver, work);
        var peak = ThinFilmSpectrum.RefineTransmissionPeak(solver,
            sourceSamples.Where(s => Math.Abs(s.WavelengthNanometers - d.Target.CenterNm) <= 2 * d.Target.FwhmNm).ToArray(),
            d.Target.AngleDegrees, d.Target.Polarization);
        work.Spectrum += peak.Evaluations;
        // Formal convergence has absolute wavelength tolerances. Do not feed its grid density into
        // the phase problem. A locally refined physical peak anchors the dimensionless guide instead.
        var anchors = initialGrid.Min(w => Math.Abs(w - peak.Peak.WavelengthNanometers)) > d.Target.FwhmNm * GeneralConstraintInteriorGuard
            ? new[] { peak.Peak.WavelengthNanometers } : Array.Empty<double>();
        var grid = GeneralGrid(d, initialGrid.Concat(anchors)); var rankingGrid = grid;
        var baselineSystem = GeneralConstraintSystem(d, solver, rankingGrid, false, work);
        var baselineSamples = baselineSystem.Samples;
        CoatingConstraintAssessment GuidanceAssessment(ThinFilmSample[] samples)
        {
            if (unscaledSampledDirection) return AssessGeneral(d, samples);
            var assessment = AssessGeneral(d, samples); work.BandMetrics++;
            var location = ThinFilmSpectrum.EstimatePeakWavelength(samples.Where(s =>
                Math.Abs(s.WavelengthNanometers - d.Target.CenterNm) <= 2 * d.Target.FwhmNm).ToArray());
            if (location is null) return assessment;
            // Use the same Core peak estimate for constraint guidance and retained-trial ranking.
            // A coarse sample inside the center gate must not mask an estimated peak outside it.
            // Dense final protection still uses the original formally measured sample peak.
            var violations = assessment.Violations.ToArray();
            violations[2] = Math.Max(0, Math.Abs(location.Value - d.Target.CenterNm) - d.Target.CenterToleranceNm) / d.Target.FwhmNm;
            return assessment with { Violations = violations };
        }
        var baseline = GuidanceAssessment(baselineSamples); var bestAssessment = baseline; var best = d.Layers;
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId],
            d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, j) => d.Layers[i].ThicknessNm / units[j]).ToArray();
        var initialPhase = phase.ToArray();
        FilmLayer[] Layers()
        {
            var layers = d.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            {
                ThicknessNm = phase[j] == initialPhase[j] ? d.Layers[indices[j]].ThicknessNm
                    : Math.Clamp(phase[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm)
            };
            return layers;
        }
        static int Metric(GeneralConstraintKey key) => key.Group switch { 0 => 0, -2 or -3 => 1, -4 or -5 => 2, >= 6 => key.Group - 3, _ => -1 };
        double Interior(int metric) => metric == 0 && d.Target.PeakTransmittance == 1 ? 0 : GeneralConstraintInteriorGuard;
        var guardCeilings = baseline.Violations.Select((v, metric) =>
        {
            var rows = Enumerable.Range(0, baselineSystem.Values.Length).Where(i => Metric(baselineSystem.Keys[i]) == metric && double.IsFinite(baselineSystem.Values[i])).ToArray();
            return rows.Length == 0 ? v + Interior(metric) : Math.Max(Interior(metric), rows.Max(i => baselineSystem.Values[i]));
        }).ToArray();
        double GuardScale(GeneralConstraintKey key) => Metric(key) is var metric && metric >= 0 && guardCeilings[metric] > Interior(metric)
            ? guardCeilings[metric] - Interior(metric) : 1;
        var relativeProgress = true;
        // First minimize squared fractional deficits. If no protected physical improvement survives
        // a pass, use sum(g_i^2 / starting_deficit_i) instead: equal initial marginal costs in the
        // original dimensionless target units. Completed targets retain unit scale in both models.
        // Only the search direction changes; both retention grids and final gates stay unchanged.
        double TargetScale(GeneralConstraintKey key) => unscaledSampledDirection ? 1 : relativeProgress ? GuardScale(key) : Math.Sqrt(GuardScale(key));
        double Guard(GeneralSystem system, int i)
        {
            var metric = Metric(system.Keys[i]);
            return metric < 0 || double.IsNegativeInfinity(system.Values[i]) ? double.NegativeInfinity
                : (system.Values[i] - guardCeilings[metric]) / GuardScale(system.Keys[i]);
        }
        var penalty = 1.0; var previousViolation = double.PositiveInfinity;
        var multiplierMap = new Dictionary<GeneralConstraintKey, double>();
        var targetMultiplierMap = new Dictionary<GeneralConstraintKey, double>();
        var remaining = maximumIterations; var iterations = 0; long evaluations = 0; var reason = "MaximumIterations";
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested(); work.Passes++; work.ProtectionPasses++;
            var passStartAssessment = bestAssessment;
            var allowance = Math.Min(remaining, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations))));
            var problem = new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
            for (var j = 0; j < phase.Length; j++)
            {
                var k = j; problem.AddVariable(new DelegateVariable("光学相位变量 " + (j + 1), () => phase[k], v => phase[k] = v,
                    d.Settings.MinimumThicknessNm / units[k], d.Settings.MaximumThicknessNm / units[k], stepHint: 1));
            }
            GeneralSystem? cached = null; double[]? cachedPhase = null;
            var analytic = d.Settings.Optimizer == "Damped Least Squares";
            GeneralSystem Current()
            {
                token.ThrowIfCancellationRequested();
                if (cachedPhase is not null && cachedPhase.SequenceEqual(phase)) return cached!;
                var layers = Layers(); var current = d with { Layers = layers };
                cached = GeneralConstraintSystem(current, Solver(current, materials), grid, analytic, work); cachedPhase = phase.ToArray();
                var assessment = GuidanceAssessment(cached.Samples);
                if (WithinGeneralTargetEnvelope(assessment, baseline))
                {
                    var ranking = ThinFilmSpectrum.Sample(Solver(current, materials), rankingGrid, d.Target.AngleDegrees, d.Target.Polarization);
                    work.Spectrum += ranking.Length;
                    assessment = GuidanceAssessment(ranking);
                    if (WithinGeneralTargetEnvelope(assessment, baseline) && PreferGeneral(assessment, bestAssessment))
                    { best = layers; bestAssessment = assessment; }
                    else if (!WithinGeneralTargetEnvelope(assessment, baseline)) work.ProtectedRejections++;
                }
                else work.ProtectedRejections++;
                return cached;
            }
            var raw = Current(); var multipliers = raw.Keys.Select(k => multiplierMap.GetValueOrDefault(k)).ToArray();
            var targetMultipliers = raw.Keys.Select(k => targetMultiplierMap.GetValueOrDefault(k)).ToArray();
            double Target(GeneralSystem system, int i) => system.Values[i] / TargetScale(system.Keys[i]);
            double Objective(int i) => Math.Max(0, Target(Current(), i) + targetMultipliers[i]);
            double Protected(int i) => double.IsNegativeInfinity(Guard(Current(), i)) ? 0
                : Math.Sqrt(penalty) * Math.Max(0, Guard(Current(), i) + multipliers[i] / penalty);
            for (var i = 0; i < raw.Values.Length; i++)
            {
                var k = i; problem.AddOperand(new Operand("目标超限 " + i, 0, 1, () => Objective(k)));
                problem.AddOperand(new Operand("目标保护 " + i, 0, 1, () => Protected(k)));
            }
            if (analytic) problem.SetValueJacobianEvaluator(vector =>
            {
                var saved = phase.ToArray(); Array.Copy(vector.ToArray(), phase, phase.Length);
                try
                {
                    var system = Current(); var matrix = new double[system.Values.Length * 2, phase.Length];
                    for (var i = 0; i < system.Values.Length; i++) for (var j = 0; j < phase.Length; j++)
                    {
                        var derivative = system.Derivatives![i, indices[j]] * units[j];
                        if (Target(system, i) + targetMultipliers[i] > 0) matrix[2 * i, j] = derivative / TargetScale(system.Keys[i]);
                        // Use the active linearization at a precision-near hinge. The residual and
                        // the final target envelope remain exact; starting raw ceilings avoid cancellation.
                        if (Guard(system, i) + multipliers[i] / penalty >= -GeneralConstraintInteriorGuard)
                            matrix[2 * i + 1, j] = Math.Sqrt(penalty) * derivative / GuardScale(system.Keys[i]);
                    }
                    return matrix;
                }
                finally { Array.Copy(saved, phase, phase.Length); }
            });
            var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, allowance);
            evaluations += report.FunctionEvaluations; iterations += report.Iterations; remaining -= Math.Max(1, report.Iterations);
            work.Jacobians += problem.AnalyticJacobianEvaluations; reason = report.StopReason;
            var system = Current(); var violation = 0.0;
            for (var i = 0; i < system.Values.Length; i++)
            {
                var g = Guard(system, i); violation = Math.Max(violation, Math.Max(0, g));
                multiplierMap[system.Keys[i]] = double.IsNegativeInfinity(g) ? 0 : Math.Min(1e12, Math.Max(0, multipliers[i] + penalty * g));
                targetMultiplierMap[system.Keys[i]] = double.IsNegativeInfinity(system.Values[i]) ? 0
                    : Math.Min(1e12, Math.Max(0, targetMultipliers[i] + Target(system, i)));
            }
            progress?.Report(new("逐项目标保护膜厚优化", evaluations, bestAssessment.Merit));
            if (bestAssessment.CoarselyFeasible)
            {
                if (best.SequenceEqual(d.Layers) && validatedStart?.Result?.Passed == true) { reason = "ProtectedTargetsSatisfied"; break; }
                var certified = Evaluate(d with { Layers = best, Result = null }, progress: progress, token: token); work.Dense++;
                if (certified.Result!.Passed) { reason = "ProtectedTargetsSatisfied"; break; }
                // Feed independently measured failing features back into the guide while budget remains.
                var m = certified.Result.Metrics;
                var feedback = new[] { m.PeakWavelengthNanometers, m.LeftHalfMaximumNanometers ?? d.Target.CenterNm, m.RightHalfMaximumNanometers ?? d.Target.CenterNm }
                    .Concat(d.Target.StopBands!.Select(b => certified.Result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm)
                        .MaxBy(s => s.Power.LogTransmittance)!.WavelengthNanometers));
                grid = GeneralGrid(d, grid.Concat(feedback));
                bestAssessment = GuidanceAssessment(ThinFilmSpectrum.Sample(Solver(d, materials, best), grid, d.Target.AngleDegrees, d.Target.Polarization));
                work.Spectrum += grid.Length;
            }
            if (violation > previousViolation / 2) penalty = Math.Min(1e12, penalty * 10);
            previousViolation = violation;
            if (remaining > 0 && !unscaledSampledDirection && relativeProgress && !PreferGeneral(bestAssessment, passStartAssessment))
            {
                relativeProgress = false;
                for (var j = 0; j < indices.Length; j++) phase[j] = best[indices[j]].ThicknessNm / units[j];
                multiplierMap.Clear(); targetMultiplierMap.Clear(); penalty = 1;
                previousViolation = double.PositiveInfinity; work.ProtectionRestarts++;
            }
            if (remaining > 0)
            {
                var tolerance = Math.Min(d.Target.CenterToleranceNm / d.Target.FwhmNm, d.Target.FwhmToleranceNm / d.Target.FwhmNm) / 16;
                var refined = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, Layers()), grid, d.Target.AngleDegrees, d.Target.Polarization,
                    Math.Max(double.Epsilon, tolerance), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
                work.Spectrum += refined.Evaluations; work.Refinements++; work.Limit |= refined.LimitReached;
                grid = GeneralGrid(d, refined.Samples.Select(s => s.WavelengthNanometers));
            }
        }
        return (best, bestAssessment.Merit, new(d.Settings.Optimizer, GeneralCoatingProtocol, reason, evaluations, iterations, d.Settings.RandomSeed)
        {
            DesignProtocol = GeneralCoatingProtocol,
            GeneralWork = work.Snapshot(),
            TargetViolationCeilings = validatedStart is null ? baseline.Violations : AssessGeneral(validatedStart, validatedStart.Result!.Spectrum).Violations,
            TargetEnvelopeSampling = validatedStart is null ? "guidance-start" : "dense-start",
            TargetProtectionPhase = "joint-targets"
        });
    }
}
