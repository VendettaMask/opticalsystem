using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string TargetRepairProtocol = "pareto-target-correction/3";
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineFeasibleGeneralTargets(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations, GeneralCounter work,
        Experiment? validatedStart, IProgress<DesignProgress>? progress, CancellationToken token)
    {
        var main = RefineProtectedGeneralThickness(d, materials, initialGrid, maximumIterations, work, validatedStart, progress, token, unscaledSampledDirection: true);
        if (validatedStart is null) return main;
        var anchor = Evaluate(d with { Layers = main.Layers, Result = null }, progress: progress, token: token); work.Dense++;
        var raw = anchor;
        var inputAssessment = AssessGeneral(d, validatedStart.Result!.Spectrum);
        var anchorAssessment = AssessGeneral(d, anchor.Result!.Spectrum);
        if (!WithinGeneralTargetEnvelope(anchorAssessment, inputAssessment) || validatedStart.Result.Passed && !anchor.Result.Passed
            || validatedStart.Result.Passed == anchor.Result.Passed && PreferGeneral(inputAssessment, anchorAssessment))
        { anchor = validatedStart; anchorAssessment = inputAssessment; }
        var direction = ReferenceEquals(anchor, validatedStart) ? "validated-input" : "original-scale";
        // No single residual scaling dominates across peak, width and OD. Compare the two
        // declared general directions, then protect their certified winner during repair.
        // This is extra optimization work and is recorded separately from the main budget.
        var comparisonWork = new GeneralCounter();
        var comparison = RefineProtectedGeneralThickness(d, materials, initialGrid, maximumIterations, comparisonWork, validatedStart, progress, token);
        work.Add(comparison.Run.GeneralWork); work.ComparisonRuns++; work.ComparisonIterations += comparison.Run.Iterations;
        var relative = Evaluate(d with { Layers = comparison.Layers, Result = null }, progress: progress, token: token); work.Dense++;
        var relativeAssessment = AssessGeneral(d, relative.Result!.Spectrum);
        if (WithinGeneralTargetEnvelope(relativeAssessment, inputAssessment) && (!validatedStart.Result.Passed || relative.Result.Passed)
            && (relative.Result.Passed && !anchor.Result!.Passed || relative.Result.Passed == anchor.Result!.Passed && PreferGeneral(relativeAssessment, anchorAssessment)))
        { anchor = relative; anchorAssessment = relativeAssessment; direction = "relative-deficit"; }
        var combinedRun = main.Run with
        {
            Evaluations = main.Run.Evaluations + comparison.Run.Evaluations, StopReason = "CertifiedTargetDirectionComparison",
            TargetRepairBaselineDirection = direction,
            TargetDirectionTrials = [
                new("original-scale", raw.Layers, raw.Result!.Metrics, raw.Result.Merit, raw.Result.Passed, main.Run.Iterations, main.Run.Evaluations, main.Run.StopReason, direction == "original-scale"),
                new("relative-deficit", relative.Layers, relative.Result.Metrics, relative.Result.Merit, relative.Result.Passed, comparison.Run.Iterations, comparison.Run.Evaluations, comparison.Run.StopReason, direction == "relative-deficit")]
        };
        main = (main.Layers, main.Merit, combinedRun);
        if (anchor.Result!.Passed || anchorAssessment.CoarselyFeasible)
            return (anchor.Layers, anchor.Result.Merit, main.Run with { GeneralWork = work.Snapshot() });
        var best = anchor;
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, j) => anchor.Layers[i].ThicknessNm / units[j]).ToArray(); var initialPhase = phase.ToArray();
        FilmLayer[] Layers(double[] values)
        {
            var layers = anchor.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            { ThicknessNm = values[j] == initialPhase[j] ? anchor.Layers[indices[j]].ThicknessNm : Math.Clamp(values[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) };
            return layers;
        }
        static int Metric(GeneralConstraintKey key) => key.Group switch { 0 => 0, -2 or -3 => 1, -4 or -5 => 2, >= 6 => key.Group - 3, _ => -1 };
        CoatingConstraintAssessment Guide(GeneralSystem system)
        {
            var assessment = AssessGeneral(d, system.Samples); work.BandMetrics++;
            var center = system.Values.Where((v, i) => Metric(system.Keys[i]) == 2 && double.IsFinite(v)).ToArray();
            if (center.Length == 0) return assessment;
            var violations = assessment.Violations.ToArray(); violations[2] = Math.Max(0, center.Max() - GeneralConstraintInteriorGuard);
            return assessment with { Violations = violations };
        }
        var waveLow = Math.Max(d.Target.MinimumNm, d.Target.CenterNm - 2 * d.Target.FwhmNm);
        var waveHigh = Math.Min(d.Target.MaximumNm, d.Target.CenterNm + 2 * d.Target.FwhmNm);
        var baseSolver = Solver(d, materials, anchor.Layers);
        double[] PeakWaves(FilmLayer[] layers, ThinFilmSample[] samples)
        {
            var solver = Solver(d, materials, layers); var peaks = new List<double>();
            foreach (var band in new[] { new SpectralBand(waveLow, waveHigh) }.Concat(d.Target.StopBands!))
            {
                var local = samples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).ToArray();
                if (local.Length < 3) continue;
                var peak = ThinFilmSpectrum.RefineTransmissionPeak(solver, local, d.Target.AngleDegrees, d.Target.Polarization);
                work.Spectrum += peak.Evaluations; peaks.Add(peak.Peak.WavelengthNanometers);
            }
            return peaks.ToArray();
        }
        double[] WithPeaks(double[] fixedWaves, double[] peaks)
        {
            var waves = fixedWaves.ToList();
            foreach (var peak in peaks) if (waves.Min(w => Math.Abs(w - peak)) > d.Target.FwhmNm * GeneralConstraintInteriorGuard) waves.Add(peak);
            return GeneralGrid(d, waves);
        }
        double[] ReferencePeaks(Experiment current) => PeakWaves(current.Layers,
            SearchPeakReferenceSamples(current, Solver(current, materials), work));
        var peakAnchors = ReferencePeaks(anchor);
        double[] Grid(int intervals) => WithPeaks(GeneralGrid(d, initialGrid.Concat(ThinFilmSpectrum.Grid(waveLow, waveHigh, intervals))
            .Concat(d.Target.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, intervals / 16)))), peakAnchors);
        var coarseGrid = Grid(512); var grid = Grid(1024);
        var fixedGrid = GeneralGrid(d, initialGrid.Concat(ThinFilmSpectrum.Grid(waveLow, waveHigh, 1024))
            .Concat(d.Target.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 64))));
        var fixedCoarseGrid = GeneralGrid(d, initialGrid.Concat(ThinFilmSpectrum.Grid(waveLow, waveHigh, 512))
            .Concat(d.Target.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 32))));
        GeneralSystem Read(FilmLayer[] layers, bool derivatives) => GeneralConstraintSystem(d, Solver(d, materials, layers), grid, derivatives, work);
        var original = Read(anchor.Layers, false); var baseline = Guide(original);
        var coarse = Guide(GeneralConstraintSystem(d, baseSolver, coarseGrid, false, work));
        var goal = Enumerable.Range(0, baseline.Violations.Length).Where(i => baseline.Violations[i] > 0 && anchorAssessment.Violations[i] > 0)
            .OrderBy(i => baseline.Violations[i]).FirstOrDefault(-1);
        if (anchor.Result.Passed || goal < 0 || !original.Keys.Where((k, i) => k.Group is -2 or -3 && double.IsFinite(original.Values[i])).Any())
            return (anchor.Layers, anchor.Result.Merit, main.Run with { GeneralWork = work.Snapshot() });
        var firstGoal = goal; var stageAssessment = anchorAssessment;
        var marginFactor = 2.0;
        double[] Desired() => baseline.Violations.Select((v, i) => i == goal ? 0 : Math.Max(0, v - marginFactor * Math.Abs(v - coarse.Violations[i]) - GeneralConstraintInteriorGuard)).ToArray();
        var desired = Desired();
        // Keep guidance inside the existing dense sampling resolution. This is a search
        // buffer, not a relaxation of the exact peak/centre/OD checks below.
        double Value(GeneralSystem system, int i) => system.Values[i] + (Metric(system.Keys[i]) switch
        {
            0 when d.Target.PeakTransmittance < 1 => Math.Min(1e-5, (1 - d.Target.PeakTransmittance) / 2) / d.Target.PeakTransmittance,
            2 => d.Target.CenterToleranceNm / (10 * d.Target.FwhmNm),
            _ => 0
        });
        double Violation(GeneralSystem system) => Enumerable.Range(0, system.Values.Length).Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .Select(i => Math.Max(0, Value(system, i) - desired[Metric(system.Keys[i])])).DefaultIfEmpty(0).Max();
        long extraEvaluations = 0;
        var history = new List<TargetRepairTrial>();
        var firstWindow = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)));
        var maximumCorrections = maximumIterations; var allowance = firstWindow;
        var repairStop = "InitialIterationLimit";
        FilmLayer[]? continuationStart = null; double[]? continuationCeilings = null;
        var restorationGoal = goal; var lastRestoration = anchorAssessment.Violations[goal];
        var previousRestoration = double.PositiveInfinity; var restorationTrials = 0; var lastRestorationStep = 0;
        var windowMerit = best.Result!.Merit; var windowRestoration = lastRestoration;
        var windowGoal = goal; var windowAccepted = work.RepairAccepted;
        var windows = new List<TargetRepairWindow>();
        var finished = false;
        double[]? lastTangentPhase = null;
        bool Certify(int correction, FilmLayer[] layers, bool monotone = false)
        {
            var certified = Evaluate(d with { Layers = layers, Result = null }, progress: progress, token: token); work.Dense++;
            var assessment = AssessGeneral(d, certified.Result!.Spectrum);
            var ceiling = monotone ? AssessGeneral(d, best.Result!.Spectrum) : stageAssessment;
            var restoration = Math.Max(assessment.Violations[goal], assessment.Violations.Zip(ceiling.Violations).Max(p => Math.Max(0, p.First - p.Second)));
            if (!monotone)
            {
                if (restorationGoal != goal) { restorationGoal = goal; lastRestoration = double.PositiveInfinity; restorationTrials = 0; }
                previousRestoration = lastRestoration; lastRestoration = restoration; restorationTrials++; lastRestorationStep = correction + 1;
            }
            var completesGoal = assessment.Violations[goal] == 0 && AssessGeneral(d, best.Result!.Spectrum).Violations[goal] > 0;
            var retained = certified.Result.Metrics.FwhmNanometers.HasValue && WithinGeneralTargetEnvelope(assessment, ceiling) && (certified.Result.Passed && !best.Result!.Passed
                || certified.Result.Passed == best.Result!.Passed && (completesGoal || certified.Result.Merit < best.Result.Merit));
            if (retained) { best = certified; if (monotone) work.ParetoAccepted++; else work.RepairAccepted++; }
            else if (!WithinGeneralTargetEnvelope(assessment, anchorAssessment)) work.DenseRejections++;
            history.Add(new(correction + 1, layers, certified.Result.Metrics, assessment.Violations, certified.Result.SamplingConverged, retained)
            { GoalIndex = goal, Stage = monotone ? "monotone-pareto" : "target-restoration", ViolationCeilings = ceiling.Violations.ToArray(), RestorationViolation = restoration });
            progress?.Report(new("逐项目标可行性校正", extraEvaluations, best.Result!.Merit));
            if (best.Result!.Passed) finished = true;
            else if (retained && assessment.Violations[goal] == 0)
            {
                stageAssessment = assessment;
                var stageSolver = Solver(d, materials, best.Layers); var stagePeaks = ReferencePeaks(best);
                grid = WithPeaks(fixedGrid, stagePeaks); coarseGrid = WithPeaks(fixedCoarseGrid, stagePeaks);
                baseline = Guide(Read(best.Layers, false)); coarse = Guide(GeneralConstraintSystem(d, stageSolver, coarseGrid, false, work));
                goal = Enumerable.Range(0, baseline.Violations.Length).Where(i => baseline.Violations[i] > 0 && stageAssessment.Violations[i] > 0)
                    .OrderBy(i => baseline.Violations[i]).FirstOrDefault(-1);
                if (goal < 0) finished = true;
                else
                {
                    marginFactor = 2; desired = Desired();
                    windowGoal = goal; windowRestoration = assessment.Violations[goal];
                }
            }
            return retained;
        }
        for (var correction = 0; correction < allowance && !finished; correction++)
        {
            token.ThrowIfCancellationRequested(); work.RepairIterations++;
            if (correction >= firstWindow) work.ContinuationIterations++;
            var preview = Read(Layers(phase), false); extraEvaluations++;
            grid = WithPeaks(fixedGrid, PeakWaves(Layers(phase), preview.Samples));
            var current = Read(Layers(phase), true); work.Jacobians++; extraEvaluations++;
            var rows = Enumerable.Range(0, current.Values.Length).Where(i => Metric(current.Keys[i]) >= 0 && double.IsFinite(current.Values[i])).ToArray();
            var elastic = Violation(current);
            var objective = new double[1, phase.Length + 1]; objective[0, phase.Length] = 1;
            var a = new double[rows.Length + 2 * phase.Length + 2, phase.Length + 1]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < rows.Length; i++)
            {
                b[i] = Math.Max(0, desired[Metric(current.Keys[rows[i]])] - Value(current, rows[i]) + elastic); a[i, phase.Length] = -1;
                for (var j = 0; j < phase.Length; j++) a[i, j] = current.Derivatives![rows[i], indices[j]] * units[j];
            }
            for (var j = 0; j < phase.Length; j++)
            {
                a[rows.Length + 2 * j, j] = 1; a[rows.Length + 2 * j + 1, j] = -1;
                b[rows.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                b[rows.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            a[a.GetLength(0) - 2, phase.Length] = -1; b[^2] = elastic; a[a.GetLength(0) - 1, phase.Length] = 1;
            var step = InequalityLeastSquaresStep.Solve(objective, [1 + elastic], a, b, .001);
            work.InequalitySteps++; work.InequalityIterations += step.ActiveSetIterations; if (!step.Converged) work.InequalityLimits++;
            var accepted = false;
            for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
            {
                var trialPhase = phase.Select((p, j) => Math.Clamp(p + fraction * step.Step[j], d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray();
                if (trialPhase.SequenceEqual(phase)) break;
                var layers = Layers(trialPhase); var trial = Read(layers, false); extraEvaluations++;
                if (!trial.Keys.Where((k, i) => k.Group is -2 or -3 && double.IsFinite(trial.Values[i])).Any()) continue;
                if (Violation(trial) >= elastic) continue;
                phase = trialPhase; accepted = true;
                Certify(correction, layers);
                break;
            }
            if (!accepted && elastic > GeneralConstraintInteriorGuard && (lastTangentPhase is null || !lastTangentPhase.SequenceEqual(phase)))
            {
                lastTangentPhase = phase.ToArray();
                var normals = new double[rows.Length, phase.Length];
                for (var i = 0; i < rows.Length; i++) for (var j = 0; j < phase.Length; j++) normals[i, j] = a[i, j];
                var projector = InequalityLeastSquaresStep.TangentProjector(normals);
                var radius = Math.Min(1, Math.Sqrt(Math.Max(elastic, GeneralConstraintInteriorGuard)));
                var candidate = phase; var candidateViolation = elastic;
                // Search actual curvature in the common first-order tangent space. All free
                // coordinates receive both signs; no layer identity selects a direction.
                for (var column = 0; column < phase.Length; column++)
                {
                    var norm = Math.Sqrt(Enumerable.Range(0, phase.Length).Sum(j => projector[j, column] * projector[j, column]));
                    if (norm <= GeneralConstraintInteriorGuard) continue;
                    foreach (var sign in new[] { -1, 1 })
                    {
                        var trialPhase = phase.Select((p, j) => Math.Clamp(p + sign * radius * projector[j, column] / norm,
                            d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray();
                        var trial = Read(Layers(trialPhase), false); extraEvaluations++; work.TangentTrials++;
                        if (!trial.Keys.Where((k, i) => k.Group is -2 or -3 && double.IsFinite(trial.Values[i])).Any()) continue;
                        var violation = Violation(trial);
                        if (violation < candidateViolation - GeneralConstraintInteriorGuard * Math.Max(1, elastic))
                        { candidate = trialPhase; candidateViolation = violation; }
                    }
                }
                if (!candidate.SequenceEqual(phase)) { phase = candidate; accepted = true; Certify(correction, Layers(phase)); }
            }
            if (!accepted)
            {
                if (marginFactor > .25) { marginFactor /= 2; desired = Desired(); }
                else { repairStop = "ModelNoDescent"; break; }
            }
            if (correction + 1 == allowance && maximumCorrections > allowance && !finished && lastRestorationStep == allowance)
            {
                var certifiedProgress = work.RepairAccepted > windowAccepted
                    && best.Result!.Merit < windowMerit - GeneralConstraintInteriorGuard * Math.Max(windowMerit, GeneralConstraintInteriorGuard);
                var contractingRecovery = restorationGoal == goal && windowGoal == goal && restorationTrials >= 2
                    && lastRestoration > GeneralConstraintInteriorGuard
                    && lastRestoration < previousRestoration - GeneralConstraintInteriorGuard
                    && lastRestoration <= windowRestoration / 2;
                if (certifiedProgress || contractingRecovery)
                {
                    var previousLimit = allowance; allowance = Math.Min(maximumCorrections, allowance + firstWindow);
                    repairStop = "ContinuationIterationLimit";
                    stageAssessment = AssessGeneral(d, best.Result!.Spectrum);
                    continuationStart ??= best.Layers; continuationCeilings ??= stageAssessment.Violations.ToArray();
                    windows.Add(new(previousLimit, allowance, goal, best.Result!.Merit, stageAssessment.Violations.ToArray(),
                        certifiedProgress ? "CertifiedProgress" : "ContractingRecovery"));
                    windowMerit = best.Result.Merit;
                    windowRestoration = restorationGoal == goal ? lastRestoration : stageAssessment.Violations[goal];
                    windowGoal = goal;
                    windowAccepted = work.RepairAccepted;
                }
            }
        }
        if (finished) repairStop = best.Result!.Passed ? "Passed" : "NoRemainingGuidedTarget";
        // A separate constrained LS phase improves the certified point itself. All
        // metric ceilings are hard linearized bounds; failed dense trials never
        // become the next iterate. This avoids spending peak/OD to chase width.
        var polishLambda = .001; var polishStop = "IterationLimit";
        var polishAllowance = Math.Min(firstWindow, Math.Max(0, maximumIterations - work.RepairIterations));
        for (var polish = 0; polish < polishAllowance && !best.Result!.Passed && goal >= 0; polish++)
        {
            token.ThrowIfCancellationRequested(); work.ParetoIterations++;
            phase = indices.Select((i, j) => best.Layers[i].ThicknessNm / units[j]).ToArray();
            grid = WithPeaks(fixedGrid, ReferencePeaks(best));
            var current = Read(best.Layers, true); work.Jacobians++; extraEvaluations++;
            var currentAssessment = Guide(current);
            var rows = Enumerable.Range(0, current.Values.Length).Where(i => Metric(current.Keys[i]) >= 0 && double.IsFinite(current.Values[i])).ToArray();
            var objective = new double[rows.Length, phase.Length]; var residuals = new double[rows.Length];
            var a = new double[rows.Length + 2 * phase.Length, phase.Length]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; residuals[i] = Math.Max(0, Value(current, row));
                b[i] = Math.Max(0, currentAssessment.Violations[Metric(current.Keys[row])] - Value(current, row));
                for (var j = 0; j < phase.Length; j++)
                {
                    a[i, j] = current.Derivatives![row, indices[j]] * units[j];
                    if (residuals[i] > 0) objective[i, j] = a[i, j];
                }
            }
            for (var j = 0; j < phase.Length; j++)
            {
                a[rows.Length + 2 * j, j] = 1; a[rows.Length + 2 * j + 1, j] = -1;
                b[rows.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                b[rows.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            var accepted = false;
            for (var attempt = 0; attempt < 6 && !accepted; attempt++)
            {
                var step = InequalityLeastSquaresStep.Solve(objective, residuals, a, b, polishLambda);
                work.InequalitySteps++; work.InequalityIterations += step.ActiveSetIterations; if (!step.Converged) work.InequalityLimits++;
                for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
                {
                    var trialPhase = phase.Select((p, j) => Math.Clamp(p + fraction * step.Step[j], d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray();
                    if (trialPhase.SequenceEqual(phase)) break;
                    var trial = Read(Layers(trialPhase), false); extraEvaluations++;
                    var assessment = Guide(trial);
                    if (!WithinGeneralTargetEnvelope(assessment, currentAssessment) || assessment.Merit >= currentAssessment.Merit) continue;
                    accepted = Certify(work.RepairIterations + polish, Layers(trialPhase), monotone: true);
                    if (accepted) { polishLambda = Math.Max(1e-10, polishLambda / 3); break; }
                }
                if (!accepted) polishLambda = Math.Min(1e12, polishLambda * 10);
            }
            if (!accepted) { polishStop = "NoCertifiedDescent"; break; }
        }
        if (best.Result!.Passed) polishStop = "Passed";
        else if (goal < 0) polishStop = "NoRemainingGuidedTarget";
        return (best.Layers, best.Result!.Merit, main.Run with
        { Evaluations = main.Run.Evaluations + extraEvaluations, GeneralWork = work.Snapshot(), TargetProtectionPhase = "pareto-target-correction",
            TargetRepairStartLayers = anchor.Layers, TargetRepairViolationCeilings = anchorAssessment.Violations, TargetRepairGoalIndex = firstGoal, TargetRepairTrials = history.ToArray(),
            TargetRepairStopReason = repairStop, TargetRepairContinuationStartLayers = continuationStart, TargetRepairContinuationCeilings = continuationCeilings,
            TargetParetoStopReason = polishStop, TargetRepairWindows = windows.ToArray(), TargetRefinementIterationLimit = maximumIterations });
    }
}
