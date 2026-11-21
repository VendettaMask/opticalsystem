using System.Text.Json;
using System.Globalization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
if (!Directory.Exists(Path.Combine(root,"src"))) root = Directory.GetCurrentDirectory();
var evidence = Path.Combine(root,"validation/rechecks/2026-11-21/nsc-color-parity");
var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"native/detector.json"))).RootElement;
var viewer = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"native/viewer-rgb.json"))).RootElement;
var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"validation/rechecks/2026-11-21/nsc-color-fringes/million-ray-data.json"))).RootElement;
var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"after.json"))).RootElement;
double[] Read(JsonElement e, string p) => e.GetProperty(p).Deserialize<double[]>()!;
var nPower = Read(native,"power"); var nx=Read(native,"x"); var ny=Read(native,"y"); var nz=Read(native,"z");
var nr=Read(viewer,"r"); var ng=Read(viewer,"g"); var nb=Read(viewer,"b");
var doc = ZemaxNonSequentialReader.Read(Path.Combine(root,"src/OpticalSystemDesign.App/Assets/NonSequentialExamples/Birefringence/Color Fringes.ZMX")).Document!;
if(args.Contains("--trace"))
{
    var timer=System.Diagnostics.Stopwatch.StartNew();
    var result=new NonSequentialDocumentTracer().Trace(doc,new(),new(RayCountOverride:1_000_000,OutputMode:NonSequentialTraceOutputMode.SummaryOnly));
    var frame=result.Detectors.Single(); var rgb=NonSequentialColorimetry.DetectorRgb(frame,doc);
    File.WriteAllText(Path.Combine(evidence,"after.json"),JsonSerializer.Serialize(new{frame.PixelsX,frame.PixelsY,Rgb=rgb,Power=frame.PowerByWavelength,result.EnergyBalance,elapsedSeconds=timer.Elapsed.TotalSeconds}));
    Console.WriteLine($"Traced {timer.Elapsed}, W={frame.TotalPowerWatts:R}");return;
}
if(args.Contains("--controls"))
{
    var registry=new OpticalSystemDesign.Core.Materials.MaterialRegistry();var no=registry.Resolve("BIREFRINGENT:QUARTZ").RefractiveIndex(550);var ne=registry.Resolve("BIREFRINGENT:QUARTZ-E").RefractiveIndex(550);
    var axis=doc.Objects[2].Parameters.Waveplate!.LocalAxis;var slope=Math.Tan(.19551*Math.PI/180);var normal=new OpticalSystemDesign.Core.Backend.Vector3D(0,slope,1);normal/=normal.Length;
    double Dot(OpticalSystemDesign.Core.Backend.Vector3D u,OpticalSystemDesign.Core.Backend.Vector3D v)=>u.X*v.X+u.Y*v.Y+u.Z*v.Z;
    OpticalSystemDesign.Core.Backend.Vector3D A(OpticalSystemDesign.Core.Backend.Vector3D v)=>v/(ne*ne)+(1/(no*no)-1/(ne*ne))*Dot(v,axis)*axis;
    var p=new OpticalSystemDesign.Core.Backend.Vector3D(0,0,no);var t=p-Dot(p,normal)*normal;
    var aa=Dot(normal,A(normal));var bb=Dot(normal,A(t));var cc=Dot(t,A(t))-1;
    var ke=t+((-bb+Math.Sqrt(bb*bb-aa*cc))/aa)*normal;
    Console.WriteLine($"extra phase index normal-entry={1/Math.Sqrt((1-axis.Z*axis.Z)/(ne*ne)+axis.Z*axis.Z/(no*no)):R}; tilted-entry={ke.Z:R}");
    var phaseNormal=new OpticalSystemDesign.Core.Backend.Vector3D(0,0,1);var energy=A(phaseNormal);var projectedIndex=1/Math.Sqrt(Dot(phaseNormal,A(phaseNormal)))*Dot(energy,phaseNormal)/energy.Length;
    Console.WriteLine($"Maxwell phase index projected on extraordinary energy direction={projectedIndex:R}");
    var nativeRows=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"native/ray-controls.json"))).RootElement.EnumerateArray().ToArray();
    double Phase(JsonElement row){var ex=new System.Numerics.Complex(row.GetProperty("exr").GetDouble(),row.GetProperty("exi").GetDouble());var ey=new System.Numerics.Complex(row.GetProperty("eyr").GetDouble(),row.GetProperty("eyi").GetDouble());return (ex*System.Numerics.Complex.Conjugate(ey)).Phase;}
    var nativeEnds=nativeRows.Where(r=>r.GetProperty("obj").GetInt32()==3&&r.GetProperty("face").GetInt32()==2&&r.GetProperty("position").GetDouble()==0).ToArray();
    Console.WriteLine($"Native second crystal phase-index from length perturbation: {no+(Phase(nativeEnds[1])-Phase(nativeEnds[0]))/.0001*.00055/(2*Math.PI):R}");
    foreach(var r in nativeRows.Where(r=>r.GetProperty("obj").GetInt32()==4)) {Console.WriteLine($"Native y={r.GetProperty("position")}, length={r.GetProperty("plateLength")}, phaseXY={Phase(r):R}");}
    var source=doc.Objects[0]; var physics=((SourceParameters)source.Parameters).Physics;
    foreach(var position in new[]{-3d,0,3d})
    {
        var probe=doc.Clone();
        probe.Replace(source.Id,source with{Kind=NonSequentialObjectKind.SourceRay,LocalCoordinateSystem=new(new(0,position,-10),RotationZDegrees:45),
            Parameters=new SourceRayParameters{Physics=physics is null?null:physics with{Spectrum=null}}});
        var trace=new NonSequentialDocumentTracer().Trace(probe,new());
        Console.WriteLine($"CONTROL y={position} detector={trace.Detectors.Single().TotalPowerWatts:R}");
        foreach(var s in trace.Branches.SelectMany(b=>b.Segments))
        {if(s.IncidentField is not{} f)continue;var e=f.State.First.Field;Console.WriteLine($" {doc.Objects.ToList().FindIndex(o=>o.Id==s.ObjectId)+1} face={s.FaceNumber}, Z={s.End.Z:R}, W={s.Intensity:R}, Q={e.X.Magnitude*e.X.Magnitude-e.Y.Magnitude*e.Y.Magnitude:R}, U={2*(e.X*System.Numerics.Complex.Conjugate(e.Y)).Real:R}, V={2*(e.X*System.Numerics.Complex.Conjugate(e.Y)).Imaginary:R}");}
    }
    return;
}
var bands = before.GetProperty("Power").Deserialize<Dictionary<int,double[]>>()!;
var power = Enumerable.Range(0,10000).Select(i=>bands.Values.Sum(a=>a[i])).ToArray();
if(args.Contains("--metrics"))
{
    var afterBands=after.GetProperty("Power").Deserialize<Dictionary<int,double[]>>()!;
    var afterPower=Enumerable.Range(0,10000).Select(i=>afterBands.Values.Sum(a=>a[i])).ToArray();
    var beforeRgb=Read(before,"Rgb");var afterRgb=Read(after,"Rgb");
    var nativeRgb=Enumerable.Range(0,10000).SelectMany(i=>new[]{nr[i]/255,ng[i]/255,nb[i]/255}).ToArray();
    var xyzNative=Enumerable.Range(0,10000).SelectMany(i=>new[]{nx[i],ny[i],nz[i]}).ToArray();
    var reconstructed=NonSequentialColorimetry.TristimulusRgb(xyzNative);
    var conversionErrors=reconstructed.Zip(nativeRgb,(a,b)=>255*Math.Abs(a-b)).ToArray();
    var mask=Enumerable.Range(0,10000).Where(i=>Math.Pow(i%100-49.5,2)+Math.Pow(i/100-49.5,2)<45*45).ToArray();
    object Metrics(double[] p,double[] c)
    {
        var differences=mask.SelectMany(i=>Enumerable.Range(0,3).Select(k=>255*(c[i*3+k]-nativeRgb[i*3+k]))).ToArray();
        var profiles=new List<(double N,double P,double[] C,double[] R)>();
        foreach(var group in mask.GroupBy(i=>(i%100+i/100)/3))
            profiles.Add((group.Average(i=>nPower[i]),group.Average(i=>p[i]),Enumerable.Range(0,3).Select(k=>group.Average(i=>c[3*i+k])).ToArray(),Enumerable.Range(0,3).Select(k=>group.Average(i=>nativeRgb[3*i+k])).ToArray()));
        var profileError=profiles.SelectMany(v=>Enumerable.Range(0,3).Select(k=>255*(v.C[k]-v.R[k]))).ToArray();
        return new{totalPower=p.Sum(),relativePowerError=p.Sum()/nPower.Sum()-1,pixelPowerCorrelation=Corr(p,nPower),pixelRgbRmse8Bit=Math.Sqrt(differences.Average(d=>d*d)),
            stripePowerCorrelation=Corr(profiles.Select(p=>p.N).ToArray(),profiles.Select(p=>p.P).ToArray()),stripeRgbRmse8Bit=Math.Sqrt(profileError.Average(d=>d*d))};
    }
    var result=new{mask="radius < 9 mm; stripe bins 3 pixel anti-diagonals",nativePower=nPower.Sum(),before=Metrics(power,beforeRgb),after=Metrics(afterPower,afterRgb),
        nativeXyzToRgb=new{maximum8BitError=conversionErrors.Max(),rms8BitError=Math.Sqrt(conversionErrors.Average(v=>v*v))}};
    var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);File.WriteAllText(Path.Combine(evidence,"comparison.json"),json);return;
}
double[] Transform(double[] a, int variant) => Enumerable.Range(0,10000).Select(i=> { var x=i%100;var y=i/100; if((variant&1)!=0)x=99-x;if((variant&2)!=0)y=99-y;return a[(variant&4)!=0?x*100+y:y*100+x]; }).ToArray();
double Corr(double[] a,double[] b) {var ma=a.Average();var mb=b.Average();return a.Zip(b,(x,y)=>(x-ma)*(y-mb)).Sum()/Math.Sqrt(a.Sum(x=>(x-ma)*(x-ma))*b.Sum(y=>(y-mb)*(y-mb)));}
Console.WriteLine("Power correlations by orientation");
for(var t=0;t<8;t++) Console.WriteLine($"{t}: {Corr(power,Transform(nPower,t)):F8}");
Console.WriteLine("Native RGB orientation vs XYZ Y");
for(var t=0;t<8;t++) Console.WriteLine($"{t}: {Corr(ny,Transform(nr.Zip(ng,(r,g)=>r+g).Zip(nb,(rg,b)=>rg+b).ToArray(),t)):F8}");
var xyz = Enumerable.Range(0,3).Select(_=>new double[10000]).ToArray();
foreach(var band in bands) {var c=NonSequentialColorimetry.Response(doc.Wavelengths[band.Key-1].Nanometers);for(var i=0;i<10000;i++){xyz[0][i]+=band.Value[i]*c.X;xyz[1][i]+=band.Value[i]*c.Y;xyz[2][i]+=band.Value[i]*c.Z;}}
Console.WriteLine($"W before={power.Sum():R}; native={nPower.Sum():R}; XYZ before={string.Join(',',xyz.Select(a=>a.Sum()))}; native radiometric={nx.Sum()*.04/683},{ny.Sum()*.04/683},{nz.Sum()*.04/683}");
Console.WriteLine($"Native XYZ maxima: {nx.Max():R},{ny.Max():R},{nz.Max():R}; exposures Y={1/ny.Max():R}, XYZ={1/Math.Max(nx.Max(),Math.Max(ny.Max(),nz.Max())):R}");
var nativeLinear=Enumerable.Range(0,10000).SelectMany(i=>new[]{3.2406255*nx[i]-1.537208*ny[i]-.4986286*nz[i],-.9689307*nx[i]+1.8757561*ny[i]+.0415175*nz[i],.0557101*nx[i]-.2040211*ny[i]+1.0569959*nz[i]}).ToArray();
Console.WriteLine($"Native max linear RGB={nativeLinear.Max():R}; exposure={1/nativeLinear.Max():R}");
var lines=File.ReadAllLines(Path.Combine(evidence,"native/prescription.txt"));
var table=lines.Select(l=>l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Where(p=>p.Length==6&&p[0].StartsWith("0.")&&p[1].StartsWith("0.")).Take(20).Select(p=>p.Take(5).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
var oldFit=((SourceParameters)doc.Objects[0].Parameters).Physics!.Spectrum!;
for(var i=0;i<20;i++)Console.WriteLine($"SPD {table[i][0]:F6}: native={table[i][1]:F8}; before={oldFit[i].RelativePower:F8}; difference={oldFit[i].RelativePower-table[i][1]:E3}");
File.WriteAllText(Path.Combine(evidence,"native/source-spectrum.json"),JsonSerializer.Serialize(table,new JsonSerializerOptions{WriteIndented=true}));
var a = table.Select(p=>NonSequentialColorimetry.Response(p[0]*1000)).ToArray();
foreach(var useNative in new[]{false,true})
{
    var aa=useNative?table.Select(p=>(X:p[2],Y:p[3],Z:p[4])).ToArray():a;
    var w=new double[20];var target=new[]{.9505,1d,1.089}; var step=1/aa.Sum(v=>v.X*v.X+v.Y*v.Y+v.Z*v.Z);
    for(var iteration=0;iteration<50000;iteration++){var ex=-target[0];var ey=-target[1];var ez=-target[2];for(var i=0;i<20;i++){ex+=aa[i].X*w[i];ey+=aa[i].Y*w[i];ez+=aa[i].Z*w[i];}for(var i=0;i<20;i++)w[i]=Math.Max(0,w[i]-step*(aa[i].X*ex+aa[i].Y*ey+aa[i].Z*ez));}
    var sum=w.Sum();for(var i=0;i<20;i++)w[i]/=sum;
    Console.WriteLine($"Zero-prior nativeCMF={useNative}, maxWeightError={w.Select((v,i)=>Math.Abs(v-table[i][1])).Max():E4}; nativeCMFerror={table.Select((p,i)=>Math.Abs(a[i].X-p[2])+Math.Abs(a[i].Y-p[3])+Math.Abs(a[i].Z-p[4])).Max():E4}");
}
double[] Solve3(double[,] matrix,double[] rhs){for(var k=0;k<3;k++){var pivot=Enumerable.Range(k,3-k).MaxBy(i=>Math.Abs(matrix[i,k]));for(var j=0;j<3;j++)(matrix[k,j],matrix[pivot,j])=(matrix[pivot,j],matrix[k,j]);(rhs[k],rhs[pivot])=(rhs[pivot],rhs[k]);var d=matrix[k,k];for(var j=0;j<3;j++)matrix[k,j]/=d;rhs[k]/=d;for(var i=0;i<3;i++){if(i==k)continue;var factor=matrix[i,k];for(var j=0;j<3;j++)matrix[i,j]-=factor*matrix[k,j];rhs[i]-=factor*rhs[k];}}return rhs;}
foreach(var gamma in new[]{1d,2.2,2.4})
{
    Console.WriteLine($"RGB transform fit gamma={gamma}");
    foreach(var channel in new[]{nr,ng,nb})
    {
        double Decode(double v){v/=255;return gamma==2.4?(v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4)):Math.Pow(v,gamma);}
        var rows=Enumerable.Range(0,10000).Where(i=>channel[i]>15&&channel[i]<240).ToArray();var matrix=new double[3,3];var rhs=new double[3];
        foreach(var i in rows){var v=new[]{nx[i],ny[i],nz[i]};for(var j=0;j<3;j++){rhs[j]+=v[j]*Decode(channel[i]);for(var k=0;k<3;k++)matrix[j,k]+=v[j]*v[k];}}
        var c=Solve3(matrix,rhs);var rms=Math.Sqrt(rows.Average(i=>Math.Pow(c[0]*nx[i]+c[1]*ny[i]+c[2]*nz[i]-Decode(channel[i]),2)));
        Console.WriteLine($"  {string.Join(',',c)}; normalized={string.Join(',',c.Select(v=>v/c[0]))}; rms={rms}");
    }
}
