using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class RegressionProbe
{
 internal static void Run(string root)
 {
  var rows=JsonFiles.Read<Row[]>(Path.Combine(root,"rows.json"));
  var results=JsonFiles.Read<JsonElement>(Path.Combine(root,"results.json")).EnumerateArray().ToArray();
  var identity=JsonFiles.Read<JsonElement>(Path.Combine(root,"identity.json"));
  var old=JsonFiles.Read<LegacyItem[]>(Path.Combine(identity.GetProperty("prior").GetString()!,"analysis-results.json"));
  var evidence=new List<object>();
  for(var i=0;i<rows.Length;i++)
  {
   if(old[i].Run.Conclusion!=Conclusion.Pass||results[i].GetProperty("conclusion").GetString()=="Pass")continue;
   var r=rows[i];var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(r.Snapshot!));
   var previous=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(old[i].Snapshot));
   var request=r.Run.Request!;
   var maximum=optic.Fields.Max(f=>Math.Sqrt(f.X*f.X+f.Y*f.Y));
   var field=optic.Fields[Math.Clamp(request.Field-1,0,optic.Fields.Count-1)];
   var hx=maximum>0?field.X/maximum:0;var hy=maximum>0?field.Y/maximum:0;
   var wavelength=request.Wavelength>0?optic.Wavelengths[request.Wavelength-1]:optic.Wavelengths.FirstOrDefault(w=>w.IsPrimary)??optic.Wavelengths[0];
   object Trace(Optic model)
   {
    try{var s=model.TraceGenericFinalSample(hx,hy,0,0,wavelength.Micrometers,model.RayAimingEnabled);
     return new{imageSample=s is not null&&s.SurfaceNumber==model.SurfaceGroup.Items.Count-1&&!s.Vignetted&&s.Intensity>0,
      surface=s?.SurfaceNumber,vignetted=s?.Vignetted,intensity=s?.Intensity};}
    catch(Exception e){return new{error=e.Message};}
   }
   object[] Apertures(Optic model)=>model.SurfaceGroup.Items.Select((s,index)=>new{index,s.PhysicalAperture})
    .Where(s=>s.PhysicalAperture!=null).Select(s=>(object)new{s.index,type=s.PhysicalAperture!.GetType().Name,
     parameters=JsonSerializer.SerializeToElement(s.PhysicalAperture,s.PhysicalAperture.GetType(),JsonFiles.Options)}).ToArray();
   var physical=Trace(optic);var oldPhysical=Trace(previous);
   var currentApertures=Apertures(optic);var oldApertures=Apertures(previous);
   var diagnostic=Optic.FromSnapshot(optic.ToSnapshot());foreach(var s in diagnostic.SurfaceGroup.Items)s.PhysicalAperture=null;
   evidence.Add(new{r.Index,r.Lens,r.Key,oldVerdict="Pass",currentVerdict=results[i].GetProperty("conclusion").GetString(),
    currentPhysicalChief=physical,previousPhysicalChief=oldPhysical,unclippedChiefDiagnosticOnly=Trace(diagnostic),
    currentApertures,oldApertures,acceptanceChanged=false,diagnosticReplacesReference=false});
  }
  JsonFiles.Write(Path.Combine(root,"regression-probe.json"),new{coreSha256=identity.GetProperty("coreSha256").GetString(),cases=evidence});
 }
}
