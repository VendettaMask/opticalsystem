using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class DirectReview
{
    internal static void Run(string root,string source,bool white)
    {
        var path=Path.Combine(root,"native-direct-result.xmp");float[][]? layers=null;int resolution=0;
        foreach(var n in new[]{32,64,128,256})try{layers=NativeXmpControl.ReadLayers(path,n,1);resolution=n;break;}catch(InvalidDataException){}
        if(layers is null)throw new InvalidDataException("Unsupported direct control grid");
        var total=NativeXmpControl.Integral(layers[0],resolution);
        var data=BsdfFileImporter.Read(source,new(BsdfImportSides.Front,SpeosPeak:new(60,.1,white?550:null)));var c=new BsdfContext(new(0,0,1),new(0,0,1),400);
        var e=new MeasuredBsdf(data).Integrate(c);
        var report=new{SourceSha256=data.SourceSha256,DataSha256=data.DataSha256,Resolution=resolution,NativeIntegratedWatts=total,CoreReflection=e.Reflection,CoreTransmission=e.Transmission,Difference=total-e.Reflection,
            NativeMeanCosine=NativeXmpControl.Integral(layers[0],resolution,1)/total,NativeMeanSquareCosine=NativeXmpControl.Integral(layers[0],resolution,2)/total,
            NativeMaximum=layers[0].Max(),NativeMinimum=layers[0].Min(),CoreUsesExplicitManualConeOverride=true,WhiteSpecularRequested=white,MapScope="Weighted Direct MC, 1 W monochromatic 400 nm near-normal input, conoscopic +Z hemisphere"};
        File.WriteAllText(Path.Combine(root,"direct-review.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
