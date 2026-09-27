using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

if (args[0] != "--roots") { await SearchVerification.Run(args); return; }
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var checkpoint = await new FlatStartSearchCheckpointStore().LoadAsync(args[1]);
var rows = new List<object>();
var clock = Stopwatch.StartNew();
foreach (var family in checkpoint.RootPlan)
{
    var spec = checkpoint.Specification with
    {
        Budget = checkpoint.Specification.Budget with { MaximumEvaluations = checkpoint.RootEvaluationQuota, TimeLimit = TimeSpan.FromMinutes(2) }
    };
    var result = new FlatStartDesignService().Solve(spec, family.ElementCount, family: family, screenOnly: true);
    var evaluation = result.FinalValidation!;
    rows.Add(new
    {
        Family = family,
        result.EvaluationCount,
        result.TracedRayCount,
        State = result.State.ToString(),
        Final = evaluation with { Residuals = [], GeometryResiduals = [], ConstraintResiduals = [], TransmissionResiduals = [], ImageResiduals = [] },
        Stages = result.Steps.Where(step => step.Operation == "progressive-stage-entry"),
        result.Diagnostics
    });
    File.WriteAllText(args[2], JsonSerializer.Serialize(new { ElapsedSeconds = clock.Elapsed.TotalSeconds, Rows = rows }, json));
    Console.WriteLine($"root {family.SeedIndex} N={family.ElementCount} {family.BinaryStart!.Signs}: continuous={evaluation.HasContinuousSearchResiduals}, feasible={evaluation.IsFeasible}, RMS={evaluation.Fields.Max(f => f.RmsRadiusMillimeters):G6}, evals={result.EvaluationCount}");
}
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
