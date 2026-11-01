using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class RuntimeReview
{
    internal static void Probe(string source,string output,double angle,double height)
    {
        var data=BsdfFileImporter.Read(source,angle<0?new(BsdfImportSides.Front):new(BsdfImportSides.Front,SpeosPeak:new(angle,height,550)));
        var model=new MeasuredBsdf(data);
        var legacy=data.SpeosSpectral! with{RuntimeWhite=false};
        var diffuse=new MeasuredBsdf(new(data.Name,data.SourceFileName,data.SourceSha256,data.Points,coordinates:data.Coordinates,sourceFormat:data.SourceFormat,storedQuantity:data.StoredQuantity,speosSpectral:legacy));
        var peak=new MeasuredBsdf(new(data.Name,data.SourceFileName,data.SourceSha256,data.Points.Select(p=>p with{BrdfPerSteradian=0,BtdfPerSteradian=0}).ToArray(),coordinates:data.Coordinates,sourceFormat:data.SourceFormat,storedQuantity:data.StoredQuantity,speosSpectral:legacy,speosPeak:data.SpeosPeak! with{WhiteReferenceNanometers=null}));
        BsdfContext Context(double theta,double wave)=>new(new(Math.Sin(theta*Math.PI/180),0,Math.Cos(theta*Math.PI/180)),new(0,0,1),wave);
        var prediction=new[]{400d,550,700}.Select(w=>{
            var d=diffuse.Integrate(Context(30,w)).Reflection;var p=peak.Integrate(Context(30,w)).Reflection;
            var ps=peak.Integrate(Context(30,550)).Reflection/p;
            var ds=d>0?Math.Max(0,d+p-p*ps)/d:0;
            return new{Wavelength=w,UpperInputDegrees=30,SharedDiffuseScale=ds,SharedPeakScale=ps,PredictedReflection=diffuse.Integrate(Context(0,w)).Reflection*ds+peak.Integrate(Context(0,w)).Reflection*ps};
        }).ToArray();
        var alpha=double.Parse(System.Text.RegularExpressions.Regex.Match(data.ImportNotes!,@"archive scalar=([^;]+)").Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
        var nodeDiffuse=new[]{0d,30,60}.Select(t=>new{Incidence=t,RawDiffuseIntegral=diffuse.Integrate(Context(t,550)).Reflection/(alpha*.3)}).ToArray();
        var results=new[]{400d,550,700}.Select(w=>new{Wavelength=w,Energy=model.Integrate(new(new(0,0,1),new(0,0,1),w))}).ToArray();
        var json=JsonSerializer.Serialize(new{data.SourceSha256,data.DataSha256,data.SpeosPeak,data.SpeosSpectral,Results=results,SharedCellPrediction=prediction,NodeDiffuse=nodeDiffuse},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(output,json);Console.WriteLine(JsonSerializer.Serialize(new{Results=results,SharedCellPrediction=prediction,NodeDiffuse=nodeDiffuse}));
    }
    internal static void Run(string root)
    {
        var control=BsdfFileImporter.Read(Path.Combine(root,"native-valid-control-flag1.anisotropicbsdf"),new(BsdfImportSides.Front));
        File.WriteAllText(Path.Combine(root,"core-control.json"),JsonSerializer.Serialize(control,new JsonSerializerOptions{WriteIndented=true}));
        var output = new List<object>();
        foreach (var directory in Directory.GetDirectories(root))
        {
            var file = Path.Combine(directory, "native-direct-result.xmp");
            if (!File.Exists(file)) continue;
            var values = NativeXmpControl.ReadLayers(file, 64, 1)[0];
            var total = NativeXmpControl.Integral(values, 64);
            var rings = new double[9]; var counts = new int[9];
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            {
                var angle = Math.Sqrt(Math.Pow((x + .5) / 32 - 1, 2) + Math.Pow((y + .5) / 32 - 1, 2)) * 90;
                if (angle >= 90) continue;
                var i = (int)(angle / 10); rings[i] += values[y * 64 + x]; counts[i]++;
            }
            output.Add(new { Case = Path.GetFileName(directory), Flux = total,
                First = NativeXmpControl.Integral(values,64,1)/total,
                Second = NativeXmpControl.Integral(values,64,2)/total,
                RingMeanIntensity = rings.Select((v,i)=>v/counts[i]).ToArray() });
        }
        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root,"runtime-map-review.json"),json);
        Console.WriteLine(json);
    }
}
