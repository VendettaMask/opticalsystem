using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);
if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh output required");
Directory.CreateDirectory(root);
var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples/Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX");
var sourceHash=JsonFiles.Hash(File.ReadAllBytes(source));var optic=OpticalFormatCatalog.Import(File.ReadAllText(source),".zmx");
var snapshot=JsonSerializer.SerializeToUtf8Bytes(optic.ToSnapshot(),JsonFiles.Options);
var core=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location));
var priorRoot=Path.Combine(repo,"validation/rechecks/2026-11-04/zemax-definition-review");
var priorEntries=JsonFiles.Read<JsonElement[]>(Path.Combine(priorRoot,"archive-index.json"));
using var archive=ZipFile.OpenRead(Path.Combine(priorRoot,"evidence.zip"));
var defaults=new List<object>();var surfaces=new List<object>();
foreach(var analysis in new[]{"Optical Path Difference","Wavefront","PSF","MTF"})
foreach(var surface in new[]{-1,2,12,999})
{
    var settings=new Dictionary<string,string>{{"Sampling","32"},{"NumberOfRays","20"},{"FieldNumber","1"},{"WavelengthNumber","2"},{"SurfaceNumber",surface.ToString()}};
    var folder=Path.Combine(root,analysis.Replace(' ','-')+"-surface-"+surface);
    AnalysisData data;
    try{data=new WorkbenchRuntime(optic).BuildAnalysisData(analysis,settings);}
    catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException)
    {surfaces.Add(new{analysis,surface,outcome="Rejected",error=e.Message,coreSha256=core});continue;}
    JsonFiles.Write(folder+".json",data);
    surfaces.Add(new{analysis,surface,data.Outcome,data.OutcomeReason,numericHash=NumericHash(data),coreSha256=core});
    if(surface==-1)
    {
        var member=analysis.Replace(' ','-')+"-surface--1.json";
        var entry=priorEntries.Single(e=>e.GetProperty("logicalPath").GetString()==member);
        using var stream=archive.GetEntry(entry.GetProperty("entry").GetString()!)!.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);
        if(JsonFiles.Hash(bytes.ToArray())!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("Prior control changed");
        var before=JsonSerializer.Deserialize<AnalysisData>(bytes.ToArray(),JsonFiles.Options)!;
        var same=NumericHash(before)==NumericHash(data);if(!same)throw new InvalidDataException("Default numerical output changed: "+analysis);
        defaults.Add(new{analysis,numericHash=NumericHash(data),priorNumericalOutputIdentical=same,priorPayloadSha256=entry.GetProperty("sha256").GetString(),coreSha256=core});
    }
}
JsonFiles.Write(Path.Combine(root,"surfaces.json"),surfaces);JsonFiles.Write(Path.Combine(root,"default-controls.json"),defaults);
var modes=new List<object>();
foreach(var mode in Enum.GetValues<ReferenceOpdMode>())
{
    var copy=optic.Clone();copy.ReferenceOpd=mode;var wave=copy.Wavelengths.First(w=>w.IsPrimary);
    (double X,double Y)[] pupils=[(0,0),(0,.3),(0,.8),(-.7,0)];
    try
    {
        var result=WavefrontEngine.GenerateChiefRaySamples(copy,(0,1),wave,pupils,copy.RayAimingEnabled);
        var chief=copy.SequentialRayTracer.TraceFinalSamples(copy.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,0,0,wave.Micrometers,copy.RayAimingEnabled)).Single()!;
        var rays=copy.SequentialRayTracer.TraceFinalSamples(copy.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0,1,wave.Micrometers,pupils.Select(p=>new PupilSample(p.X,p.Y,1)).ToArray(),copy.RayAimingEnabled));
        var controls=pupils.Select((p,i)=>new{p.X,p.Y,actual=result.Samples[i].OpdWaves,imageOplDefinition=((chief.PhaseInclusiveOpticalPathLength??chief.CumulativeOpticalPathLength)-(rays[i]!.PhaseInclusiveOpticalPathLength??rays[i]!.CumulativeOpticalPathLength))/(wave.Micrometers*.001)}).ToArray();
        modes.Add(new{mode,outcome="Success",controls,coreSha256=core});
    }
    catch(NotSupportedException e){modes.Add(new{mode,outcome="Unsupported",error=e.Message,coreSha256=core});}
}
JsonFiles.Write(Path.Combine(root,"reference-modes.json"),modes);
var maps=new List<object>();
foreach(var wave in new[]{1,2,3})foreach(var primary in new[]{false,true})
{
    var data=new WorkbenchRuntime(optic).BuildAnalysisData("Wavefront",new Dictionary<string,string>{{"Sampling","32"},{"FieldNumber","3"},{"WavelengthNumber",wave.ToString()},{"ReferenceToPrimary",primary.ToString()},{"UseExitPupilShape","False"}});
    JsonFiles.Write(Path.Combine(root,$"wavefront-{wave}-{primary}.json"),data);
    maps.Add(new{wave,primary,numericHash=NumericHash(data),rms=data.Values["RmsWaves"],referenceWavelength=data.Values["ReferenceWavelengthMicrometers"],coreSha256=core});
}
JsonFiles.Write(Path.Combine(root,"primary-reference.json"),maps);
if(!snapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(optic.ToSnapshot(),JsonFiles.Options))||sourceHash!=JsonFiles.Hash(File.ReadAllBytes(source)))throw new InvalidOperationException("Source mutated");
JsonFiles.Write(Path.Combine(root,"identity.json"),new{coreSha256=core,sourceSha256=sourceHash,priorAuthority="validation/rechecks/2026-11-04/zemax-definition-review",sourcePrescriptionChanged=false,nativeCaptured=false,originalTolerancesChanged=false,completedUtc=DateTimeOffset.UtcNow});
Console.WriteLine($"{defaults.Count} default arrays identical; {surfaces.Count} surface requests; {modes.Count} reference modes; {maps.Count} primary-reference maps");

static string NumericHash(AnalysisData data)
{
    var series=data.PlotPanes is {Count:>0}?data.PlotPanes.SelectMany(p=>p.Series).ToArray():data.PlotSeries.ToArray();
    return JsonFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(series.Select(s=>new{s.Kind,s.XQuantity,s.XUnit,s.YQuantity,s.YUnit,s.ValueQuantity,s.ValueUnit,s.Points}),JsonFiles.Options));
}
