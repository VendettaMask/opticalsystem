using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if(args.Length<3)throw new ArgumentException("crystal|convention|convention-replay|validity|fourier <repo> <fresh-output> [capture-plan|convention-capture-root]");
var repo=Path.GetFullPath(args[1]);var root=Path.GetFullPath(args[2]);
if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh output required");
Directory.CreateDirectory(root);
string Hash(string p){using var s=File.OpenRead(p);return Convert.ToHexStringLower(SHA256.HashData(s));}
var core=Hash(typeof(Optic).Assembly.Location);
if(args[0]=="validity")
{
    var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
    var results=new List<object>();
    foreach(var index in new[]{1365,1367,1369,1372})
    {
        var row=rows.Single(r=>r.Index==index);var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);
        var optic=(await WorkbenchExecutor.Import(source)).Configurations[row.Configuration-1];var folder=Path.Combine(root,index.ToString());
        var snapshot=Path.Combine(folder,"source.optic.json");JsonFiles.Write(snapshot,optic.ToSnapshot());
        var job=new WorkbenchJob(snapshot,Path.Combine(folder,"core"),row.Run.Request! with{SurfaceCount=optic.SurfaceGroup.Items.Count});
        JsonFiles.Write(Path.Combine(folder,"request.json"),job);WorkbenchExecutor.Execute(job);
        var data=JsonFiles.Read<AnalysisData>(Path.Combine(job.OutputDirectory,"data.json"));
        results.Add(new{index,row.Key,data.Outcome,data.OutcomeReason,diagnostics=data.Values,coreSha256=core});
        JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{index} {data.Outcome}: {data.OutcomeReason}");
    }
    return;
}
var replay=args[0]=="convention-replay";
using var lease=replay?null:new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var api=replay?null:ZemaxExecutor.Locate(null);var native=replay?null:await ZemaxExecutor.Build(api!,root,CancellationToken.None);
var records=new List<object>();
void CheckSlot(){var processes=Process.GetProcessesByName("OpticStudio");var count=processes.Length;foreach(var p in processes)p.Dispose();if(count>1)throw new InvalidOperationException("No independent native slot; no user application changed");}
if(args[0] is "convention" or "convention-replay")
{
    var row=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json")).Single(r=>r.Index==125);
    foreach(var item in new[]{("original-c1",1),("original-c2",2),("aperture-10",2),("aperture-40",2),("thickness-25",2)})
    {
        var source=item.Item1.StartsWith("original")?Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples/Birefringent prisms/Birefringent cube.zmx")
            :Path.Combine(repo,"artifacts/validation/definition-crystal-probe-20261103",item.Item1+".zmx");
        var sourceSha=Hash(source);var optic=(await WorkbenchExecutor.Import(source)).Configurations[item.Item2-1];
        var folder=Path.Combine(root,item.Item1);var snapshot=Path.Combine(folder,"source.optic.json");JsonFiles.Write(snapshot,optic.ToSnapshot());
        var request=row.Run.Request! with{Configuration=item.Item2,Reference=FirstOrderAnalysis.ReferenceConvention,SurfaceCount=optic.SurfaceGroup.Items.Count};
        var job=new WorkbenchJob(snapshot,Path.Combine(folder,"core"),request);JsonFiles.Write(Path.Combine(folder,"request.json"),job);WorkbenchExecutor.Execute(job);
        var nativeFolder=replay?Path.Combine(args[3],item.Item1,"native"):Path.Combine(folder,"native");
        if(!replay)
        {
            CheckSlot();var execution=await native!.Capture(source,nativeFolder,"2026 R1","first-order","SystemData",request,item.Item2,120,false,CancellationToken.None);
            JsonFiles.Write(Path.Combine(folder,"execution.json"),execution);if(execution.ExitCode!=0)throw new InvalidDataException("Native first-order convention capture failed");
        }
        var captured=JsonFiles.Read<JsonElement>(Path.Combine(nativeFolder,"captured-settings.json")).GetProperty("request");
        if(captured.GetProperty("configuration").GetInt32()!=item.Item2 || captured.GetProperty("reference").GetString()!=request.Reference
            || captured.GetProperty("wavelength").GetInt32()!=request.Wavelength)throw new InvalidDataException("Convention reference request differs");
        if(replay && JsonFiles.Read<JsonElement>(Path.Combine(args[3],"results.json")).EnumerateArray()
            .Single(r=>r.GetProperty("caseId").GetString()==item.Item1).GetProperty("sourceSha256").GetString()!=sourceSha)
            throw new InvalidDataException("Convention reference source differs");
        var entry=AnalysisComparisonRegistry.Get("First Order");var run=new AnalysisRun{Key=row.Key,Request=request,Directory=Path.Combine(folder,"comparison")};
        ComparisonRunner.Compare(run,entry,row.Settings,ResultNormalizer.Workbench(Path.Combine(job.OutputDirectory,"data.json"),entry,request),ResultNormalizer.Zemax(Path.Combine(nativeFolder,"data.json"),entry,request),run.Directory);
        JsonFiles.Write(Path.Combine(run.Directory,"comparison.json"),run);
        var rejected=false;try{ResultNormalizer.Zemax(Path.Combine(row.LegacyPath,"native/data.json"),entry,request);}catch(InvalidDataException){rejected=true;}
        if(!rejected || Hash(source)!=sourceSha)throw new InvalidDataException("Mixed convention accepted or source changed");
        records.Add(new{caseId=item.Item1,run.Conclusion,run.Metrics,legacyMixedConventionRejected=rejected,sourceSha256=sourceSha,nativeFolder,coreSha256=core});JsonFiles.Write(Path.Combine(root,"results.json"),records);Console.WriteLine(item.Item1+": "+run.Conclusion);
    }
}
else if(args[0]=="crystal")
{
    var original=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples/Birefringent prisms/Birefringent cube.zmx");
    var originalSha=Hash(original);var text=File.ReadAllText(original);
    foreach(var variant in new[]{("original-c1",1,0),("original-c2",2,0),("parax-ignore",2,1),("aperture-10",2,2),("aperture-40",2,3),("thickness-25",2,4)})
    {
        var source=original;
        if(variant.Item3>0)
        {
            var edited=text;
            if(variant.Item3==1)edited=Regex.Replace(edited,@"(?ms)(^SURF 2\r?\n.*?)(?=^SURF 3\r?\n)",m=>m.Value.Replace("PARM 5 0","PARM 5 1"));
            if(variant.Item3==2)edited=edited.Replace("ENPD 20","ENPD 10");
            if(variant.Item3==3)edited=edited.Replace("ENPD 20","ENPD 40");
            if(variant.Item3==4)edited=Regex.Replace(edited,@"(?ms)(^SURF 2\r?\n.*?)(?=^SURF 3\r?\n)",m=>m.Value.Replace("DISZ 50","DISZ 25"));
            source=Path.Combine(root,variant.Item1+".zmx");File.WriteAllText(source,edited);
        }
        var inputs=new List<object>();
        object Op(string code,int int1,int int2,double a=0,double b=0,double c=0,double d=0)=>new{code,int1,int2,data1=a,data2=b,data3=c,data4=d,data5=0d,data6=0d};
        foreach(var wave in new[]{0,1,2,3})inputs.Add(Op("EFFL",0,wave));
        inputs.Add(Op("EPDI",0,0));inputs.Add(Op("EFLX",1,4));inputs.Add(Op("EFLY",1,4));
        foreach(var py in new[]{0d,1e-5,.001,1})foreach(var si in new[]{1,2,3,4,5})foreach(var code in new[]{"PARY","PATY","REAY","REAB","REAC"})inputs.Add(Op(code,si,2,0,0,0,py));
        var folder=Path.Combine(root,variant.Item1);var request=Path.Combine(root,variant.Item1+".request.json");
        JsonFiles.Write(request,new{zosApiPath=api,input=source,inputSha256=Hash(source),configuration=variant.Item2,wavelength=2,zemaxVersion="2026 R1",adapter="surface-model-controls",captureScreenshots=false,formalAcceptanceRequest=false,modelOperands=inputs});
        CheckSlot();var result=await ProcessIsolation.Run(Path.Combine(root,"host/ZemaxHost.exe"),[request,folder],root,120,CancellationToken.None,new Dictionary<string,string>{["PATH"]=api+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH")});
        JsonFiles.Write(Path.Combine(folder,"execution.json"),result);if(result.ExitCode!=0)throw new InvalidDataException("Native model capture failed");
        var optic=(await WorkbenchExecutor.Import(source)).Configurations[variant.Item2-1];
        JsonFiles.Write(Path.Combine(folder,"core-first-order.json"),new FirstOrderAnalysis(optic).GenerateData());
        var data=JsonFiles.Read<JsonElement>(Path.Combine(folder,"model-controls.json"));
        records.Add(new{variant=variant.Item1,configuration=variant.Item2,sourceSha256=Hash(source),coreSha256=core,scalarOperands=data.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("input").GetProperty("code").GetString() is "EFFL" or "EPDI" or "EFLX" or "EFLY").ToArray()});
        JsonFiles.Write(Path.Combine(root,"results.json"),records);Console.WriteLine(variant.Item1+" captured");
    }
    if(Hash(original)!=originalSha)throw new InvalidDataException("Original source changed");
}
else if(args[0]=="fourier")
{
    var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
    foreach(var spec in JsonFiles.Read<CapturePlan[]>(args[3]))
    {
        var row=rows.Single(r=>r.Index==spec.Index);var entry=AnalysisComparisonRegistry.Get(row.Key);
        var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);var sourceSha=Hash(source);
        var wb=new Dictionary<string,string>(row.Run.Request!.WorkbenchSettings);var zs=new Dictionary<string,object>(row.Run.Request.ZemaxSettings);
        foreach(var key in new[]{"Sampling","PupilSampling","NumRays"})if(wb.ContainsKey(key))wb[key]=spec.Sampling.ToString();
        wb["ImageDeltaMicrometers"]=spec.Delta.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        if(zs.ContainsKey("SampleSize"))zs["SampleSize"]=$"S_{spec.Sampling}x{spec.Sampling}";
        if(zs.ContainsKey("PupilSampleSize"))zs["PupilSampleSize"]=$"S_{spec.Sampling}x{spec.Sampling}";
        if(row.Key=="FFT PSF Cross Section")zs["PlotScale"]=spec.Scale;
        var request=row.Run.Request with{PupilSampling=spec.Sampling,ImageDeltaMicrometers=spec.Delta,WorkbenchSettings=wb,ZemaxSettings=zs};
        var folder=Path.Combine(root,$"{spec.Index}-{spec.Sampling}-{spec.Delta:G}-{spec.Scale:G}");
        CheckSlot();var result=await native!.Capture(source,Path.Combine(folder,"native"),"2026 R1",entry.ZemaxSettingsMapper,entry.ZemaxAnalysisType,request,row.Configuration,120,false,CancellationToken.None);
        JsonFiles.Write(Path.Combine(folder,"execution.json"),result);var valid=false;string? reason=null;
        if(result.ExitCode==0)try
        {
            NativeReferenceDiagnostics.Validate(JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/data.json")));
            var captured=JsonFiles.Read<JsonElement>(Path.Combine(folder,"native/captured-settings.json")).GetProperty("properties");
            var samplingProperty=row.Key.StartsWith("Huygens")?"PupilSampleSize":"SampleSize";
            var actual=captured.GetProperty(samplingProperty).GetString()!;
            if(!actual.EndsWith($"_{spec.Sampling}x{spec.Sampling}",StringComparison.Ordinal))throw new InvalidDataException("Native sampling readback differs from the declared job");
            valid=true;
        }
        catch(InvalidDataException e){reason=e.Message;}
        records.Add(new{row.Index,row.Key,spec.Sampling,spec.Delta,spec.Scale,result,valid,reason,sourceSha256=sourceSha,coreSha256=core});
        JsonFiles.Write(Path.Combine(root,"results.json"),records);Console.WriteLine($"{row.Index}/{spec.Sampling}/{spec.Delta}: {valid} {reason}");
        if(Hash(source)!=sourceSha)throw new InvalidDataException("Source changed");
    }
}
else throw new ArgumentException("Unknown mode");
JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=core,nativeExecuted=!replay,completedUtc=DateTimeOffset.UtcNow});

internal sealed record Row(int Index,string Lens,int Configuration,string Key,string LegacyPath,AnalysisConfiguration Settings,AnalysisRun Run);
internal sealed record CapturePlan(int Index,int Sampling,double Delta,double Scale=0);
