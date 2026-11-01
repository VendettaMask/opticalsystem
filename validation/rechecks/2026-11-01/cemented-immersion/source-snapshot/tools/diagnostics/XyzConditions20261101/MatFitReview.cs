using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class MatFitReview
{
    internal static void Run(string root,string matPath,string astmPath,string repo)
    {
        Directory.CreateDirectory(root);
        static double[] Numbers(string s)=>s.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        var cmf=File.ReadAllLines(Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/derived-xyz-candidates/CIE_xyz_1931_2deg.csv")).Select(Numbers).ToDictionary(r=>(int)r[0]);
        var spd=File.ReadAllLines(Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/CIE_std_illum_D65.csv")).Select(Numbers).ToDictionary(r=>(int)r[0],r=>r[1]);
        var astm=File.ReadAllLines(astmPath).Where(s=>s.Length>0&&(char.IsDigit(s[0])||s[0]=='-')).Select(Numbers).ToArray();
        string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
        var held=astm.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
        var variables=Mat4Review.Read(matPath);var fr=variables.Single(v=>v.Name=="fr");if(fr.Rows!=31||fr.Columns!=1439||astm.Length!=1439)throw new InvalidDataException("Shape mismatch");
        var observations=new List<XyzRadiometricObservation>();double maximumRounding=0;
        for(int i=0;i<1439;i++)
        {
            var spectral=fr.Values.AsSpan(i*31,31).ToArray();for(int j=0;j<31;j++)maximumRounding=Math.Max(maximumRounding,Math.Abs(spectral[j]-astm[i][j+4]));
            var xyz=Project(spectral,cmf,spd);observations.Add(new(Key(astm[i]),550,xyz[0],xyz[1],xyz[2],spectral[15],held.Contains(Key(astm[i]))));
        }
        if(maximumRounding>5.00001e-7)throw new InvalidDataException("MAT/ASTM samples not paired by column");
        var proof=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,"Full precision MAT original; exact CIE1931/D65 piecewise-linear integral",new string('1',64),new string('2',64),true);
        var gamut=XyzCalibrationGamut.Create("Input features only",proof.XyzSha256,observations.Select(p=>new XyzCalibrationColor(p.X,p.Y,p.Z)).ToArray());
        var fit=XyzRadiometricCalibrator.FitWithinGamut(observations,proof,new(.01,.01,1e-6),gamut);
        var positive=XyzRadiometricCalibrator.FitPositiveColorTransfer(observations,proof,new(.01,.01,1e-6),gamut,PositiveFits.Projector(repo).Conditions,6);
        var output=new{MaximumAstmRounding=maximumRounding,DomainVertices=gamut.Vertices.Count,Result=fit,PositiveResult=positive,ApprovedExported=false,DiagnosticOnlyProvenancePlaceholders=true};
        File.WriteAllText(Path.Combine(root,"mat-fit.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{maximumRounding,Stats=fit.Evidence.Bands,fit.Bands}));
    }
    internal static double[] Project(double[] s,Dictionary<int,double[]> cmf,Dictionary<int,double> spd)
    {
        var xyz=new double[3];double white=0;
        for(int w=400;w<700;w++)foreach(var node in new[]{-1/Math.Sqrt(3),1/Math.Sqrt(3)})
        {
            var t=(1+node)/2;var l=w+t;var lo=Math.Min(29,(int)((l-400)/10));var f=(l-400-lo*10)/10;var value=s[lo]*(1-f)+s[lo+1]*f;var power=spd[w]*(1-t)+spd[w+1]*t;
            for(int c=0;c<3;c++){var response=cmf[w][c+1]*(1-t)+cmf[w+1][c+1]*t;xyz[c]+=value*power*response/2;if(c==1)white+=power*response/2;}
        }
        return xyz.Select(v=>v/white).ToArray();
    }
}
