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
        var main = RefineProtectedGeneralThickness(d, materials, initialGrid, maximumIterations, work, validatedStart, progress, token, unscaledSampledDirection: true);
        if (validatedStart is null) return main;
        var anchor = Evaluate(d with { Layers = main.Layers, Result = null }, progress: progress, token: token); work.Dense++;
        var inputAssessment = AssessGeneral(d, validatedStart.Result!.Spectrum);
        var anchorAssessment = AssessGeneral(d, anchor.Result!.Spectrum);
        if (!WithinGeneralTargetEnvelope(anchorAssessment, inputAssessment) || validatedStart.Result.Passed && !anchor.Result.Passed
            || validatedStart.Result.Passed == anchor.Result.Passed && PreferGeneral(inputAssessment, anchorAssessment))
        { anchor = validatedStart; anchorAssessment = inputAssessment; }
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
        var refinedPeak = ThinFilmSpectrum.RefineTransmissionPeak(baseSolver, anchor.Result!.Spectrum.Where(s => s.WavelengthNanometers >= waveLow && s.WavelengthNanometers <= waveHigh).ToArray(),
            d.Target.AngleDegrees, d.Target.Polarization); work.Spectrum += refinedPeak.Evaluations;
        var peakAnchor = initialGrid.Min(w => Math.Abs(w - refinedPeak.Peak.WavelengthNanometers)) > d.Target.FwhmNm * GeneralConstraintInteriorGuard
            ? new[] { refinedPeak.Peak.WavelengthNanometers } : Array.Empty<double>();
        double[] Grid(int intervals) => GeneralGrid(d, initialGrid.Concat(peakAnchor).Concat(ThinFilmSpectrum.Grid(waveLow, waveHigh, intervals))
            .Concat(d.Target.StopBands!.SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, intervals / 16))));
        var coarseGrid = Grid(512); var grid = Grid(1024);
        GeneralSystem Read(FilmLayer[] layers, bool derivatives) => GeneralConstraintSystem(d, Solver(d, materials, layers), grid, derivatives, work);
        var original = Read(anchor.Layers, false); var baseline = Guide(original);
        var coarse = Guide(GeneralConstraintSystem(d, baseSolver, coarseGrid, false, work));
        var goal = Enumerable.Range(0, baseline.Violations.Length).Where(i => baseline.Violations[i] > 0 && anchorAssessment.Violations[i] > 0)
            .OrderBy(i => baseline.Violations[i]).FirstOrDefault(-1);
        if (anchor.Result.Passed || goal < 0 || !original.Keys.Where((k, i) => k.Group is -2 or -3 && double.IsFinite(original.Values[i])).Any())
            return (anchor.Layers, anchor.Result.Merit, main.Run with { GeneralWork = work.Snapshot() });
        var desired = baseline.Violations.Select((v, i) => i == goal ? 0 : Math.Max(0, v - 2 * Math.Abs(v - coarse.Violations[i]) - GeneralConstraintInteriorGuard)).ToArray();
        double Violation(GeneralSystem system) => Enumerable.Range(0, system.Values.Length).Where(i => Metric(system.Keys[i]) >= 0 && double.IsFinite(system.Values[i]))
            .Select(i => Math.Max(0, system.Values[i] - desired[Metric(system.Keys[i])])).DefaultIfEmpty(0).Max();
        long extraEvaluations = 0;
        var maximumCorrections = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)));
        for (var correction = 0; correction < maximumCorrections; correction++)
        {
            token.ThrowIfCancellationRequested(); work.RepairIterations++;
            var current = Read(Layers(phase), true); work.Jacobians++; extraEvaluations++;
            var rows = Enumerable.Range(0, current.Values.Length).Where(i => Metric(current.Keys[i]) >= 0 && double.IsFinite(current.Values[i])).ToArray();
            var elastic = Violation(current);
            var objective = new double[1, phase.Length + 1]; objective[0, phase.Length] = 1;
            var a = new double[rows.Length + 2 * phase.Length + 2, phase.Length + 1]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < rows.Length; i++)
            {
                b[i] = Math.Max(0, desired[Metric(current.Keys[rows[i]])] - current.Values[rows[i]] + elastic); a[i, phase.Length] = -1;
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
                if (Violation(trial) >= elastic) continue;
                phase = trialPhase; accepted = true;
                var certified = Evaluate(d with { Layers = layers, Result = null }, progress: progress, token: token); work.Dense++;
                var assessment = AssessGeneral(d, certified.Result!.Spectrum);
                if (WithinGeneralTargetEnvelope(assessment, anchorAssessment) && (certified.Result.Passed && !best.Result!.Passed
                    || certified.Result.Passed == best.Result!.Passed && certified.Result.Merit < best.Result.Merit))
                { best = certified; work.RepairAccepted++; }
                else if (!WithinGeneralTargetEnvelope(assessment, anchorAssessment)) work.DenseRejections++;
                progress?.Report(new("逐项目标可行性校正", extraEvaluations, best.Result!.Merit));
                if (best.Result!.Passed || best.Result.Merit < anchor.Result.Merit && AssessGeneral(d, best.Result.Spectrum).Violations[goal] == 0)
                    correction = maximumCorrections;
                break;
            }
            if (!accepted) break;
        }
        return (best.Layers, best.Result!.Merit, main.Run with
        { Evaluations = main.Run.Evaluations + extraEvaluations, GeneralWork = work.Snapshot(), TargetProtectionPhase = "pareto-target-correction" });
    }
}
