using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string ReflectanceQualityProtocol = "worst-reflectance/7";

    // Minimize the epigraph of R (AR) or 1-R (HR), on either side of the original acceptance threshold.
    // The level is bounded by the starting worst loss; no empirical feasible-side weight is added.
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineGeneralReflectanceQuality(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations, GeneralCounter work,
        IProgress<DesignProgress>? progress, CancellationToken token, bool certifiedRefinement = false)
    {
        var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId],
            materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i, j) => d.Layers[i].ThicknessNm / units[j]).ToArray();
        var grid = initialGrid; var direction = d.Target.Kind == DesignKind.Antireflection ? 1.0 : -1.0;
        FilmLayer[] Layers()
        {
            var layers = d.Layers.ToArray();
            for (var j = 0; j < indices.Length; j++) layers[indices[j]] = layers[indices[j]] with
            { ThicknessNm = Math.Clamp(phase[j] * units[j], d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) };
            return layers;
        }
        (double[] Loss, double[,]? Gradient) Read(FilmLayer[] layers, bool derivatives)
        {
            var solver = Solver(d, materials, layers); var loss = new double[grid.Length];
            var gradient = derivatives ? new double[grid.Length, indices.Length] : null;
            for (var i = 0; i < grid.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var sensitivity = derivatives ? solver.EvaluateThicknessSensitivity(grid[i], d.Target.AngleDegrees, d.Target.Polarization) : null;
                var p = sensitivity?.Power ?? solver.Evaluate(grid[i], d.Target.AngleDegrees, d.Target.Polarization);
                if (derivatives) work.Sensitivity++; else work.Spectrum++;
                loss[i] = Math.Max(0, direction > 0 ? p.Reflectance : 1 - p.Reflectance);
                if (derivatives && loss[i] > 0) for (var j = 0; j < indices.Length; j++)
                    gradient![i, j] = direction * sensitivity!.Layers[indices[j]].Reflectance * units[j];
            }
            return (loss, gradient);
        }
        var best = d.Layers; var initialLoss = Read(best, false).Loss; var bestLoss = initialLoss.Max(); var scale = bestLoss;
        if (scale <= 0) return (best, 0, new(d.Settings.Optimizer, GeneralCoatingProtocol, "ReflectancePhysicalLimit", 0, 0, d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() });
        var requestedLoss = d.Target.Kind == DesignKind.Antireflection ? d.Target.Reflectance : 1 - d.Target.Reflectance;
        if (bestLoss > requestedLoss && d.Settings.Optimizer == "Damped Least Squares")
            return RefineFeasibleReflectanceQuality(d, materials, initialGrid, maximumIterations, work, progress, token, certifiedRefinement: certifiedRefinement);
        var level = 1.0; var penalty = 1.0; var remaining = maximumIterations;
        var multiplierMap = new Dictionary<double, double>();
        // For a violated target, initialize the active worst-point duals to support z=1:
        // sum(lambda)/sqrt(N)=z. This supplies a physical thickness direction immediately.
        // A feasible start retains the established zero-dual quality continuation.
        if (bestLoss > requestedLoss)
        {
            var active = initialLoss.Count(loss => loss == bestLoss);
            for (var i = 0; i < grid.Length; i++)
                if (initialLoss[i] == bestLoss) multiplierMap[grid[i]] = Math.Sqrt(grid.Length) / active;
        }
        var previousViolation = double.PositiveInfinity;
        var canRestartDuals = bestLoss > requestedLoss;
        long evaluations = 0; var iterations = 0; var reason = "MaximumIterations";
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested(); work.Passes++; work.QualityPasses++;
            var passStartLoss = bestLoss;
            var problem = new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
            for (var j = 0; j < phase.Length; j++)
            {
                var k = j;
                problem.AddVariable(new DelegateVariable("光学相位变量 " + (j + 1), () => phase[k], v => phase[k] = v,
                    d.Settings.MinimumThicknessNm / units[k], d.Settings.MaximumThicknessNm / units[k], stepHint: 1));
            }
            problem.AddVariable(new DelegateVariable("归一化最差反射损失上界", () => level, v => level = v, 0, 1, stepHint: 1));
            var analytic = d.Settings.Optimizer == "Damped Least Squares";
            double[]? cachedPhase = null; (double[] Loss, double[,]? Gradient) cached = default;
            (double[] Loss, double[,]? Gradient) Current()
            {
                token.ThrowIfCancellationRequested();
                if (cachedPhase is not null && cachedPhase.SequenceEqual(phase)) return cached;
                var layers = Layers(); cached = Read(layers, analytic); cachedPhase = phase.ToArray();
                var worst = cached.Loss.Max();
                if (worst < bestLoss) { best = layers; bestLoss = worst; }
                return cached;
            }
            var normalizer = Math.Sqrt(grid.Length); var multipliers = grid.Select(w => multiplierMap.GetValueOrDefault(w)).ToArray();
            double Constraint(int i) => (Current().Loss[i] / scale - level) / normalizer;
            problem.AddOperand(new Operand("最差反射损失", 0, 1, () => level));
            for (var i = 0; i < grid.Length; i++)
            {
                var k = i;
                problem.AddOperand(new Operand("反射损失上界 " + i, 0, 1,
                    () => Math.Sqrt(penalty) * Math.Max(0, Constraint(k) + multipliers[k] / penalty)));
            }
            if (analytic) problem.SetValueJacobianEvaluator(vector =>
            {
                var saved = phase.ToArray(); var savedLevel = level;
                for (var j = 0; j < phase.Length; j++) phase[j] = vector[j]; level = vector[^1];
                try
                {
                    var current = Current(); var matrix = new double[grid.Length + 1, phase.Length + 1];
                    matrix[0, phase.Length] = 1;
                    for (var i = 0; i < grid.Length; i++) if (Constraint(i) + multipliers[i] / penalty > 0)
                    {
                        for (var j = 0; j < phase.Length; j++) matrix[i + 1, j] = Math.Sqrt(penalty) * current.Gradient![i, j] / (scale * normalizer);
                        matrix[i + 1, phase.Length] = -Math.Sqrt(penalty) / normalizer;
                    }
                    return matrix;
                }
                finally { Array.Copy(saved, phase, phase.Length); level = savedLevel; }
            });
            var allowance = Math.Min(remaining, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maximumIterations))));
            var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, allowance);
            evaluations += report.FunctionEvaluations; iterations += report.Iterations; work.QualityIterations += report.Iterations;
            remaining -= Math.Max(1, report.Iterations); work.Jacobians += problem.AnalyticJacobianEvaluations; reason = report.StopReason;
            var violation = 0.0;
            for (var i = 0; i < grid.Length; i++)
            {
                var g = Constraint(i); violation = Math.Max(violation, Math.Max(0, g));
                multiplierMap[grid[i]] = Math.Min(1e12, Math.Max(0, multipliers[i] + penalty * g));
            }
            progress?.Report(new("通用反射率质量优化", evaluations, bestLoss));
            if (bestLoss == 0) { reason = "ReflectancePhysicalLimit"; break; }
            if (violation > previousViolation / 2) penalty = Math.Min(1e12, penalty * 10);
            previousViolation = violation;
            if (remaining > 0 && canRestartDuals && bestLoss >= passStartLoss)
            {
                // A dual-supported infeasible start can reduce the auxiliary merit without improving
                // the physical worst point. Restart once from the retained physical best, sharing the budget.
                for (var j = 0; j < indices.Length; j++) phase[j] = best[indices[j]].ThicknessNm / units[j];
                level = Math.Min(1, bestLoss / scale); multiplierMap.Clear(); penalty = 1;
                previousViolation = double.PositiveInfinity; canRestartDuals = false; work.QualityRestarts++;
            }
            if (remaining > 0)
            {
                var refined = AdaptiveThinFilmSpectrum.Sample(Solver(d, materials, best), grid, d.Target.AngleDegrees, d.Target.Polarization,
                    Math.Max(double.Epsilon, bestLoss / 16), Math.Max(grid.Length, Math.Min(2048, grid.Length * 2)));
                work.Spectrum += refined.Evaluations; work.Refinements++; work.Limit |= refined.LimitReached;
                var newGrid = GeneralGrid(d, refined.Samples.Select(s => s.WavelengthNanometers));
                // Preserve the existing constraints' Lagrangian coefficients when 1/sqrt(N) changes.
                var multiplierScale = Math.Sqrt((double)newGrid.Length / grid.Length);
                foreach (var wave in multiplierMap.Keys.ToArray()) multiplierMap[wave] *= multiplierScale;
                penalty = Math.Min(1e12, penalty * multiplierScale * multiplierScale);
                previousViolation /= multiplierScale;
                grid = newGrid;
                bestLoss = Read(best, false).Loss.Max(); var originalLoss = Read(d.Layers, false).Loss.Max();
                if (originalLoss < bestLoss) { best = d.Layers; bestLoss = originalLoss; }
            }
        }
        var assessment = AssessGeneral(d, GeneralConstraintSystem(d, Solver(d, materials, best), grid, false, work).Samples);
        return (best, assessment.Merit, new(d.Settings.Optimizer, GeneralCoatingProtocol, "ReflectanceQuality:" + reason, evaluations, iterations, d.Settings.RandomSeed)
        { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot(), QualityStartLayers = d.Layers });
    }
}
