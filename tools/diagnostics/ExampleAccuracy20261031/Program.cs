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

if(args.Length!=3)throw new ArgumentException("prepare|compute|worker|compare|compare-afocal <repository, worker lens index or scoped afocal policy> <study-root>");
var mode=args[0];var root=Path.GetFullPath(args[2]);
string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var coreHash=Hash(typeof(Optic).Assembly.Location);
if(mode=="prepare")
{
 var repository=Path.GetFullPath(args[1]);
 if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh study required");
 Directory.CreateDirectory(root);
 var prior=Path.GetFullPath(Path.Combine(repository,"artifacts/zemax-sequential-corpus/20261021/full-v1"));
 var policy=Path.Combine(repository,"tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
 var repositoryPolicySha256=Hash(policy);
 var originalPolicy=Path.Combine(prior,"comparison-settings.json");
 if(Hash(policy)!=Hash(originalPolicy))
 {
  if(File.ReadAllText(policy).Replace("\r\n","\n")!=File.ReadAllText(originalPolicy).Replace("\r\n","\n"))
   throw new InvalidDataException("Policy changed");
  policy=originalPolicy; // Preserve the original bytes across Git newline conversion.
 }
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
  var snapshots=new List<string>();
  IReadOnlyList<IReadOnlyList<int>> sourceSurfaceNumbers=[];
  string? error=null;
  try
  {
   var imported=await WorkbenchExecutor.Import(source);
   sourceSurfaceNumbers=imported.SourceSurfaceNumbers;
   foreach(var optic in imported.Configurations)
   {
    var path=Path.Combine(root,"models",$"{i+1:D3}-c{snapshots.Count+1}.optic.json");
    JsonFiles.Write(path,optic.ToSnapshot());snapshots.Add(path);
   }
  }
  catch(Exception e){error=e.ToString();}
  models.Add(new(i+1,id,source,expected,snapshots.ToArray(),error,sourceSurfaceNumbers));
  Console.WriteLine($"IMPORT {i+1}/95 {id}: {snapshots.Count} configurations; error={error!=null}");
 }
 JsonFiles.Write(Path.Combine(root,"models.json"),models);
 var old=JsonFiles.Read<LegacyItem[]>(Path.Combine(prior,"items.json"));var rows=new List<Row>();
 var mappings=new List<object>();
 for(var i=0;i<old.Length;i++)
 {
  var item=old[i];var model=models.Single(m=>string.Equals(m.Lens,item.Lens,StringComparison.OrdinalIgnoreCase));
  var folder=Path.Combine(root,"analyses",$"{i+1:D4}");Directory.CreateDirectory(folder);
  var snapshot=item.Configuration<=model.Snapshots.Length?model.Snapshots[item.Configuration-1]:null;
  var eligible=item.WorkbenchJob!=null&&item.Run.Request!=null&&snapshot!=null;
  var job=eligible?Path.Combine(folder,"request.json"):null;
  if(job!=null)
  {
   var request=item.Run.Request!;
   var surfaceCount=JsonFiles.Read<JsonElement>(snapshot!).GetProperty("surfaces").GetArrayLength();
   if(request.Surface>=0&&surfaceCount!=request.SurfaceCount)
    throw new InvalidDataException("An explicit historical surface needs an independent source mapping: "+(i+1));
   var effective=request with {SurfaceCount=surfaceCount};
   JsonFiles.Write(job,new WorkbenchJob(snapshot!,folder,effective));
   mappings.Add(new{index=i+1,originalFingerprint=request.Fingerprint,effectiveFingerprint=effective.Fingerprint,
    originalSurfaceCount=request.SurfaceCount,effectiveSurfaceCount=surfaceCount,explicitSurface=request.Surface});
  }
  // Rebase only storage location: the immutable corpus may have been captured
  // in another checkout. Numeric requests and native payload bytes stay intact.
  const string corpusMarker="artifacts/zemax-sequential-corpus/20261021/full-v1/";
  var recordedPath=item.Path.Replace('\\','/');
  var markerIndex=recordedPath.IndexOf(corpusMarker,StringComparison.OrdinalIgnoreCase);
  if(markerIndex<0)throw new InvalidDataException("Unrecognized native corpus storage path: "+item.Path);
  var legacyPath=Path.GetFullPath(Path.Combine(prior,recordedPath[(markerIndex+corpusMarker.Length)..]));
  if(!legacyPath.StartsWith(prior+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Native corpus storage path escaped its root");
  rows.Add(new(i+1,model.Index,item.Lens,item.Configuration,item.Scope,item.Run.Key,legacyPath,snapshot,job,item.Settings,item.Run));
 }
 JsonFiles.Write(Path.Combine(root,"rows.json"),rows);
 JsonFiles.Write(Path.Combine(root,"request-mapping.json"),mappings);
 JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=coreHash,repository,prior,policy,policySha256=Hash(policy),repositoryPolicySha256,
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
if(mode=="compare-explicit-fans"){FanCoverageProbe.Run(root,args[1]);return;}
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
var supplement=mode=="compare-afocal";
if(mode!="compare"&&!supplement)throw new ArgumentException("Unknown mode");
var reportRoot=supplement?Path.Combine(root,"afocal-policy"):root;
var modelsFinal=JsonFiles.Read<Model[]>(Path.Combine(root,"models.json"));
ComparisonConfiguration? scopedPolicy=null;
if(supplement)
{
 var policyPath=Path.GetFullPath(args[1]);
 scopedPolicy=ComparisonConfiguration.Load(policyPath);
 scopedPolicy.Validate();
 JsonFiles.Write(Path.Combine(reportRoot,"policy-identity.json"),new{path=policyPath,sha256=Hash(policyPath)});
}
var results=new List<object>();
foreach(var row in all)
{
 var output=Path.Combine(root,"analyses",$"{row.Index:D4}");
 var effectiveRequest=row.Job is null?row.Run.Request:JsonFiles.Read<WorkbenchJob>(row.Job).Request;
 var settings=row.Settings;
 if(scopedPolicy is not null)
 {
  if(row.Job is null || effectiveRequest?.ImageSpaceAfocal!=true || settings.AfocalQuantities is not null
   || scopedPolicy.Analyses.GetValueOrDefault(row.Key)?.AfocalQuantities is not { } afocalQuantities)continue;
  var quantities=new Dictionary<string,Tolerances>(afocalQuantities);
  if(quantities.ContainsKey("Distortion"))quantities["Distortion"]=settings.Quantities["Distortion"];
  settings=settings with {AfocalQuantities=quantities};
 }
 var comparisonOutput=supplement?Path.Combine(reportRoot,"analyses",$"{row.Index:D4}"):output;
 var run=new AnalysisRun{Key=row.Key,Directory=row.Run.Directory,Request=effectiveRequest,Support=row.Run.Support,Tolerances=row.Run.Tolerances};
 string? error=null;
 string? coreError=null;
 if(row.Job==null){run.Conclusion=Conclusion.Incomparable;run.Reason=row.Snapshot==null?"Current source import/configuration unavailable":"Original matrix has no executable canonical request; see the original blocker and later scoped controls";}
 else
 {
  var completion=Path.Combine(output,"execution.json");
  if(!File.Exists(completion))throw new InvalidDataException("Unfinished worker row: "+row.Index);
  try
  {
   var executed=JsonFiles.Read<JsonElement>(completion);
   var coreSucceeded=executed.GetProperty("exitCode").GetInt32()==0;
   if(!coreSucceeded)coreError=executed.GetProperty("error").GetString();
   var native=Path.Combine(row.LegacyPath,"native");
   var captured=JsonFiles.Read<JsonElement>(Path.Combine(native,"batch-result.json"));
   if(captured.GetProperty("exitCode").GetInt32()!=0)
   {
    run.Conclusion=Conclusion.Incomparable;run.Reason="Original native capture failed; no valid numeric authority";
    error=File.Exists(Path.Combine(native,"error.json"))?File.ReadAllText(Path.Combine(native,"error.json")):"Native completion failed";
   }
   else
   {
    var entry=AnalysisComparisonRegistry.Get(row.Key);
    var sourceNumbers=row.Key=="Single Ray Trace"?modelsFinal[row.LensIndex-1].SourceSurfaceNumbers?[row.Configuration-1]:null;
    var workbenchPath=coreSucceeded?Path.Combine(output,"data.json"):null;
    run.WorkbenchStatus=coreSucceeded?CaptureStatus.Captured:CaptureStatus.Failed;
    // Validate the independent native authority even when Core produced no result.
    var z=ResultNormalizer.Zemax(Path.Combine(native,"data.json"),entry,effectiveRequest!,workbenchPath,sourceNumbers);
    run.ZemaxStatus=CaptureStatus.Captured;
    if(workbenchPath is null)
    {
     run.Conclusion=Conclusion.Error;
     run.Reason="Current Core computation failed";
     error=coreError;
    }
    else
    {
     var raw=JsonFiles.Read<JsonElement>(workbenchPath);
     if(raw.TryGetProperty("outcome",out var outcome)&&outcome.GetString() is "Unavailable" or "NotApplicable")
     {
      var numericNative=z.Scalars.Count+z.Series.Count+z.Grids.Count>0;
      run.Conclusion=numericNative?Conclusion.Error:Conclusion.Incomparable;
      run.Reason="Core returned "+outcome.GetString()+(numericNative?" with numeric native reference":" without numeric native reference");
      error=raw.TryGetProperty("outcomeReason",out var why)?why.GetString():null;
     }
     else
     {
      var w=ResultNormalizer.Workbench(workbenchPath,entry,effectiveRequest!);
      ComparisonRunner.Compare(run,entry,settings,w,z,comparisonOutput);
     }
    }
   }
  }
  catch(InvalidDataException e){run.Conclusion=Conclusion.Incomparable;run.Reason=e.Message;error=e.ToString();}
  catch(Exception e){run.Conclusion=Conclusion.Error;run.Reason=e.Message;error=e.ToString();}
 }
  JsonFiles.Write(Path.Combine(comparisonOutput,"comparison.json"),run);
 results.Add(new{row.Index,row.LensIndex,row.Lens,row.Configuration,row.Scope,row.Key,run.Conclusion,run.Reason,error,coreError,run.Metrics,
  requestFingerprint=effectiveRequest?.Fingerprint,originalRequestFingerprint=row.Run.Request?.Fingerprint,
  originalSurfaceCount=row.Run.Request?.SurfaceCount,effectiveSurfaceCount=effectiveRequest?.SurfaceCount,
  nativeReference=Path.Combine(row.LegacyPath,"native"),coreSha256=coreHash,
  referenceConvergence="NotEstablishedByThisReplay"});
 if(results.Count%100==0)Console.WriteLine($"COMPARE {results.Count}/{all.Length}");
}
 JsonFiles.Write(Path.Combine(reportRoot,"results.json"),results);
 if(supplement)return;
var items=results.Select(r=>JsonSerializer.SerializeToElement(r,JsonFiles.Options)).ToArray();
var lenses=modelsFinal.Select(m=>new{m.Index,m.Lens,category=m.Lens.Split('/')[0],configurations=m.Snapshots.Length,importError=m.Error,
 total=items.Count(r=>r.GetProperty("lensIndex").GetInt32()==m.Index),
 counts=items.Where(r=>r.GetProperty("lensIndex").GetInt32()==m.Index).GroupBy(r=>r.GetProperty("conclusion").GetString()!).ToDictionary(g=>g.Key,g=>g.Count())}).ToArray();
JsonFiles.Write(Path.Combine(root,"lens-results.json"),lenses);
JsonFiles.Write(Path.Combine(root,"summary.json"),new{coreSha256=coreHash,total=items.Length,lensCount=lenses.Length,
 counts=items.GroupBy(r=>r.GetProperty("conclusion").GetString()!).ToDictionary(g=>g.Key,g=>g.Count()),
 allExamplesCertified=false,referenceConvergenceChecked=false,freshSourceImport=true,freshNativeCapture=false,completedUtc=DateTimeOffset.UtcNow});
Console.WriteLine(File.ReadAllText(Path.Combine(root,"summary.json")));

internal sealed record Model(int Index,string Lens,string Source,string SourceSha256,string[] Snapshots,string? Error,IReadOnlyList<IReadOnlyList<int>>? SourceSurfaceNumbers=null);
internal sealed record LegacyItem(string Lens,string Unit,int Configuration,string Scope,string Path,string Snapshot,string? WorkbenchJob,AnalysisConfiguration Settings,AnalysisRun Run);
internal sealed record Row(int Index,int LensIndex,string Lens,int Configuration,string Scope,string Key,string LegacyPath,string? Snapshot,string? Job,AnalysisConfiguration Settings,AnalysisRun Run);
