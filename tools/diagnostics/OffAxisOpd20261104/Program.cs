using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var repo=Path.GetFullPath(args[0]);var root=Path.GetFullPath(args[1]);
if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("Fresh output required");Directory.CreateDirectory(root);
if(args.Length>2&&args[2]=="native"){await NativeProbe.Run(repo,root,args.Skip(3).ToArray());return;}
if(args.Length>2&&args[2]=="review"){await Review.Run(repo,root);return;}
if(args.Length>2&&args[2]=="sampling-native"){await SamplingAudit.Capture(repo,root,args.Skip(3).ToArray());return;}
if(args.Length>2&&args[2]=="sampling-audit"){SamplingAudit.Run(repo,root,args[3],args.Skip(4).ToArray());return;}
var rows=JsonFiles.Read<Row[]>(Path.Combine(repo,"artifacts/validation/all-example-accuracy-20261031/rows.json"));
var results=new List<object>();
foreach(var index in new[]{600,604,608})
{
 var row=rows.Single(r=>r.Index==index);var source=Path.Combine(repo,"src/OpticalSystemDesign.App/Assets/SequentialExamples",row.Lens);
 var optic=(await WorkbenchExecutor.Import(source)).Configurations[0];var request=row.Run.Request!;
 var field=SpotAnalysisEngine.DefinedFields(optic)[request.Field-1];var wave=optic.Wavelengths[request.Wavelength-1];
 var folder=Path.Combine(root,index.ToString());var data=new WorkbenchRuntime(optic).BuildAnalysisData(row.Key,request.WorkbenchSettings);
 JsonFiles.Write(Path.Combine(folder,"baseline.json"),data);
 var entry=AnalysisComparisonRegistry.Get(row.Key);var baseline=ResultNormalizer.Workbench(Path.Combine(folder,"baseline.json"),entry,request);
 var native=ResultNormalizer.Zemax(Path.Combine(row.LegacyPath,"native/data.json"),entry,request);
 var sphere=WavefrontEngine.CreateChiefRayReferenceSphere(optic,field,wave,optic.RayAimingEnabled);
 var chief=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx,field.Hy,0,0,wave.Micrometers,optic.RayAimingEnabled)).Single()!;
 var distance=optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers);
 var primary=optic.Wavelengths.First(w=>w.IsPrimary);
 var primarySphere=WavefrontEngine.CreateChiefRayReferenceSphere(optic,field,primary,optic.RayAimingEnabled);
 var phaseRays=new List<object>();
 var generator=optic.SequentialRayTracer.RayGenerator;
 var initial=generator.GenerateGeneric(field.Hx,field.Hy,0,0,wave.Micrometers,optic.RayAimingEnabled);
 var chiefHistory=optic.SequentialRayTracer.Trace(initial).RayHistories[0];
 var collimatorChief=chiefHistory.Single(s=>s.SurfaceNumber==1);
 foreach(var (px,py) in new[]{(0d,0d),(.5,0d),(1d,0d),(0d,.5),(0d,1d)})
 {
  var ray=generator.GenerateGeneric(field.Hx,field.Hy,px,py,wave.Micrometers,optic.RayAimingEnabled);
  var history=optic.SequentialRayTracer.Trace(ray).RayHistories[0];var collimator=history.Single(s=>s.SurfaceNumber==1);
  var delta=collimator.Position-collimatorChief.Position;var plane=delta.X*collimatorChief.Direction.X+delta.Y*collimatorChief.Direction.Y+delta.Z*collimatorChief.Direction.Z;
  var actual=(collimator.PhaseInclusiveOpticalPathLength??collimator.CumulativeOpticalPathLength)-(collimatorChief.PhaseInclusiveOpticalPathLength??collimatorChief.CumulativeOpticalPathLength);
  phaseRays.Add(new{px,py,collimator.Position,collimator.Direction,collimator.CumulativeOpticalPathLength,collimator.PhaseInclusiveOpticalPathLength,plane,phaseDifference=actual,planeError=actual-plane});
 }
 JsonFiles.Write(Path.Combine(folder,"collimator-plane.json"),phaseRays);
 var geometric=ParabasalPupilEngine.Compute(optic,primary).ImageAxialDistanceMillimeters;
 var variants=new[]{("baseline",sphere),("chief-plane",sphere with{Radius=Math.Abs(distance/chief.Direction.Z)}),
  ("axial",sphere with{Radius=Math.Abs(distance)}),("primary-pupil",sphere with{Radius=primarySphere.Radius}),
  ("geometric-chief-plane",sphere with{Radius=Math.Abs(geometric/chief.Direction.Z)})};
 foreach(var (name,reference) in variants)
 {
  var coords=Enumerable.Range(0,41).Select(i=>-1+i/20d).ToArray();var pupils=coords.Select(p=>(0d,p)).Concat(coords.Select(p=>(p,0d))).ToArray();
  var samples=WavefrontEngine.GenerateChiefRaySamples(optic,field,wave,pupils,optic.RayAimingEnabled,referenceSphere:reference).Samples;
  var series=baseline.Series.Select((s,k)=>s with{Y=s.Y.Select((y,i)=>double.IsFinite(y)?samples[k*41+i].OpdWaves:y).ToArray()}).ToList();
  var candidate=baseline with{Series=series};var run=new AnalysisRun{Key=row.Key,Request=request,Directory=Path.Combine(folder,name)};
  ComparisonRunner.Compare(run,entry,row.Settings,candidate,native,Path.Combine(folder,name));
  var probes=series.Select((s,k)=>new{axis=k,points=new[]{0,10,20,30,40}.Select(i=>new{p=s.X[i],core=s.Y[i],native=native.Series[k].Y[i],error=s.Y[i]-native.Series[k].Y[i]}).ToArray()}).ToArray();
  results.Add(new{index,name,radius=reference.Radius,paraxialPupil=distance,geometricPupil=geometric,chief.Position,chief.Direction,run.Conclusion,run.Metrics,probes});
  JsonFiles.Write(Path.Combine(root,"results.json"),results);Console.WriteLine($"{index}/{name}: {run.Conclusion} max {run.Metrics.Max(m=>m.MaxAbsolute):G9}");
 }
}
internal sealed record Row(int Index,string Lens,int Configuration,string Key,string LegacyPath,AnalysisConfiguration Settings,AnalysisRun Run);
