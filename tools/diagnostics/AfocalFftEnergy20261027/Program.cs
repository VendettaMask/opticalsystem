using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

if(args.Length!=3)throw new ArgumentException("geometry <native-complex-root> <fresh-output>");
var mode=args[0];var input=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
if(mode=="energy-probe"){EnergyProbe.Run(input,output);return;}
if(mode=="energy-native-audit"){NativeEnergyAudit.Run(input,output);return;}
if(mode=="binocular-audit"){BinocularAudit.Run(input,output);return;}
if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
if(mode!="geometry")throw new ArgumentException("Unknown mode");
var results=new List<object>();
foreach(var row in JsonFiles.Read<JsonElement>(Path.Combine(input,"items.json")).EnumerateArray())
{
    var id=row.GetProperty("id").GetString()!;
    var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
    var wave=optic.Wavelengths[row.GetProperty("wavelength").GetInt32()-1];
    var native=JsonFiles.Read<JsonElement>(Path.Combine("artifacts/validation/afocal-sampling-20261027/pupil-inverse-v7",id+"-mask.json"))
        .EnumerateArray().ToDictionary(p=>(p.GetProperty("x").GetInt32(),p.GetProperty("y").GetInt32()),p=>(A:p.GetProperty("amplitude").GetDouble(),P:p.GetProperty("phaseWaves").GetDouble()));
    foreach(var h in new[]{.001,.0001,.01})
    {
        var probe=WavefrontEngine.GenerateChiefRaySamples(optic,(0,0),wave,new[]{(-h,0d),(h,0d),(0d,-h),(0d,h)},aimAtStop:optic.RayAimingEnabled);
        Vector3D Point(int i)=>new(probe.Samples[i].PupilX,probe.Samples[i].PupilY,probe.Samples[i].PupilZ);
        var dx=(Point(1)-Point(0)).Length/h;var dy=(Point(3)-Point(2)).Length/h;
        var diameter=Math.Max(dx,dy);
        foreach(var kind in new[]{"isotropic","aspect"})
        {
            var stretchY=kind=="aspect"?diameter/dy:1;
            var grid=new PupilGridSpecification(32,true,true,kind=="aspect"?diameter/dx:1);
            typeof(PupilGridSpecification).GetProperty("StretchY",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(grid,stretchY);
            var lookup=Enumerable.Range(0,32).SelectMany(y=>Enumerable.Range(0,32).Select(x=>(X:grid.Coordinate(x),Y:(double)typeof(PupilGridSpecification).GetMethod("YCoordinate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(grid,[y])!,Column:x,Row:y)))
                .Where(p=>p.X*p.X+p.Y*p.Y<=1).ToDictionary(p=>(p.X,p.Y),p=>(p.Column+16,p.Row+16));
            var coordinates=lookup.Keys.ToArray();
            var raw=WavefrontEngine.GenerateChiefRaySamples(optic,(0,0),wave,coordinates,aimAtStop:optic.RayAimingEnabled,referenceWavelength:wave);
            var smooth=(WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[raw,grid])!;
            var errors=new List<double>();var missing=0;var selected=new HashSet<(int,int)>();
            foreach(var s in smooth.Samples.Where(s=>s.Intensity>0))
            {
                var key=lookup[(s.NormalizedPupilX,s.NormalizedPupilY)];selected.Add(key);
                var value=native.GetValueOrDefault(key);if(value.A<1e-8){missing++;continue;}
                errors.Add(Complex.FromPolarCoordinates(1,2*Math.PI*(value.P-native[(32,32)].P-s.OpdWaves)).Phase/(2*Math.PI));
            }
            var nativeOnly=native.Count(p=>p.Value.A>1e-8&&!selected.Contains(p.Key));
            results.Add(new{id,h,kind,dx,dy,grid.Stretch,stretchY,count=selected.Count,coreOnly=missing,nativeOnly,phaseRms=Math.Sqrt(errors.Average(e=>e*e)),phaseMax=errors.Max(Math.Abs)});
        }
    }
}
JsonFiles.Write(Path.Combine(output,"summary.json"),results);
