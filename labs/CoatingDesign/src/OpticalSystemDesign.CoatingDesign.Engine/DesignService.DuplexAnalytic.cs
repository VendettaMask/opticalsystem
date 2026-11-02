using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string DuplexAnalyticProtocol = "duplex-analytic-restoration-quality/2";
    private Experiment OptimizeDuplexAnalytic(Experiment input, IProgress<DesignProgress>? progress, CancellationToken token,
        int? iterations, bool certify)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); d.Validate(); var options = d.Duplex!; var materials = Materials(d);
        var variables = new List<(bool Back, int Index)>();
        if (options.Optimize != CoatingDesignSurface.Back)
            variables.AddRange(d.Layers.Select((l, i) => (l, i)).Where(x => x.l.Variable).Select(x => (false, x.i)));
        if (options.Optimize != CoatingDesignSurface.Front && !options.Symmetric)
            variables.AddRange(d.EffectiveBackLayers.Select((l, i) => (l, i)).Where(x => x.l.Variable).Select(x => (true, x.i)));
        var all = d.Layers.Concat(d.EffectiveBackLayers).ToArray();
        if (all.Length == 0 || d.Layers.Length > d.Target.MaximumLayers || d.EffectiveBackLayers.Length > d.Target.MaximumLayers
            || all.Any(l => l.ThicknessNm < d.Settings.MinimumThicknessNm || l.ThicknessNm > d.Settings.MaximumThicknessNm))
            throw new ArgumentException("双面膜层为空或超出原层数/厚度约束，请先修正。");
        var budget = OptimizationLimits.RequireIterationCount(iterations ?? d.Settings.MaximumIterations);
        var grid = GeneralGrid(d, ObjectiveGrid(d)); long powers = 0, sensitivities = 0, jacobians = 0; int rejected = 0;
        var values = variables.Select(v => (v.Back ? d.EffectiveBackLayers : d.Layers)[v.Index].ThicknessNm).ToArray();
        Experiment WithValues(IReadOnlyList<double> point)
        {
            var front = d.Layers.ToArray(); var back = d.EffectiveBackLayers.ToArray();
            for (var j = 0; j < variables.Count; j++)
            { var v = variables[j]; var films = v.Back ? back : front; films[v.Index] = films[v.Index] with { ThicknessNm = point[j] }; }
            return d with { Layers = front, BackLayers = options.Symmetric ? null : back, Result = null, Tolerance = null };
        }
        var best = d; CoatingConstraintAssessment? ceiling = null, bestAssessment = null; var bestQuality = double.PositiveInfinity;
        double[]? cachedPoint = null; GeneralSystem? cachedSystem = null; bool cachedAnalytic = false;
        ThinFilmThicknessSensitivity Map(ThinFilmThicknessSensitivity s, int frontCount, int backCount, CoatingEvaluationSurface surface)
        {
            var zero = new ThinFilmThicknessDerivative(0, 0, 0, 0);
            var flattened = surface == CoatingEvaluationSurface.WholePlate ? s.Layers
                : surface == CoatingEvaluationSurface.Front ? s.Layers.Concat(Enumerable.Repeat(zero, backCount)).ToArray()
                : Enumerable.Repeat(zero, frontCount).Concat(s.Layers).ToArray();
            ThinFilmThicknessDerivative Sum(ThinFilmThicknessDerivative a, ThinFilmThicknessDerivative b) =>
                new(a.Reflectance + b.Reflectance, a.Transmittance + b.Transmittance, a.Absorptance + b.Absorptance, a.LogTransmittance + b.LogTransmittance);
            return s with
            {
                Layers = variables.Select(v => options.Symmetric
                ? Sum(flattened[v.Index], flattened[frontCount + v.Index])
                : flattened[(v.Back ? frontCount : 0) + v.Index]).ToArray()
            };
        }
        GeneralSystem SystemAt(IReadOnlyList<double> point, bool analytic)
        {
            token.ThrowIfCancellationRequested();
            if (cachedPoint is not null && point.SequenceEqual(cachedPoint) && (!analytic || cachedAnalytic)) return cachedSystem!;
            var current = WithValues(point); var evaluate = PowerEvaluator(current, materials);
            CoherentThinFilmSolver? front = null, rear = null;
            if (analytic)
            {
                front = Solver(current, materials);
                rear = new(materials[current.StackGeometry!.ExitMediumId], materials[current.SubstrateId],
                    current.EffectiveBackLayers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            }
            var samples = new ThinFilmSample[grid.Length]; var derivatives = analytic ? new ThinFilmThicknessSensitivity[grid.Length] : null;
            for (var i = 0; i < grid.Length; i++)
            {
                if (!analytic) { samples[i] = new(grid[i], evaluate(grid[i], d.Target.AngleDegrees, d.Target.Polarization)); powers++; }
                else
                {
                    var s = options.Evaluate switch
                    {
                        CoatingEvaluationSurface.Front => front!.EvaluateThicknessSensitivity(grid[i], d.Target.AngleDegrees, d.Target.Polarization),
                        CoatingEvaluationSurface.Back => rear!.EvaluateThicknessSensitivity(grid[i], d.Target.AngleDegrees, d.Target.Polarization),
                        _ => front!.EvaluateIncoherentSubstrateThicknessSensitivity(rear!, current.StackGeometry!.SubstrateThicknessMm * 1e6,
                            grid[i], d.Target.AngleDegrees, d.Target.Polarization)
                    };
                    derivatives![i] = Map(s, current.Layers.Length, current.EffectiveBackLayers.Length, options.Evaluate);
                    samples[i] = new(grid[i], s.Power); sensitivities++;
                }
            }
            var system = GeneralConstraintSystem(current, grid, samples, derivatives);
            var assessment = AssessGeneral(current, samples); var quality = Quality(current, samples);
            ceiling ??= assessment;
            if (WithinGeneralTargetEnvelope(assessment, ceiling))
            {
                if (bestAssessment is null || PreferGeneral(assessment, bestAssessment)
                    || assessment.Violations.SequenceEqual(bestAssessment.Violations) && quality < bestQuality)
                { best = current; bestAssessment = assessment; bestQuality = quality; }
            }
            else rejected++;
            cachedPoint = point.ToArray(); cachedSystem = system; cachedAnalytic = analytic;
            return system;
        }
        static double Quality(Experiment current, ThinFilmSample[] samples) => current.Target.Kind switch
        {
            DesignKind.Antireflection => samples.Average(s => s.Power.Reflectance),
            DesignKind.HighReflector => samples.Average(s => 1 - s.Power.Reflectance),
            _ => 1 - Metrics(current, samples).PeakTransmittance
        };
        var initial = SystemAt(values, false); bestAssessment = AssessGeneral(d, initial.Samples);
        var restorationBudget = budget <= 2 ? budget : (2 * budget + 2) / 3;
        var qualityBudget = budget - restorationBudget;
        var reports = new List<OptimizerResult>();
        void Stage(bool quality, int stageBudget)
        {
            if (stageBudget == 0 || variables.Count == 0) return;
            var start = variables.Select(v => (v.Back ? best.EffectiveBackLayers : best.Layers)[v.Index].ThicknessNm).ToArray();
            Array.Copy(start, values, start.Length); ceiling = bestAssessment; cachedPoint = null;
            var baseSize = SystemAt(values, false).Values.Length;
            var extra = quality ? (d.Target.Kind == DesignKind.NarrowBand ? 1 : grid.Length) : 0;
            (double[] Residuals, double[,]? Jacobian) ResidualsAt(IReadOnlyList<double> point, bool analytic)
            {
                var system = SystemAt(point, analytic); var r = new double[baseSize + extra];
                var j = analytic ? new double[r.Length, variables.Count] : null;
                for (var i = 0; i < baseSize; i++)
                {
                    r[i] = Math.Max(0, system.Values[i]);
                    if (analytic && system.Values[i] > 0) for (var k = 0; k < variables.Count; k++) j![i, k] = system.Derivatives![i, k];
                }
                if (quality)
                {
                    // Differentiate quality through the same shared power/spectrum path, never a numerical optical surrogate.
                    if (d.Target.Kind == DesignKind.NarrowBand)
                    {
                        var t = d.Target; var peak = Enumerable.Range(0, grid.Length).Where(i => Math.Abs(grid[i] - t.CenterNm) <= 2 * t.FwhmNm)
                            .MaxBy(i => system.Samples[i].Power.Transmittance);
                        r[baseSize] = .02 * (1 - system.Samples[peak].Power.Transmittance);
                        if (analytic)
                        {
                            var s = SelectedSensitivity(WithValues(point), materials, grid[peak]);
                            for (var k = 0; k < variables.Count; k++) j![baseSize, k] = -.02 * Map(s, d.Layers.Length, d.EffectiveBackLayers.Length, options.Evaluate).Layers[k].Transmittance; sensitivities++;
                        }
                    }
                    else
                    {
                        var sign = d.Target.Kind == DesignKind.Antireflection ? 1.0 : -1.0;
                        var scale = d.Target.Kind == DesignKind.Antireflection ? d.Target.Reflectance : 1 - d.Target.Reflectance;
                        if (scale == 0) scale = 1;
                        scale *= Math.Sqrt(grid.Length);
                        for (var i = 0; i < grid.Length; i++)
                        {
                            r[baseSize + i] = .02 * (sign > 0 ? system.Samples[i].Power.Reflectance : 1 - system.Samples[i].Power.Reflectance) / scale;
                            // Raw reflectance constraints differ only by their known normalization.
                            if (analytic) for (var k = 0; k < variables.Count; k++) j![baseSize + i, k] = .02 * system.Derivatives![i, k];
                        }
                    }
                }
                return (r, j);
            }
            var problem = new OptimizationProblem(); problem.DisableBatching();
            for (var k = 0; k < variables.Count; k++)
            {
                var index = k; problem.AddVariable(new DelegateVariable($"膜厚 {k + 1}", () => values[index], x => values[index] = x,
                d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm, .01));
            }
            for (var i = 0; i < baseSize + extra; i++) { var row = i; problem.AddOperand(new Operand($"双面目标 {i + 1}", 0, 1, () => ResidualsAt(values, false).Residuals[row])); }
            problem.SetValueJacobianEvaluator(point => { jacobians++; return ResidualsAt(point, true).Jacobian; });
            progress?.Report(new(quality ? "双面质量提升（解析导数）" : "双面目标恢复（解析导数）", powers + sensitivities, bestAssessment!.Merit));
            reports.Add(OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, stageBudget));
        }
        Stage(false, restorationBudget); Stage(true, qualityBudget);
        var dense = 0; long densePoints = 0, densePowers = 0;
        RunRecord Record()
        {
            var last = reports.LastOrDefault();
            return new(last?.Algorithm ?? "无变量", last?.AlgorithmVersion ?? "none", last?.StopReason ?? "全部可优化膜层固定",
                reports.Sum(r => r.FunctionEvaluations), reports.Sum(r => r.Iterations), last?.RandomSeed)
            {
                DesignProtocol = DuplexAnalyticProtocol,
                Duplex = new(options, variables.Count, d.Layers, d.EffectiveBackLayers, powers)
                {
                    Analytic = new(sensitivities, jacobians, reports.Count > 0 ? reports[0].Iterations : 0,
                    reports.Count > 1 ? reports[1].Iterations : 0, dense, densePoints, densePowers, rejected, budget)
                }
            };
        }
        if (!certify) return best with { Candidates = [new("双面解析短程候选", best.Layers, bestAssessment!.Merit, false, Record()) { BackLayers = best.BackLayers }] };
        var baseline = EvaluateCounted(d, null, progress, token, () => densePowers++); dense++; densePoints += baseline.Result!.Spectrum.Length;
        var result = EvaluateCounted(best, null, progress, token, () => densePowers++); dense++; densePoints += result.Result!.Spectrum.Length;
        var run = Record();
        if (!WithinGeneralTargetEnvelope(AssessGeneral(result, result.Result.Spectrum), AssessGeneral(baseline, baseline.Result.Spectrum))
            || baseline.Result.Passed && !result.Result.Passed)
            result = baseline with { Result = baseline.Result with { Run = run with { StopReason = "DenseValidationRetainedInput" } } };
        else result = result with { Result = result.Result with { Run = run } };
        return result with
        {
            Candidates = (d.Candidates ?? []).Append(new Candidate("双面解析优化结果", result.Layers, result.Result!.Merit,
            result.Result.Passed, result.Result.Run)
            { BackLayers = result.BackLayers }).TakeLast(12).ToArray()
        };
    }
    private static ThinFilmThicknessSensitivity SelectedSensitivity(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, double wavelength)
    {
        var front = Solver(d, materials); var rear = new CoherentThinFilmSolver(materials[d.StackGeometry!.ExitMediumId], materials[d.SubstrateId],
            d.EffectiveBackLayers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        return d.Duplex!.Evaluate switch
        {
            CoatingEvaluationSurface.Front => front.EvaluateThicknessSensitivity(wavelength, d.Target.AngleDegrees, d.Target.Polarization),
            CoatingEvaluationSurface.Back => rear.EvaluateThicknessSensitivity(wavelength, d.Target.AngleDegrees, d.Target.Polarization),
            _ => front.EvaluateIncoherentSubstrateThicknessSensitivity(rear, d.StackGeometry.SubstrateThicknessMm * 1e6, wavelength, d.Target.AngleDegrees, d.Target.Polarization)
        };
    }
}
