using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class BinocularAudit
{
    internal static void Run(string input,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var row in JsonFiles.Read<JsonElement>(Path.Combine(input,"items.json")).EnumerateArray())
        {
            var id=row.GetProperty("id").GetString()!;var configuration=row.GetProperty("configuration").GetInt32();
            foreach(var kind in new[]{"Real","Imaginary"})
            {
                var settings=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-"+kind+"/captured-settings.json"));
                if(settings.GetProperty("properties").GetProperty("Type").GetString()!=kind
                    ||settings.GetProperty("selectors").GetProperty("Wavelength").GetInt32()!=2
                    ||settings.GetProperty("request").GetProperty("configuration").GetInt32()!=configuration)
                    throw new InvalidDataException("Native complex settings/readback differ");
            }
            var real=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Real/data.json")).GetProperty("dataGrids")[0];
            var imag=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Imaginary/data.json")).GetProperty("dataGrids")[0];
            var n=real.GetProperty("nx").GetInt32();var data=new Complex[n,n];
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)data[(y+n-(n/2-1))%n,(x+n-(n/2-1))%n]=Complex.Conjugate(new(real.GetProperty("values")[y][x].GetDouble(),imag.GetProperty("values")[y][x].GetDouble()));
            typeof(DiffractionEngine).GetMethod("FourierTransform2D",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[data,false]);
            var pupil=new Complex[n,n];
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)pupil[(y+n/2)%n,(x+n/2)%n]=Complex.Conjugate(data[y,x])/(n*n);
            var max=pupil.Cast<Complex>().Max(v=>v.Magnitude);
            var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));var wave=optic.Wavelengths[1];
            var support=typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
            var aspect=((double X,double Y))support.GetMethod("AfocalFftPupilAspectStretch",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[optic,(0d,0d),wave,optic.RayAimingEnabled])!;
            var grid=new PupilGridSpecification(32,true,true,aspect.X);
            typeof(PupilGridSpecification).GetProperty("StretchY",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(grid,aspect.Y);
            var nodes=Enumerable.Range(0,32).SelectMany(y=>Enumerable.Range(0,32).Select(x=>(X:grid.Coordinate(x),Y:aspect.Y*((y-16)/15d),Column:x+16,Row:y+16)))
                .Where(p=>p.X*p.X+p.Y*p.Y<=1).ToDictionary(p=>(p.X,p.Y),p=>(p.Column,p.Row));
            var raw=WavefrontEngine.GenerateChiefRaySamples(optic,(0,0),wave,nodes.Keys.ToArray(),aimAtStop:optic.RayAimingEnabled,referenceWavelength:wave);
            var smooth=(WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[raw,grid])!;
            var phase=new List<double>();var coreOnly=0;var accepted=new HashSet<(int,int)>();
            foreach(var s in smooth.Samples.Where(s=>s.Intensity>0))
            {
                var p=nodes[(s.NormalizedPupilX,s.NormalizedPupilY)];accepted.Add(p);
                if(pupil[p.Item2,p.Item1].Magnitude<max*1e-8){coreOnly++;continue;}
                var v=pupil[p.Item2,p.Item1]/pupil[32,32];phase.Add((v*Complex.FromPolarCoordinates(1,-2*Math.PI*s.OpdWaves)).Phase/(2*Math.PI));
            }
            var nativeCount=0;var nativeOnly=0;
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)if(pupil[y,x].Magnitude>max*1e-8){nativeCount++;if(!accepted.Contains((x,y)))nativeOnly++;}
            double[][] Profile(string path)=>File.ReadLines(path).Select(l=>Regex.Match(l,@"^\s*(\d+)\s+([-+\d.eE]+)\s+([-+\d.eE]+)\s*$")).Where(m=>m.Success)
                .Select(m=>new[]{double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture),double.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture)}).ToArray();
            var fresh=Profile(Path.Combine(input,id+"-profile/data.txt"));var frozen=Profile(Path.Combine(row.GetProperty("originalProfileNative").GetString()!,"data.txt"));
            if(fresh.Length!=201||frozen.Length!=201||fresh.Zip(frozen).Any(p=>p.First[0]!=p.Second[0]||p.First[1]!=p.Second[1]))throw new InvalidDataException("Native profile grid changed");
            rows.Add(new{id,configuration,nativeCount,coreCount=accepted.Count,coreOnly,nativeOnly,aspect.X,aspect.Y,phaseRms=Math.Sqrt(phase.Average(v=>v*v)),freshProfileMaxDifference=fresh.Zip(frozen).Max(p=>Math.Abs(p.First[2]-p.Second[2]))});
        }
        JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
    }
}
