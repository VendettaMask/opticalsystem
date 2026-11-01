using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if(args.Length!=2)throw new ArgumentException("actual-dense-checkpoint new-output");
var output=Path.GetFullPath(args[1]);if(Directory.Exists(output))throw new IOException("Never overwrite a probe.");Directory.CreateDirectory(output);
var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
var saved=await new SeedPreparationCheckpointStore().LoadAsync(args[0]);
var pending=saved.Pending??throw new InvalidDataException("Requires an actual unfinished compensation point.");
var coordinates=pending.Continuation!.Adam.Coordinates.ToArray();
var compiled=TopologyCompiler.Compile(pending.Source,saved.Plan.Constraints);
var binding=TopologyParameterBinder.Bind(compiled,pending.Variables,exactMechanicalFrame:true);
var objective=new FixedTopologyObjective(compiled,binding,saved.Plan.Targets,pending.Options!.Sampling);
using var serial=ComputationParallelism.SuppressNestedParallelism();
using var budget=SequentialTraceMeasurement.BeginCpuBudget(10000000);
var before=Evaluate(coordinates); SearchLinearModel? model=null;
using(var capture=SearchPerformanceMeasurement.Begin(value=>model??=value))
    _=new TopologyGateCompensation(objective.ResidualIds,pending.Options.GateCompensationVersion!.Value).Propose(before,coordinates,binding,1,default);
if(model is null)throw new InvalidDataException("No original Core model was dispatched.");
var rows=new List<object>();var n=coordinates.Length;
foreach(var scaleColumns in new[]{false,true})foreach(var damping in new[]{.001,.01,.1})
{
    var clock=Stopwatch.StartNew();var scales=Enumerable.Repeat(1.0,n).ToArray();
    if(scaleColumns)for(var j=0;j<n;j++)
    {
        var norm=0.0;for(var i=0;i<model.Jacobian.GetLength(0);i++)norm=double.Hypot(norm,model.Jacobian[i,j]);
        scales[j]=norm>0?Math.Clamp(1/norm,.001,1000):1;
    }
    var jacobian=(double[,])model.Jacobian.Clone();var normals=(double[,])model.Normals.Clone();
    for(var i=0;i<jacobian.GetLength(0);i++)for(var j=0;j<n;j++)jacobian[i,j]*=scales[j];
    for(var i=0;i<normals.GetLength(0);i++)for(var j=0;j<n;j++)normals[i,j]*=scales[j];
    var solved=InequalityLeastSquaresStep.Solve(jacobian,model.Residuals,normals,model.Upper,damping);
    var solveSeconds=clock.Elapsed.TotalSeconds;
    var delta=solved.Step.Select((value,j)=>value*scales[j]).ToArray();
    var violation=0.0;for(var i=0;i<model.Normals.GetLength(0);i++)
    {var sum=0.0;for(var j=0;j<n;j++)sum+=model.Normals[i,j]*delta[j];violation=Math.Max(violation,sum-model.Upper[i]);}
    var proposal=coordinates.Select((value,j)=>Math.Clamp(value+delta[j],binding.Slots[j].LowerCoordinate,binding.Slots[j].UpperCoordinate)).ToArray();
    var candidate=Evaluate(proposal);var optic=TopologyParameterBinder.Apply(compiled,binding,proposal);
    var qa=TopologyValidator.Validate(optic,saved.Plan.Targets);
    rows.Add(new{scaleColumns,damping,solveSeconds,variantSeconds=clock.Elapsed.TotalSeconds,originalLinearViolation=violation,
        solved.Converged,solved.ActiveSetIterations,candidate.Available,candidate.Objective,beforeObjective=before.Objective,
        accepted=new TopologyGateCompensation(objective.ResidualIds,pending.Options.GateCompensationVersion.Value).Accept(before,candidate),
        qa.Completed,qa.MeetsTargets,qa.Violations,physicalCapsChanged=false});
}
// A bounded two-worker throughput comparison: same actual point, same sampling,
// independent evaluation contexts. No candidate ordering/budget policy is changed.
var parallel=new List<object>();string? expected=null;
foreach(var workers in new[]{1,2})
{
    var cpu=Process.GetCurrentProcess().TotalProcessorTime;var allocated=GC.GetTotalAllocatedBytes();
    var clock=Stopwatch.StartNew();using var meter=SequentialTraceMeasurement.Begin();
    var values=new TopologyObjectiveEvaluation[4];
    Parallel.For(0,values.Length,new ParallelOptions{MaxDegreeOfParallelism=workers},i=>values[i]=Evaluate(coordinates));
    clock.Stop();
    foreach(var value in values)
    {
        var key=ContentFingerprint.Compute(value with{TracedRayCount=0,GeometryProbes=0});expected??=key;
        if(key!=expected)throw new InvalidDataException("Parallel evaluation changed numerical output or backend metadata.");
    }
    if(meter.RayCount!=values.Sum(v=>v.TracedRayCount))throw new InvalidDataException("Parallel actual ray ledger disagrees.");
    parallel.Add(new{workers,evaluations=values.Length,seconds=clock.Elapsed.TotalSeconds,
        cpuSeconds=(Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalSeconds,allocatedBytes=GC.GetTotalAllocatedBytes()-allocated,
        actualRays=meter.RayCount,exact=true,backend=values[0].Execution});
}
var gpu=DesignGpu.Status;
await File.WriteAllTextAsync(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{auxiliary=true,importedCheckpointForModelProbeOnly=true,
    notAnIndependentColdStart=true,sourcePlan=saved.PlanFingerprint,point=saved.Endpoint,physicalCapsChanged=false,
    defaultScalesAndDampingChanged=false,variants=rows,parallel,actualTotalRays=budget.RayCount,
    gpuStatus=new{gpu.IsAvailable,gpu.DeviceName,gpu.UnavailableReason},gpuOpticalExecutionPerformed=false,
    explanation="CPU model/throughput comparisons only. No GPU acceleration or global parallel search adoption is inferred."},json));
Console.WriteLine($"Saved six original-model scale/damping probes and bounded CPU throughput; actual rays={budget.RayCount}.");
TopologyObjectiveEvaluation Evaluate(IReadOnlyList<double> vector)=>objective.Evaluate(vector,DesignComputeMode.Cpu,true,includeConstraintTangents:true);
