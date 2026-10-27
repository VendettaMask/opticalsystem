using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;

internal static class NativeEnergyAudit
{
    internal static void Run(string input,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var row in JsonFiles.Read<JsonElement>(Path.Combine(input,"items.json")).EnumerateArray())
        {
            var id=row.GetProperty("id").GetString()!;
            var real=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Real/data.json")).GetProperty("dataGrids")[0];
            var imag=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Imaginary/data.json")).GetProperty("dataGrids")[0];
            var linear=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-Linear/data.json")).GetProperty("dataGrids")[0];
            foreach(var kind in new[]{"Real","Imaginary","Linear"})
            {
                var readback=JsonFiles.Read<JsonElement>(Path.Combine(input,id+"-"+kind+"/captured-settings.json"));
                var settings=readback.GetProperty("properties");
                if(settings.GetProperty("Type").GetString()!=kind||Math.Abs(settings.GetProperty("ImageDelta").GetDouble()-row.GetProperty("nativePitch").GetDouble())>1e-12)
                    throw new InvalidDataException("Native grid settings did not apply");
                if(readback.GetProperty("selectors").GetProperty("Wavelength").GetInt32()!=row.GetProperty("wavelength").GetInt32()
                    ||readback.GetProperty("selectors").GetProperty("Field").GetInt32()!=1)
                    throw new InvalidDataException("Native selected wavelength/field did not apply");
            }
            var n=real.GetProperty("nx").GetInt32();var data=new Complex[n,n];
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)
                data[(y+n-(n/2-1))%n,(x+n-(n/2-1))%n]=Complex.Conjugate(new(real.GetProperty("values")[y][x].GetDouble(),imag.GetProperty("values")[y][x].GetDouble()));
            typeof(DiffractionEngine).GetMethod("FourierTransform2D",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[data,true]);
            var max=data.Cast<Complex>().Max(v=>v.Magnitude);var mask=new List<object>();
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)if(data[y,x].Magnitude>max*1e-8)
                mask.Add(new{x=(x+n/2)%n,y=(y+n/2)%n,amplitude=data[y,x].Magnitude/max,phase=data[y,x].Phase/(2*Math.PI)});
            JsonFiles.Write(Path.Combine(output,id+"-pupil.json"),mask);
            var core=JsonFiles.Read<JsonElement>(Path.Combine("artifacts/validation/afocal-fft-energy-20261027/energy-v5",id+"-actual.json"));
            var scale=PhysicalNormalization.UnitScale(row.GetProperty("nativeUnit").GetString()!,"Milliradian");
            var pitch=linear.GetProperty("dx").GetDouble()*scale;var minX=linear.GetProperty("minX").GetDouble()*scale;var minY=linear.GetProperty("minY").GetDouble()*scale;
            var total=0d;var cx=0d;var cy=0d;var error=0d;var peak=0d;
            for(var y=0;y<n;y++)for(var x=0;x<n;x++)
            {
                var weight=linear.GetProperty("values")[y][x].GetDouble();total+=weight;cx+=(minX+x*pitch)*weight;cy+=(minY+y*pitch)*weight;
                peak=Math.Max(peak,weight);error+=Math.Pow(weight-core.GetProperty("values")[y][x].GetDouble(),2);
            }
            rows.Add(new{id,n,nativePupilCount=mask.Count,nativePitch=pitch,nativeCenterX=cx/total,nativeCenterY=cy/total,coreCenterX=core.GetProperty("centerX").GetDouble(),coreCenterY=core.GetProperty("centerY").GetDouble(),nativePeak=peak,oldCorePsfNrmse=Math.Sqrt(error/(n*n))/peak});
        }
        var centroid=JsonFiles.Read<JsonElement>(Path.Combine(input,"rifle-DEE-Centroid/data.json"));
        var chief=JsonFiles.Read<JsonElement>(Path.Combine(input,"rifle-DEE-ChiefRay/data.json"));
        JsonFiles.Write(Path.Combine(output,"summary.json"),new{grids=rows,deeReferenceCurvesEqual=centroid.GetProperty("dataSeries").GetRawText()==chief.GetProperty("dataSeries").GetRawText()});
    }
}
