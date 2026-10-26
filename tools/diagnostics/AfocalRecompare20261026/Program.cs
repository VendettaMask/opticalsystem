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

if(args.Length!=3)throw new ArgumentException("prepare|workbench|compare <base run> <expanded run>");
var mode=args[0];var baseline=Path.GetFullPath(args[1]);var root=Path.GetFullPath(args[2]);
string Full(string name)=>Path.Combine(root,name);
if(mode=="prepare-fixed-grid")
{
    if(Directory.Exists(root))throw new IOException("Fresh fixed-grid control directory required");
    Directory.CreateDirectory(root);
    var expanded=Path.Combine(Path.GetDirectoryName(baseline)!,"expanded");
    var rows=new List<AuditRow>();var jobs=new List<object>();
    foreach(var original in JsonFiles.Read<List<AuditRow>>(Path.Combine(expanded,"items.json"))
        .Where(row=>row.Key is "PSF" or "Huygens PSF" or "Huygens MTF" or "Huygens Through Focus MTF"))
    {
        // Fixed before these new captures, without fitting either output grid.
        // The legacy Application setting carries mrad in afocal image space.
        var pitch=original.Lens.Contains("Beam Expander")?.005:original.Lens.Contains("Apochromat",StringComparison.OrdinalIgnoreCase)?.02:.05;
        var old=original.Request;
        var r=old with{PupilSampling=64,ImageSampling=128,GridSize=128,ImageDeltaMicrometers=pitch,
            WorkbenchSettings=new(old.WorkbenchSettings),ZemaxSettings=new(old.ZemaxSettings),ZemaxCfgSettings=new(old.ZemaxCfgSettings)};
        r.WorkbenchSettings["Sampling"]="64";r.WorkbenchSettings["PupilSampling"]="64";
        r.WorkbenchSettings["ImageSampling"]="128";r.WorkbenchSettings["Display"]="128";
        r.WorkbenchSettings["ImageDeltaMicrometers"]=pitch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nativePitch=pitch*PhysicalNormalization.UnitScale("Milliradian",r.NativeAfocalAngleUnit!);
        if(r.ZemaxSettings.ContainsKey("ImageDelta"))r.ZemaxSettings["ImageDelta"]=nativePitch;
        if(r.ZemaxSettings.ContainsKey("PupilSampleSize"))r.ZemaxSettings["PupilSampleSize"]="S_64x64";
        if(r.ZemaxSettings.ContainsKey("ImageSampleSize"))r.ZemaxSettings["ImageSampleSize"]="S_128x128";
        var id="fixed-"+original.Id;var row=original with{Id=id,Request=r};rows.Add(row);
        var directory=Full(id);Directory.CreateDirectory(Path.Combine(directory,"workbench"));Directory.CreateDirectory(Path.Combine(directory,"native"));
        JsonFiles.Write(Path.Combine(directory,"workbench/request.json"),new WorkbenchJob(row.Snapshot,Path.Combine(directory,"workbench"),r));
        var payload=JsonFiles.Read<Dictionary<string,object?>>(Path.Combine(expanded,original.Id,"native/request.json"));
        payload["pupilSampling"]=64;payload["imageSampling"]=128;payload["gridSize"]=128;
        payload["imageDeltaMicrometers"]=nativePitch;payload["zemaxSettings"]=r.ZemaxSettings;
        var requestPath=Path.Combine(directory,"native/request.json");JsonFiles.Write(requestPath,payload);
        jobs.Add(new{id,request=requestPath,output=Path.Combine(directory,"native")});
    }
    if(rows.Count!=20)throw new InvalidDataException("Expected twenty independent fixed-grid controls");
    JsonFiles.Write(Full("items.json"),rows);JsonFiles.Write(Full("native-jobs.json"),jobs);
    File.Copy(Path.Combine(expanded,"declared-policy.json"),Full("declared-policy.json"));
    JsonFiles.Write(Full("control-declaration.json"),new{fixedBeforeNativeCapture=true,pupilSampling=64,imageSampling=128,
        pitchMilliradian=new Dictionary<string,double>{{"Riflescope",.05},{"BeamExpander",.005},{"Binocular",.05},{"ApochromatPrism",.02}},
        semantics="Additional manual-angle-grid controls, separate from the original 150 requests. No fitted scale, shift, resampling or intensity fit. Native angular input is converted from mrad to each model's declared AF unit. Through-focus retains each implementation's sample count; existing bounded curve interpolation is disclosed."});
    Console.WriteLine("Prepared twenty fixed-grid controls");return;
}
if(mode=="prepare")
{
    if(Directory.Exists(root))throw new IOException("Fresh expanded directory required");Directory.CreateDirectory(root);
    var items=JsonFiles.Read<JsonElement>(Path.Combine(baseline,"items.json"));var rows=new List<AuditRow>();var nativeJobs=new List<object>();
    foreach(var item in items.EnumerateArray().Where(i=>i.GetProperty("workbenchJob").ValueKind==JsonValueKind.Null))
    {
        var old=item.GetProperty("run");var key=old.GetProperty("key").GetString()!;var id=old.GetProperty("directory").GetString()!;
        var snapshot=item.GetProperty("snapshot").GetString()!;var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
        var a=item.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
        var lens=item.GetProperty("lens").GetString()!;var source=Path.Combine(baseline,"sources",lens);
        var entry=AnalysisComparisonRegistry.Get(key);var primary=optic.Wavelengths.ToList().FindIndex(w=>w.IsPrimary)+1;
        var r=new CanonicalAnalysisRequest{CanonicalAnalysisKey=key,Configuration=item.GetProperty("configuration").GetInt32(),Field=a.Field,Wavelength=a.Wavelength,
            PupilSampling=a.PupilSampling,ImageSampling=a.ImageSampling,GridSize=a.ImageSampling,RayCount=a.RayCount,
            MaximumFrequency=a.MaximumFrequency,ImageDeltaMicrometers=a.ImageDeltaMicrometers,Apodization=JsonSerializer.Serialize(optic.ToSnapshot().Apodization,JsonFiles.Options),
            UseRayAiming=optic.RayAimingEnabled,WavelengthCount=optic.Wavelengths.Count,FieldDefinition=optic.FieldDefinition.ToString(),SurfaceCount=optic.SurfaceGroup.Items.Count,
            FieldCount=optic.Fields.Count,DefinedFields=optic.Fields.Select(f=>new[]{f.X,f.Y}).ToArray(),PrimaryWavelengthMicrometers=optic.Wavelengths[primary-1].Micrometers,
            MaximumFieldRadius=optic.Fields.Max(f=>Math.Sqrt(f.X*f.X+f.Y*f.Y)),WorkbenchSettings=new WorkbenchRuntime(optic).MergeAnalysisSettings(key,a.WorkbenchSettings)};
        r=AfocalAnalysisContracts.Configure(r,true,File.ReadAllText(source));r=ExtendedAnalysisContracts.Configure(entry,r,primary);
        r=ExtendedAnalysisContracts.ConfigureRmsFieldSampling(r,a);
        // Automatic PSF spacing is requested on both sides. Positive focal
        // micrometer spacings must never be silently reinterpreted as angles.
        if(key is "PSF" or "Huygens PSF" or "Huygens MTF" or "Huygens PSF Cross Section" or "Huygens Through Focus MTF")
        {
            r=r with{ImageDeltaMicrometers=0};
            if(r.ZemaxSettings.ContainsKey("ImageDelta"))r.ZemaxSettings["ImageDelta"]=0d;
        }
        if(key is "Huygens MTF" or "Huygens Through Focus MTF")r=r with{MaximumFrequency=1.7188733853924696};
        r=r with{WorkbenchSettings=AnalysisComparisonRegistry.MapWorkbench(entry,r,a.VignettedPupil)};
        if(key=="Huygens Through Focus MTF")r.ZemaxSettings["Frequency"]=r.MaximumFrequency*PhysicalNormalization.UnitScale("CyclesPerMilliradian",AfocalAnalysisContracts.FrequencyUnit(r));
        var directory=Full(id);Directory.CreateDirectory(Path.Combine(directory,"workbench"));Directory.CreateDirectory(Path.Combine(directory,"native"));
        var row=new AuditRow(id,lens,r.Configuration,key,snapshot,source,r,old.GetProperty("reason").GetString()!);rows.Add(row);
        JsonFiles.Write(Path.Combine(directory,"workbench/request.json"),new WorkbenchJob(snapshot,Path.Combine(directory,"workbench"),r));
        var payload=JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(r,JsonFiles.Options),JsonFiles.Options)!;
        payload["zosApiPath"]=ZemaxExecutor.Locate(null);payload["input"]=source;payload["zemaxVersion"]="2026 R1";payload["adapter"]=entry.ZemaxSettingsMapper;
        payload["analysisType"]=entry.ZemaxAnalysisType;payload["configuration"]=r.Configuration;payload["captureScreenshots"]=false;
        if(key=="Huygens MTF")payload["maximumFrequency"]=r.MaximumFrequency*PhysicalNormalization.UnitScale("CyclesPerMilliradian",AfocalAnalysisContracts.FrequencyUnit(r));
        var requestPath=Path.Combine(directory,"native/request.json");JsonFiles.Write(requestPath,payload);
        nativeJobs.Add(new{id,request=requestPath,output=Path.Combine(directory,"native")});
    }
    if(rows.Count!=120)throw new InvalidDataException("Expected all 120 formerly blocked entries");
    JsonFiles.Write(Full("items.json"),rows);JsonFiles.Write(Full("native-jobs.json"),nativeJobs);
    var policy=ComparisonConfiguration.Load(Path.Combine(baseline,"comparison-settings.json"));
    JsonFiles.Write(Full("declared-policy.json"),new{fixedBeforeNativeCapture=true,originalGlobalPolicyHash=JsonFiles.Hash(File.ReadAllBytes("tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json")),
        angularRadius=policy.Analyses["Single Ray Trace"].Quantities["IncidentAngle"] with{Absolute=policy.Analyses["Single Ray Trace"].Quantities["IncidentAngle"].Absolute*Math.PI/180*1000},
        angularRadiusUnit="Milliradian",wavefront=policy.Analyses["Wavefront"].Quantities,psf=policy.Analyses["PSF"].Quantities,
        semantics="Diagnostic afocal audit of every formerly blocked request. Typed quantity/unit/readback mismatches remain Incomparable. No length-to-angle substitution, fitted scale, grid registration, changed mask, or tolerance relaxation."});
    Console.WriteLine($"Prepared {rows.Count} additional native and Workbench requests");return;
}
if(mode=="controls")
{
    if(File.Exists(Full("core-summary.json")))throw new IOException("Fresh control comparison required");
    var results=new List<object>();var models=JsonFiles.Read<JsonElement>(Path.Combine(baseline,"models.json"));
    var policy=ComparisonConfiguration.Load(Path.Combine(baseline,"comparison-settings.json"));
    foreach(var job in JsonFiles.Read<JsonElement>(Full("native-jobs.json")).EnumerateArray())
    {
        var id=job.GetProperty("id").GetString()!;var directory=job.GetProperty("output").GetString()!;
        var nativeRequest=JsonFiles.Read<JsonElement>(Path.Combine(directory,"request.json"));var source=nativeRequest.GetProperty("input").GetString()!;
        var configuration=nativeRequest.GetProperty("configuration").GetInt32();
        var model=models.EnumerateArray().Single(m=>Path.GetFileName(m.GetProperty("id").GetString()!).Equals(Path.GetFileName(source),StringComparison.OrdinalIgnoreCase)&&m.GetProperty("configuration").GetInt32()==configuration);
        var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(model.GetProperty("snapshot").GetString()!));
        if(id.StartsWith("opd-",StringComparison.Ordinal))
        {
            var native=JsonFiles.Read<JsonElement>(Path.Combine(directory,"point-reference.json"));
            var inputs=native.GetProperty("inputs").EnumerateArray().ToArray();var wave=native.GetProperty("wavelength").GetInt32();
            var expected=native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
            var generated=WavefrontEngine.GenerateChiefRaySamples(optic,(inputs[0].GetProperty("hx").GetDouble(),inputs[0].GetProperty("hy").GetDouble()),optic.Wavelengths[wave-1],
                inputs.Select(p=>(p.GetProperty("px").GetDouble(),p.GetProperty("py").GetDouble())).ToArray(),aimAtStop:optic.RayAimingEnabled);
            var gate=policy.Analyses["Optical Path Difference"].Quantities["WavefrontError"];
            var points=generated.Samples.Select((s,i)=>new{input=inputs[i],s.OpdWaves,native=expected[i].GetProperty("opdWaves").GetDouble(),nativeError=expected[i].GetProperty("error").GetInt32(),nativeVignette=expected[i].GetProperty("vignette").GetInt32(),s.Intensity,
                delta=Math.Abs(s.OpdWaves-expected[i].GetProperty("opdWaves").GetDouble()),limit=gate.Absolute+gate.Relative*Math.Abs(expected[i].GetProperty("opdWaves").GetDouble())}).ToArray();
            var pass=points.All(p=>p.nativeError==0&&p.nativeVignette==0&&p.Intensity>0&&p.delta<=p.limit);
            JsonFiles.Write(Path.Combine(directory,"core-points.json"),points);results.Add(new{id,key="Explicit OPD",conclusion=pass?"Pass":"Difference",count=points.Length,maxAbsolute=points.Max(p=>p.delta)});
        }
        else
        {
            var r=JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(directory,"canonical-request.json"));var data=Path.Combine(directory,"core-data.json");
            JsonFiles.Write(data,new WorkbenchRuntime(optic).BuildAnalysisData("MTF",r.WorkbenchSettings));
            var entry=AnalysisComparisonRegistry.Get("MTF");var run=new AnalysisRun{Key="MTF",Directory=id,Request=r,WorkbenchStatus=CaptureStatus.Captured,ZemaxStatus=CaptureStatus.Captured};
            ComparisonRunner.Compare(run,entry,policy.Analyses["MTF"],ResultNormalizer.Workbench(data,entry,r),ResultNormalizer.Zemax(Path.Combine(directory,"data.json"),entry,r),directory);
            JsonFiles.Write(Path.Combine(directory,"comparison.json"),run);results.Add(new{id,key="MTF",conclusion=run.Conclusion.ToString(),run.Reason,run.Metrics});
        }
    }
    JsonFiles.Write(Full("core-summary.json"),results);JsonFiles.Write(Full("core-identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location))});return;
}
var auditRows=JsonFiles.Read<List<AuditRow>>(Full("items.json"));
if(mode=="workbench")
{
    var results=new System.Collections.Concurrent.ConcurrentBag<object>();
    await Parallel.ForEachAsync(auditRows,new ParallelOptions{MaxDegreeOfParallelism=2},async(row,ct)=>
    {
        var output=Full(row.Id+"/workbench");var result=await ProcessIsolation.Run("dotnet",[Path.Combine(Path.GetDirectoryName(baseline)!,"runners-v4/SequentialCorpus20261019/OpticalSystemDesign.ZemaxComparison.dll"),"--workbench-worker",Path.Combine(output,"request.json")],Environment.CurrentDirectory,180,ct);
        File.WriteAllText(Path.Combine(output,"process.log"),result.StandardOutput+result.StandardError);JsonFiles.Write(Path.Combine(output,"worker-result.json"),result);
        results.Add(new{row.Id,result.ExitCode,result.TimedOut});Console.WriteLine($"WB {row.Id}: {result.ExitCode}");
    });
    JsonFiles.Write(Full("workbench-results.json"),results);return;
}
if(mode.StartsWith("compare",StringComparison.Ordinal))
{
    var reportRoot=Full(mode);if(Directory.Exists(reportRoot))throw new IOException("Fresh comparison report required");Directory.CreateDirectory(reportRoot);
    var results=new List<object>();var policy=ComparisonConfiguration.Load(Path.Combine(baseline,"comparison-settings.json"));
    var angularRadius=JsonFiles.Read<JsonElement>(Full("declared-policy.json")).GetProperty("angularRadius").Deserialize<Tolerances>(JsonFiles.Options)!;
    foreach(var row in auditRows)
    {
        var directory=Full(row.Id);var run=new AnalysisRun{Key=row.Key,Directory=row.Id,Request=row.Request};
        string? error=null;
        try
        {
            var native=Path.Combine(directory,"native/data.json");var wb=Path.Combine(directory,"workbench/data.json");
            if(!File.Exists(native)||File.Exists(Path.Combine(directory,"native/error.json")))throw new InvalidDataException("Native calculation failed; see the raw native error/capture");
            if(!File.Exists(wb))throw new InvalidDataException("Workbench calculation failed or unsupported; see worker output");
            var entry=AnalysisComparisonRegistry.Get(row.Key);
            var decoded=ScopedAfocalNormalizer.Read(wb,native,entry,row.Request,policy.Analyses[row.Key],angularRadius);
            run.WorkbenchStatus=CaptureStatus.Captured;run.ZemaxStatus=CaptureStatus.Captured;
            ComparisonRunner.Compare(run,entry,decoded.Settings,decoded.Actual,decoded.Expected,Path.Combine(reportRoot,row.Id));
        }
        catch(Exception e){error=e.ToString();run.Conclusion=Conclusion.Incomparable;run.Reason=e.Message;}
        JsonFiles.Write(Path.Combine(reportRoot,row.Id,"comparison.json"),run);
        results.Add(new{row.Id,row.Lens,row.Configuration,row.Key,conclusion=run.Conclusion.ToString(),run.Reason,run.Metrics,error});
    }
    JsonFiles.Write(Path.Combine(reportRoot,"summary.json"),results);return;
}
throw new ArgumentException("Unknown mode");

internal sealed record AuditRow(string Id,string Lens,int Configuration,string Key,string Snapshot,string Source,CanonicalAnalysisRequest Request,string PreviousBlockReason);
