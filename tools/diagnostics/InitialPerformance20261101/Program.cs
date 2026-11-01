using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if(args.Length!=4 || args[0]!="run") throw new ArgumentException("run original-input new-output seed");
var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,
    NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
var output=Path.GetFullPath(args[2]);if(Directory.Exists(output))throw new IOException("Never overwrite an experiment.");Directory.CreateDirectory(output);
var original=await File.ReadAllBytesAsync(args[1]);using var input=JsonDocument.Parse(original);
var plan=input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
var options=input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
options=options with{ColdStart=new(){Version=6},Retrieval=null,Evolution=null,Competition=null,ImageScreening=null,
    MaximumFunctionEvaluations=600,MaximumEvaluationsPerStart=600,MaximumTracedRays=10000000,TimeLimit=TimeSpan.FromSeconds(600)};
plan=plan with{Options=plan.Options with{Seed=int.Parse(args[3]),SeedPreparation=options}};
await AutonomousDesignFiles.SavePlanAsync(plan,Path.Combine(output,"input.topology-plan.json"));
await File.WriteAllTextAsync(Path.Combine(output,"source.json"),JsonSerializer.Serialize(new{sourceSha256=Convert.ToHexStringLower(SHA256.HashData(original)),
    seed=plan.Options.Seed,importedEndpoint=false,importedCheckpoint=false,
    engine=typeof(SeedPreparationSearch).Assembly.ManifestModule.ModuleVersionId,core=typeof(SequentialTraceMeasurement).Assembly.ManifestModule.ModuleVersionId},json));
var store=new SeedPreparationCheckpointStore();var state=SeedPreparationSearch.Create("performance-"+Guid.NewGuid().ToString("N"),plan) with{Revision=1};
var checkpoint=Path.Combine(output,"run.preparation.json");await store.SaveAsync(state,checkpoint,0);long revision=1;
using var performance=SearchPerformanceMeasurement.Begin();using var rays=SequentialTraceMeasurement.Begin();
state=SeedPreparationSearch.Run(state,next=>
{
    store.SaveAsync(next,checkpoint,revision).AsTask().GetAwaiter().GetResult();revision=next.Revision;
    var text=JsonSerializer.Serialize(performance.Snapshot(),json);var pending=Path.Combine(output,"profile-live.tmp");
    File.WriteAllText(pending,text);File.Move(pending,Path.Combine(output,"profile-live.json"),true);
    if(next.Pending is {Name:"Even6",Continuation.Steps.Length:>=8}&&!File.Exists(Path.Combine(output,"dense.preparation.json")))
        File.Copy(checkpoint,Path.Combine(output,"dense.preparation.json"));
},progress:(stage,functions)=>Console.WriteLine($"seed{args[3]} stage={stage} fn={functions}"));
if(rays.RayCount!=state.Consumed.TracedRays)throw new InvalidDataException("Actual rays were not completely settled.");
await File.WriteAllTextAsync(Path.Combine(output,"profile.json"),JsonSerializer.Serialize(performance.Snapshot(),json));
await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{state.Status,state.StopReason,state.Consumed,
    qualified=state.Result?.Seeds.Length==1,actualRays=rays.RayCount,state.PersistenceElapsed,state.PlanFingerprint,state.ProtocolFingerprint,
    timingIsInclusive=true,measurementsDoNotChangePhysicalGates=true},json));
Console.WriteLine($"END {state.Status}, qualified={state.Result?.Seeds.Length}, work={state.Consumed}");
