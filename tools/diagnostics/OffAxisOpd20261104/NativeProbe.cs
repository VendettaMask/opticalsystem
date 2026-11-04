using System.Diagnostics;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Zemax;
using System.Text.RegularExpressions;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class NativeProbe
{
 internal static async Task Run(string repo,string root,string[] options)
 {
  using var lease=new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var engines=Process.GetProcessesByName("OpticStudio");var count=engines.Length;foreach(var p in engines)p.Dispose();if(count>1)throw new InvalidOperationException("No independent native slot");
  var api=ZemaxExecutor.Locate(null);var native=await ZemaxExecutor.Build(api,root,CancellationToken.None);
  var original=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples/Image Simulation",
   options.Contains("example5")?"Example 5, spatially varying resolution.ZMX":"Example 2, Double Gauss Experimental Arrangement.ZMX");
  var source=original;var originalHash=JsonFiles.Hash(File.ReadAllBytes(original));
  var mode=options.Contains("mode3")?3:options.Contains("mode0")?0:1;
  if(mode!=1)
  {
   source=Path.Combine(root,$"paraxial-mode{mode}.zmx");var text=File.ReadAllText(original);
   text=Regex.Replace(text,@"(?ms)(^SURF 1\r?\n.*?)(?=^SURF 2\r?\n)",m=>m.Value.Replace("PARM 2 1",$"PARM 2 {mode}"));
   File.WriteAllText(source,text);
  }
  var sourceHash=JsonFiles.Hash(File.ReadAllBytes(source));var inputs=new List<object>();
  foreach(var wave in new[]{1,2,3})foreach(var hy in new[]{0d,1d})foreach(var (px,py) in new[]{(0d,0d),(.5,0d),(1d,0d),(0d,.5),(0d,1d)})
   foreach(var surface in new[]{1,7,13})foreach(var code in new[]{"REAX","REAY","REAZ","REAA","REAB","REAC","OPTH"})
    inputs.Add(new{code,int1=surface,int2=wave,data1=0d,data2=hy,data3=px,data4=py,data5=0d,data6=0d});
  var request=Path.Combine(root,"request.json");var dictionary=new Dictionary<string,object>{{"zosApiPath",api},{"input",source},{"inputSha256",sourceHash},{"configuration",1},{"wavelength",2},{"zemaxVersion","2026 R1"},{"adapter","surface-model-controls"},{"captureScreenshots",false},{"formalAcceptanceRequest",false},{"modelOperands",inputs}};
  if(options.Contains("cache-off")){dictionary["pointKind"]="pupil-scale-origin";dictionary["diagnosticPupilAiming"]=new Dictionary<string,object>{{"UseRayAimingCache",false}};}
  JsonFiles.Write(request,dictionary);
  var capture=Path.Combine(root,"native");var result=await ProcessIsolation.Run(Path.Combine(root,"host/ZemaxHost.exe"),[request,capture],root,120,CancellationToken.None,new Dictionary<string,string>{["PATH"]=api+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH")});
  JsonFiles.Write(Path.Combine(root,"execution.json"),result);if(result.ExitCode!=0||result.TimedOut)throw new InvalidOperationException("Native capture failed");
  if(sourceHash!=JsonFiles.Hash(File.ReadAllBytes(source))||originalHash!=JsonFiles.Hash(File.ReadAllBytes(original)))throw new InvalidOperationException("Source changed");
  if(options.Contains("plots"))
  {
   var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
   foreach(var index in options.Contains("example5")?new[]{690,694,698}:new[]{600,604,608})
   {
    var row=rows.Single(r=>r.Index==index);var entry=AnalysisComparisonRegistry.Get(row.Key);
    var execution=await native.Capture(source,Path.Combine(root,"plot-"+index),"2026 R1",entry.ZemaxSettingsMapper,entry.ZemaxAnalysisType,row.Run.Request!,1,120,false,CancellationToken.None);
    JsonFiles.Write(Path.Combine(root,"plot-"+index+".execution.json"),execution);if(execution.ExitCode!=0)throw new InvalidOperationException("Native plot failed");
   }
  }
  JsonFiles.Write(Path.Combine(root,"identity.json"),new{sourceSha256=sourceHash,originalSha256=originalHash,options,mode,operandCacheOverrideOnly=options.Contains("cache-off"),plotCacheSetting="Preserved from source",originalChanged=false});
  Console.WriteLine($"Native original-system operands: {inputs.Count}");
 }
}
