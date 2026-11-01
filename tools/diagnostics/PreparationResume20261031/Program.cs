using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,
    NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
var store=new SeedPreparationCheckpointStore();
if(args[0]=="run")
{
    if(args.Length!=5)throw new ArgumentException("run original-input new-output seed continuous|construction|sphere|profile|dense");
    var directory=Path.GetFullPath(args[2]);if(Directory.Exists(directory))throw new IOException("Never overwrite evidence.");Directory.CreateDirectory(directory);
    var original=await File.ReadAllBytesAsync(args[1]);using var input=JsonDocument.Parse(original);
    var plan=input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
    var options=input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
    options=options with{ColdStart=new(){Version=6},Retrieval=null,Evolution=null,Competition=null,ImageScreening=null,
        MaximumFunctionEvaluations=600,MaximumEvaluationsPerStart=600,MaximumTracedRays=10000000,TimeLimit=TimeSpan.FromSeconds(600)};
    plan=plan with{Options=plan.Options with{Seed=int.Parse(args[3]),SeedPreparation=options}};
    await AutonomousDesignFiles.SavePlanAsync(plan,Path.Combine(directory,"input.topology-plan.json"));
    await File.WriteAllTextAsync(Path.Combine(directory,"source.json"),JsonSerializer.Serialize(new{sourceSha256=Convert.ToHexStringLower(SHA256.HashData(original)),
        seed=args[3],mode=args[4],cold=true,importedEndpoint=false,importedCheckpoint=false},json));
    var state=SeedPreparationSearch.Create("preparation-"+Guid.NewGuid().ToString("N"),plan) with{Revision=1};
    var path=Path.Combine(directory,"run.preparation.json");await store.SaveAsync(state,path,0);
    using var stop=new CancellationTokenSource();using var rays=SequentialTraceMeasurement.Begin();long revision=1;
    state=SeedPreparationSearch.Run(state,Save,stop.Token,(stage,functions)=>Console.WriteLine($"{args[3]}/{args[4]} stage={stage} fn={functions}"));
    if(state.Status==SeedPreparationStatus.Suspended)File.Copy(path,Path.Combine(directory,"suspended.preparation.json"),overwrite:false);
    await Summary(state,rays.RayCount,0,Path.Combine(directory,"segment.json"));
    Console.WriteLine($"END {state.Status}, fn={state.Consumed.FunctionEvaluations}, rays={state.Consumed.TracedRays}, qualified={state.Result?.Seeds.Length}");
    void Save(SeedPreparationCheckpoint next)
    {
        store.SaveAsync(next,path,revision).AsTask().GetAwaiter().GetResult();revision=next.Revision;
        if(args[4]=="construction"&&next.Candidates.Length==2&&next.Stage=="Construction"&&next.Pending is null
            ||args[4]=="sphere"&&next.Pending is {Name:"SphereSparse",Continuation.Steps.Length:>=8}
            ||args[4]=="profile"&&next.Pending is {Name:"Even4Sparse" or "Even4",Continuation.Steps.Length:>=8}
            ||args[4]=="dense"&&next.Pending is {Name:"Even6",Continuation.Steps.Length:>=8})stop.Cancel();
    }
}
else if(args[0]=="resume")
{
    var path=Path.GetFullPath(args[1]);var state=await store.LoadAsync(path);var before=state.Consumed.TracedRays;long revision=state.Revision;
    using var rays=SequentialTraceMeasurement.Begin();
    state=SeedPreparationSearch.Run(state,next=>{store.SaveAsync(next,path,revision).AsTask().GetAwaiter().GetResult();revision=next.Revision;},
        progress:(stage,functions)=>Console.WriteLine($"resumed stage={stage} fn={functions}"));
    await Summary(state,rays.RayCount,before,args[2]);Console.WriteLine($"END {state.Status}, qualified={state.Result?.Seeds.Length}");
}
else if(args[0]=="compare")
{
    var a=await store.LoadAsync(args[1]);var b=await store.LoadAsync(args[2]);var errors=new List<string>();
    void Same(object? x,object? y,string label){if(ContentFingerprint.Compute(x)!=ContentFingerprint.Compute(y))errors.Add(label);}
    Same(a.PlanFingerprint,b.PlanFingerprint,"original task");Same(a.ProtocolFingerprint,b.ProtocolFingerprint,"checkpoint protocol");
    Same(a.RandomState,b.RandomState,"RNG");Same(a.Cursor,b.Cursor,"cursor");Same(a.Status,b.Status,"status");Same(a.StopReason,b.StopReason,"stop reason");
    Same(a.Endpoint,b.Endpoint,"endpoint");Same(a.Assessment,b.Assessment,"original complete assessment");
    Same(a.Candidates.Select(c=>c with{Work=c.Work with{Elapsed=TimeSpan.Zero}}).ToArray(),b.Candidates.Select(c=>c with{Work=c.Work with{Elapsed=TimeSpan.Zero}}).ToArray(),"candidate pool");
    Same(a.Phases.Select(p=>p with{Work=p.Work with{Elapsed=TimeSpan.Zero}}).ToArray(),b.Phases.Select(p=>p with{Work=p.Work with{Elapsed=TimeSpan.Zero}}).ToArray(),"phase endpoints/diagnostics/work");
    Same(a.Consumed with{Elapsed=TimeSpan.Zero},b.Consumed with{Elapsed=TimeSpan.Zero},"all discrete budget counters");
    if(a.Result?.Seeds.Length!=1||b.Result?.Seeds.Length!=1)errors.Add("original qualification");
    await File.WriteAllTextAsync(args[3],JsonSerializer.Serialize(new{errors,exactPhysicalAndDiscreteState=errors.Count==0,
        elapsedIsMeasuredNotBitCompared=true,continuous=a.Consumed,resumed=b.Consumed},json));
    if(errors.Count>0)throw new InvalidDataException(string.Join(", ",errors));Console.WriteLine("EXACT: RNG, pool, stages, endpoint, original QA and all discrete counters.");
}
else throw new ArgumentException("Unknown mode.");

async Task Summary(SeedPreparationCheckpoint state,long actual,long prior,string path)
{
    if(state.Consumed.TracedRays-prior!=actual)throw new InvalidDataException("Actual segment rays were replayed or not settled.");
    await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{state.Status,state.RandomState,state.Stage,state.Cursor,state.Consumed,
        actualSegmentRays=actual,priorRays=prior,workSettledExactly=true,qualified=state.Result?.Seeds.Length==1,
        checkpointFingerprint=ContentFingerprint.Compute(state),elapsedExcludesIdleAndPersistence=true},json));
}
