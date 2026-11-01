using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class PositiveFits
{
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
    internal static double[] Numbers(string s)=>s.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
    internal static string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
    internal static double[][] Rows(string path)=>File.ReadAllLines(path).Where(s=>s.Length>0&&(char.IsDigit(s[0])||s[0]=='-')).Select(Numbers).ToArray();
    internal static XyzSpectralProjector Projector(string repo)
    {
        var cmf=Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/derived-xyz-candidates/CIE_xyz_1931_2deg.csv");
        var d65=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/CIE_std_illum_D65.csv");
        if(Sha(cmf)!="fa663e3535a7e0763a745993a1f0a192eb0275ac46ad2d1befd7626841e713c1"||Sha(d65)!="e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb")throw new InvalidDataException("Official CIE identity mismatch");
        return new("CIE 1931 2-degree nominal CMFs; explicit 400-700 nm band","CIE D65; explicit 400-700 nm band, zero outside",Sha(cmf),Sha(d65),
            File.ReadAllLines(cmf).Select(Numbers).Where(r=>r[0]>=400&&r[0]<=700).Select(r=>new XyzObserverSample(r[0],r[1],r[2],r[3])).ToArray(),
            File.ReadAllLines(d65).Select(Numbers).Where(r=>r[0]>=400&&r[0]<=700).Select(r=>new XyzSpectralSample(r[0],r[1])).ToArray(),400,700);
    }
    internal static void Run(string root,string repo)
    {
        Directory.CreateDirectory(root);var projector=Projector(repo);var cases=new List<object>();
        var measured=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured");
        foreach(var name in new[]{"garnet_red","krylon_blue","acryl_blue","mystique","cayman"})
        {
            var path=name=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm"):name=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm"):Path.Combine(measured,name+".astm");
            var dir=Path.Combine(root,name);Directory.CreateDirectory(dir);var rows=Rows(path);
            if(rows.Length!=1439||rows.Any(r=>r.Length!=35))throw new InvalidDataException("Unexpected original table");
            var held=rows.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
            var pairs=rows.OrderBy(Key,StringComparer.Ordinal).Select(r=>new{Id=Key(r),Xyz=projector.Project(r.Skip(4).Select((v,i)=>new XyzSpectralSample(400+10*i,v)).ToArray()),Target=r[19],Validation=held.Contains(Key(r))}).ToArray();
            var csv=new StringBuilder("sample_id,X,Y,Z,measured_550_sr_inv,validation\n");
            foreach(var p in pairs)csv.Append(p.Id).Append(',').AppendJoin(',',new[]{p.Xyz.X,p.Xyz.Y,p.Xyz.Z,p.Target}.Select(v=>v.ToString("G17",CultureInfo.InvariantCulture))).Append(',').Append(p.Validation?"1":"0").Append('\n');
            var pairPath=Path.Combine(dir,"known-conditions-pairs.csv");File.WriteAllText(pairPath,csv.ToString(),new UTF8Encoding(false));
            var proof=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,"Cornell original measured spectral BRDF; newly derived XYZ under explicitly declared official CIE1931/D65 conditions. Original published XYZ observer is not assumed. Not independent XYZ instrument measurements.",Sha(pairPath),Sha(path),true);
            var observations=pairs.Select(p=>new XyzRadiometricObservation(p.Id,550,p.Xyz.X,p.Xyz.Y,p.Xyz.Z,p.Target,p.Validation)).ToArray();
            var gamut=XyzCalibrationGamut.Create("Complete unlabelled input colours fixed before fitting; no held-out radiometric labels",proof.XyzSha256,pairs.Select(p=>p.Xyz).ToArray());
            var policy=new XyzCalibrationFitPolicy(.01,.01,1e-6);
            var linear=XyzRadiometricCalibrator.FitWithinGamut(observations,proof,policy,gamut);
            // Fixed general six-neighbour method. No per-material model selection or parameter branch.
            var fit=XyzRadiometricCalibrator.FitPositiveColorTransfer(observations,proof,policy,gamut,projector.Conditions,6);
            bool exported=false;string? rejection=null;string? calibrationSha=null;
            try{var calibration=fit.CreateCalibration(name+" / measured spectrum / known-condition XYZ / 550 nm",projector.Conditions.Observer,projector.Conditions.Illuminant);var output=Path.Combine(dir,"approved-550-calibration.json");File.WriteAllText(output,JsonSerializer.Serialize(calibration,Json));_ = XyzRadiometricCalibration.Read(output);calibrationSha=Sha(output);exported=true;}catch(InvalidDataException ex){rejection=ex.Message;}
            var result=new{Material=name,MeasuredSpectralSource=path,MeasuredSha256=Sha(path),projector.Conditions,PairSha256=Sha(pairPath),PrescribedWavelengthNm=550,InputColorsUseValidationFeaturesButNotLabels=true,DerivedXyzIsNotIndependentInstrumentMeasurement=true,Linear=linear.Evidence.Bands,Result=fit,ApprovedExported=exported,CalibrationSha256=calibrationSha,Rejection=rejection};
            File.WriteAllText(Path.Combine(dir,"fit.json"),JsonSerializer.Serialize(result,Json));
            cases.Add(new{Material=name,Fit=fit.Evidence.Bands,Exported=exported,TrainingNodes=fit.PositiveModel!.Nodes.Count,calibrationSha});
        }
        var execution=new{CoreSha256=Sha(typeof(XyzRadiometricCalibrator).Assembly.Location),DriverSha256=Sha(System.Reflection.Assembly.GetExecutingAssembly().Location)};
        File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{Execution=execution,Cases=cases,Conditions=projector.Conditions},Json));Console.WriteLine(JsonSerializer.Serialize(cases));
    }
}
