using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;

internal static class RayRecheck
{
 internal static void Run(string root)
 {
  var identity=JsonFiles.Read<JsonElement>(Path.Combine(root,"identity.json"));
  var prior=identity.GetProperty("prior").GetString()!;
  var models=JsonFiles.Read<Model[]>(Path.Combine(root,"models.json"));
  var results=new List<RayResult>();
  foreach(var directory in Directory.EnumerateDirectories(Path.Combine(prior,"rays")).Order(StringComparer.Ordinal))
  {
   var id=Path.GetFileName(directory);var row=new RayResult{Id=id};results.Add(row);
   try
   {
    var jobPath=Path.Combine(directory,"job.json");
    if(!File.Exists(jobPath))throw new InvalidDataException("Original ray request was not materialized");
    var job=JsonFiles.Read<RayAuditJob>(jobPath);
    var model=models.Single(m=>m.SourceSha256==job.SourceSha256);row.Lens=model.Lens;
    if(job.Configuration>model.Snapshots.Length)throw new InvalidDataException("Current source import/configuration unavailable");
    var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(model.Snapshots[job.Configuration-1]));
    var nativeFolder=Path.Combine(directory,"native");
    var completion=Path.Combine(nativeFolder,"batch-result.json");
    if(!File.Exists(completion)||JsonFiles.Read<JsonElement>(completion).GetProperty("exitCode").GetInt32()!=0)
     throw new InvalidDataException("No completed valid native ray reference");
    var native=JsonFiles.Read<JsonElement>(Path.Combine(nativeFolder,"ray-audit.json"));
    var inputs=JsonFiles.Read<JsonElement>(Path.Combine(directory,"inputs.json"));
    if(!Equivalent(inputs,native.GetProperty("inputs")))throw new InvalidDataException("Native normalized pupil inputs changed");
    job.ValidateSurfaceBudget(optic.SurfaceGroup.Items.Count);
    // Fixed-ray controls use the recorded physical pupil request. Current
    // analysis quadrature nodes are not a requirement for an old ray control.
    var pupils=inputs.EnumerateArray().Where(p=>p.GetProperty("fieldIndex").GetInt32()==0)
     .Select(p=>new PupilSample(p.GetProperty("px").GetDouble(),p.GetProperty("py").GetDouble(),p.GetProperty("weight").GetDouble())).ToArray();
    var expected=job.Fields.SelectMany((f,fi)=>pupils.Select((p,pi)=>new{fieldIndex=fi,pupilIndex=pi,hx=f[0],hy=f[1],px=p.X,py=p.Y,weight=p.Weight}));
    if(!Equivalent(JsonSerializer.SerializeToElement(expected,JsonFiles.Options),inputs))throw new InvalidDataException("Recorded pupil/field request differs");
    var surfaces=native.GetProperty("surfaces").EnumerateArray().ToArray();
    if(surfaces.Length!=optic.SurfaceGroup.Items.Count-1)throw new InvalidDataException("Folded coordinate-break surfaces require an explicit native/Core mapping");
    var preflight=OpticCapabilityPreflight.Inspect(optic);
    if(preflight.Count!=0)throw new InvalidDataException("Unsupported imported model capability; diagnostic rays cannot certify this model");
    var nativeModel=JsonFiles.Read<JsonElement>(Path.Combine(nativeFolder,"model.json"));
    var signs=NativeReflectionConvention.PropagationSigns(nativeModel);
    var unit=nativeModel.GetProperty("units").GetProperty("LensUnits").GetString();
    var scale=unit switch{"Millimeters"=>1d,"Centimeters"=>10d,"Inches"=>25.4d,"Meters"=>1000d,_=>throw new InvalidDataException("Unknown native unit")};
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(900));using var scope=ComputationCancellation.Push(deadline.Token);
    var histories=job.Fields.SelectMany(f=>
    {
     var bundle=optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(f[0],f[1],optic.Wavelengths[job.Wavelength-1].Micrometers,pupils,aimAtStop:optic.RayAimingEnabled);
     return optic.SequentialRayTracer.Trace(bundle).RayHistories;
    }).ToArray();
    if(histories.Length!=inputs.GetArrayLength())throw new InvalidDataException("Incomplete Core input coverage");
    JsonFiles.Write(Path.Combine(root,"ray-output",id+".json"),histories.Select((h,i)=>new{inputIndex=i,samples=h.Select(s=>new{
     s.SurfaceNumber,s.Vignetted,s.Intensity,hit=s.RefractiveIndexBefore.HasValue,
     position=optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
     direction=optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction)}).ToArray()}).ToArray());
    row.Inputs=histories.Length;
    var lastNative=surfaces[^1].GetProperty("rays");
    for(var i=0;i<histories.Length;i++)
    {
     var a=lastNative[i];var b=histories[i].Last();
     var nativeAccepted=a.GetProperty("error").GetInt32()==0&&a.GetProperty("vignette").GetInt32()==0;
     var coreAccepted=b.SurfaceNumber==optic.SurfaceGroup.Items.Count-1&&!b.Vignetted&&b.Intensity>0;
     if(nativeAccepted!=coreAccepted)row.AcceptanceDifferences++;
     if(a.GetProperty("error").GetInt32()==0&&a.GetProperty("vignette").GetInt32()>0&&b.Vignetted&&b.RefractiveIndexBefore.HasValue
      &&a.GetProperty("vignette").GetInt32()!=b.SurfaceNumber)row.FirstClipDifferences++;
    }
    foreach(var surface in surfaces)
    {
     var si=surface.GetProperty("surface").GetInt32();var rays=surface.GetProperty("rays");
     if(rays.GetArrayLength()!=histories.Length)throw new InvalidDataException("Incomplete native ray coverage");
     for(var i=0;i<histories.Length;i++)
     {
      var a=rays[i];if(a.GetProperty("number").GetInt32()!=i+1)throw new InvalidDataException("Native ray order differs");
      var sample=histories[i].FirstOrDefault(s=>s.SurfaceNumber==si);
      if(a.GetProperty("error").GetInt32()!=0||a.GetProperty("vignette").GetInt32()!=0||sample is null||sample.Vignetted||!sample.RefractiveIndexBefore.HasValue)continue;
      var p=optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalPoint(sample.Position);
      var d=optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalDirection(sample.Direction);row.SurfaceComparisons++;
      foreach(var pair in new[]{("x",p.X),("y",p.Y),("z",p.Z)})
      {var value=a.GetProperty(pair.Item1).GetDouble()*scale;var error=Math.Abs(pair.Item2-value);row.MaximumCoordinate=Math.Max(row.MaximumCoordinate,error);if(error>1e-6+1e-6*Math.Abs(value))row.CoordinateFailures++;}
      foreach(var pair in new[]{("l",d.X),("m",d.Y),("n",d.Z)})
      {var value=a.GetProperty(pair.Item1).GetDouble()*signs[si];var error=Math.Abs(pair.Item2-value);row.MaximumDirection=Math.Max(row.MaximumDirection,error);if(error>1e-8+1e-6*Math.Abs(value))row.DirectionFailures++;}
     }
    }
    row.Conclusion=row.AcceptanceDifferences+row.FirstClipDifferences+row.CoordinateFailures+row.DirectionFailures>0?"Difference":row.SurfaceComparisons>0?"Pass":"Incomparable";
   }
   catch(InvalidDataException e){row.Conclusion="Incomparable";row.Reason=e.Message;}
   catch(Exception e){row.Conclusion="Error";row.Reason=e.ToString();}
   JsonFiles.Write(Path.Combine(root,"ray-cases",id+".json"),row);
   if(results.Count%25==0)Console.WriteLine($"RAYS {results.Count}: {row.Conclusion}");
  }
  JsonFiles.Write(Path.Combine(root,"ray-results.json"),results);
  JsonFiles.Write(Path.Combine(root,"ray-summary.json"),new{total=results.Count,counts=results.GroupBy(r=>r.Conclusion).ToDictionary(g=>g.Key,g=>g.Count()),
   physicalSurfaceComparisons=results.Sum(r=>r.SurfaceComparisons),coreSha256=identity.GetProperty("coreSha256").GetString(),freshNativeCapture=false});
 }
 private static bool Equivalent(JsonElement a,JsonElement b)
 {
  if(a.ValueKind!=b.ValueKind)return false;
  return a.ValueKind switch
  {
   JsonValueKind.Number=>a.GetDouble()==b.GetDouble(),
   JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count()&&a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&Equivalent(p.Value,v)),
   JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Equivalent(p.First,p.Second)),
   _=>a.ToString()==b.ToString()
  };
 }
 private sealed class RayResult
 {
  public string Id{get;set;}="";public string Lens{get;set;}="Unknown";public string Conclusion{get;set;}="Error";public string Reason{get;set;}="";
  public int Inputs{get;set;}public int SurfaceComparisons{get;set;}public int AcceptanceDifferences{get;set;}public int FirstClipDifferences{get;set;}
  public int CoordinateFailures{get;set;}public int DirectionFailures{get;set;}public double MaximumCoordinate{get;set;}public double MaximumDirection{get;set;}
 }
}
