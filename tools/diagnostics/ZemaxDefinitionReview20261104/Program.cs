using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;

var repo=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples/Image Simulation/Example 2, Double Gauss Experimental Arrangement.ZMX");
var sourceHash=JsonFiles.Hash(File.ReadAllBytes(source));
var baseline=OpticalFormatCatalog.Import(File.ReadAllText(source),".zmx");
var snapshot=JsonSerializer.SerializeToUtf8Bytes(baseline.ToSnapshot(),JsonFiles.Options);
var waves=new List<object>();
var pupils=new[]{(0d,0d),(0d,.3),(0d,.8),(-.7,0d)};
foreach(var mode in Enum.GetValues<ReferenceOpdMode>())
{
 var optic=Optic.FromSnapshot(baseline.ToSnapshot());optic.ReferenceOpd=mode;
 var wave=optic.Wavelengths.First(w=>w.IsPrimary);
 var result=WavefrontEngine.GenerateChiefRaySamples(optic,(0,1),wave,pupils,aimAtStop:optic.RayAimingEnabled);
 var chief=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,0,0,wave.Micrometers,optic.RayAimingEnabled)).Single()!;
 var rows=new List<object>();
 for(var i=0;i<pupils.Length;i++)
 {
  var (x,y)=pupils[i];
  var ray=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,x,y,wave.Micrometers,optic.RayAimingEnabled)).Single()!;
  // Independent diagnostic of the documented focal Absolute reference only:
  // shared-tracer phase-inclusive OPL at the actual image, no sphere correction.
  var absolute=((chief.PhaseInclusiveOpticalPathLength??chief.CumulativeOpticalPathLength)
   -(ray.PhaseInclusiveOpticalPathLength??ray.CumulativeOpticalPathLength))/(wave.Micrometers*1e-3);
  rows.Add(new{x,y,actual=result.Samples[i].OpdWaves,documentedAbsoluteFromImageOpl=absolute,
   errorAgainstAbsolute=result.Samples[i].OpdWaves-absolute});
 }
 var data=new WorkbenchRuntime(optic).BuildAnalysisData("Optical Path Difference",new Dictionary<string,string>{{"NumberOfRays","20"},{"FieldNumber","3"},{"WavelengthNumber","2"},{"SurfaceNumber","-1"}});
 JsonFiles.Write(Path.Combine(output,"reference-"+mode+".json"),data);
 waves.Add(new{mode,rows,curveHash=Curves(data)});
}
JsonFiles.Write(Path.Combine(output,"reference-modes.json"),waves);
var surfaces=new List<object>();
foreach(var analysis in new[]{"Optical Path Difference","Wavefront","PSF","MTF"})
foreach(var surface in new[]{-1,2,12,999})
{
 var optic=Optic.FromSnapshot(baseline.ToSnapshot());
 var settings=new Dictionary<string,string>{{"Sampling","32"},{"NumberOfRays","20"},{"FieldNumber","1"},{"WavelengthNumber","2"},{"SurfaceNumber",surface.ToString()}};
 AnalysisData data;
 try{data=new WorkbenchRuntime(optic).BuildAnalysisData(analysis,settings);}
 catch(Exception error){surfaces.Add(new{analysis,requestedSurface=surface,outcome="Thrown",error=error.Message});continue;}
 JsonFiles.Write(Path.Combine(output,analysis.Replace(' ','-')+"-surface-"+surface+".json"),data);
 surfaces.Add(new{analysis,requestedSurface=surface,data.Outcome,data.OutcomeReason,
  reportedSurface=data.Values.GetValueOrDefault("SurfaceNumber"),curveHash=Curves(data),
  pointCount=data.PlotPanes is {Count:>0}?data.PlotPanes.Sum(p=>p.Series.Sum(s=>s.Points.Count)):data.PlotSeries.Sum(s=>s.Points.Count)});
}
JsonFiles.Write(Path.Combine(output,"surfaces.json"),surfaces);
if(!snapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(baseline.ToSnapshot(),JsonFiles.Options)))throw new InvalidOperationException("Source optic mutated");
if(sourceHash!=JsonFiles.Hash(File.ReadAllBytes(source)))throw new InvalidOperationException("Source file changed");
JsonFiles.Write(Path.Combine(output,"identity.json"),new{source,sourceSha256=sourceHash,
 coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
 scope="Documentation/code review and independent diagnostic controls; no new native captures or product fixes",
 sourcePrescriptionChanged=false,originalComparisonsChanged=false,productCodeChanged=false});
Console.WriteLine(JsonSerializer.Serialize(new{waves,surfaces},JsonFiles.Options));

static string Curves(AnalysisData data)
{
 var series=data.PlotPanes is {Count:>0}?data.PlotPanes.SelectMany(p=>p.Series).ToArray():data.PlotSeries.ToArray();
 return JsonFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(series.Select(s=>new{s.Kind,s.XQuantity,s.XUnit,s.YQuantity,s.YUnit,s.ValueQuantity,s.ValueUnit,s.Points}),JsonFiles.Options));
}
