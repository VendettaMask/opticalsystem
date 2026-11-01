using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class MeasuredPairCalibration
{
    internal static void Run(string root)
    {
        var spectralPath=Path.Combine(root,"cayman.astm");var xyzPath=Path.Combine(root,"cayman_XYZ.astm");
        static double[][] Rows(string path)=>File.ReadAllLines(path).Where(l=>l.Length>0&&(char.IsDigit(l[0])||l[0]=='-')).Select(l=>l.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        static string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
        var rows=Rows(spectralPath);var xyz=Rows(xyzPath).ToDictionary(Key);
        var keys=rows.Select(Key).Order(StringComparer.Ordinal).ToArray();if(!keys.ToHashSet().SetEquals(xyz.Keys))throw new InvalidDataException("Paired geometry mismatch");
        var hold=keys.Where((_,i)=>i%3==0).ToHashSet();
        var obs=rows.Select(r=>{var id=Key(r);var v=xyz[id];return new XyzRadiometricObservation(id,550,v[4],v[5],v[6],r[19],hold.Contains(id));}).ToArray();
        string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        var source=new XyzCalibrationProvenance(XyzCalibrationDataKind.PublishedPairedMeasurements,"Cornell PCG Cayman lacquer paired spectral BRDF and published XYZ; D65 declared by official Data File Formats section; observer angle not explicitly specified",Sha(xyzPath),Sha(spectralPath),false);
        var result=XyzRadiometricCalibrator.Fit(obs,source,new(.01,.01,1e-6));
        var report=new{Source=source,PrescribedWavelengthNm=550,IlluminantVerified="D65",ObserverVerified=false,PublishedXyzDerivedFromMeasuredSpectra=true,SampleCount=rows.Length,Training=obs.Count(p=>!p.Validation),Validation=hold.Count,Result=result,Exported=false};
        File.WriteAllText(Path.Combine(root,"paired-green-fit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
