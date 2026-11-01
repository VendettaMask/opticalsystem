using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if(args.Length!=3)throw new ArgumentException("prepare|compute|worker|compare <repository or worker lens index> <study-root>");
var mode=args[0];var root=Path.GetFullPath(args[2]);
string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var coreHash=Hash(typeof(Optic).Assembly.Location);
if(mode=="prepare")
{
 var repository=Path.GetFullPath(args[1]);
 if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh study required");
 Directory.CreateDirectory(root);
 var prior=Path.Combine(repository,"artifacts/zemax-sequential-corpus/20261021/full-v1");
 var policy=Path.Combine(repository,"tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
 if(Hash(policy)!=Hash(Path.Combine(prior,"comparison-settings.json")))throw new InvalidDataException("Policy changed");
 var index=JsonFiles.Read<JsonElement>(Path.Combine(repository,"src/OpticalSystemDesign.App/Assets/SequentialExamples/index.json"));
 var sources=index.GetProperty("Files").EnumerateArray().ToArray();
 if(sources.Length!=95)throw new InvalidDataException("95 product examples required");
 var models=new List<Model>();
 for(var i=0;i<sources.Length;i++)
 {
  var id=sources[i].GetProperty("RelativePath").GetString()!;var expected=sources[i].GetProperty("Sha256").GetString()!;
  var source=Path.Combine(repository,"src/OpticalSystemDesign.App/Assets/SequentialExamples",id);
  var previous=Path.Combine(prior,"sources",id);
  if(Hash(source)!=expected||Hash(previous)!=expected)throw new InvalidDataException("Source/reference identity differs: "+id);
  var snapshots=new List<string>();string? error=null;
  try
  {
   var imported=await WorkbenchExecutor.Import(source);
   foreach(var optic in imported.Configurations)
   {
    var path=Path.Combine(root,"models",$"{i+1:D3}-c{snapshots.Count+1}.optic.json");
    JsonFiles.Write(path,optic.ToSnapshot());snapshots.Add(path);
   }
  }
  catch(Exception e){error=e.ToString();}
  models.Add(new(i+1,id,source,expected,snapshots.ToArray(),error));
  Console.WriteLine($"IMPORT {i+1}/95 {id}: {snapshots.Count} configurations; error={error!=null}");
 }
 JsonFiles.Write(Path.Combine(root,"models.json"),models);
 var old=JsonFiles.Read<LegacyItem[]>(Path.Combine(prior,"items.json"));var rows=new List<Row>();
 for(var i=0;i<old.Length;i++)
 {
  var item=old[i];var model=models.Single(m=>string.Equals(m.Lens,item.Lens,StringComparison.OrdinalIgnoreCase));
  var folder=Path.Combine(root,"analyses",$"{i+1:D4}");Directory.CreateDirectory(folder);
  var snapshot=item.Configuration<=model.Snapshots.Length?model.Snapshots[item.Configuration-1]:null;
  var eligible=item.WorkbenchJob!=null&&item.Run.Request!=null&&snapshot!=null;
  var job=eligible?Path.Combine(folder,"request.json"):null;
  if(job!=null)JsonFiles.Write(job,new WorkbenchJob(snapshot!,folder,item.Run.Request!));
  rows.Add(new(i+1,model.Index,item.Lens,item.Configuration,item.Scope,item.Run.Key,item.Path,snapshot,job,item.Settings,item.Run));
 }
 JsonFiles.Write(Path.Combine(root,"rows.json"),rows);
 JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=coreHash,repository,prior,policy,policySha256=Hash(policy),
  indexSha256=Hash(Path.Combine(repository,"src/OpticalSystemDesign.App/Assets/SequentialExamples/index.json")),
  baselineItemsSha256=Hash(Path.Combine(prior,"items.json")),baselineResultsSha256=Hash(Path.Combine(prior,"analysis-results.json")),
  nativeReference="FrozenOpticStudio2026R1_20261021",freshNativeCapture=false,freshSourceImport=true,
  lensCount=models.Count,requests=rows.Count,eligible=rows.Count(r=>r.Job!=null),startedUtc=DateTimeOffset.UtcNow,
  assemblies=new[]{typeof(Optic).Assembly.Location,typeof(WorkbenchExecutor).Assembly.Location,typeof(Row).Assembly.Location}.Select(p=>new{path=p,sha256=Hash(p)})});
 return;
}
var identity=JsonFiles.Read<JsonElement>(Path.Combine(root,"identity.json"));
if(identity.GetProperty("coreSha256").GetString()!=coreHash)throw new InvalidDataException("Frozen Core changed");
if(mode=="rays"){RayRecheck.Run(root);return;}
if(mode=="regressions"){RegressionProbe.Run(root);return;}
var all=JsonFiles.Read<Row[]>(Path.Combine(root,"rows.json"));
if(mode=="worker")
{
 var lens=int.Parse(args[1],CultureInfo.InvariantCulture);
 foreach(var row in all.Where(r=>r.LensIndex==lens&&r.Job!=null))
 {
  var folder=Path.GetDirectoryName(row.Job!)!;var completion=Path.Combine(folder,"execution.json");
  if(File.Exists(completion))throw new IOException("Completed result must not be overwritten");
  var timer=Stopwatch.StartNew();var exit=0;string? error=null;
  try
  {
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(900));
   using var scope=ComputationCancellation.Push(deadline.Token);
   WorkbenchExecutor.Execute(JsonFiles.Read<WorkbenchJob>(row.Job!));
  }
  catch(Exception e){exit=3;error=e.ToString();File.WriteAllText(Path.Combine(folder,"error.txt"),error);}
  JsonFiles.Write(completion,new{row.Index,exitCode=exit,error,seconds=timer.Elapsed.TotalSeconds,coreSha256=coreHash});
  Console.WriteLine($"ROW {row.Index}: {exit}; {timer.Elapsed.TotalSeconds:F2}s");
 }
 return;
}
if(mode=="compute")
{
 var groups=all.Where(r=>r.Job!=null).GroupBy(r=>r.LensIndex).ToArray();var completed=0;
 await Parallel.ForEachAsync(groups,new ParallelOptions{MaxDegreeOfParallelism=2},async(group,ct)=>
 {
  var timer=Stopwatch.StartNew();
  var result=await ProcessIsolation.Run("dotnet",[typeof(Row).Assembly.Location,"worker",group.Key.ToString(CultureInfo.InvariantCulture),root],
   identity.GetProperty("repository").GetString()!,group.Count()*900+30,ct);
  JsonFiles.Write(Path.Combine(root,"workers",group.Key+".json"),new{lensIndex=group.Key,result,seconds=timer.Elapsed.TotalSeconds});
  Console.WriteLine($"LENS {Interlocked.Increment(ref completed)}/{groups.Length} {group.First().Lens}: {result.ExitCode}; {timer.Elapsed.TotalSeconds:F1}s");
 });
 JsonFiles.Write(Path.Combine(root,"compute-completed.json"),new{completedLenses=completed,coreSha256=coreHash,completedUtc=DateTimeOffset.UtcNow});return;
}
if(mode!="compare")throw new ArgumentException("Unknown mode");
var results=new List<object>();
foreach(var row in all)
{
 var output=Path.Combine(root,"analyses",$"{row.Index:D4}");
 var run=new AnalysisRun{Key=row.Key,Directory=row.Run.Directory,Request=row.Run.Request,Support=row.Run.Support,Tolerances=row.Run.Tolerances};
 string? error=null;
 if(row.Job==null){run.Conclusion=Conclusion.Incomparable;run.Reason=row.Snapshot==null?"Current source import/configuration unavailable":"Original matrix has no executable canonical request; see the original blocker and later scoped controls";}
 else
 {
  var completion=Path.Combine(output,"execution.json");
  if(!File.Exists(completion))throw new InvalidDataException("Unfinished worker row: "+row.Index);
  try
  {
   var executed=JsonFiles.Read<JsonElement>(completion);
   var native=Path.Combine(row.LegacyPath,"native");
   var captured=JsonFiles.Read<JsonElement>(Path.Combine(native,"batch-result.json"));
   if(captured.GetProperty("exitCode").GetInt32()!=0)
   {
    run.Conclusion=Conclusion.Incomparable;run.Reason="Original native capture failed; no valid numeric authority";
    error=File.Exists(Path.Combine(native,"error.json"))?File.ReadAllText(Path.Combine(native,"error.json")):"Native completion failed";
   }
   else if(executed.GetProperty("exitCode").GetInt32()!=0)
   {run.Conclusion=Conclusion.Error;run.Reason="Current Core computation failed";error=executed.GetProperty("error").GetString();}
   else
   {
    run.ZemaxStatus=run.WorkbenchStatus=CaptureStatus.Captured;
    var entry=AnalysisComparisonRegistry.Get(row.Key);
    var z=ResultNormalizer.Zemax(Path.Combine(native,"data.json"),entry,row.Run.Request!);
    var raw=JsonFiles.Read<JsonElement>(Path.Combine(output,"data.json"));
    if(raw.TryGetProperty("outcome",out var outcome)&&outcome.GetString() is "Unavailable" or "NotApplicable")
    {
     var numericNative=z.Scalars.Count+z.Series.Count+z.Grids.Count>0;
     run.Conclusion=numericNative?Conclusion.Error:Conclusion.Incomparable;
     run.Reason="Core returned "+outcome.GetString()+(numericNative?" with numeric native reference":" without numeric native reference");
     error=raw.TryGetProperty("outcomeReason",out var why)?why.GetString():null;
    }
    else
    {
     var w=ResultNormalizer.Workbench(Path.Combine(output,"data.json"),entry,row.Run.Request!);
     ComparisonRunner.Compare(run,entry,row.Settings,w,z,output);
    }
   }
  }
  catch(InvalidDataException e){run.Conclusion=Conclusion.Incomparable;run.Reason=e.Message;error=e.ToString();}
  catch(Exception e){run.Conclusion=Conclusion.Error;run.Reason=e.Message;error=e.ToString();}
 }
 JsonFiles.Write(Path.Combine(output,"comparison.json"),run);
 results.Add(new{row.Index,row.LensIndex,row.Lens,row.Configuration,row.Scope,row.Key,run.Conclusion,run.Reason,error,run.Metrics,
  requestFingerprint=row.Run.Request?.Fingerprint,nativeReference=Path.Combine(row.LegacyPath,"native"),coreSha256=coreHash,
  referenceConvergence="NotEstablishedByThisReplay"});
 if(results.Count%100==0)Console.WriteLine($"COMPARE {results.Count}/{all.Length}");
}
JsonFiles.Write(Path.Combine(root,"results.json"),results);
var items=results.Select(r=>JsonSerializer.SerializeToElement(r,JsonFiles.Options)).ToArray();
var modelsFinal=JsonFiles.Read<Model[]>(Path.Combine(root,"models.json"));
var lenses=modelsFinal.Select(m=>new{m.Index,m.Lens,category=m.Lens.Split('/')[0],configurations=m.Snapshots.Length,importError=m.Error,
 total=items.Count(r=>r.GetProperty("lensIndex").GetInt32()==m.Index),
 counts=items.Where(r=>r.GetProperty("lensIndex").GetInt32()==m.Index).GroupBy(r=>r.GetProperty("conclusion").GetString()!).ToDictionary(g=>g.Key,g=>g.Count())}).ToArray();
JsonFiles.Write(Path.Combine(root,"lens-results.json"),lenses);
JsonFiles.Write(Path.Combine(root,"summary.json"),new{coreSha256=coreHash,total=items.Length,lensCount=lenses.Length,
 counts=items.GroupBy(r=>r.GetProperty("conclusion").GetString()!).ToDictionary(g=>g.Key,g=>g.Count()),
 allExamplesCertified=false,referenceConvergenceChecked=false,freshSourceImport=true,freshNativeCapture=false,completedUtc=DateTimeOffset.UtcNow});
Console.WriteLine(File.ReadAllText(Path.Combine(root,"summary.json")));

internal sealed record Model(int Index,string Lens,string Source,string SourceSha256,string[] Snapshots,string? Error);
internal sealed record LegacyItem(string Lens,string Unit,int Configuration,string Scope,string Path,string Snapshot,string? WorkbenchJob,AnalysisConfiguration Settings,AnalysisRun Run);
internal sealed record Row(int Index,int LensIndex,string Lens,int Configuration,string Scope,string Key,string LegacyPath,string? Snapshot,string? Job,AnalysisConfiguration Settings,AnalysisRun Run);
