using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class PupilInverse
{
    internal static void Run(string input,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh inverse output required");Directory.CreateDirectory(output);
        var results=new List<object>();
        foreach(var row in JsonFiles.Read<JsonElement>(Path.Combine(input,"items.json")).EnumerateArray())
        {
            var id=row.GetProperty("id").GetString()!;
            foreach(var kind in new[]{"Real","Imaginary"})
                if(JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-"+kind+"/captured-settings.json")).GetProperty("properties").GetProperty("Type").GetString()!=kind)
                    throw new InvalidDataException("Native complex PSF type did not apply; do not invert a linear intensity grid");
            var real=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Real/data.json")).GetProperty("dataGrids")[0];
            var imag=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Imaginary/data.json")).GetProperty("dataGrids")[0];
            var n=real.GetProperty("nx").GetInt32();var data=new Complex[n,n];
            var peak=0d;
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)
            {
                var v=new Complex(real.GetProperty("values")[y][x].GetDouble(),imag.GetProperty("values")[y][x].GetDouble());
                peak=Math.Max(peak,v.Magnitude*v.Magnitude);
                data[(y+n-(n/2-1))%n,(x+n-(n/2-1))%n]=Complex.Conjugate(v);
            }
            typeof(DiffractionEngine).GetMethod("Fft2D",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[data]);
            var max=0d;for(var y=0;y<n;y++)for(var x=0;x<n;x++){data[y,x]=Complex.Conjugate(data[y,x])/(n*n);max=Math.Max(max,data[y,x].Magnitude);}
            // Native complex fields are Fourier transforms of a pupil centered
            // at index zero; restore the centered padded pupil before lookup.
            var centered=new Complex[n,n];
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)centered[(y+n/2)%n,(x+n/2)%n]=data[y,x];
            data=centered;
            var mask=new List<object>();var count=0;var bounds=new[]{n,n,0,0};
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)if(data[y,x].Magnitude>max*1e-8){count++;bounds[0]=Math.Min(bounds[0],x);bounds[1]=Math.Min(bounds[1],y);bounds[2]=Math.Max(bounds[2],x);bounds[3]=Math.Max(bounds[3],y);}
            var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
            var wave=optic.Wavelengths[row.GetProperty("wavelength").GetInt32()-1];var grid=new PupilGridSpecification(32,true,true,1);
            var coordinates=Enumerable.Range(0,32).SelectMany(y=>Enumerable.Range(0,32).Select(x=>(X:grid.Coordinate(x),Y:grid.Coordinate(y)))).Where(p=>p.X*p.X+p.Y*p.Y<=1).ToArray();
            var raw=WavefrontEngine.GenerateChiefRaySamples(optic,(0,0),wave,coordinates,aimAtStop:optic.RayAimingEnabled,referenceWavelength:wave);
            var smoothed=(WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[raw,grid])!;
            var errors=new List<object>();
            foreach(var (name,wf) in new[]{("raw",raw),("smoothed",smoothed)})
            {
                var minus=new List<double>();var plus=new List<double>();var center=data[n/2,n/2];
                var points=new List<object>();
                foreach(var s in wf.Samples.Where(s=>s.Intensity>0))
                {
                    var x=(int)Math.Round(s.NormalizedPupilX*15+16)+16;
                    var y=(int)Math.Round(s.NormalizedPupilY*15+16)+16;
                    var v=data[y,x]/center;
                    if(v.Magnitude<1e-8)continue;
                    var d=(v*Complex.FromPolarCoordinates(1,2*Math.PI*s.OpdWaves)).Phase/(2*Math.PI);
                    var e=(v*Complex.FromPolarCoordinates(1,-2*Math.PI*s.OpdWaves)).Phase/(2*Math.PI);
                    minus.Add(d);plus.Add(e);
                    points.Add(new{px=s.NormalizedPupilX,py=s.NormalizedPupilY,nativeAmplitude=v.Magnitude,nativePhaseWaves=v.Phase/(2*Math.PI),coreOpdWaves=s.OpdWaves,d,e});
                }
                errors.Add(new{name,count=minus.Count,minusRms=Math.Sqrt(minus.Average(v=>v*v)),plusRms=Math.Sqrt(plus.Average(v=>v*v)),maximumMinus=minus.Max(Math.Abs)});
                JsonFiles.Write(Path.Combine(output,id+"-"+name+".json"),points);
            }
            for(var y=bounds[1];y<=bounds[3];y++)for(var x=bounds[0];x<=bounds[2];x++)mask.Add(new{x,y,amplitude=data[y,x].Magnitude/max,phaseWaves=data[y,x].Phase/(2*Math.PI)});
            results.Add(new{id,n,nativeComplexPeak=peak,nonzeroPupil=count,bounds,corePupil=raw.Samples.Count,coreValidPupil=raw.Samples.Count(s=>s.Intensity>0),errors});
            JsonFiles.Write(Path.Combine(output,id+"-mask.json"),mask);
        }
        JsonFiles.Write(Path.Combine(output,"summary.json"),results);
    }
}
