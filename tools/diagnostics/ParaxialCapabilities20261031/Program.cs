using System.Text.Json;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

var root=Path.GetFullPath(args[0]);var results=new List<object>();
var coreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
if(args.Length>1&&args[1]=="grid-propagation-rule"){await GridPropagationRules.Run(root);return;}
if(args.Length>1&&args[1]=="grid-jacobian"){await GridJacobianRules.Run(root);return;}
if(args.Length>1&&args[1]=="inventory")
{
 var options=new JsonSerializerOptions{WriteIndented=true,PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
  NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
 options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
 var path=Path.Combine(root,File.Exists(Path.Combine(root,"prepared-all-models.json"))?"prepared-all-models.json":"models.json");
 foreach(var model in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray())
 {
  var configuration=0;
  foreach(var snapshotPath in model.GetProperty("snapshots").EnumerateArray())
  {
   var optic=Optic.FromSnapshot(JsonSerializer.Deserialize<OpticalSystemDesign.Core.Serialization.OpticSnapshot>(File.ReadAllText(snapshotPath.GetString()!),options)!);
   results.Add(new{lensIndex=model.GetProperty("index").GetInt32(),lens=model.GetProperty("lens").GetString(),configuration=++configuration,
    issues=OpticalSystemDesign.Core.Capabilities.OpticCapabilityPreflight.Inspect(optic),coreSha256});
  }
 }
 File.WriteAllText(Path.Combine(root,"capability-inventory.json"),JsonSerializer.Serialize(results,options));return;
}
if(args.Length>1&&args[1]=="grid-curvature")
{
 foreach(var kind in new[]{"random","smooth","linear","estimated"})
 {
  var id="grid-"+kind;var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
  var grid=(GridSagGeometry)optic.SurfaceGroup.Items[2].Geometry;var n=optic.SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550);
  var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement.GetProperty("rows");
  var f=rows.EnumerateArray().Single(r=>r.GetProperty("input").GetProperty("code").GetString()=="EFFL").GetProperty("value").GetDouble();
  var expected=-1/(-f*(n-1));
  foreach(var step in new[]{.001,.005,.01,.02,.04,.1})
  {
   var z=grid.Sag(0,0);var plus=grid.Sag(0,step)-z;var minus=grid.Sag(0,-step)-z;var mean=(plus+minus)/2;
   var derivative=2*mean/(step*step);var circle=2*mean/(step*step+mean*mean);
   var separate=plus/(step*step+plus*plus)+minus/(step*step+minus*minus);
   var method=typeof(GridSagGeometry).GetMethod("Normal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
   var np=(Vector3D)method.Invoke(grid,[new Vector3D(0,step,z+plus),false])!;
   var nm=(Vector3D)method.Invoke(grid,[new Vector3D(0,-step,z+minus),false])!;
   var normalCurvature=-(np.Y-nm.Y)/(2*step);
   var sp=-np.Y/np.Z;var sm=-nm.Y/nm.Z;
   var meridionalNormalCurvature=(sp/Math.Sqrt(1+sp*sp)-sm/Math.Sqrt(1+sm*sm))/(2*step);
   var projectedNormalCurvature=Math.CopySign(double.Hypot(np.X-nm.X,np.Y-nm.Y)/(2*step),nm.Y-np.Y);
   var gxp=(grid.Sag(step,step)-grid.Sag(-step,step))/(2*step);var gxm=(grid.Sag(step,-step)-grid.Sag(-step,-step))/(2*step);
   var fdXCurvature=(sp/Math.Sqrt(1+sp*sp+gxp*gxp)-sm/Math.Sqrt(1+sm*sm+gxm*gxm))/(2*step);
   var centerNormal=grid.SurfaceNormal(new(0,0,z));var centerX=-centerNormal.X/centerNormal.Z;
   var fixedXCurvature=(sp/Math.Sqrt(1+sp*sp+centerX*centerX)-sm/Math.Sqrt(1+sm*sm+centerX*centerX))/(2*step);
   results.Add(new{id,step,expected,derivative,circle,separate,normalCurvature,normalError=normalCurvature-expected,meridionalNormalCurvature,meridionalError=meridionalNormalCurvature-expected,projectedNormalCurvature,projectedNormalError=projectedNormalCurvature-expected,fdXCurvature,fdXError=fdXCurvature-expected,fixedXCurvature,fixedXError=fixedXCurvature-expected,circleError=circle-expected,separateError=separate-expected});
  }
 }
 File.WriteAllText(Path.Combine(root,"curvature-probes.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Length>1&&args[1]=="firstorder")
{
 foreach(var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
 {
  var id=job.GetProperty("id").GetString()!;var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
  foreach(var row in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement.GetProperty("rows").EnumerateArray())
  {
   var p=row.GetProperty("input");var code=p.GetProperty("code").GetString();var wave=p.GetProperty("int2").GetInt32();
   if(code is "PARX" or "PATX")
   {
    results.Add(new{id,code,input=p,expected=row.GetProperty("value").GetDouble(),actual=(double?)null,
        issue="Independent X Gaussian transfer API not validated by this scalar-Y control",coreSha256});continue;
   }
   var py=p.GetProperty("data4").GetDouble();var index=p.GetProperty("int1").GetInt32();
   var trace=code=="EFFL"?null:optic.Paraxial.TraceNormalizedPupil(0,new[]{py},optic.Wavelengths[Math.Max(0,wave-1)].Micrometers);
   var actual=code switch{"EFFL"=>optic.Paraxial.EstimateEffectiveFocalLength(),"PARY"=>trace!.Heights[index][0],"PATY"=>trace!.Slopes[index][0],_=>throw new InvalidDataException(code)};
   var expected=row.GetProperty("value").GetDouble();results.Add(new{id,code,input=p,expected,actual,error=actual-expected,coreSha256});
  }
 }
 File.WriteAllText(Path.Combine(root,"product-firstorder.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 foreach(var r in results)Console.WriteLine(JsonSerializer.Serialize(r));return;
}
if(args.Length>1&&args[1]=="linear-normals")
{
 var id="grid-linear";var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
 var grid=(GridSagGeometry)optic.SurfaceGroup.Items[2].Geometry;
 var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement.GetProperty("rows").EnumerateArray().ToArray();
 foreach(var hy in new[]{0d,1d})foreach(var px in new[]{0d,.4})foreach(var py in new[]{-.8,0d,.8})
 {
  double N(string code,int index)=>rows.Single(r=>{var p=r.GetProperty("input");return p.GetProperty("code").GetString()==code&&p.GetProperty("int1").GetInt32()==index
   &&p.GetProperty("data2").GetDouble()==hy&&p.GetProperty("data3").GetDouble()==px&&p.GetProperty("data4").GetDouble()==py;}).GetProperty("value").GetDouble();
  var hit=new Vector3D(N("REAX",2),N("REAY",2),N("REAZ",2));var incoming=new Vector3D(N("REAA",1),N("REAB",1),N("REAC",1));
  var outgoing=new Vector3D(N("REAA",2),N("REAB",2),N("REAC",2));var normal=optic.SurfaceGroup.Items[2].MaterialAfter.RefractiveIndex(550)*outgoing-incoming;
  var baseline=grid.Base.SurfaceNormal(hit);var expectedDx=-normal.X/normal.Z+baseline.X/baseline.Z;var expectedDy=-normal.Y/normal.Z+baseline.Y/baseline.Z;
  var u=(hit.X-grid.DecenterX)/grid.SpacingX+(grid.Columns-1)/2d;var v=(grid.Rows-1)/2d-(hit.Y-grid.DecenterY)/grid.SpacingY;
  var i=Math.Min((int)Math.Floor(u),grid.Columns-2);var j=Math.Min((int)Math.Floor(v),grid.Rows-2);u-=i;v-=j;
  double Field(Func<GridSagPoint,double> f)=>(1-v)*((1-u)*f(grid.Points[j*grid.Columns+i])+u*f(grid.Points[j*grid.Columns+i+1]))
   +v*((1-u)*f(grid.Points[(j+1)*grid.Columns+i])+u*f(grid.Points[(j+1)*grid.Columns+i+1]));
  results.Add(new{hy,px,py,hit,expectedDx,expectedDy,nodeDx=Field(p=>p.Dx),nodeDy=Field(p=>p.Dy),errorDx=Field(p=>p.Dx)-expectedDx,errorDy=Field(p=>p.Dy)-expectedDy});
 }
 File.WriteAllText(Path.Combine(root,"linear-normal-rule.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Length>1&&args[1] is "product" or "all-product" or "binary-product")
{
 foreach(var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
 {
  var id=job.GetProperty("id").GetString()!;if(args[1]=="product"&&!id.EndsWith("mode1")&&!id.EndsWith("mode2"))continue;
  if(args[1]=="binary-product"&&!id.StartsWith("binary"))continue;
  var sourceSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,id+".ZMX"))));
  if(sourceSha256!=job.GetProperty("inputSha256").GetString())throw new InvalidDataException("Native source identity changed");
  var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
  var rows=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,id,"model-controls.json"))).RootElement.GetProperty("rows").EnumerateArray().ToArray();
  var beams=new Dictionary<(int Wave,double Hx,double Hy,double Px,double Py),Dictionary<int,RayTraceSampleValue>>();
  foreach(var row in rows)
  {
   var p=row.GetProperty("input");var code=p.GetProperty("code").GetString()!;var index=p.GetProperty("int1").GetInt32();
   double N(string name)=>p.GetProperty(name).GetDouble();
   if(code=="INDX")
   {
    var waveIndex=p.GetProperty("int2").GetInt32();var wavelength=optic.Wavelengths[waveIndex-1].Nanometers;
    var material=optic.SurfaceGroup.Items[index].MaterialAfter;
    var actualIndex=material is OpticalSystemDesign.Core.Materials.GradientIndexMaterial g
        ? g.Profile is OpticalSystemDesign.Core.Materials.GridGradientIndexProfile grid ? grid.InterfaceRefractiveIndex(Vector3D.Zero,wavelength) : g.RefractiveIndex(Vector3D.Zero,wavelength)
        : material.RefractiveIndex(wavelength);
    var expectedIndex=row.GetProperty("value").GetDouble();results.Add(new{id,code,input=p,expected=expectedIndex,actual=actualIndex,error=actualIndex-expectedIndex,coreSha256,sourceSha256});continue;
   }
   if(code=="SSAG")
   {
    var sag=optic.SurfaceGroup.Items[index].Geometry.Sag(N("data1"),N("data2"));var expectedSag=row.GetProperty("value").GetDouble();
    results.Add(new{id,code,input=p,expected=expectedSag,actual=sag,error=sag-expectedSag,coreSha256,sourceSha256});continue;
   }
   var wave=p.GetProperty("int2").GetInt32();
   var beamKey=(wave,N("data1"),N("data2"),N("data3"),N("data4"));
   if(!beams.TryGetValue(beamKey,out var samples))
   {
    var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(beamKey.Item2,beamKey.Item3,beamKey.Item4,beamKey.Item5,optic.Wavelengths[wave-1].Micrometers);
    var indices=Enumerable.Range(1,optic.SurfaceGroup.Items.Count-1).ToArray();
    using var trace=optic.SequentialRayTracer.Trace(bundle,TraceRequest.Selected(indices));
    samples=indices.ToDictionary(i=>i,i=>trace.GetSurfaceSamples(i)[0]!.Value);beams.Add(beamKey,samples);
   }
   var sample=samples[index];var surface=optic.SurfaceGroup.Items[index];
   var position=surface.CoordinateSystem.ToLocalPoint(sample.Position);var direction=surface.CoordinateSystem.ToLocalDirection(sample.Direction);
   var actual=code switch{"REAX"=>position.X,"REAY"=>position.Y,"REAZ"=>position.Z,"REAA"=>direction.X,"REAB"=>direction.Y,"REAC"=>direction.Z,"OPTH"=>sample.PhaseInclusiveOpticalPathLength!.Value,_=>throw new Exception(code)};
   var expected=row.GetProperty("value").GetDouble();
   if(code is "REAA" or "REAB" or "REAC" && optic.SurfaceGroup.Items.Take(index+1).Count(s=>s.IsReflective)%2==1)expected=-expected;
   if(code=="OPTH")
   {
    actual-=samples[1].PhaseInclusiveOpticalPathLength!.Value;
    expected-=rows.Single(r=>{var q=r.GetProperty("input");return q.GetProperty("code").GetString()=="OPTH"&&q.GetProperty("int1").GetInt32()==1
      &&q.GetProperty("int2").GetInt32()==wave&&q.GetProperty("data1").GetDouble()==N("data1")&&q.GetProperty("data2").GetDouble()==N("data2")
      &&q.GetProperty("data3").GetDouble()==N("data3")&&q.GetProperty("data4").GetDouble()==N("data4");}).GetProperty("value").GetDouble();
   }
   results.Add(new{id,code,input=p,expected,actual,error=actual-expected,coreSha256,sourceSha256,
       pathReference=code=="OPTH"?"OPTH(surface) - OPTH(surface 1)":null,
       referenceOnly=code=="OPTH"&&index==1});
  }
 }
 File.WriteAllText(Path.Combine(root,"product-controls.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 foreach(var g in results.GroupBy(r=>r.GetType().GetProperty("id")!.GetValue(r)))Console.WriteLine($"{g.Key} max={g.Max(r=>Math.Abs((double)r.GetType().GetProperty("error")!.GetValue(r)!)):G17}");
 return;
}
foreach(var job in JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"jobs.json"))).RootElement.EnumerateArray())
{
 var id=job.GetProperty("id").GetString()!;
 var nativePath=Path.Combine(root,id,"model-controls.json");if(!File.Exists(nativePath))continue;
 var optic=await new ZemaxZmxImporter().ImportFileAsync(Path.Combine(root,id+".ZMX"));
 var lens=optic.SurfaceGroup.Items[2];lens.Geometry=new PlaneGeometry();lens.InteractionModel=new ThinLensInteractionModel(100);
 var rows=JsonDocument.Parse(File.ReadAllText(nativePath)).RootElement.GetProperty("rows").EnumerateArray().ToArray();
 foreach(var hy in new[]{0.0,1.0})foreach(var px in new[]{0.0,.4})foreach(var py in new[]{-.8,.8})
 {
  double N(string code,int surf)=>rows.Single(r=>{var p=r.GetProperty("input");return p.GetProperty("code").GetString()==code&&p.GetProperty("int1").GetInt32()==surf&&p.GetProperty("data2").GetDouble()==hy&&p.GetProperty("data3").GetDouble()==px&&p.GetProperty("data4").GetDouble()==py;}).GetProperty("value").GetDouble();
  var source=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,hy,px,py,.55).Rays[0].Normalize();
  var chiefSource=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,hy,0,0,.55).Rays[0].Normalize();
  var target=new Vector3D(N("REAX",2),N("REAY",2),0);
  double Chief(string code)=>rows.Single(r=>{var p=r.GetProperty("input");return p.GetProperty("code").GetString()==code&&p.GetProperty("int1").GetInt32()==2&&p.GetProperty("data2").GetDouble()==hy&&p.GetProperty("data3").GetDouble()==0&&p.GetProperty("data4").GetDouble()==0;}).GetProperty("value").GetDouble();
  var chief=new Vector3D(Chief("REAX"),Chief("REAY"),0);var delta=target-chief;
  var a=new Vector3D(N("REAX",1),N("REAY",1),N("REAZ",1));
  var before=optic.SurfaceGroup.Items[1].MaterialAfter.RefractiveIndex(550);
  var after=lens.MaterialAfter.RefractiveIndex(550);
  var expected=N("OPTH",2)-N("OPTH",1)-before*Math.Sqrt(Math.Pow(target.X-a.X,2)+Math.Pow(target.Y-a.Y,2)+Math.Pow(5-a.Z,2));
  var infinite=double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness);
  Vector3D Incident(Vector3D point)
  {
   var x=0.0;var y=0.0;
   RayTraceSampleValue Evaluate(double ox,double oy)
   {
    var ray=infinite?source with{Origin=source.Origin+new Vector3D(ox,oy,0)}
      :source with{Direction=source.Direction+new Vector3D(ox,oy,0),IsNormalized=false};
    using var trace=optic.SequentialRayTracer.Trace(new RealRayBundle([ray]),TraceRequest.IncidentSurface(2));
    return trace.GetRaySamples(0)[0]??throw new Exception("miss");
   }
   for(var k=0;k<15;k++)
   {
    var v=Evaluate(x,y);var p=lens.CoordinateSystem.ToLocalPoint(v.Position);var ex=p.X-point.X;var ey=p.Y-point.Y;
    if(Math.Max(Math.Abs(ex),Math.Abs(ey))<1e-10)return lens.CoordinateSystem.ToLocalDirection(v.Direction);
    var h=infinite?1e-4:1e-6;var vx=lens.CoordinateSystem.ToLocalPoint(Evaluate(x+h,y).Position);var vy=lens.CoordinateSystem.ToLocalPoint(Evaluate(x,y+h).Position);
    var j00=(vx.X-p.X)/h;var j10=(vx.Y-p.Y)/h;var j01=(vy.X-p.X)/h;var j11=(vy.Y-p.Y)/h;var det=j00*j11-j01*j10;
    x-=(j11*ex-j01*ey)/det;y-=(j00*ey-j10*ex)/det;
   }
   throw new Exception("aim");
  }
  var baseDirection=Incident(Vector3D.Zero);var chiefDirection=Incident(chief);const double epsilon=1e-3;
  var probeX=Incident(new(epsilon,0,0));var probeY=Incident(new(0,epsilon,0));
  Vector3D Affine(Vector3D r)
  {
   var ux=baseDirection.X/baseDirection.Z+((probeX.X/probeX.Z-baseDirection.X/baseDirection.Z)*r.X+(probeY.X/probeY.Z-baseDirection.X/baseDirection.Z)*r.Y)/epsilon;
   var uy=baseDirection.Y/baseDirection.Z+((probeX.Y/probeX.Z-baseDirection.Y/baseDirection.Z)*r.X+(probeY.Y/probeY.Z-baseDirection.Y/baseDirection.Z)*r.Y)/epsilon;
   var d=new Vector3D(ux,uy,1);return d/d.Length;
  }
  double Integral(Func<Vector3D,Vector3D> incident,Vector3D? start=null,Vector3D? step=null)
  {
   var integrationStart=start??chief;var integrationDelta=step??delta;
   double Integrand(double t)
   {
    var r=integrationStart+t*integrationDelta;var d=incident(r);var ux=(before*d.X/d.Z-r.X/100)/after;var uy=(before*d.Y/d.Z-r.Y/100)/after;
    var o=new Vector3D(ux,uy,1);o/=o.Length;
    return (after*o.X-before*d.X)*integrationDelta.X+(after*o.Y-before*d.Y)*integrationDelta.Y;
   }
   var sum=0.0;const int steps=128;for(var k=0;k<=steps;k++)sum+=(k==0||k==steps?1:k%2==0?2:4)*Integrand(k/(double)steps);
   return sum/(3*steps);
  }
  var actual=Integral(Incident);var affine=Integral(Affine);var collimated=Integral(_=>chiefDirection);
  var xy=Integral(Incident,chief,new(delta.X,0,0))+Integral(Incident,chief+new Vector3D(delta.X,0,0),new(0,delta.Y,0));
  var yx=Integral(Incident,chief,new(0,delta.Y,0))+Integral(Incident,chief+new Vector3D(0,delta.Y,0),new(delta.X,0,0));
  RayTraceSampleValue Family(double t)
  {
   var ray=infinite?source with{Origin=chiefSource.Origin+t*(source.Origin-chiefSource.Origin)}
     :source with{Direction=new((1-t)*chiefSource.Direction.X/chiefSource.Direction.Z+t*source.Direction.X/source.Direction.Z,
       (1-t)*chiefSource.Direction.Y/chiefSource.Direction.Z+t*source.Direction.Y/source.Direction.Z,1),IsNormalized=false};
   using var trace=optic.SequentialRayTracer.Trace(new RealRayBundle([ray]),TraceRequest.IncidentSurface(2));
   return trace.GetRaySamples(0)[0]!.Value;
  }
  double FamilyIntegrand(double t)
  {
   var v=Family(t);var r=lens.CoordinateSystem.ToLocalPoint(v.Position);var d=lens.CoordinateSystem.ToLocalDirection(v.Direction);
   const double h=1e-4;var derivative=lens.CoordinateSystem.ToLocalDirection((Family(t+h).Position-Family(t-h).Position)/(2*h));
   var o=new Vector3D((before*d.X/d.Z-r.X/100)/after,(before*d.Y/d.Z-r.Y/100)/after,1);o/=o.Length;
   return (after*o.X-before*d.X)*derivative.X+(after*o.Y-before*d.Y)*derivative.Y;
  }
  var familySum=0.0;for(var k=0;k<=128;k++)familySum+=(k==0||k==128?1:k%2==0?2:4)*FamilyIntegrand(k/128.0);
  var family=familySum/384;
  results.Add(new{id,hy,px,py,target,expected,actual,error=actual-expected,affine,affineError=affine-expected,collimated,collimatedError=collimated-expected,xyError=xy-expected,yxError=yx-expected,familyError=family-expected});
 }
}
File.WriteAllText(Path.Combine(root,"phase-integral-controls.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
foreach(var g in results.GroupBy(r=>r.GetType().GetProperty("id")!.GetValue(r)))
 Console.WriteLine($"{g.Key}: actual={g.Max(r=>Math.Abs((double)r.GetType().GetProperty("error")!.GetValue(r)!)):G17} affine={g.Max(r=>Math.Abs((double)r.GetType().GetProperty("affineError")!.GetValue(r)!)):G17} collimated={g.Max(r=>Math.Abs((double)r.GetType().GetProperty("collimatedError")!.GetValue(r)!)):G17}");
