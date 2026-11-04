using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

var mode=args[0];var repo=Path.GetFullPath(args[1]);var root=Path.GetFullPath(args[2]);
if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh output required");Directory.CreateDirectory(root);
var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
var six=new[]{789,831,835,1989,2567,2853};var thirteen=new[]{313,791,969,991,1115,1119,1767,2229,2569,2679,2813,2835,2877};
var results=new List<object>();var core=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location));
using var inputs=new Archive(Path.Combine(repo,"validation/rechecks/2026-11-04/all-example-accuracy"));
using var previous=new Archive(Path.Combine(repo,"validation/rechecks/2026-11-04/offaxis-opd-sampling"));
if(mode=="capture")
{
 using var lease=new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 Slot();var host=await ZemaxExecutor.Build(ZemaxExecutor.Locate(null),root,CancellationToken.None);
 foreach(var spec in JsonFiles.Read<Plan[]>(args[3]))
 {
  var row=rows.Single(r=>r.Index==spec.Index);var entry=AnalysisComparisonRegistry.Get(row.Key);var original=row.Run.Request!;
  var zs=new Dictionary<string,object>(original.ZemaxSettings);foreach(var key in new[]{"SampleSize","PupilSampleSize"})if(zs.ContainsKey(key))zs[key]=$"S_{spec.Sampling}x{spec.Sampling}";
  var request=original with{PupilSampling=spec.Sampling,ZemaxSettings=zs};var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);var hash=Hash(source);
  var folder=Path.Combine(root,$"{spec.Index}-{spec.Sampling}");Slot();var run=await host.Capture(source,Path.Combine(folder,"native"),"2026 R1",entry.ZemaxSettingsMapper,entry.ZemaxAnalysisType,request,row.Configuration,120,false,CancellationToken.None);
  JsonFiles.Write(Path.Combine(folder,"execution.json"),run);string? reason=null;
  try
  {
   if(run.ExitCode!=0||run.TimedOut)throw new InvalidDataException("Native execution failed");
   NativeReferenceDiagnostics.Validate(JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/data.json")));
   var p=JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/captured-settings.json")).GetProperty("properties");
   if(!p.GetProperty("SampleSize").GetString()!.EndsWith($"_{spec.Sampling}x{spec.Sampling}"))throw new InvalidDataException("Wrong actual pupil sampling");
   if(row.Key=="PSF"&&(!p.GetProperty("OutputSize").GetString()!.EndsWith($"_{original.ImageSampling}x{original.ImageSampling}")||p.GetProperty("ImageDelta").GetDouble()!=original.ImageDeltaMicrometers))throw new InvalidDataException("Image grid changed");
  }
  catch(Exception e)when(e is InvalidDataException or KeyNotFoundException){reason=e.Message;}
  if(Hash(source)!=hash)throw new InvalidDataException("Source changed");
  results.Add(new{spec.Index,spec.Sampling,valid=reason is null,reason,sourceSha256=hash,run});Save();Console.WriteLine($"{spec.Index}/{spec.Sampling}: {reason??"Captured and read back"}");
 }
}
else if(mode is "six" or "thirteen")
{
 foreach(var index in args.Length>3?args.Skip(3).Select(int.Parse):mode=="six"?six:thirteen)
 {
  var row=rows.Single(r=>r.Index==index);var entry=AnalysisComparisonRegistry.Get(row.Key);var original=JsonSerializer.Deserialize<WorkbenchJob>(inputs.Read($"study/analyses/{index:D4}/request.json"),JsonFiles.Options)!;
  var folder=Path.Combine(root,index.ToString());var snapshot=inputs.Restore("study/models/"+Path.GetFileName(original.SnapshotPath),Path.Combine(root,"inputs",Path.GetFileName(original.SnapshotPath)));var snapshotHash=Hash(snapshot);
  var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));var request=original.Request;
  var stateBefore=JsonSerializer.Serialize(optic.ToSnapshot(),JsonFiles.Options);
  JsonFiles.Write(Path.Combine(folder,"original-request.json"),request);
  var fixedData=new WorkbenchRuntime(optic).BuildAnalysisData(row.Key,request.WorkbenchSettings);JsonFiles.Write(Path.Combine(folder,"fixed-metadata.json"),new{fixedData.Outcome,fixedData.OutcomeReason,fixedData.Values});
  var domain=OriginalDomain(index,entry,request);
  foreach(var algorithm in index==831?new[]{"ValidateRequested","Adaptive"}:new[]{"Adaptive"})
  {
   var settings=new Dictionary<string,string>(request.WorkbenchSettings){{"FourierSamplingMode",algorithm},{"FourierMaximumSampling",row.Key=="FFT PSF Cross Section"?"4096":"2048"},{"FourierRelativeAccuracy",algorithm=="ValidateRequested"?".005":".001"}};
   var actual=request with{WorkbenchSettings=settings};var data=new WorkbenchRuntime(optic).BuildAnalysisData(row.Key,settings);
   JsonFiles.Write(Path.Combine(folder,algorithm+"-request.json"),actual);
   JsonFiles.Write(Path.Combine(folder,algorithm+"-metadata.json"),new{data.Outcome,data.OutcomeReason,data.Values});
   if(data.Outcome!=AnalysisOutcome.Success){results.Add(new{row.Index,row.Key,algorithm,data.Outcome,data.OutcomeReason,data.Values});Save();Console.WriteLine($"{index}/{algorithm}: {data.Outcome} {data.OutcomeReason}");continue;}
   var w=Normalize(data,entry,actual,Path.Combine(folder,"transient.json"));JsonFiles.Write(Path.Combine(folder,algorithm+"-numeric.json"),w);
   if(mode=="six")
   {
    var capture=index==2853?"offaxis-opd-sampling-native8x-extra-20261104":"offaxis-opd-sampling-native8x-20261104";
    var reference=previous.Restore($"runs/{capture}/{index}-8/native/data.json",Path.Combine(folder,"reference8/data.json"));
    previous.Restore($"runs/{capture}/{index}-8/native/data.txt",Path.Combine(folder,"reference8/data.txt"));
    if(request.NativeLensUnit is not null)
     foreach(var file in new[]{"model.json","unit-normalization.json"})previous.Restore($"runs/{capture}/{index}-8/native/{file}",Path.Combine(folder,"reference8",file));
    Compare("native-8x",w,Domain(ResultNormalizer.Zemax(reference,entry,request with{PupilSampling=request.PupilSampling*8}),domain));
    var receipt=Directory.GetFiles(Path.GetDirectoryName(reference)!).Select(file=>new{logicalPath=$"runs/{capture}/{index}-8/native/"+Path.GetFileName(file),sha256=Hash(file),length=new FileInfo(file).Length}).ToArray();
    JsonFiles.Write(Path.Combine(folder,"reference8-reuse.json"),receipt);
    foreach(var file in Directory.GetFiles(Path.GetDirectoryName(reference)!))File.Delete(file);
   }
   else
   {
    foreach(var n in new[]{512,1024,2048,4096,8192})
    {
     var path=Path.Combine(repo,$"artifacts/validation/fourier-resolution-native-{n}-20261104/{index}-{n}/native/data.json");if(!File.Exists(path))continue;
     try{Compare("native-"+n,w,Domain(ResultNormalizer.Zemax(path,entry,request with{PupilSampling=n}),domain));}catch(InvalidDataException e){results.Add(new{row.Index,algorithm,reference=n,conclusion="Incomparable",reason=e.Message});Save();}
    }
   }
   void Compare(string protocol,NumericResult a,NumericResult b)
   {
    var target=Path.Combine(folder,algorithm+"-"+protocol);var run=new AnalysisRun{Key=row.Key,Request=actual,Directory=target};ComparisonRunner.Compare(run,entry,row.Settings,a,b,target);
    results.Add(new{row.Index,row.Key,algorithm,protocol,run.Conclusion,run.Metrics,validation=data.Values.GetValueOrDefault("FourierSamplingValidation"),coreSha256=core});Save();Console.WriteLine($"{index}/{algorithm}/{protocol}: {run.Conclusion}");
   }
  }
  if(Hash(snapshot)!=snapshotHash||stateBefore!=JsonSerializer.Serialize(optic.ToSnapshot(),JsonFiles.Options))throw new InvalidDataException("Source snapshot changed");
 }
}
else if(mode=="levels")
{
 var factory=typeof(WorkbenchRuntime).GetMethod("CreateAnalysis",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
 foreach(var index in args.Skip(3).Select(int.Parse))
 {
  var row=rows.Single(r=>r.Index==index);var job=JsonSerializer.Deserialize<WorkbenchJob>(inputs.Read($"study/analyses/{index:D4}/request.json"),JsonFiles.Options)!;
  var path=inputs.Restore("study/models/"+Path.GetFileName(job.SnapshotPath),Path.Combine(root,"inputs",Path.GetFileName(job.SnapshotPath)));var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(path));
  var originalPath=previous.Restore($"runs/offaxis-opd-sampling-audit-with8x-20261104/{index}/finite-sum.json",Path.Combine(root,"domain.json"));var original=JsonFiles.Read<AnalysisData>(originalPath);File.Delete(originalPath);
  AnalysisData? prior=null;
  foreach(var n in row.Key=="PSF"?new[]{512,1024,2048}:new[]{512,1024,2048,4096})
  {
   var settings=new Dictionary<string,string>(job.Request.WorkbenchSettings){["Sampling"]=n.ToString()};var phase=new List<FftPupilSampling>();AnalysisData data;
   using(FftPupilSampling.RequireResolvedResults())using(FftPupilSampling.ObserveForValidation(phase))data=((BaseAnalysis)factory.Invoke(new WorkbenchRuntime(optic),[row.Key,settings])!).GenerateData();
   var projected=FourierResultValidationAnalysis.Project(data,original);
   var values=projected.Values.Where(p=>p.Value is double&&p.Key is "StrehlRatio" or "UnaberratedPeakRatio" or "WorkingFNumber" or "DisplayedWindowPeakRatio").ToDictionary(p=>p.Key,p=>(double)p.Value);
   var errors=prior is null?null:values.Where(p=>prior.Values.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>Math.Abs(p.Value-Convert.ToDouble(prior.Values[p.Key]))/Math.Max(1e-30,Math.Abs(p.Value)));
   var shapes=prior is null?((double Rms,double Max)?)null:FourierResultValidationAnalysis.Difference(prior with{Values=new Dictionary<string,object>()},projected with{Values=new Dictionary<string,object>()});
   results.Add(new{index,n,values,errors,shapeRms=shapes?.Rms,shapeMax=shapes?.Max});Save();Console.WriteLine($"{index}/{n}: shape {shapes?.Rms:G7}; scalars {JsonSerializer.Serialize(errors)}");prior=projected;
  }
 }
}
else if(mode=="native-convergence")
{
 foreach(var index in thirteen)
 {
  var row=rows.Single(r=>r.Index==index);var entry=AnalysisComparisonRegistry.Get(row.Key);var request=row.Run.Request!;var domain=OriginalDomain(index,entry,request);
  foreach(var (a,b) in new[]{(512,1024),(1024,2048),(2048,4096),(4096,8192)})
  {
   var pa=Path.Combine(repo,$"artifacts/validation/fourier-resolution-native-{a}-20261104/{index}-{a}/native/data.json");var pb=Path.Combine(repo,$"artifacts/validation/fourier-resolution-native-{b}-20261104/{index}-{b}/native/data.json");if(!File.Exists(pa)||!File.Exists(pb))continue;
   var target=Path.Combine(root,$"{index}-{a}-{b}");var run=new AnalysisRun{Key=row.Key,Request=request,Directory=target};
   try{ComparisonRunner.Compare(run,entry,row.Settings,Domain(ResultNormalizer.Zemax(pa,entry,request with{PupilSampling=a}),domain),Domain(ResultNormalizer.Zemax(pb,entry,request with{PupilSampling=b}),domain),target);}
   catch(InvalidDataException e){run.Conclusion=Conclusion.Incomparable;run.Reason=e.Message;}
   results.Add(new{index,a,b,run.Conclusion,run.Reason,run.Metrics});Save();Console.WriteLine($"{index}/{a}-{b}: {run.Conclusion}");
  }
 }
}
else throw new ArgumentException("Unknown mode");
JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=core,mode,completedUtc=DateTimeOffset.UtcNow,originalRequestsReplaced=false,tolerancesChanged=false});
void Save()=>JsonFiles.Write(Path.Combine(root,"results.json"),results);
string Hash(string path)=>JsonFiles.Hash(File.ReadAllBytes(path));
NumericResult OriginalDomain(int index,AnalysisComparisonEntry entry,CanonicalAnalysisRequest request)
{
 var path=previous.Restore($"runs/offaxis-opd-sampling-audit-with8x-20261104/{index}/finite-sum.json",Path.Combine(root,"transient-domain.json"));
 var d=ResultNormalizer.Workbench(path,entry,request);File.Delete(path);return d;
}
NumericResult Normalize(AnalysisData d,AnalysisComparisonEntry e,CanonicalAnalysisRequest r,string temp)
{JsonFiles.Write(temp,d);try{return ResultNormalizer.Workbench(temp,e,r);}finally{File.Delete(temp);}}
static NumericResult Domain(NumericResult source,NumericResult domain)
{
 return source with{Series=domain.Series.Select(d=>{var s=PhysicalNormalization.Convert(source.Series.Single(s=>s.Id==d.Id),d.XAxis,d.YAxis,[]);var p=PhysicalNormalization.Sort(s.X,s.Y);if(d.X.Any(x=>x<p.X[0]-1e-10||x>p.X[^1]+1e-10))throw new InvalidDataException("Incomplete requested domain");return d with{Y=d.X.Select(x=>PhysicalNormalization.Interpolate(p.X,p.Y,Math.Clamp(x,p.X[0],p.X[^1]))).ToArray()};}).ToList()};
}
static void Slot(){var p=Process.GetProcessesByName("OpticStudio");var n=p.Length;foreach(var x in p)x.Dispose();if(n>1)throw new InvalidOperationException("No independent native slot");}
internal sealed record Row(int Index,string Lens,int Configuration,string Key,string LegacyPath,AnalysisConfiguration Settings,AnalysisRun Run);
internal sealed record Plan(int Index,int Sampling);
internal sealed class Archive(string root):IDisposable
{
 readonly Dictionary<string,Payload> entries=JsonFiles.Read<Payload[]>(Path.Combine(root,"archive-index.json")).ToDictionary(e=>e.LogicalPath);readonly Dictionary<string,ZipArchive> open=[];
 internal byte[] Read(string logical){var e=entries[logical];var path=Path.GetFullPath(Path.Combine(root,e.Archive));byte[] data;if(e.Entry is null)data=File.ReadAllBytes(path);else{if(!open.TryGetValue(path,out var z))open[path]=z=ZipFile.OpenRead(path);using var s=z.GetEntry(e.Entry)!.Open();using var m=new MemoryStream();s.CopyTo(m);data=m.ToArray();}if(data.Length!=e.Length||JsonFiles.Hash(data)!=e.Sha256)throw new InvalidDataException(logical);return data;}
 internal string Restore(string logical,string path){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,Read(logical));return path;}
 public void Dispose(){foreach(var z in open.Values)z.Dispose();}
}
internal sealed record Payload(string LogicalPath,string Archive,string? Entry,string Sha256,long Length);
