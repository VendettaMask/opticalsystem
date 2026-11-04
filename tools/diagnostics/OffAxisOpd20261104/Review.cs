using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class Review
{
 internal static async Task Run(string repo,string root)
 {
  var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
  var controls=JsonFiles.Read<JsonElement[]>(Path.Combine(repo,"validation/rechecks/2026-11-04/analysis-surface-reference/cross-analysis-results.json")).ToDictionary(e=>e.GetProperty("index").GetInt32());
  var regression=controls.Keys.ToHashSet();using var previous=new FrozenArchive(Path.Combine(repo,"validation/rechecks/2026-11-04/analysis-surface-reference"));
  var six=new[]{600,604,608,690,694,698};var records=new List<object>();
  foreach(var row in rows.Where(r=>regression.Contains(r.Index)||r.Lens.StartsWith("Image Simulation/Example 2,")||r.Lens.StartsWith("Image Simulation/Example 5,")))
  {
   var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);
   var optic=(await WorkbenchExecutor.Import(source)).Configurations[row.Configuration-1];var request=row.Run.Request!;
   if(regression.Contains(row.Index))
   {
    var oldDirectory=Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(controls[row.Index].GetProperty("a").GetString()!))!);
    request=JsonSerializer.Deserialize<WorkbenchJob>(previous.Read($"runs/analysis-definition-cross-20261104/{oldDirectory}/request.json"),JsonFiles.Options)!.Request;
   }
   var path=Path.Combine(root,row.Index.ToString(),"data.json");var data=new WorkbenchRuntime(optic).BuildAnalysisData(row.Key,request.WorkbenchSettings);JsonFiles.Write(path,data);
   var entry=AnalysisComparisonRegistry.Get(row.Key);NumericResult core;
   try
   {
    core=ResultNormalizer.Workbench(path,entry,request);
    var native=ResultNormalizer.Zemax(regression.Contains(row.Index)?controls[row.Index].GetProperty("b").GetString()!:Path.Combine(row.LegacyPath,"native/data.json"),entry,request);
    Compare("original",core,native);
   }
   catch(InvalidDataException e)
   {
    records.Add(new{row.Index,row.Lens,row.Key,protocol="original",conclusion="Incomparable",reason=e.Message,regression=regression.Contains(row.Index)});
    JsonFiles.Write(Path.Combine(root,"results.json"),records);Console.WriteLine($"{row.Index}/original: Incomparable");continue;
   }
   if(six.Contains(row.Index))
   {
    var folder=row.Index<690?"offaxis-opd-native-mode0-20261104":"offaxis-opd-native-mode0-example5-20261104";
    var z0=ResultNormalizer.Zemax(Path.Combine(repo,$"artifacts/validation/{folder}/plot-{row.Index}/data.json"),entry,request);
    Compare("perfect-collimation-mode0-control-v1",core,z0);
    if(row.Index<690)
    {
     var z3=ResultNormalizer.Zemax(Path.Combine(repo,$"artifacts/validation/offaxis-opd-native-mode3-20261104/plot-{row.Index}/data.json"),entry,request);
     Compare("perfect-collimation-mode3-control-v1",core,z3);Compare("native-mode0-mode3-control-v1",z0,z3);
    }
   }
   void Compare(string protocol,NumericResult a,NumericResult b)
   {
    var folder=Path.Combine(root,row.Index.ToString(),protocol);var run=new AnalysisRun{Key=row.Key,Request=request,Directory=folder};
    ComparisonRunner.Compare(run,entry,row.Settings,a,b,folder);
    records.Add(new{row.Index,row.Lens,row.Key,protocol,run.Conclusion,run.Metrics,regression=regression.Contains(row.Index),sourceSha256=JsonFiles.Hash(File.ReadAllBytes(source))});
    JsonFiles.Write(Path.Combine(root,"results.json"),records);Console.WriteLine($"{row.Index}/{protocol}: {run.Conclusion}");
   }
  }
  var plane=new List<object>();
  foreach(var name in new[]{"native","native-cache-off","native-cache-verified","native-mode0","native-mode3","native-mode0-example5"})
  {
   var folder=Path.Combine(repo,$"artifacts/validation/offaxis-opd-{name}-20261104");
   var data=JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/model-controls.json")).GetProperty("rows").EnumerateArray().ToArray();
   var rays=data.GroupBy(r=>{var p=r.GetProperty("input");return (surface:p.GetProperty("int1").GetInt32(),wave:p.GetProperty("int2").GetInt32(),hy:p.GetProperty("data2").GetDouble(),px:p.GetProperty("data3").GetDouble(),py:p.GetProperty("data4").GetDouble());})
    .Select(g=>new{key=g.Key,v=g.ToDictionary(r=>r.GetProperty("input").GetProperty("code").GetString()!,r=>r.GetProperty("value").GetDouble())}).ToArray();
   var entries=new List<object>();
   foreach(var r in rays.Where(r=>r.key.surface==1))
   {
    var c=rays.Single(c=>c.key==(1,r.key.wave,r.key.hy,0d,0d));var v=r.v;var b=c.v;
    var planeDelta=(v["REAX"]-b["REAX"])*b["REAA"]+(v["REAY"]-b["REAY"])*b["REAB"]+(v["REAZ"]-b["REAZ"])*b["REAC"];
    entries.Add(new{r.key.wave,r.key.hy,r.key.px,r.key.py,planeErrorMillimeters=v["OPTH"]-b["OPTH"]-planeDelta,
     directionError=new[]{"REAA","REAB","REAC"}.Max(k=>Math.Abs(v[k]-b[k]))});
   }
   plane.Add(new{name,cacheOverrideVerified=File.Exists(Path.Combine(folder,"native/diagnostic-pupil-aiming.json")),
    excludedCacheOverrideClaim=name=="native-cache-off",entries});
  }
  JsonFiles.Write(Path.Combine(root,"plane-controls.json"),plane);
  JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
   originalReferencesReplaced=false,originalRequestsChanged=false,tolerancesChanged=false,newProtocol="PerfectCollimationPhaseReference/v1",completedUtc=DateTimeOffset.UtcNow});
 }
}
