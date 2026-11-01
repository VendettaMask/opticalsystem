using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineFeasibleReflectanceQuality(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations, GeneralCounter work,
        IProgress<DesignProgress>? progress, CancellationToken token, bool spectralMetric = false, bool certifiedRefinement = false)
    {
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, j) => d.Layers[i].ThicknessNm / units[j]).ToArray(); var initialPhase = phase.ToArray();
        var grid = initialGrid; var ar = d.Target.Kind == DesignKind.Antireflection;
        FilmLayer[] Layers(double[] values)
        {
            var layers = d.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            { ThicknessNm = values[j] == initialPhase[j] ? d.Layers[indices[j]].ThicknessNm : Math.Clamp(values[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) };
            return layers;
        }
        (double[] Loss, double[,]? Gradient) Read(FilmLayer[] layers, bool derivatives)
        {
            var solver = Solver(d, materials, layers); var loss = new double[grid.Length];
            var gradient = derivatives ? new double[grid.Length, phase.Length] : null;
            for (var i = 0; i < grid.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var sensitivity = derivatives ? solver.EvaluateThicknessSensitivity(grid[i], d.Target.AngleDegrees, d.Target.Polarization) : null;
                var power = sensitivity?.Power ?? solver.Evaluate(grid[i], d.Target.AngleDegrees, d.Target.Polarization);
                if (derivatives) work.Sensitivity++; else work.Spectrum++;
                loss[i] = Math.Max(0, ar ? power.Reflectance : 1 - power.Reflectance);
                if (derivatives) for (var j = 0; j < phase.Length; j++) gradient![i, j] = (ar ? 1 : -1) * sensitivity!.Layers[indices[j]].Reflectance * units[j];
            }
            return (loss, gradient);
        }
        var best = d.Layers; var worst = Read(best, false).Loss.Max(); var scale = worst;
        if (worst <= 0) return (best, 0, new(d.Settings.Optimizer, GeneralCoatingProtocol, "ReflectancePhysicalLimit", 0, 0, d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() });
        var lambda = 1e-3; var iterations = 0; long evaluations = 0;
        var period = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations)));
        for (; iterations < maximumIterations; iterations++)
        {
            token.ThrowIfCancellationRequested();
            if (iterations % period == 0) { work.Passes++; work.QualityPasses++; }
            var current = Read(Layers(phase), true); work.Jacobians++; evaluations++;
            var level = current.Loss.Max() / scale;
            // Penalize the mean squared predicted spectral change as a positive
            // sensitivity metric. It changes step conditioning, not the worst-loss
            // objective or its physical inequality bounds.
            var jacobian = new double[spectralMetric ? grid.Length + 1 : 1, phase.Length + 1]; jacobian[0, phase.Length] = 1;
            var residuals = new double[jacobian.GetLength(0)]; residuals[0] = level;
            var metricScale = scale * Math.Sqrt(grid.Length);
            if (spectralMetric) for (var i = 0; i < grid.Length; i++) for (var j = 0; j < phase.Length; j++)
                jacobian[i + 1, j] = current.Gradient![i, j] / metricScale;
            var a = new double[grid.Length + 2 * phase.Length + 2, phase.Length + 1]; var b = new double[a.GetLength(0)];
            for (var i = 0; i < grid.Length; i++)
            {
                b[i] = Math.Max(0, level - current.Loss[i] / scale); a[i, phase.Length] = -1;
                for (var j = 0; j < phase.Length; j++) a[i, j] = current.Gradient![i, j] / scale;
            }
            for (var j = 0; j < phase.Length; j++)
            {
                a[grid.Length + 2 * j, j] = 1; a[grid.Length + 2 * j + 1, j] = -1;
                b[grid.Length + 2 * j] = Math.Max(0, Math.Min(1, d.Settings.MaximumThicknessNm / units[j] - phase[j]));
                b[grid.Length + 2 * j + 1] = Math.Max(0, Math.Min(1, phase[j] - d.Settings.MinimumThicknessNm / units[j]));
            }
            a[a.GetLength(0) - 2, phase.Length] = -1; b[^2] = level;
            a[a.GetLength(0) - 1, phase.Length] = 1;
            var accepted = false;
            for (var attempt = 0; attempt < 6 && !accepted; attempt++)
            {
                var step = InequalityLeastSquaresStep.Solve(jacobian, residuals, a, b, lambda); work.InequalitySteps++; work.InequalityIterations += step.ActiveSetIterations;
                if (!step.Converged) work.InequalityLimits++;
                for (var fraction = 1.0; fraction >= 1.0 / 128; fraction /= 2)
                {
                    var trialPhase = phase.Select((p, j) => Math.Clamp(p + fraction * step.Step[j], d.Settings.MinimumThicknessNm / units[j], d.Settings.MaximumThicknessNm / units[j])).ToArray(); if (trialPhase.SequenceEqual(phase)) break;
                    var layers = Layers(trialPhase); var trial = Read(layers, false).Loss.Max(); evaluations++;
                    if (trial >= worst) continue;
                    phase = trialPhase; best = layers; worst = trial; accepted = true; lambda = Math.Max(1e-10, lambda / 3); break;
                }
                if (!accepted) lambda = Math.Min(1e12, lambda * 10);
            }
            work.QualityIterations++;
            progress?.Report(new("通用反射率质量优化", evaluations, worst));
            if (worst == 0) break;
            if ((iterations + 1) % period != 0 || iterations + 1 == maximumIterations) continue;
            var adaptive = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, best), grid, d.Target.AngleDegrees, d.Target.Polarization,
                Math.Max(double.Epsilon, worst / 16), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
            work.Spectrum += adaptive.Evaluations; work.Refinements++; work.Limit |= adaptive.LimitReached;
            grid = GeneralGrid(d, adaptive.Samples.Select(s => s.WavelengthNanometers));
            worst = Read(best, false).Loss.Max(); var original = Read(d.Layers, false).Loss.Max();
            if (original < worst) { best = d.Layers; worst = original; }
            for (var j = 0; j < phase.Length; j++) phase[j] = best[indices[j]].ThicknessNm / units[j];
        }
        var assessment = AssessGeneral(d, GeneralConstraintSystem(d, Solver(d, materials, best), grid, false, work).Samples);
        var run = new RunRecord(d.Settings.Optimizer, GeneralCoatingProtocol, "ReflectanceInequalityStep", evaluations, Math.Min(maximumIterations, iterations + 1), d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot(), QualityStartLayers = d.Layers };
        if (!spectralMetric && certifiedRefinement)
        {
            var baseline = Evaluate(d with { Layers = best, Result = null }, progress: progress, token: token); work.Dense++;
            if (!baseline.Result!.Passed)
            {
                var metricWork = new GeneralCounter();
                var worstPoint = ar ? baseline.Result.Spectrum.MaxBy(s => s.Power.Reflectance)! : baseline.Result.Spectrum.MinBy(s => s.Power.Reflectance)!;
                var metricGrid = GeneralGrid(d, grid.Append(worstPoint.WavelengthNanometers));
                var metric = RefineFeasibleReflectanceQuality(d with { Layers = best, Result = null }, materials, metricGrid,
                    Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations))), metricWork, progress, token, spectralMetric: true);
                work.Add(metric.Run.GeneralWork! with { QualityIterations = 0, QualityPasses = 0 });
                work.MetricIterations += metric.Run.Iterations;
                var candidate = Evaluate(d with { Layers = metric.Layers, Result = null }, progress: progress, token: token); work.Dense++;
                var accepted = (!baseline.Result.SamplingConverged || candidate.Result!.SamplingConverged)
                    && WithinGeneralTargetEnvelope(AssessGeneral(d, candidate.Result!.Spectrum), AssessGeneral(d, baseline.Result.Spectrum))
                    && (ar ? candidate.Result.Metrics.MaximumReflectance < baseline.Result.Metrics.MaximumReflectance
                        : candidate.Result.Metrics.MinimumReflectance > baseline.Result.Metrics.MinimumReflectance);
                run = run with { Evaluations = run.Evaluations + metric.Run.Evaluations,
                    ReflectanceMetricStartLayers = best, ReflectanceMetricResultLayers = metric.Layers, ReflectanceMetricAccepted = accepted };
                if (accepted) { best = candidate.Layers; assessment = AssessGeneral(d, candidate.Result!.Spectrum); }
            }
        }
        return (best, assessment.Merit, run with { GeneralWork = work.Snapshot() });
    }
}
