using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class RbfFitting
{
    internal static void Run(string root,string repo)
    {
        var conditions=PositiveFits.Projector(repo).Conditions;var results=new List<object>();
        foreach(var config in new[]{(Id:"colour_strength",Features:XyzAbsoluteRbfFeatures.ChromaticityLogStrength,Neighbours:96,Smoothing:.01),(Id:"log_xyz",Features:XyzAbsoluteRbfFeatures.LogXyz,Neighbours:192,Smoothing:.0001)})
        foreach(var name in new[]{"acryl_blue","mystique","cayman"})
        {
            var dir=Path.Combine(root,config.Id,name);Directory.CreateDirectory(dir);var pairs=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-conditions/final-positive",name,"known-conditions-pairs.csv");
            var source=name=="mystique"?Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/mystique.astm"):name=="cayman"?Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured/cayman.astm"):Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured/acryl_blue.astm");
            double N(string s)=>double.Parse(s,CultureInfo.InvariantCulture);
            var obs=File.ReadAllLines(pairs).Skip(1).Select(s=>s.Split(',')).Select(c=>new XyzRadiometricObservation(c[0],550,N(c[1]),N(c[2]),N(c[3]),N(c[4]),c[5]=="1")).ToArray();
            var proof=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,"Original Cornell measured spectra, independently derived known-condition XYZ. Absolute XYZ is normalized BSDF, not radiance. Two general RBF configurations are explored for every material; the original development holdout is not a new blind test. Original published observer is not assumed.",PositiveFits.Sha(pairs),PositiveFits.Sha(source),true);
            var gamut=XyzCalibrationGamut.Create("Unlabelled input chromaticities only, no held-out radiometric labels",proof.XyzSha256,obs.Select(p=>new XyzCalibrationColor(p.X,p.Y,p.Z)).ToArray());
            try
            {
                var fit=XyzRadiometricCalibrator.FitAbsoluteRbf(obs,proof,new(.01,.01,1e-6),gamut,conditions,config.Features,config.Neighbours,config.Smoothing);
                bool exported=false;string? rejection=null,sha=null;
                try{var calibration=fit.CreateCalibration(name+" / absolute XYZ / "+config.Id,conditions.Observer,conditions.Illuminant);var output=Path.Combine(dir,"approved-550-calibration.json");File.WriteAllText(output,JsonSerializer.Serialize(calibration,PositiveFits.Json));_=XyzRadiometricCalibration.Read(output);exported=true;sha=PositiveFits.Sha(output);}catch(InvalidDataException ex){rejection=ex.Message;}
                File.WriteAllText(Path.Combine(dir,"fit.json"),JsonSerializer.Serialize(new{Material=name,Configuration=config.Id,Conditions=conditions,DevelopmentHoldoutUsedForModelExploration=true,Result=fit,Exported=exported,CalibrationSha256=sha,Rejection=rejection},PositiveFits.Json));
                var result=new{Material=name,Configuration=config.Id,Exported=exported,Stats=fit.Evidence.Bands,CalibrationSha256=sha};results.Add(result);Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(dir,"failure.json"),JsonSerializer.Serialize(new{Material=name,Configuration=config.Id,Error=ex.ToString()},PositiveFits.Json));results.Add(new{Material=name,Configuration=config.Id,Exported=false,Error=ex.Message});Console.WriteLine(name+" / "+config.Id+": "+ex.Message);}
        }
        File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{CoreSha256=PositiveFits.Sha(typeof(XyzRadiometricCalibrator).Assembly.Location),DriverSha256=PositiveFits.Sha(System.Reflection.Assembly.GetExecutingAssembly().Location),Cases=results},PositiveFits.Json));
    }
}
