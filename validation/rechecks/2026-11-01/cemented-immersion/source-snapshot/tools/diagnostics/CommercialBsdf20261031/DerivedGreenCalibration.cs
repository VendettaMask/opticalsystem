using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

internal static class DerivedGreenCalibration
{
    internal static void Run(string root)
    {
        var spectral=Path.Combine(root,"krylon_blue.astm");var cie=Path.Combine(root,"CIE_xyz_1931_2deg.csv");
        var digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cie))).ToLowerInvariant();
        if(digest!="fa663e3535a7e0763a745993a1f0a192eb0275ac46ad2d1befd7626841e713c1")throw new InvalidDataException("CIE source checksum mismatch");
        static double[] Numbers(string line)=>line.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
        var cmf=File.ReadAllLines(cie).Select(Numbers).ToDictionary(r=>(int)r[0]);var lines=File.ReadAllLines(spectral);
        var rows=lines.Where(l=>l.Length>0&&(char.IsDigit(l[0])||l[0]=='-')).Select(Numbers).ToArray();
        if(rows.Length!=1439||rows.Any(r=>r.Length!=35||r.Any(v=>!double.IsFinite(v))))throw new InvalidDataException("Unexpected measured table");
        string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
        var heldout=rows.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
        double normal=0;for(int w=400;w<=700;w++)normal+=cmf[w][2]*(w is 400 or 700?.5:1);
        var text=new StringBuilder("sample_id,X,Y,Z\n");var obs=new List<XyzRadiometricObservation>();
        foreach(var row in rows.OrderBy(Key,StringComparer.Ordinal))
        {
            var xyz=new double[3];for(int w=400;w<=700;w++)
            {
                var lo=Math.Min(29,(w-400)/10);var t=(w-400-lo*10)/10d;var brdf=row[4+lo]*(1-t)+row[5+lo]*t;
                var weight=w is 400 or 700?.5:1;for(int c=0;c<3;c++)xyz[c]+=brdf*cmf[w][c+1]*weight/normal;
            }
            var id=Key(row);text.AppendLine(id+","+string.Join(",",xyz.Select(v=>v.ToString("G17",CultureInfo.InvariantCulture))));
            // 550 nm is prescribed before fitting as the camera primary-green calibration band.
            obs.Add(new(id,550,xyz[0],xyz[1],xyz[2],row[19],heldout.Contains(id)));
        }
        var xyzPath=Path.Combine(root,"krylon-blue-derived-xyz.csv");File.WriteAllText(xyzPath,text.ToString(),new UTF8Encoding(false));
        var provenance=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,
            "Cornell measured Krylon Blue BRDF; mathematically derived XYZ under CIE 1931 2-deg observer; bandlimited equal-energy test illuminant 400-700 nm, zero outside; linear spectral interpolation, 1 nm trapezoid, Y-white normalization; CIE SHA256="+digest,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(xyzPath))).ToLowerInvariant(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(spectral))).ToLowerInvariant(),true);
        try
        {
            var fit=XyzRadiometricCalibrator.Fit(obs,provenance,new(.01,.01,1e-6));bool exported=false;string? reason=null;
            try{var calibration=fit.CreateCalibration("Cornell Krylon Blue / prescribed 550 nm only","CIE 1931 2-degree; published nominal CMFs, 400-700 nm band","Bandlimited equal-energy test illuminant, 400-700 nm, zero outside; k=1/integral(ybar)");File.WriteAllText(Path.Combine(root,"krylon-blue-550nm-calibration.json"),JsonSerializer.Serialize(calibration,new JsonSerializerOptions{WriteIndented=true}));exported=true;}catch(InvalidDataException ex){reason=ex.Message;}
            var result=new{Source=provenance,ObserverDataSha256=digest,DerivedXyzIsNotIndependentColorMeasurement=true,PrescribedCalibrationWavelengthNm=550,TrainingDirections=rows.Length-heldout.Count,ValidationDirections=heldout.Count,PolicyIsInternalExplorationOnly=true,fit.ModelValidationPassed,ApprovedCalibrationExported=exported,Rejection=reason,Result=fit,ZemaxMaterialChainCertified=false};
            File.WriteAllText(Path.Combine(root,"derived-green-fit.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{fit.ModelValidationPassed,Exported=exported,Band=fit.Evidence.Bands.Single()}));
        }
        catch(InvalidDataException ex){File.WriteAllText(Path.Combine(root,"derived-green-fit.json"),JsonSerializer.Serialize(new{Source=provenance,Rejected=true,Reason=ex.Message,ZemaxMaterialChainCertified=false},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(ex.Message);}
    }
}
