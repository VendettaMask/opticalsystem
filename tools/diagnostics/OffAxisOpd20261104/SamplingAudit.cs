using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class SamplingAudit
{
 internal static readonly int[] Indices=[313,789,791,831,835,969,991,1115,1119,1767,1989,2229,2567,2569,2679,2813,2835,2853,2877];
 internal static async Task Capture(string repo,string root,string[] selected)
 {
  using var lease=new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  CheckSlot();var api=ZemaxExecutor.Locate(null);var host=await ZemaxExecutor.Build(api,root,CancellationToken.None);
  var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));var results=new List<object>();
  var scales=selected.Contains("8x")?new[]{8}:new[]{2,4};var indices=selected.Where(s=>s!="8x").Select(int.Parse).ToArray();
  foreach(var index in indices.Length>0?indices:Indices)
  {
   var row=rows.Single(r=>r.Index==index);var original=row.Run.Request!;var entry=AnalysisComparisonRegistry.Get(row.Key);
   var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);var hash=JsonFiles.Hash(File.ReadAllBytes(source));
   foreach(var scale in scales)
   {
    var n=original.PupilSampling*scale;var settings=new Dictionary<string,object>(original.ZemaxSettings);
    foreach(var key in new[]{"SampleSize","PupilSampleSize"})if(settings.ContainsKey(key))settings[key]=$"S_{n}x{n}";
    var request=original with{PupilSampling=n,ZemaxSettings=settings};var folder=Path.Combine(root,$"{index}-{scale}");
    CheckSlot();var result=await host.Capture(source,Path.Combine(folder,"native"),"2026 R1",entry.ZemaxSettingsMapper,entry.ZemaxAnalysisType,request,original.Configuration,120,false,CancellationToken.None);
    JsonFiles.Write(Path.Combine(folder,"execution.json"),result);string? error=null;
    try
    {
     if(result.ExitCode!=0||result.TimedOut)throw new InvalidDataException("Native execution failed");
     NativeReferenceDiagnostics.Validate(JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/data.json")));
     var p=JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/captured-settings.json")).GetProperty("properties");
     var key="SampleSize";
     if(!p.GetProperty(key).GetString()!.EndsWith($"_{n}x{n}"))throw new InvalidDataException("Actual sampling differs");
     if(row.Key=="PSF"&&(!p.GetProperty("OutputSize").GetString()!.EndsWith($"_{original.ImageSampling}x{original.ImageSampling}")||Math.Abs(p.GetProperty("ImageDelta").GetDouble()-original.ImageDeltaMicrometers)>1e-12))throw new InvalidDataException("Image grid changed");
    }
    catch(Exception e)when(e is InvalidDataException or KeyNotFoundException){error=e.Message;}
    if(JsonFiles.Hash(File.ReadAllBytes(source))!=hash)throw new InvalidDataException("Original source changed");
    results.Add(new{index,row.Lens,row.Key,scale,pupilSampling=n,sourceSha256=hash,valid=error is null,error,result});
    JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{index} x{scale}: {error??"Captured and read back"}");
   }
  }
  static void CheckSlot(){var p=Process.GetProcessesByName("OpticStudio");var n=p.Length;foreach(var item in p)item.Dispose();if(n>1)throw new InvalidOperationException("Independent native slot unavailable");}
 }
 internal static void Run(string repo,string root,string captures,string[] extraCaptures)
 {
  using var originalInputs=new FrozenArchive(Path.Combine(repo,"validation/rechecks/2026-11-04/all-example-accuracy"));
  using var oldSampling=new FrozenArchive(Path.Combine(repo,"validation/rechecks/2026-11-04/fft-sampling-policy"));
  var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
  var factory=typeof(WorkbenchRuntime).GetMethod("CreateAnalysis",BindingFlags.Instance|BindingFlags.NonPublic)!;var results=new List<object>();
  foreach(var index in Indices)
  {
   var row=rows.Single(r=>r.Index==index);var entry=AnalysisComparisonRegistry.Get(row.Key);var folder=Path.Combine(root,index.ToString());
   var oldJob=JsonSerializer.Deserialize<WorkbenchJob>(originalInputs.Read($"study/analyses/{index:D4}/request.json"),JsonFiles.Options)!;
   var snapshot=originalInputs.Restore("study/models/"+Path.GetFileName(oldJob.SnapshotPath),Path.Combine(root,"inputs",Path.GetFileName(oldJob.SnapshotPath)));
   var optic=Optic.FromSnapshot(JsonFiles.Read<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(snapshot));var request=oldJob.Request;
   var original=new WorkbenchRuntime(optic).BuildAnalysisData(row.Key,request.WorkbenchSettings);JsonFiles.Write(Path.Combine(folder,"product.json"),original);
   if(original.Outcome!=AnalysisOutcome.Unavailable)throw new InvalidDataException("Original refusal unexpectedly changed");
   // Explicit diagnostic finite sum; the production request's required-result scope stays on.
   var analysis=(BaseAnalysis)factory.Invoke(new WorkbenchRuntime(optic),[row.Key,new Dictionary<string,string>(request.WorkbenchSettings)])!;
   var path=Path.Combine(folder,"finite-sum.json");JsonFiles.Write(path,analysis.GenerateData());var core=ResultNormalizer.Workbench(path,entry,request);
   var oldPath=oldSampling.Restore($"final/{index}/1-data.json",Path.Combine(folder,"prior-finite-sum.json"));
   var old=ResultNormalizer.Workbench(oldPath,entry,request);
   var highPath=oldSampling.Restore($"final/{index}/8-data.json",Path.Combine(folder,"prior-8x.json"));
   var oldHigh=ResultNormalizer.Workbench(highPath,entry,request);
   var nativeOriginal=ResultNormalizer.Zemax(Path.Combine(row.LegacyPath,"native/data.json"),entry,request);
   var records=new List<object>();
   Compare("current-prior-finite-sum",core,old);
   SafeCompare("prior-self-8x-reused",()=>old,()=>OnOriginalNodes(oldHigh,old));
   SafeCompare("current-finite-original-native",()=>core,()=>OnOriginalNodes(nativeOriginal,core));
   NumericResult? z2=null,z4=null,z8=null;
   var extraRoot=extraCaptures.FirstOrDefault(d=>Directory.Exists(Path.Combine(d,$"{index}-8")));var include8=extraRoot is not null;
   foreach(var scale in include8?new[]{2,4,8}:new[]{2,4})
   {
    var captureRoot=scale==8?extraRoot!:captures;var capture=Path.Combine(captureRoot,$"{index}-{scale}/native/data.json");
    try
    {
     var captured=JsonFiles.Read<JsonElement>(Path.Combine(captureRoot,$"{index}-{scale}/native/captured-settings.json"));
     var properties=captured.GetProperty("properties");
     if(!properties.GetProperty("SampleSize").GetString()!.EndsWith($"_{request.PupilSampling*scale}x{request.PupilSampling*scale}"))throw new InvalidDataException("Native sampling readback differs");
     if(row.Key=="PSF"&&(!properties.GetProperty("OutputSize").GetString()!.EndsWith($"_{request.ImageSampling}x{request.ImageSampling}")||Math.Abs(properties.GetProperty("ImageDelta").GetDouble()-request.ImageDeltaMicrometers)>1e-12))throw new InvalidDataException("Native image window changed");
     var sourceHash=JsonFiles.Hash(File.ReadAllBytes(captured.GetProperty("request").GetProperty("input").GetString()!));
     var receipt=JsonFiles.Read<JsonElement[]>(Path.Combine(captureRoot,"results.json")).Single(e=>e.GetProperty("index").GetInt32()==index&&e.GetProperty("scale").GetInt32()==scale);
     if(sourceHash!=receipt.GetProperty("sourceSha256").GetString())throw new InvalidDataException("Native source identity changed");
     JsonFiles.Write(Path.Combine(folder,$"native-{scale}x-readback.json"),new{sampling=request.PupilSampling*scale,readback=properties,sourceInputSha256=sourceHash});
     var high=ResultNormalizer.Zemax(capture,entry,request with{PupilSampling=request.PupilSampling*scale});var z=OnOriginalNodes(high,core);
     if(scale==2)z2=z;else if(scale==4)z4=z;else z8=z;Compare($"current-finite-native-{scale}x",core,z);
    }
    catch(Exception e)when(e is InvalidDataException or IOException or InvalidOperationException){records.Add(new{protocol=$"current-finite-native-{scale}x",conclusion="Incomparable",reason=e.Message});}
   }
   if(z2 is not null&&z4 is not null)Compare("native-2x-4x-on-original-domain",z2,z4);
   if(z4 is not null&&z8 is not null)Compare("native-4x-8x-on-original-domain",z4,z8);
   if(index is 831 or 835 && z8 is not null)
   {
    NumericResult? previousCore=null;
    foreach(var scale in new[]{2,4,8})
    {
     var settings=new Dictionary<string,string>(request.WorkbenchSettings);settings["Sampling"]=(request.PupilSampling*scale).ToString();
     var independent=(BaseAnalysis)factory.Invoke(new WorkbenchRuntime(optic),[row.Key,settings])!;
     var dataPath=Path.Combine(folder,$"current-{scale}x.json");JsonFiles.Write(dataPath,independent.GenerateData());
     var c=OnOriginalNodes(ResultNormalizer.Workbench(dataPath,entry,request with{PupilSampling=request.PupilSampling*scale,WorkbenchSettings=settings}),core);
     Compare($"current-{scale}x-native-8x",c,z8);
     if(previousCore is not null)Compare($"current-{scale/2}x-{scale}x-on-original-domain",previousCore,c);
     previousCore=c;
    }
   }
   results.Add(new{index,row.Lens,row.Key,originalOutcome=original.Outcome,original.OutcomeReason,originalSampling=request.PupilSampling,records});
   JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{index} audited");
   void Compare(string protocol,NumericResult a,NumericResult b)
   {
    var target=Path.Combine(folder,protocol);var run=new AnalysisRun{Key=row.Key,Request=request,Directory=target};
    ComparisonRunner.Compare(run,entry,row.Settings,a,b,target);records.Add(new{protocol,run.Conclusion,run.Metrics,run.Normalizations});
   }
   void SafeCompare(string protocol,Func<NumericResult> a,Func<NumericResult> b)
   {
    try{Compare(protocol,a(),b());}
    catch(InvalidDataException e){records.Add(new{protocol,conclusion="Incomparable",reason=e.Message});}
   }
  }
  JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
   priorHighSamplingCore="a052ac75b497c5f1ecf254141fbef1cb61b915db5d8ef1898eeda672527f75a0",productGateChanged=false,originalRequestsChanged=false,tolerancesChanged=false,completedUtc=DateTimeOffset.UtcNow});
 }
 // All original curve nodes must be covered. No new window, shifts or value fitting.
 internal static NumericResult OnOriginalNodes(NumericResult source,NumericResult domain)
 {
  var curves=domain.Series.Select(d=>
  {
   var s=PhysicalNormalization.Convert(source.Series.Single(s=>s.Id==d.Id),d.XAxis,d.YAxis,[]);var sorted=PhysicalNormalization.Sort(s.X,s.Y);
   if(d.X.Any(x=>x<sorted.X[0]-1e-10||x>sorted.X[^1]+1e-10))throw new InvalidDataException("Incomplete original physical domain");
   return d with{Y=d.X.Select(x=>PhysicalNormalization.Interpolate(sorted.X,sorted.Y,Math.Clamp(x,sorted.X[0],sorted.X[^1]))).ToArray()};
  }).ToList();
  // Fixed PSF pitch and image count are required; the existing comparator rejects grid changes.
  return source with{Series=curves};
 }
}
internal sealed class FrozenArchive(string root):IDisposable
{
 private readonly Dictionary<string,PayloadRef> entries=JsonFiles.Read<PayloadRef[]>(Path.Combine(root,"archive-index.json")).ToDictionary(e=>e.LogicalPath);
 private readonly Dictionary<string,ZipArchive> opened=[];
 internal byte[] Read(string logical)
 {
  var e=entries[logical];var path=Path.GetFullPath(Path.Combine(root,e.Archive));byte[] bytes;
  if(e.Entry is null)bytes=File.ReadAllBytes(path);
  else{if(!opened.TryGetValue(path,out var zip))opened[path]=zip=ZipFile.OpenRead(path);using var input=zip.GetEntry(e.Entry)!.Open();using var memory=new MemoryStream();input.CopyTo(memory);bytes=memory.ToArray();}
  if(bytes.Length!=e.Length||JsonFiles.Hash(bytes)!=e.Sha256)throw new InvalidDataException(logical);return bytes;
 }
 internal string Restore(string logical,string destination){Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllBytes(destination,Read(logical));return destination;}
 public void Dispose(){foreach(var zip in opened.Values)zip.Dispose();}
}
internal sealed record PayloadRef(string LogicalPath,string Archive,string? Entry,string Sha256,long Length);
