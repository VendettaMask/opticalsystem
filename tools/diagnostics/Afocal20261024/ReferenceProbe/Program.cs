using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
if(args.Length==2&&args[0]=="curvature-response")
{
    if(File.Exists(args[1]))throw new IOException("Fresh output required");var rows4=new List<object>();
    var models=await new ZemaxZmxImporter().ImportConfigurationSetFileAsync("artifacts/validation/afocal-20261024/selected-sources/Afocal/Binocular_System.ZMX");
    for(var c=0;c<models.Configurations.Count;c++)
    foreach(var hy in new[]{0d,.01,.5,.84,1})
    foreach(var delta in new[]{1e-5,1e-4,2e-4,1e-3})
    foreach(var symmetric in new[]{false,true})
    {
        var optic=models.Configurations[c];var wave=optic.Wavelengths.Single(w=>w.IsPrimary);var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
        (OpticalSystemDesign.Core.Backend.Vector3D P,OpticalSystemDesign.Core.Backend.Vector3D D) Trace(double px,double py)
        {
            var sample=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,hy,px,py,wave.Micrometers,optic.RayAimingEnabled)).Single()!;
            return(frame.ToLocalPoint(sample.Position),frame.ToLocalDirection(sample.Direction));
        }
        var chief=Trace(0,0);var x=Trace(delta,0);var y=Trace(0,delta);var xb=symmetric?Trace(-delta,0):chief;var yb=symmetric?Trace(0,-delta):chief;
        var pxx=x.P.X-xb.P.X;var pyx=x.P.Y-xb.P.Y;var pxy=y.P.X-yb.P.X;var pyy=y.P.Y-yb.P.Y;
        var axx=x.D.X/x.D.Z-xb.D.X/xb.D.Z;var ayx=x.D.Y/x.D.Z-xb.D.Y/xb.D.Z;
        var axy=y.D.X/y.D.Z-yb.D.X/yb.D.Z;var ayy=y.D.Y/y.D.Z-yb.D.Y/yb.D.Z;
        var det=pxx*pyy-pxy*pyx;var detA=axx*ayy+axy*ayx;
        rows4.Add(new{configuration=c+1,hy,delta,symmetric,pxx,pyx,pxy,pyy,axx,ayx,axy,ayy,chiefD=chief.D,
            physicalS=-1000*chief.D.Z*(axx*pyy-axy*pyx)/det,physicalT=-1000*chief.D.Z*(ayy*pxx-ayx*pxy)/det,
            generalizedS=1000*chief.D.Z*detA/(pxx*ayy+pxy*ayx),generalizedT=1000*chief.D.Z*detA/(pyy*axx+pyx*axy)});
    }
    File.WriteAllText(args[1],JsonSerializer.Serialize(rows4,new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Length==2&&args[0]=="vergence")
{
    if(File.Exists(args[1]))throw new IOException("Fresh output required");var rows3=new List<object>();
    var models=await new ZemaxZmxImporter().ImportConfigurationSetFileAsync("artifacts/validation/afocal-20261024/selected-sources/Afocal/Binocular_System.ZMX");
    for(var c=0;c<models.Configurations.Count;c++)
    foreach(var hy in new[]{0d,.01,.5,.84,1})
    foreach(var h in new[]{.001,.003,.01})
    {
        var optic=models.Configurations[c];var wave=optic.Wavelengths.Single(w=>w.IsPrimary);
        var points=new[]{(0d,0d),(h,0d),(-h,0d),(0d,h),(0d,-h)};
        var result=WavefrontEngine.GenerateChiefRaySamples(optic,(0,hy),wave,points,aimAtStop:false);
        var center=result.Samples[0];
        double Curvature(int p,int m)
        {
            var a=result.Samples[p];var b=result.Samples[m];
            var rp=Math.Sqrt(Math.Pow(a.PupilX-center.PupilX,2)+Math.Pow(a.PupilY-center.PupilY,2)+Math.Pow(a.PupilZ-center.PupilZ,2));
            var rm=-Math.Sqrt(Math.Pow(b.PupilX-center.PupilX,2)+Math.Pow(b.PupilY-center.PupilY,2)+Math.Pow(b.PupilZ-center.PupilZ,2));
            return 2*wave.Micrometers*((a.OpdWaves-center.OpdWaves)/rp-(b.OpdWaves-center.OpdWaves)/rm)/(rp-rm);
        }
        rows3.Add(new{configuration=c+1,hy,h,sagittal=Curvature(1,2),tangential=Curvature(3,4)});
    }
    File.WriteAllText(args[1],JsonSerializer.Serialize(rows3,new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Length==2&&args[0]=="pupils")
{
    if(File.Exists(args[1]))throw new IOException("Fresh output required");var rows2=new List<object>();
    foreach(var source in Directory.EnumerateFiles("artifacts/validation/afocal-20261024/selected-sources","*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p).Equals(".zmx",StringComparison.OrdinalIgnoreCase)))
    {
        var models=await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source);
        for(var c=0;c<models.Configurations.Count;c++)
        {
            var optic=models.Configurations[c];var wave=optic.Wavelengths.Single(w=>w.IsPrimary);var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
            foreach(var h in new[]{1e-3,1e-2})
            {
                var pupils=new[]{(0d,0d),(h,0d),(0d,h),(-h,0d),(0d,-h)};
                var image=ExitPupilGeometryEngine.Compute(optic,(0,0),wave,pupils);
                var points=image.Points.Select(p=>frame.ToLocalPoint(new(p.PhysicalX,p.PhysicalY,p.PhysicalZ))).ToArray();
                var x=(points[1]-points[3])/(2*h);var y=(points[2]-points[4])/(2*h);var det=x.X*y.Y-y.X*x.Y;
                var r=optic.Paraxial.EstimateEntrancePupilDiameter()/2;
                rows2.Add(new{source,configuration=c+1,h,m00=r*y.Y/det,m01=-r*x.Y/det,m10=-r*y.X/det,m11=r*x.X/det,center=frame.ToLocalPoint(image.Center),diameter=image.DisplayRadiusMillimeters*2/h});
            }
        }
    }
    File.WriteAllText(args[1],JsonSerializer.Serialize(rows2,new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Length is <1 or >2||File.Exists(args[^1]))throw new ArgumentException("[native root] fresh output file");
var rows=new List<object>();
foreach(var path in Directory.EnumerateFiles(args.Length==2?args[0]:"artifacts/validation/afocal-20261024/point-opd-v1","point-reference.json",SearchOption.AllDirectories))
{
    using var job=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!,"request.json")));
    var source=job.RootElement.GetProperty("input").GetString()!;
    var configuration=job.RootElement.GetProperty("configuration").GetInt32();
    var optic=(await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source)).Configurations[configuration-1];
    using var doc=JsonDocument.Parse(File.ReadAllText(path));var n=doc.RootElement;
    var inputs=n.GetProperty("inputs").EnumerateArray().ToArray();
    var wave=n.GetProperty("wavelength").GetInt32();
    var native=n.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
    var hx=inputs[0].GetProperty("hx").GetDouble();var hy=inputs[0].GetProperty("hy").GetDouble();
    var pupils=inputs.Select(p=>(p.GetProperty("px").GetDouble(),p.GetProperty("py").GetDouble())).ToArray();
    foreach(var mode in new[]{ReferenceOpdMode.ExitPupil,ReferenceOpdMode.Absolute,ReferenceOpdMode.Absolute2})
    {
        optic.ReferenceOpd=mode;
        var result=WavefrontEngine.GenerateChiefRaySamples(optic,(hx,hy),optic.Wavelengths[wave-1],pupils,aimAtStop:optic.RayAimingEnabled);
        var differences=result.Samples.Select((s,i)=>s.OpdWaves-native[i].GetProperty("opdWaves").GetDouble()).ToArray();
        var max=differences.Max(Math.Abs);
        rows.Add(new{source,configuration,hx,hy,wave,mode=mode.ToString(),max,differences});Console.WriteLine($"{Path.GetFileName(source)} c{configuration} f={hy} w={wave} {mode}: {max:G9}");
    }
}
File.WriteAllText(args[^1],JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
