using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string SearchVersion = "bounded-structure-multistart/1";
    public const string ScreenedSearchVersion = "bounded-structure-multistart/2";

    public Experiment Search(Experiment input, IProgress<DesignProgress>? progress = null, CancellationToken token = default)
    {
        using var scope = ComputationCancellation.Push(token);
        var d = input.Snapshot(); d.Validate();
        if (d.Settings.DesignProtocolVersion == 4 && d.Target.Kind == DesignKind.NarrowBand) return GenerateGeneralNarrowBand(d, Materials(d), true, progress, token);
        if (d.Settings.DesignProtocolVersion == 3) return GenerateNarrowBand(d, Materials(d), true, progress, token);
        var options = d.Settings.Search ?? new();
        var seeds = new List<FilmLayer[]>();
        Experiment? generation = null;
        if (d.Layers.Length > 0) seeds.Add(d.Layers);
        // Fixed layers prevent topology changes, including regeneration; thickness-only starts retain them exactly.
        var structural = options.VaryStructure && d.Layers.All(l => l.Variable);
        var generatedStructures = structural || seeds.Count == 0;
        if (generatedStructures)
        {
            var generated = generation = Generate(d, progress, token);
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
        var generalWork = new GeneralCounter(); generalWork.Add(generation?.Result?.Run.GeneralWork);
        seeds = d.Settings.DesignProtocolVersion == 4
            ? seeds.OrderBy(l => GeneralReflectanceRank(d, materials, l, grid, generalWork)).ToList()
            : seeds.OrderBy(l => ObjectiveMerit(d, Objective(d, Solver(d, materials, l), grid))).ToList();
        var rankingEvaluations = seeds.Count;
        var random = new Random(d.Settings.RandomSeed);
        var jitter = new UniformSampler(-options.JitterPercent / 100, options.JitterPercent / 100);
        var candidates = new List<Candidate>();
        Experiment? best = null;
        long evaluations = 0;
        var iterations = 0;
        var screens = new List<ScreeningRun>();
        if (d.Settings.DesignProtocolVersion is 2 or 4)
        {
            screens.AddRange(generation?.Result?.Run.Screening ?? []);
            evaluations += screens.Sum(s => s.Run.Evaluations); iterations += screens.Sum(s => s.Run.Iterations);
            var pool = DiverseScreenPool(seeds, 32).ToArray();
            var eligible = new List<ScreeningRun>();
            for (var i = 0; i < pool.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var previous = screens.FirstOrDefault(s => LayerIdentity(s.Layers) == LayerIdentity(pool[i]));
                if (previous is not null) { eligible.Add(previous); continue; }
                if (screens.Count == 32) continue;
                progress?.Report(new($"候选短程优化 {i + 1}/{pool.Length}", evaluations));
                var refined = RefineThickness(d with { Layers = pool[i] }, materials, grid,
                    Math.Min(2, d.Settings.MaximumIterations), progress, token);
                evaluations += refined.Run.Evaluations; iterations += refined.Run.Iterations;
                var screen = new ScreeningRun(screens.Count + 1, pool[i], refined.Layers, refined.Merit, refined.Run);
                generalWork.Add(refined.Run.GeneralWork);
                screens.Add(screen); eligible.Add(screen);
            }
            seeds = eligible.OrderBy(s => s.Merit).Select(s => s.Layers).ToList();
            if (d.Settings.DesignProtocolVersion == 4)
                seeds = seeds.OrderBy(l => GeneralReflectanceRank(d, materials, l, grid, generalWork)).ToList();
        }
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
            generalWork.Add(result.Run.GeneralWork);
            evaluations += result.Run.Evaluations; iterations += result.Run.Iterations;
            candidates.Add(new($"搜索 {start + 1} · {layers.Length} 层 · {(result.Passed ? "达标" : "未达标")}", candidate.Layers, result.Merit, result.Passed, result.Run));
            if (best is null || (result.Passed && !best.Result!.Passed) || (result.Passed == best.Result!.Passed
                && (d.Settings.DesignProtocolVersion == 4 ? PreferGeneral(AssessGeneral(candidate, result.Spectrum), AssessGeneral(best, best.Result.Spectrum)) : result.Merit < best.Result.Merit))) best = candidate;
        }
        var run = new RunRecord((generatedStructures ? "结构枚举 + " : "")
            + (screens.Count > 0 ? "候选短程筛选 + " : "") + "多起点 " + d.Settings.Optimizer,
            d.Settings.DesignProtocolVersion == 4 ? GeneralCoatingProtocol : d.Settings.DesignProtocolVersion == 2 ? ScreenedSearchVersion : SearchVersion,
            "起点预算完成；达标由独立复验判定", evaluations, iterations, d.Settings.RandomSeed)
        {
            DesignProtocol = Protocol(d),
            Screening = screens.Count > 0 ? screens.ToArray() : null,
            TemplateEvaluations = d.Settings.DesignProtocolVersion is 2 or 4 ? (generation?.Result?.Run.TemplateEvaluations ?? 0) + rankingEvaluations : null,
            GeneralWork = d.Settings.DesignProtocolVersion == 4 ? generalWork.Snapshot() : null
        };
        return best! with { Candidates = candidates.OrderByDescending(x => x.Passed).ThenBy(x => x.Merit).ToArray(), Result = best.Result! with { Run = run } };
    }
}
