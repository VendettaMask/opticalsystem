using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var checkpoint = await new FlatStartSearchCheckpointStore().LoadAsync(args[0]);
var records = new List<object>();
foreach (var trial in checkpoint.Trials.Where(t => t.RefinementStartOptic is not null))
{
    var start = trial.RefinementStartOptic!;
    var problem = new FlatStartDesignProblem(checkpoint.Specification, trial.Family.ElementCount, start, trial.Family);
    var vector = problem.Vector(start);
    var initial = problem.Evaluate(problem.CreateOptic(vector, FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, default);
    if (!initial.HasContinuousSearchResiduals || initial.IsFeasible) continue;
    var root = trial;
    while (root.ParentTrialId is not null) root = checkpoint.Trials.Single(t => t.TrialId == root.ParentTrialId);
    var spec = checkpoint.Specification with { Budget = checkpoint.Specification.Budget with { MaximumEvaluations = trial.AllocatedEvaluations } };
    var result = new FlatStartDesignService().Continue(spec, trial.Family, root.BootstrapProof!, start, trial.ParentTrialId!, improveBeyondTargets: trial.Operation == "refine-selected");
    if (args.Length > 2 && args[2] == "--baseline"
        && (result.EvaluationCount != trial.CompletedEvaluations
            || ContentFingerprint.Compute(result.FinalValidation! with { Residuals = [] }) != ContentFingerprint.Compute(trial.FinalValidation!)))
        throw new InvalidDataException("Baseline replay differs from the saved trial.");
    records.Add(new
    {
        trial.TrialId,
        trial.Operation,
        trial.Family,
        Specification = spec,
        Start = start,
        RootProof = root.BootstrapProof,
        Initial = initial,
        result.EvaluationCount,
        Final = result.FinalValidation,
        Stages = result.Steps.Where(s => s.Operation.EndsWith("entry")),
        result.Diagnostics
    });
    Console.WriteLine($"{trial.TrialId} {trial.Operation}: entry finite={initial.HasContinuousSearchResiduals}, geometry={initial.GeometryFeasible}; final RMS={result.FinalValidation!.Fields.Max(f => f.RmsRadiusMillimeters):G6}, feasible={result.FinalValidation.IsFeasible}");
    File.WriteAllText(args[1], JsonSerializer.Serialize(records, json));
}
Console.WriteLine($"Audited {checkpoint.Trials.Count(t => t.RefinementStartOptic is not null)} restarts; {records.Count} have complete rays across a geometry bound.");
