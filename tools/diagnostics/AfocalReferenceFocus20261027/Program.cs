using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if(args.Length!=3)throw new ArgumentException("prepare|workbench|compare <audit root> <new run>");
var mode=args[0];var audit=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
if(mode=="prepare-existing")
{
    // Reuse the exact prior requests and original reference paths. This mode
    // creates only new worker jobs and identity records, never native arrays.
    if(Directory.Exists(output))throw new IOException("Fresh recheck output required");
    var source=Path.Combine(audit,"items.json");var prior=JsonFiles.Read<List<Row>>(source);
    if(prior.Count==0||prior.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=prior.Count)
        throw new InvalidDataException("Empty or repeated recheck identities");
    var sources=new List<object>();
    foreach(var row in prior)
    {
        if(Path.GetFileName(row.Id)!=row.Id||row.Id is "." or "..")throw new InvalidDataException("Invalid recheck ID");
        if(!File.Exists(row.Snapshot)||!Directory.Exists(row.Native))throw new FileNotFoundException("Missing frozen input/reference");
        sources.Add(new{row.Id,requestFingerprint=row.Request.Fingerprint,snapshot=row.Snapshot,
            snapshotSha256=JsonFiles.Hash(File.ReadAllBytes(row.Snapshot)),native=row.Native,
            files=Directory.EnumerateFiles(row.Native).Order(StringComparer.Ordinal).Select(path=>new{
                path,sha256=JsonFiles.Hash(File.ReadAllBytes(path))}).ToArray()});
    }
    Directory.CreateDirectory(output);
    foreach(var row in prior)
    {
        var wb=Path.Combine(output,row.Id,"workbench");Directory.CreateDirectory(wb);
        JsonFiles.Write(Path.Combine(wb,"request.json"),new WorkbenchJob(row.Snapshot,wb,row.Request));
    }
    JsonFiles.Write(Path.Combine(output,"items.json"),prior);
    JsonFiles.Write(Path.Combine(output,"input-identity.json"),new{source,sourceSha256=JsonFiles.Hash(File.ReadAllBytes(source)),
        count=prior.Count,inputs=sources});
    JsonFiles.Write(Path.Combine(output,"identity.json"),new{
        coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
        applicationSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchRuntime).Assembly.Location))});
    Console.WriteLine($"Prepared {prior.Count} unchanged requests with shared frozen references");return;
}
if(mode=="focus-probe"){ ReferenceFocusProbe.Focus(audit,output);return; }
if(mode=="reference-probe"){ ReferenceFocusProbe.Reference(audit,output);return; }
if(mode=="focal-guards"){ FocalGuards.Run(output); return; }
if(mode=="gee-centroids")
{
    if(Directory.Exists(output))throw new IOException("Fresh centroid probe required");Directory.CreateDirectory(output);
    var rows=new List<object>();
    foreach(var model in JsonFiles.Read<JsonElement>(Path.Combine(audit,"baseline-v4/models.json")).EnumerateArray())
    {
        var optic=Optic.FromSnapshot(JsonFiles.Read<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(model.GetProperty("snapshot").GetString()!));
        var max=optic.Fields.Max(f=>Math.Sqrt(f.X*f.X+f.Y*f.Y));var primary=optic.Wavelengths.FirstOrDefault(w=>w.IsPrimary)??optic.Wavelengths[0];
        foreach(var density in new[]{32,33,64,65,128,129})
        {
        var result=new SpotDiagramAnalysis(optic,new SpotDiagramSettings(RayDensity:density,Pattern:"uniform",Reference:"absolute")).GenerateData();
        for(var field=0;field<optic.Fields.Count;field++)
        {
            var f=optic.Fields[field];var chief=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(max==0?0:f.X/max,max==0?0:f.Y/max,0,0,primary.Micrometers,optic.RayAimingEnabled))[0]!;
            var direction=optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(chief.Direction);var angle=Math.Atan2(direction.Y,direction.Z)*1000;
            var means=result.PlotPanes![field].Series.Select(s=>s.Points.Average(p=>p.Y)).ToArray();
            var chiefs=optic.Wavelengths.Select(w=>{
                var c=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(max==0?0:f.X/max,max==0?0:f.Y/max,0,0,w.Micrometers,optic.RayAimingEnabled))[0]!;
                var d=optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(c.Direction);return Math.Atan2(d.Y,d.Z)*1000;
            }).ToArray();
            rows.Add(new{id=model.GetProperty("id").GetString(),configuration=model.GetProperty("configuration").GetInt32(),density,field=field+1,chiefAngle=angle,chiefs,means,polychromaticOffset=means.Average()-angle});
        }
        }
    }
    JsonFiles.Write(Path.Combine(output,"summary.json"),rows);return;
}
if(mode=="gee-probe")
{
    if(Directory.Exists(output))throw new IOException("Fresh GEE probe required");Directory.CreateDirectory(output);
    var results=new List<object>();var policy=ComparisonConfiguration.Load(Path.Combine(audit,"baseline-v4/comparison-settings.json"));
    foreach(var item in JsonFiles.Read<JsonElement>(Path.Combine(audit,"expanded/items.json")).EnumerateArray().Where(i=>i.GetProperty("key").GetString()=="Encircled Energy"))
    foreach(var variant in new[]{"centroid","chief","no-aim","no-vignette"})
    {
        var optic=Optic.FromSnapshot(JsonFiles.Read<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
        if(variant=="no-aim")optic.RayAimingEnabled=false;
        if(variant=="no-vignette")foreach(var f in optic.Fields){f.VignetteFactorX=0;f.VignetteFactorY=0;f.VignetteDecenterX=0;f.VignetteDecenterY=0;f.VignetteAngleDegrees=0;}
        var r=item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;var radius=((JsonElement)r.ZemaxSettings["RadiusMaximum"]).GetDouble()*PhysicalNormalization.UnitScale(r.NativeAfocalAngleUnit!,"Milliradian");
        var data=new EncircledEnergyAnalysis(optic,32,"uniform",401,r.Wavelength,variant=="chief"?"chief":"centroid",radius,false){ZemaxCompatibleOutput=true}.GenerateData();
        var directory=Path.Combine(output,item.GetProperty("id").GetString()!+"-"+variant);Directory.CreateDirectory(directory);var path=Path.Combine(directory,"data.json");JsonFiles.Write(path,data);
        var entry=AnalysisComparisonRegistry.Get("Encircled Energy");var run=new AnalysisRun{Key="Encircled Energy",Directory=Path.GetFileName(directory),Request=r};
        ComparisonRunner.Compare(run,entry,policy.Analyses["Encircled Energy"],ExtendedResultNormalizer.Workbench(JsonFiles.Read<AnalysisData>(path),r),ExtendedResultNormalizer.Zemax(Path.Combine(audit,"expanded",item.GetProperty("id").GetString()!,"native/data.json"),r),directory);
        results.Add(new{lens=item.GetProperty("lens").GetString(),variant,conclusion=run.Conclusion.ToString(),run.Metrics});
    }
    JsonFiles.Write(Path.Combine(output,"summary.json"),results);return;
}
if(mode=="probe")
{
    if(Directory.Exists(output))throw new IOException("Fresh probe output required");Directory.CreateDirectory(output);
    var results=new List<object>();
    foreach(var model in JsonFiles.Read<JsonElement>(Path.Combine(audit,"baseline-v4/models.json")).EnumerateArray())
    {
        var optic=Optic.FromSnapshot(JsonFiles.Read<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(model.GetProperty("snapshot").GetString()!));
        var wave=optic.Wavelengths.FirstOrDefault(w=>w.IsPrimary)??optic.Wavelengths[0];
        var first=optic.SurfaceGroup.Items[1].CoordinateSystem;var last=optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var traces=new List<object>();
        foreach(var delta in new[]{.01,.001,.0001})
        {
            var rays=new[]{new OpticalSystemDesign.Core.Backend.Vector3D(0,-delta,0),new OpticalSystemDesign.Core.Backend.Vector3D(0,delta,0)}
                .Select(p=>new OpticalSystemDesign.Core.Rays.RealRay(first.ToGlobalPoint(p-new OpticalSystemDesign.Core.Backend.Vector3D(0,0,.001)),first.ToGlobalDirection(new(0,0,1)),wave.Nanometers));
            var history=optic.SequentialRayTracer.Trace(new OpticalSystemDesign.Core.Rays.RealRayBundle(rays)).RayHistories;
            var directions=history.Select(h=>last.ToLocalDirection(h.Last().Direction)).ToArray();
            var power=(directions[1].Y/directions[1].Z-directions[0].Y/directions[0].Z)/(2*delta);
            traces.Add(new{delta,efl=-1/power,histories=history.Select(h=>new{last=h.Last().SurfaceNumber,h.Last().Vignetted}).ToArray()});
        }
        var spot=new List<object>();
        foreach(var step in new[]{.02,.01,.005})
        {
            var rays=Enumerable.Range(1,4).Select(i=>new OpticalSystemDesign.Core.Rays.RealRay(first.ToGlobalPoint(new(0,i*step,-.001)),first.ToGlobalDirection(new(0,0,1)),wave.Nanometers));
            var directions=optic.SequentialRayTracer.TraceFinalSamples(new OpticalSystemDesign.Core.Rays.RealRayBundle(rays)).Select(s=>last.ToLocalDirection(s!.Direction)).Select(d=>d.Y/d.Z).ToArray();
            var c=(-13d/3*directions[0]+19d/2*directions[1]-7*directions[2]+11d/6*directions[3])/step;
            traces.Add(new{step,forwardEfl=-1/c});
        }
        if(model.GetProperty("id").GetString()!.Contains("Beam"))
        foreach(var factor in new[]{0d,1d,2d})foreach(var density in new[]{1,6,20,40})
        {
            optic.Apodization=new OpticalSystemDesign.Core.Apodization.ZemaxApodization(OpticalSystemDesign.Core.Apodization.ZemaxApodizationType.Gaussian,factor);
            var data=new SpotDiagramAnalysis(optic,new SpotDiagramSettings(RayDensity:density,FieldNumber:1,WavelengthNumber:1,Reference:"chief")).GenerateData();
            var samples=OpticalSystemDesign.Core.Raytrace.ApertureSampler.GenerateHexapolarRings(Math.Max(2,density)).Select(p=>{
                var r2=p.X*p.X+p.Y*p.Y;var mapped=factor==0||r2==0?r2:-Math.Log(1-r2*(1-Math.Exp(-2*factor)))/(2*factor);
                var scale=r2==0?1:Math.Sqrt(mapped/r2);var weight=Math.Exp(2*factor*mapped);
                return new OpticalSystemDesign.Core.Raytrace.PupilSample(p.X*scale,p.Y*scale,weight);
            }).ToArray();
            var bundle=optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,0,wave.Micrometers,samples,false);
            var rays=optic.SequentialRayTracer.TraceFinalSamples(bundle).Select(s=>s!).ToArray();
            var rms=Math.Sqrt(rays.Average(s=>{var d=last.ToLocalDirection(s.Direction);return Math.Pow(Math.Atan2(d.X,d.Z)*1000,2)+Math.Pow(Math.Atan2(d.Y,d.Z)*1000,2);}));
            spot.Add(new{factor,density,metrics=data.PlotPanes![0].Metrics,importanceRms=rms});
        }
        results.Add(new{id=model.GetProperty("id").GetString(),configuration=model.GetProperty("configuration").GetInt32(),traces,spot});
    }
    JsonFiles.Write(Path.Combine(output,"probes.json"),results);return;
}
if(mode=="prepare-fixed")
{
    if(Directory.Exists(output))throw new IOException("Fresh fixed-control output required");Directory.CreateDirectory(output);
    var rows=new List<Row>();
    foreach(var item in JsonFiles.Read<JsonElement>(Path.Combine(audit,"fixed-grid-controls/items.json")).EnumerateArray())
    {
        var id=item.GetProperty("id").GetString()!;var r=item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var snapshot=item.GetProperty("snapshot").GetString()!;var wb=Path.Combine(output,id,"workbench");Directory.CreateDirectory(wb);
        JsonFiles.Write(Path.Combine(wb,"request.json"),new WorkbenchJob(snapshot,wb,r));
        rows.Add(new(id,item.GetProperty("lens").GetString()!,r.Configuration,r.CanonicalAnalysisKey,snapshot,Path.Combine(audit,"fixed-grid-controls",id,"native"),r));
    }
    JsonFiles.Write(Path.Combine(output,"items.json"),rows);return;
}
if(mode=="prepare")
{
    if(Directory.Exists(output))throw new IOException("Fresh repair output required");Directory.CreateDirectory(output);
    var expanded=JsonFiles.Read<JsonElement>(Path.Combine(audit,"expanded/items.json")).EnumerateArray().ToDictionary(e=>e.GetProperty("id").GetString()!);
    var rows=new List<Row>();
    foreach(var item in JsonFiles.Read<JsonElement>(Path.Combine(audit,"baseline-v4/analysis-results.json")).EnumerateArray())
    {
        var old=item.GetProperty("run");var id=old.GetProperty("directory").GetString()!;
        var request=expanded.TryGetValue(id,out var e)?e.GetProperty("request"):old.GetProperty("request");
        var r=request.Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        if(r.CanonicalAnalysisKey is "Encircled Energy" or "Diffraction Encircled Energy")
        {
            // Keep the captured native physical radius, decoding its declared
            // AF unit before requesting the corrected Application mrad axis.
            r=r with{WorkbenchSettings=new(r.WorkbenchSettings)};
            var radius=r.ZemaxSettings["RadiusMaximum"] is JsonElement value ? value.GetDouble()
                : Convert.ToDouble(r.ZemaxSettings["RadiusMaximum"],System.Globalization.CultureInfo.InvariantCulture);
            r.WorkbenchSettings["MaximumDistanceMicrometers"]=(radius*PhysicalNormalization.UnitScale(r.NativeAfocalAngleUnit!,"Milliradian")).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        }
        var native=expanded.ContainsKey(id)?Path.Combine(audit,"expanded",id,"native"):Path.Combine(audit,"baseline-v4/analyses",id,"native");
        var snapshot=item.GetProperty("snapshot").GetString()!;var optic=Optic.FromSnapshot(JsonFiles.Read<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(snapshot));
        var wb=Path.Combine(output,id,"workbench");Directory.CreateDirectory(wb);
        JsonFiles.Write(Path.Combine(wb,"request.json"),new WorkbenchJob(snapshot,wb,r));
        rows.Add(new(id,item.GetProperty("lens").GetString()!,r.Configuration,r.CanonicalAnalysisKey,snapshot,native,r));
    }
    JsonFiles.Write(Path.Combine(output,"items.json"),rows);
    JsonFiles.Write(Path.Combine(output,"identity.json"),new{coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),applicationSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchRuntime).Assembly.Location))});return;
}
var items=JsonFiles.Read<List<Row>>(Path.Combine(output,"items.json"));
if(mode=="workbench")
{
    var results=new System.Collections.Concurrent.ConcurrentBag<object>();
    await Parallel.ForEachAsync(items,new ParallelOptions{MaxDegreeOfParallelism=2},async(row,ct)=>{
        var wb=Path.Combine(output,row.Id,"workbench");var result=await ProcessIsolation.Run("dotnet",[typeof(WorkbenchExecutor).Assembly.Location,"--workbench-worker",Path.Combine(wb,"request.json")],Environment.CurrentDirectory,180,ct);
        File.WriteAllText(Path.Combine(wb,"process.log"),result.StandardOutput+result.StandardError);JsonFiles.Write(Path.Combine(wb,"worker-result.json"),result);
        results.Add(new{row.Id,result.ExitCode,result.TimedOut});Console.WriteLine($"WB {row.Id}: {result.ExitCode}");
    });JsonFiles.Write(Path.Combine(output,"workbench-results.json"),results);return;
}
if(mode=="compare")
{
    var policy=ComparisonConfiguration.Load(Path.Combine(audit,"baseline-v4/comparison-settings.json"));var results=new List<object>();
    var angular=JsonFiles.Read<JsonElement>(Path.Combine(audit,"expanded/declared-policy.json")).GetProperty("angularRadius").Deserialize<Tolerances>(JsonFiles.Options)!;
    foreach(var row in items)
    {
        var run=new AnalysisRun{Key=row.Key,Directory=row.Id,Request=row.Request};string? error=null;
        try{
            var wb=Path.Combine(output,row.Id,"workbench/data.json");var native=Path.Combine(row.Native,"data.json");
            if(!File.Exists(wb)||!File.Exists(native)||File.Exists(Path.Combine(row.Native,"error.json")))throw new InvalidDataException("Missing valid calculation; see raw logs");
            var entry=AnalysisComparisonRegistry.Get(row.Key);var decoded=ScopedAfocalNormalizer.Read(wb,native,entry,row.Request,policy.Analyses[row.Key],angular);
            ComparisonRunner.Compare(run,entry,decoded.Settings,decoded.Actual,decoded.Expected,Path.Combine(output,"comparison",row.Id));
        }catch(Exception ex){error=ex.ToString();run.Conclusion=Conclusion.Incomparable;run.Reason=ex.Message;}
        JsonFiles.Write(Path.Combine(output,"comparison",row.Id,"comparison.json"),run);
        results.Add(new{row.Id,row.Lens,row.Configuration,row.Key,conclusion=run.Conclusion.ToString(),run.Reason,run.Metrics,error});
    }JsonFiles.Write(Path.Combine(output,"summary.json"),results);return;
}
throw new ArgumentException("Unknown mode");
internal sealed record Row(string Id,string Lens,int Configuration,string Key,string Snapshot,string Native,CanonicalAnalysisRequest Request);
