using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
if (args[0] == "--replay")
{
    await FrozenReplay.Run(args[1], args[2], json);
    return;
}
var specs = JsonSerializer.Deserialize<InitialStructureSpecification[]>(File.ReadAllText(args[0]), json)!;
Directory.CreateDirectory(args[1]);
if (args[2] is "--full" or "--quick")
{
    var spec = specs[1] with { FlatStart = specs[1].FlatStart! with { UsePhysicalStop = true } };
    var options = new FlatStartSearchOptions
    {
        MaximumDisplayedCandidates = 10,
        DesignSearch = new() { Mode = args[2] == "--full" ? DesignSearchMode.Full : DesignSearchMode.Quick }
    };
    var watch = Stopwatch.StartNew();
    var last = 0;
    var result = await new FlatStartSearchService().RunAsync(spec, options, checkpointSink: (checkpoint, _) =>
    {
        if (checkpoint.ChargedEvaluations > last + 1000)
        {
            last = checkpoint.ChargedEvaluations;
            Console.WriteLine($"{options.DesignSearch.Mode} {last} evaluations; {checkpoint.Trials.Count} trials; {watch.Elapsed.TotalSeconds:F1}s");
        }
        return ValueTask.CompletedTask;
    });
    FlatStartCheckpointValidation.Validate(result.Checkpoint);
    await new FlatStartSearchCheckpointStore().SaveAsync(result.Checkpoint, Path.Combine(args[1], "search.family.json"));
    File.WriteAllText(Path.Combine(args[1], "summary.json"), JsonSerializer.Serialize(new
    {
        CoreHash = Hash(typeof(Optic).Assembly.Location),
        EngineHash = Hash(typeof(FlatStartSearchService).Assembly.Location),
        spec,
        options,
        result.Checkpoint.Algorithm,
        result.Checkpoint.ChargedEvaluations,
        result.Checkpoint.TracedRealRayCount,
        ElapsedSeconds = watch.Elapsed.TotalSeconds,
        Candidates = result.Candidates
    }, json));
    return;
}
var rows = new List<object>();
var recovery = args[2] == "--recovery";
foreach (var original in specs)
{
    var physical = original with
    {
        FlatStart = original.FlatStart! with { UsePhysicalStop = true },
        Budget = original.Budget with { MaximumEvaluations = 800 }
    };
    var families = DesignFormSearch.Roots(original, new(), ["N-BK7", "N-F2", "N-SF6"]);
    foreach (var index in recovery ? new[] { 6, 7 } : new[] { 0, 1, 6, 7 })
    {
        var family = families[index];
        foreach (var deferred in recovery ? new[] { false } : new[] { false, true })
        {
            var watch = Stopwatch.StartNew();
            var service = new FlatStartDesignService();
            var first = service.Solve(deferred ? physical with
            {
                FlatStart = physical.FlatStart! with { UsePhysicalStop = false },
                Budget = physical.Budget with { MaximumEvaluations = 400 }
            } : physical, family.ElementCount, family: family);
            var result = first;
            var charged = first.EvaluationCount;
            var traced = first.TracedRayCount;
            if (deferred && first.Candidate is { } start)
            {
                result = service.Continue(physical with { Budget = physical.Budget with { MaximumEvaluations = 800 - charged } },
                    family, first.Bootstrap, start.Optic, start.CandidateId, improveBeyondTargets: true);
                charged += result.EvaluationCount;
                traced += result.TracedRayCount;
            }
            rows.Add(new
            {
                FNumber = original.FNumber,
                Index = index,
                Deferred = deferred,
                family,
                Charged = charged,
                Traced = traced,
                ElapsedSeconds = watch.Elapsed.TotalSeconds,
                PhysicalFinal = result.FinalValidation?.Objective?.UsePhysicalStop == true,
                result.State,
                result.Candidate,
                result.FinalValidation,
                result.Diagnostics,
                StageEntries = result.Steps.Where(s => s.Operation != "curvature-thickness-step").Select(s => new
                {
                    s.Operation,
                    s.Stage,
                    s.EvaluationNumber,
                    s.Evaluation.HasContinuousSearchResiduals,
                    s.Evaluation.GeometryFeasible,
                    s.Evaluation.MeetsTargets
                })
            });
            File.WriteAllText(Path.Combine(args[1], "starts.json"), JsonSerializer.Serialize(rows, json));
            Console.WriteLine($"F/{original.FNumber} root {index} deferred={deferred}: {charged} evaluations; {result.Candidate?.Status}; RMS {result.Candidate?.Evaluation.RmsSpotRadiusMillimeters}; {watch.Elapsed.TotalSeconds:F1}s");
        }
    }
}

static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
