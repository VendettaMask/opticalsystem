using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

internal static class EnergyProbe
{
    internal static void Run(string audit,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        foreach(var model in JsonFiles.Read<JsonElement>(Path.Combine(audit,"baseline-v4/models.json")).EnumerateArray()
            .Where(m=>m.GetProperty("id").GetString()!.Contains("Riflescope",StringComparison.OrdinalIgnoreCase)||m.GetProperty("id").GetString()!.Contains("Beam Expander",StringComparison.OrdinalIgnoreCase)))
        {
            var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(model.GetProperty("snapshot").GetString()!));
            var wave=optic.Wavelengths.First(w=>w.IsPrimary);var id=Path.GetFileNameWithoutExtension(model.GetProperty("id").GetString()!).Replace(' ','-').ToLowerInvariant();
            foreach(var ideal in new[]{false,true})
            {
                var spectra=((ValueTuple<Wavelength,PsfResult>[])typeof(DiffractionEngine).GetMethod("ComputeFftCumulativeEnergySpectrum",BindingFlags.Static|BindingFlags.NonPublic)!
                    .Invoke(null,[optic,(0d,0d),new[]{wave},32,ideal])!);
                var result=spectra[0].Item2;var n=result.GridSize;var pitch=result.SampleSpacingMicrometers;
                var pixels=Enumerable.Range(0,n).SelectMany(y=>Enumerable.Range(0,n).Select(x=>new{
                    X=(x-n/2d+1)*pitch,Y=(y-n/2d+1)*pitch,Weight=result.Values[(y+1)%n,(x+1)%n]})).ToArray();
                var total=pixels.Sum(p=>p.Weight);var centerX=pixels.Sum(p=>p.X*p.Weight)/total;var centerY=pixels.Sum(p=>p.Y*p.Weight)/total;
                var data=new{n,pitch,centerX,centerY,values=Enumerable.Range(0,n).Select(y=>Enumerable.Range(0,n).Select(x=>result.Values[(y+1)%n,(x+1)%n]/100).ToArray()).ToArray()};
                JsonFiles.Write(Path.Combine(output,id+(ideal?"-ideal":"-actual")+".json"),data);
                rows.Add(new{id,ideal,n,pitch,centerX,centerY,total,result.PeakStrehlRatio,model=model.Clone()});
            }
        }
        if(rows.Count!=4)throw new InvalidDataException("Expected actual/ideal for two control files");
        JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
    }
}
