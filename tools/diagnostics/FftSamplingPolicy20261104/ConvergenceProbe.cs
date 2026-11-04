using System.Globalization;
using System.Reflection;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Workbench;

// Independent diagnostic only. Original product requests and captures are never replaced.
internal static class ConvergenceProbe
{
 internal static void Run(string repo,string root,int index,WorkbenchJob job)
 {
  var factory=typeof(WorkbenchRuntime).GetMethod("CreateAnalysis",BindingFlags.Instance|BindingFlags.NonPublic)!;
  var before=File.ReadAllBytes(job.SnapshotPath);
  foreach(var scale in new[]{1,2,4,8})
  {
   var independent=new Dictionary<string,string>(job.Request.WorkbenchSettings);
   foreach(var setting in new[]{"Sampling","PupilSampling","NumRays"})
    if(independent.TryGetValue(setting,out var value)&&int.TryParse(value,out var n))independent[setting]=(n*scale).ToString(CultureInfo.InvariantCulture);
   var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(job.SnapshotPath));
   // The exact Workbench factory, without its required-result scope, exposes the
   // finite Fourier sum for this independent diagnostic. Product gates stay on.
   var analysis=(BaseAnalysis)factory.Invoke(new WorkbenchRuntime(optic),new object[]{job.Request.CanonicalAnalysisKey,independent})!;
   var data=analysis.GenerateData();
   JsonFiles.Write(Path.Combine(root,index.ToString(),scale+"-data.json"),data);
   JsonFiles.Write(Path.Combine(root,index.ToString(),scale+"-settings.json"),independent);
   Console.WriteLine($"{index} {job.Request.CanonicalAnalysisKey} x{scale} complete");
  }
  if(!before.SequenceEqual(File.ReadAllBytes(job.SnapshotPath)))throw new InvalidOperationException("Source snapshot changed");
 }
 internal static void Summarize(string repo,string destination,string[] roots)
 {
  var policy=ComparisonConfiguration.Load(Path.Combine(repo,"tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"));
  var reports=new List<object>();
  foreach(var root in roots)foreach(var directory in Directory.GetDirectories(root).Where(d=>File.Exists(Path.Combine(d,"1-data.json"))).Order())
  {
   var original=JsonFiles.Read<AnalysisData>(Path.Combine(directory,"1-data.json"));
   var tolerance=policy.Analyses[original.Name].Quantities.Values.Single();
   AnalysisData? previous=null;var records=new List<object>();
   foreach(var scale in new[]{1,2,4,8})
   {
    var data=JsonFiles.Read<AnalysisData>(Path.Combine(directory,scale+"-data.json"));
    records.Add(new{scale,againstOriginal=Compare(original,data,tolerance),againstPrevious=previous is null?null:Compare(previous,data,tolerance)});
    previous=data;
   }
   reports.Add(new{index=Path.GetFileName(directory),analysis=original.Name,root,scope="Core self-refinement only",tolerance,records});
  }
  JsonFiles.Write(destination,reports);
 }
 private static object Compare(AnalysisData a,AnalysisData b,Tolerances tolerance)
 {
  var metrics=new List<ComparisonMetric>();var maximum=0d;var count=0;var missing=0;
  for(var i=0;i<a.PlotSeries.Count;i++)
  {
   var source=a.PlotSeries[i];
   if(i>=b.PlotSeries.Count||source.Kind!=b.PlotSeries[i].Kind||source.XUnit!=b.PlotSeries[i].XUnit||source.YUnit!=b.PlotSeries[i].YUnit){missing+=source.Points.Count;continue;}
   var valuesA=new List<double>();var valuesB=new List<double>();var x=new List<double>();
   if(source.Points.Any(p=>p.Value.HasValue))
   {
    var lookup=b.PlotSeries[i].Points.ToDictionary(p=>(Math.Round(p.X,10),Math.Round(p.Y,10)));
    foreach(var p in source.Points){if(!lookup.TryGetValue((Math.Round(p.X,10),Math.Round(p.Y,10)),out var q)){missing++;continue;}valuesA.Add(p.Value!.Value);valuesB.Add(q.Value!.Value);x.Add(p.X);}
   }
   else
   {
    var points=b.PlotSeries[i].Points.OrderBy(p=>p.X).ToArray();var at=0;
    foreach(var p in source.Points.OrderBy(p=>p.X))
    {
     if(p.X<points[0].X-1e-10||p.X>points[^1].X+1e-10){missing++;continue;}
     while(at+1<points.Length-1&&points[at+1].X<p.X)at++;
     var q=points[at];var r=points[Math.Min(at+1,points.Length-1)];
     var y=q.X==r.X?q.Y:q.Y+(r.Y-q.Y)*(p.X-q.X)/(r.X-q.X);
     valuesA.Add(p.Y);valuesB.Add(y);x.Add(p.X);
    }
   }
   if(valuesA.Count==0)continue;
   var metric=ComparisonMetrics.Calculate(source.Name+"/"+i,source.YUnit.ToString(),new(x.ToArray(),null,valuesA.ToArray(),valuesB.ToArray(),(double)valuesA.Count/source.Points.Count),tolerance);
   metrics.Add(metric);maximum=Math.Max(maximum,valuesA.Zip(valuesB,(p,q)=>Math.Abs(p-q)).Max());count+=valuesA.Count;
  }
  return new{maximum,count,missing,series=a.PlotSeries.Count,metrics};
 }
}
