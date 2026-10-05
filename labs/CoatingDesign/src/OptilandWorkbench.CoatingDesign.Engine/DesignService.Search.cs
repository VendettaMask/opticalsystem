using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string SearchVersion = "bounded-structure-multistart/1";

    public Experiment Search(Experiment input, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); d.Validate();
        var options = d.Settings.Search ?? new();
        var seeds = new List<FilmLayer[]>();
        if (d.Layers.Length > 0) seeds.Add(d.Layers);
        // Fixed layers prevent topology changes, including regeneration; thickness-only starts retain them exactly.
        var structural = options.VaryStructure && d.Layers.All(l => l.Variable);
        var generatedStructures = structural || seeds.Count == 0;
        if (generatedStructures)
        {
            var generated = Generate(d, progress, token);
            seeds.AddRange((generated.Candidates ?? []).Select(c => c.Layers));
            if (structural && d.Layers.Length > 0)
            {
                seeds.Add(d.Layers.Reverse().ToArray());
                if (d.Layers.Length > 1) seeds.Add(d.Layers.Take(d.Layers.Length - 1).ToArray());
                if (d.Layers.Length + 2 <= d.Target.MaximumLayers)
                    seeds.Add(d.Layers.Concat(generated.Layers.Take(2)).ToArray());
                seeds.Add(d.Layers.Select(l => l with { MaterialId = l.MaterialId == d.LowId ? d.HighId : d.LowId }).ToArray());
            }
        }
        seeds = seeds.Where(l => l.Length <= d.Target.MaximumLayers && l.All(x => x.ThicknessNm >= d.Settings.MinimumThicknessNm
            && x.ThicknessNm <= d.Settings.MaximumThicknessNm)).DistinctBy(l => string.Join(';', l.Select(x => $"{x.MaterialId}:{x.ThicknessNm:R}:{x.Variable}"))).ToList();
        if (seeds.Count == 0) throw new ArgumentException("没有满足膜厚和层数约束的搜索起点。请生成膜系或修正当前结构。");
        // Rank every topology through the same objective before spending the local optimization budget.
        var materials = Materials(d with { Layers = seeds.SelectMany(x => x).Distinct().Take(200).ToArray() });
        var grid = ObjectiveGrid(d);
        seeds = seeds.OrderBy(l => Merit(Objective(d, Solver(d, materials, l), grid))).ToList();
        var random = new Random(d.Settings.RandomSeed);
        var jitter = new UniformSampler(-options.JitterPercent / 100, options.JitterPercent / 100);
        var candidates = new List<Candidate>();
        Experiment? best = null;
        long evaluations = 0;
        var iterations = 0;
        for (var start = 0; start < options.Starts; start++)
        {
            token.ThrowIfCancellationRequested();
            var layers = seeds[start % seeds.Count].Select(l => start < seeds.Count || !l.Variable ? l : l with
            {
                ThicknessNm = Math.Clamp(l.ThicknessNm * (1 + jitter.Sample(random)), d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm)
            }).ToArray();
            progress?.Report(new($"结构搜索 {start + 1}/{options.Starts}", evaluations, best?.Result?.Merit));
            var candidate = Optimize(d with { Layers = layers, Result = null, Candidates = null, Tolerance = null }, progress, token);
            var result = candidate.Result!;
            evaluations += result.Run.Evaluations; iterations += result.Run.Iterations;
            candidates.Add(new($"搜索 {start + 1} · {layers.Length} 层 · {(result.Passed ? "达标" : "未达标")}", candidate.Layers, result.Merit, result.Passed, result.Run));
            if (best is null || (result.Passed && !best.Result!.Passed) || (result.Passed == best.Result!.Passed && result.Merit < best.Result.Merit)) best = candidate;
        }
        var run = new RunRecord((generatedStructures ? "结构枚举 + " : "") + "多起点 " + d.Settings.Optimizer, SearchVersion,
            "起点预算完成；达标由独立复验判定", evaluations, iterations, d.Settings.RandomSeed);
        return best! with { Candidates = candidates.OrderByDescending(x => x.Passed).ThenBy(x => x.Merit).ToArray(), Result = best.Result! with { Run = run } };
    }
}
