using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class DerivedGamutRun
{
    internal static void Run(string root, string spectralPath, string cmfPath, string d65Path)
    {
        Directory.CreateDirectory(root);
        static string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
        static double[] Numbers(string s)=>s.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(Sha(cmfPath)!="fa663e3535a7e0763a745993a1f0a192eb0275ac46ad2d1befd7626841e713c1"||Sha(d65Path)!="e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb")throw new InvalidDataException("Official CIE source mismatch");
        var lines=File.ReadAllLines(spectralPath);var columns=lines.Single(s=>s.StartsWith("VARS "))[5..].Split(',');
        var rows=lines.Where(s=>s.Length>0&&(char.IsDigit(s[0])||s[0]=='-')).Select(Numbers).ToArray();
        if(rows.Length!=1439||rows.Any(r=>r.Length!=35||r.Any(v=>!double.IsFinite(v))||r.Skip(4).Any(v=>v<0))||!columns.Skip(4).SequenceEqual(Enumerable.Range(0,31).Select(i=>(400+10*i)+"nm")))throw new InvalidDataException("Unexpected measured spectral table");
        var cmf=File.ReadAllLines(cmfPath).Select(Numbers).ToDictionary(r=>(int)r[0]);var d65=File.ReadAllLines(d65Path).Select(Numbers).ToDictionary(r=>(int)r[0],r=>r[1]);
        string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
        var held=rows.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
        var weights=new double[301,3];double normal=0;
        for(int w=400;w<=700;w++){var trap=w is 400 or 700?.5:1;normal+=cmf[w][2]*d65[w]*trap;for(int c=0;c<3;c++)weights[w-400,c]=cmf[w][c+1]*d65[w]*trap;}
        var pairs=rows.OrderBy(Key,StringComparer.Ordinal).Select(r=>
        {
            var xyz=new double[3];for(int w=400;w<=700;w++){var lo=Math.Min(29,(w-400)/10);var t=(w-400-lo*10)/10d;var f=r[4+lo]*(1-t)+r[5+lo]*t;for(int c=0;c<3;c++)xyz[c]+=f*weights[w-400,c]/normal;}
            return new{Id=Key(r),Color=new XyzCalibrationColor(xyz[0],xyz[1],xyz[2]),Target=r[19]};
        }).ToArray();
        var text=new StringBuilder("sample_id,X,Y,Z\n");foreach(var p in pairs)text.Append(p.Id).Append(',').AppendJoin(',',new[]{p.Color.X,p.Color.Y,p.Color.Z}.Select(v=>v.ToString("G17",CultureInfo.InvariantCulture))).Append('\n');
        var xyzPath=Path.Combine(root,"derived-xyz.csv");File.WriteAllText(xyzPath,text.ToString(),new UTF8Encoding(false));
        // Domain is frozen from unlabelled input colours, before radiometric fitting.
        var gamut=XyzCalibrationGamut.Create("All input XYZ features only; no radiometric holdout labels; 400-700 nm D65, CIE 1931 2-degree",Sha(xyzPath),pairs.Select(p=>p.Color).ToArray());
        File.WriteAllText(Path.Combine(root,"declared-input-gamut.json"),JsonSerializer.Serialize(gamut,new JsonSerializerOptions{WriteIndented=true}));
        var source=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,"Cornell measured spectral BRDF; XYZ derived using official CIE 1931 2-degree and D65, both truncated to 400-700 nm, zero outside; linear spectral interpolation, 1 nm trapezoid, white-Y normalization. Not independent XYZ instrument measurements.",Sha(xyzPath),Sha(spectralPath),true);
        var observations=pairs.Select(p=>new XyzRadiometricObservation(p.Id,550,p.Color.X,p.Color.Y,p.Color.Z,p.Target,held.Contains(p.Id))).ToArray();
        var policy=new XyzCalibrationFitPolicy(.01,.01,1e-6);
        var old=XyzRadiometricCalibrator.Fit(observations,source,policy);
        var fit=XyzRadiometricCalibrator.FitWithinGamut(observations,source,policy,gamut);
        bool exported=false;string? reason=null;
        try{var calibration=fit.CreateCalibration("Cornell measured BRDF / D65-derived XYZ / 550 nm / declared colour domain","CIE 1931 2-degree; 400-700 nm band","CIE D65; 400-700 nm band, zero outside; white Y normalized to one");var output=Path.Combine(root,"gamut-550-calibration.json");File.WriteAllText(output,JsonSerializer.Serialize(calibration,new JsonSerializerOptions{WriteIndented=true}));_ = XyzRadiometricCalibration.Read(output);exported=true;}catch(InvalidDataException ex){reason=ex.Message;}
        var report=new{Sources=new{Spectral=Sha(spectralPath),Cmf=Sha(cmfPath),D65=Sha(d65Path),DerivedXyz=Sha(xyzPath)},Execution=new{CoreSha256=Sha(typeof(XyzRadiometricCalibrator).Assembly.Location),DiagnosticSha256=Sha(System.Reflection.Assembly.GetExecutingAssembly().Location)},PrescribedWavelengthNm=550,InputColorsDeclaredBeforeFit=true,ValidationRadiometricLabelsUsedInFit=false,DerivedXyzIsNotIndependentColorMeasurement=true,Legacy=old.Evidence.Bands,Result=fit,ApprovedExported=exported,Rejection=reason,FullCommercialMaterialCertification=false};
        File.WriteAllText(Path.Combine(root,"derived-gamut-fit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{fit.ModelValidationPassed,Exported=exported,Statistics=fit.Evidence.Bands,fit.Bands}));
    }
}
