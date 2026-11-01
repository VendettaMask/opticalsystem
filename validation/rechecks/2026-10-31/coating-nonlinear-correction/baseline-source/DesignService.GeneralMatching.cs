using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    private IncidentMatchingTrial[] MatchGeneralIncidentMedium(Experiment d, IReadOnlyDictionary<string, IMaterial> materials, CancellationToken token)
    {
        if (d.Layers.Length == 0 || d.Layers.Any(l => !l.Variable)) return [];
        if (!WithinBounds(d, d.Layers)) throw new ArgumentException("匹配前结构不满足原层数/膜厚界。");
        var before = Solver(d, materials).Evaluate(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization);
        var trials = new List<IncidentMatchingTrial>();
        foreach (var ids in new[] { new[] { d.LowId }, new[] { d.HighId }, new[] { d.LowId, d.HighId }, new[] { d.HighId, d.LowId } })
        {
            var units = ids.Select(id => CoherentThinFilmSolver.CharacteristicThickness(materials[id], materials[d.IncidentId],
                d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
            var lower = ids.Select(_ => d.Settings.MinimumThicknessNm).ToArray();
            var upper = units.Select(q => Math.Min(2 * q, d.Settings.MaximumThicknessNm)).ToArray();
            var merges = ids[^1] == d.Layers[0].MaterialId;
            if (merges)
            {
                // A matched interface is a physical role, not a layer-number exception: optimize the increment of the same material.
                lower[^1] = 0; upper[^1] = Math.Min(upper[^1], d.Settings.MaximumThicknessNm - d.Layers[0].ThicknessNm);
            }
            if (upper.Where((u, i) => u < lower[i]).Any()) continue;
            var canonicalCount = d.Layers.Length + ids.Length - (merges ? 1 : 0);
            if (canonicalCount > d.Target.MaximumLayers) continue;
            var starts = ids.Length == 1 ? new[] { new[] { .3 }, new[] { 1.0 }, new[] { 1.7 } }
                : (from a in new[] { .3, 1, 1.7 } from b in new[] { .3, 1, 1.7 } select new[] { a, b }).ToArray();
            foreach (var start in starts)
            {
                token.ThrowIfCancellationRequested();
                var phase = start.Select((v, i) => Math.Clamp(v, lower[i] / units[i], upper[i] / units[i])).ToArray();
                FilmLayer[] Construct()
                {
                    var cap = ids.Select((id, i) => new FilmLayer(id, Math.Clamp(phase[i] * units[i], lower[i], upper[i]))).ToArray();
                    if (!merges) return cap.Concat(d.Layers).ToArray();
                    return cap.Take(cap.Length - 1).Append(d.Layers[0] with
                    { ThicknessNm = Math.Clamp(d.Layers[0].ThicknessNm + cap[^1].ThicknessNm, d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm) }).Concat(d.Layers.Skip(1)).ToArray();
                }
                var initialLayers = Construct();
                var work = new GeneralCounter(); var problem = new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
                for (var j = 0; j < phase.Length; j++)
                { var k = j; problem.AddVariable(new DelegateVariable("入射界面相位 " + j, () => phase[k], v => phase[k] = v, lower[k] / units[k], upper[k] / units[k], stepHint: 1)); }
                double[]? cachedPhase = null; ThinFilmThicknessSensitivity? sensitivity = null;
                FilmLayer[]? best = null; var bestTransmission = double.NegativeInfinity;
                ThinFilmThicknessSensitivity Current()
                {
                    token.ThrowIfCancellationRequested();
                    if (cachedPhase is not null && cachedPhase.SequenceEqual(phase)) return sensitivity!;
                    var layers = Construct();
                    if (!WithinBounds(d, layers)) throw new InvalidOperationException("匹配候选规范化后违反约束。");
                    sensitivity = Solver(d, materials, layers).EvaluateThicknessSensitivity(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization);
                    work.Sensitivity++; cachedPhase = phase.ToArray();
                    if (sensitivity.Power.Transmittance > bestTransmission) { bestTransmission = sensitivity.Power.Transmittance; best = layers; }
                    return sensitivity;
                }
                // Throughput is the existing narrowband target. Minimizing R alone could instead increase absorption.
                double Loss() => Math.Sqrt(Math.Max(0, 1 - Current().Power.Transmittance));
                problem.AddOperand(new Operand("实际中心透射损失", 0, 1, Loss));
                if (d.Settings.Optimizer == "Damped Least Squares") problem.SetValueJacobianEvaluator(vector =>
                {
                    var saved = phase.ToArray(); Array.Copy(vector.ToArray(), phase, phase.Length);
                    try
                    {
                        var current = Current(); var loss = Loss(); var matrix = new double[1, phase.Length];
                        if (loss > Math.Sqrt(double.Epsilon))
                            for (var j = 0; j < phase.Length; j++) matrix[0, j] = -current.Layers[j].Transmittance * units[j] / (2 * loss);
                        return matrix;
                    }
                    finally { Array.Copy(saved, phase, phase.Length); }
                });
                var report = OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem, Math.Min(32, d.Settings.MaximumIterations));
                work.Jacobians += problem.AnalyticJacobianEvaluations;
                best ??= Construct();
                var after = Solver(d, materials, best).Evaluate(d.Target.CenterNm, d.Target.AngleDegrees, d.Target.Polarization); work.Spectrum++;
                // Every phase root is retained with its own real work record; global spectral/Pareto ranking follows.
                trials.Add(new(string.Join('/', ids.Select(id => id == d.LowId ? "L" : "H")) + " 入射侧匹配", best, before, after,
                    new(d.Settings.Optimizer, "throughput-multiple-root-matching/2", report.StopReason, report.FunctionEvaluations, report.Iterations, null)
                    { DesignProtocol = GeneralCoatingProtocol, GeneralWork = work.Snapshot() })
                { InitialMatchingLayers = initialLayers });
            }
        }
        return trials.ToArray();
    }
}
