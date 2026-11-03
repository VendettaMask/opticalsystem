using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

if (args.Length < 3) throw new ArgumentException("probe|capture|compare <repo> <fresh-output> [plan]");
var repo = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[2]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Fresh output required");
Directory.CreateDirectory(root);
string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexStringLower(SHA256.HashData(s)); }
var core = Hash(typeof(Optic).Assembly.Location);
if(args[0]=="models") { await ModelProbes.Run(repo,root,core);return; }
var rows = JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
var results = new List<object>();
FileStream? lease = null; ZemaxExecutor? native = null;
if (args[0] == "capture")
{
    lease = new FileStream(Path.Combine(repo,"artifacts/validation/.background-small-native-controls.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null),root,CancellationToken.None);
}
try
{
    var plan = args.Length > 3 ? JsonFiles.Read<Job[]>(args[3]) : new[] {107,108,125,1376}.Select(i=>new Job(i,0)).ToArray();
    foreach (var spec in plan)
    {
        var row = rows.Single(r=>r.Index==spec.Index); var entry = AnalysisComparisonRegistry.Get(row.Key);
        var source = Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens); var sourceSha = Hash(source);
        var optic = (await WorkbenchExecutor.Import(source)).Configurations[row.Configuration-1];
        var folder = Path.Combine(root,$"{spec.Index}-{spec.Sampling}"); Directory.CreateDirectory(folder);
        var snapshot=Path.Combine(folder,"source.optic.json"); JsonFiles.Write(snapshot,optic.ToSnapshot());
        var original = row.Run.Request!; var wb = new Dictionary<string,string>(original.WorkbenchSettings);
        var zs = new Dictionary<string,object>(original.ZemaxSettings);
        var n = spec.Sampling == 0 ? original.PupilSampling : spec.Sampling;
        var image = original.ImageSampling;
        if (spec.Sampling > 0)
        {
            foreach(var key in new[]{"Sampling","PupilSampling","NumRays"}) if(wb.ContainsKey(key)) wb[key]=n.ToString();
            if(row.Key=="PSF") { zs["PupilSampleSize"]=$"S_{n}x{n}"; zs["ImageSampleSize"]=$"S_{image}x{image}"; }
            else if(row.Key=="FFT PSF Cross Section") { zs["SampleSize"]=$"S_{n}x{n}"; }
            else if(zs.ContainsKey("SampleSize")) zs["SampleSize"]=$"S_{n}x{n}";
        }
        var request=original with {PupilSampling=n,SurfaceCount=optic.SurfaceGroup.Items.Count,WorkbenchSettings=wb,ZemaxSettings=zs};
        if(args[0]=="compare" && spec.CoreRequest is not null) request=JsonFiles.Read<WorkbenchJob>(spec.CoreRequest).Request;
        var job=new WorkbenchJob(snapshot,Path.Combine(folder,"core"),request);
        JsonFiles.Write(Path.Combine(folder,"request.json"),job);
        if(!spec.NativeOnly) WorkbenchExecutor.Execute(job);
        var nativeFolder = spec.Native ?? Path.Combine(row.LegacyPath,"native");
        if(native is not null)
        {
            var engines=Process.GetProcessesByName("OpticStudio"); var count=engines.Length; foreach(var p in engines)p.Dispose();
            if(count>1) throw new InvalidOperationException("Independent native slot unavailable");
            nativeFolder=Path.Combine(folder,"native");
            var execution=await native.Capture(source,nativeFolder,"2026 R1",entry.ZemaxSettingsMapper,entry.ZemaxAnalysisType,request,row.Configuration,120,false,CancellationToken.None);
            JsonFiles.Write(Path.Combine(nativeFolder,"execution.json"),execution);
            if(execution.ExitCode!=0 || execution.TimedOut)
            {
                results.Add(new{row.Index,row.Key,sampling=n,kind="native-capture",conclusion=Conclusion.Incomparable,execution,nativeFolder,sourceSha256=sourceSha,coreSha256=core});
                JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{spec.Index}/{n}: native capture failed");continue;
            }
        }
        if(spec.NativeOnly)
        {
            if(Hash(source)!=sourceSha || Hash(typeof(Optic).Assembly.Location)!=core) throw new InvalidDataException("Source/Core changed");
            var verdict=Conclusion.Pass; string? reason=null;
            try{NativeReferenceDiagnostics.Validate(JsonFiles.Read<JsonElement>(Path.Combine(nativeFolder,"data.json")));}
            catch(InvalidDataException e){verdict=Conclusion.Incomparable;reason=e.Message;}
            results.Add(new{row.Index,row.Key,sampling=n,kind="native-validity",conclusion=verdict,reason,nativeFolder,sourceSha256=sourceSha,coreSha256=core});
            JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{row.Index}/{n} native-validity: {verdict}");continue;
        }
        Compare("same",Path.Combine(job.OutputDirectory,"data.json"),false,request,Path.Combine(nativeFolder,"data.json"),true,request);
        if(spec.Reference is not null)
        {
            var rr=JsonFiles.Read<JsonElement>(Path.Combine(spec.Reference,"captured-settings.json")).GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            Compare("native-convergence",Path.Combine(spec.Reference,"data.json"),true,rr,Path.Combine(nativeFolder,"data.json"),true,request);
            Compare("core-reference",Path.Combine(job.OutputDirectory,"data.json"),false,request,Path.Combine(spec.Reference,"data.json"),true,rr);
        }
        if(spec.CoreReference is not null)
        {
            var prior=JsonFiles.Read<WorkbenchJob>(Path.Combine(spec.CoreReference,"request.json"));
            var fingerprint=JsonFiles.Read<JsonElement>(Path.Combine(prior.OutputDirectory,"provenance.json")).GetProperty("codeFingerprint").GetString()!;
            if(fingerprint.Split(':')[^1]!=core)throw new InvalidDataException("Core convergence requires the same Core binary");
            Compare("core-convergence",Path.Combine(prior.OutputDirectory,"data.json"),false,prior.Request,Path.Combine(job.OutputDirectory,"data.json"),false,request);
        }
        if(args[0]=="probe" && row.Index==1376)
        {
            foreach(var delta in new[]{.01,.001,.0001,.00001,.000001})
            {
                var data=new FieldCurvatureAndDistortionAnalysis(optic,numPoints:101,parabasalDelta:delta,wavelengthNumber:1,parabasalRayPair:ParabasalRayPair.ChiefAndPositive).GenerateData();
                JsonFiles.Write(Path.Combine(folder,$"field-{delta:G}.json"),data);
            }
            var pencils=new List<object>();
            foreach(var hy in new[]{0d,.5,1}) foreach(var delta in new[]{.001,.00001})
            {
                var rays=new List<object>();
                foreach(var p in new[]{(0d,0d),(delta,0d),(0d,delta)})
                {
                    var s=optic.TraceGenericFinalSample(0,hy,p.Item1,p.Item2,.5)!;
                    var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
                    rays.Add(new{pupil=p,position=frame.ToLocalPoint(s.Position),direction=frame.ToLocalDirection(s.Direction)});
                }
                pencils.Add(new{hy,delta,rays});
            }
            JsonFiles.Write(Path.Combine(folder,"pencils.json"),pencils);
        }
        if(Hash(source)!=sourceSha || Hash(typeof(Optic).Assembly.Location)!=core) throw new InvalidDataException("Source/Core changed");
        void Compare(string kind,string a,bool na,CanonicalAnalysisRequest ra,string b,bool nb,CanonicalAnalysisRequest rb)
        {
            var output=Path.Combine(folder,kind); var run=new AnalysisRun {Key=row.Key,Request=rb,Directory=output,Support=row.Run.Support,Tolerances=row.Run.Tolerances};
            try { ComparisonRunner.Compare(run,entry,row.Settings,na?ResultNormalizer.Zemax(a,entry,ra):ResultNormalizer.Workbench(a,entry,ra),nb?ResultNormalizer.Zemax(b,entry,rb):ResultNormalizer.Workbench(b,entry,rb),output); }
            catch(InvalidDataException e) {run.Conclusion=Conclusion.Incomparable;run.Reason=e.Message;}
            JsonFiles.Write(Path.Combine(output,"comparison.json"),run);
            results.Add(new {row.Index,row.Lens,row.Configuration,row.Key,sampling=n,kind,run.Conclusion,run.Reason,run.Metrics,coreSha256=core,sourceSha256=sourceSha,nativeFolder,a,b,requestA=ra.Fingerprint,requestB=rb.Fingerprint});
            JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{row.Index}/{n} {kind}: {run.Conclusion}");
        }
    }
    JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=core,completedUtc=DateTimeOffset.UtcNow,nativeExecuted=native is not null,originalTolerances=true});
}
finally {lease?.Dispose();}

internal sealed record Job(int Index,int Sampling,string? Native=null,string? Reference=null,string? CoreRequest=null,bool NativeOnly=false,string? CoreReference=null);
internal sealed record Row(int Index,string Lens,int Configuration,string Key,string LegacyPath,AnalysisConfiguration Settings,AnalysisRun Run);
