using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Scattering;

if(args[0]=="--mat"){Mat4Review.Inspect(args[1],args[2]);return;}
if(args[0]=="--matfit"){MatFitReview.Run(args[1],args[2],args[3],args[4]);return;}
if(args[0]=="--positive"){PositiveFits.Run(args[1],args[2]);return;}
if(args[0]=="--chain"){await RealCalibrationChain.Run(args[1],args[2]);return;}
var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
var repo=Path.GetFullPath(args.Length>1?args[1]:".");
static string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
static double[] Numbers(string s)=>s.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
static string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
static double[][] Read(string p)=>File.ReadAllLines(p).Where(s=>s.Length>0&&(char.IsDigit(s[0])||s[0]=='-')).Select(Numbers).ToArray();
var cmfPath=Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration/derived-xyz-candidates/CIE_xyz_1931_2deg.csv");
var d65Path=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/CIE_std_illum_D65.csv");
if(Sha(cmfPath)!="fa663e3535a7e0763a745993a1f0a192eb0275ac46ad2d1befd7626841e713c1"||Sha(d65Path)!="e76f210bffff3d552ef7113025da5f325d5dfec200dd4b878b1a2f3a507032cb")throw new InvalidDataException("CIE checksum mismatch");
var cmf=File.ReadAllLines(cmfPath).Select(Numbers).ToDictionary(r=>(int)r[0]);var spd=File.ReadAllLines(d65Path).Select(Numbers).ToDictionary(r=>(int)r[0],r=>r[1]);
double[] Project(double[] spectral,int subdivisions=1)
{
    double ywhite=0;var xyz=new double[3];
    // Two-node Gauss exactly integrates the product of the three piecewise-linear
    // factors on each merged 1 nm interval. Subdivision is an independent convergence check.
    for(int w=400;w<700;w++)for(int sub=0;sub<subdivisions;sub++)foreach(var node in new[]{-1/Math.Sqrt(3),1/Math.Sqrt(3)})
    {
        var t=(sub+(1+node)/2)/subdivisions;var lambda=w+t;var low=Math.Min(29,(int)((lambda-400)/10));var f=(lambda-400-low*10)/10;
        var brdf=spectral[low]*(1-f)+spectral[low+1]*f;var source=spd[w]*(1-t)+spd[w+1]*t;var weight=1d/(2*subdivisions);
        for(int c=0;c<3;c++){var v=cmf[w][c+1]*(1-t)+cmf[w+1][c+1]*t;xyz[c]+=brdf*v*source*weight;if(c==1)ywhite+=v*source*weight;}
    }
    return xyz.Select(v=>v/ywhite).ToArray();
}
var datasets=new List<(string Name,string Spectral,string Xyz)>();
var phase=Path.Combine(repo,"validation/rechecks/2026-11-01/xyz-radiometry/measured");
foreach(var name in new[]{"garnet_red","krylon_blue","acryl_blue"})datasets.Add((name,Path.Combine(phase,name+".astm"),Path.Combine(phase,name+"_XYZ.astm")));
var prior=Path.Combine(repo,"validation/rechecks/2026-10-31/commercial-bsdf-calibration");datasets.Add(("mystique",Path.Combine(prior,"mystique.astm"),Path.Combine(prior,"mystique_XYZ.astm")));
prior=Path.Combine(repo,"validation/rechecks/2026-10-31/white-specular-runtime/xyz-measured");datasets.Add(("cayman",Path.Combine(prior,"cayman.astm"),Path.Combine(prior,"cayman_XYZ.astm")));
var samples=new List<Row>();var sourceHashes=new List<object>();double convergence=0;
foreach(var data in datasets)
{
    var spectra=Read(data.Spectral);var xyz=Read(data.Xyz).ToDictionary(Key);var keys=spectra.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
    if(spectra.Length!=1439||xyz.Count!=1439||!spectra.Select(Key).ToHashSet().SetEquals(xyz.Keys))throw new InvalidDataException("Pair mismatch");
    sourceHashes.Add(new{data.Name,SpectralSha=Sha(data.Spectral),XyzSha=Sha(data.Xyz)});
    foreach(var r in spectra){var projected=Project(r[4..]);var refined=Project(r[4..],2);convergence=Math.Max(convergence,projected.Zip(refined).Max(p=>Math.Abs(p.First-p.Second)));samples.Add(new(data.Name,Key(r),r[4..],xyz[Key(r)][4..],projected,keys.Contains(Key(r))));}
}
var modelChecks=new List<object>();
foreach(var data in datasets)
{
    var rows=samples.Where(r=>r.Material==data.Name).ToArray();
    var obs=rows.Select(r=>new XyzRadiometricObservation(r.Id,550,r.Derived[0],r.Derived[1],r.Derived[2],r.Spectral[15],r.Validation)).ToArray();
    var proof=new XyzCalibrationProvenance(XyzCalibrationDataKind.SpectralMeasurementsWithDerivedXyz,"Known CIE1931/D65 exact piecewise-linear integration diagnostic",new string('1',64),new string('2',64),true);
    var domain=XyzCalibrationGamut.Create("Input colours only",proof.XyzSha256,obs.Select(p=>new XyzCalibrationColor(p.X,p.Y,p.Z)).ToArray());
    var linear=XyzRadiometricCalibrator.FitWithinGamut(obs,proof,new(.01,.01,1e-6),domain);
    var train=rows.Where(r=>!r.Validation&&r.Derived.Sum()>0).ToArray();var validation=rows.Where(r=>r.Validation).ToArray();
    double Distance(Row a,Row b)=>a.Derived.Select((v,j)=>Math.Pow(v/a.Derived.Sum()-b.Derived[j]/b.Derived.Sum(),2)).Sum();
    var knn=new[]{1,6,12}.Select(k=>
    {
        var predictions=validation.Select(r=>
        {
            var sum=r.Derived.Sum();if(sum==0)return 0d;
            var near=train.Select(t=>(Row:t,Distance:Distance(r,t))).OrderBy(p=>p.Distance).Take(k).ToArray();
            var weight=near.Sum(p=>1/Math.Max(1e-30,p.Distance));
            return sum*near.Sum(p=>p.Row.Spectral[15]/p.Row.Derived.Sum()/Math.Max(1e-30,p.Distance))/weight;
        }).ToArray();
        var errors=predictions.Select((p,i)=>p-validation[i].Spectral[15]).ToArray();var nrmse=Math.Sqrt(errors.Sum(e=>e*e)/validation.Sum(r=>r.Spectral[15]*r.Spectral[15]));
        return new{Neighbours=k,ValidationNrmse=nrmse,MaximumAbsolute=errors.Max(Math.Abs),Passed=nrmse<=.01&&errors.Max(Math.Abs)<=.01};
    }).ToArray();
    modelChecks.Add(new{data.Name,Linear=linear.Evidence.Bands,NearestColorDiagnosticOnly=knn});
}
var kernelChecks=new List<object>();
foreach(var heldMaterial in datasets.Select(d=>d.Name))
{
    var train=samples.Where(r=>r.Material!=heldMaterial).ToArray();var validation=samples.Where(r=>r.Material==heldMaterial).ToArray();
    var scales=Enumerable.Range(0,31).Select(j=>train.Max(r=>r.Spectral[j])).ToArray();var matrix=new double[train.Length,31];
    for(int i=0;i<train.Length;i++)for(int j=0;j<31;j++)matrix[i,j]=train[i].Spectral[j]/scales[j];
    var factor=JacobiSvd.Factor(matrix,1e-12);var weights=Enumerable.Range(0,3).Select(c=>JacobiSvd.Solve(factor,train.Select(r=>r.Published[c]).ToArray()).Select((v,j)=>v/scales[j]).ToArray()).ToArray();
    var errors=validation.Select(r=>Enumerable.Range(0,3).Select(c=>weights[c].Select((v,j)=>v*r.Spectral[j]).Sum()-r.Published[c]).ToArray()).ToArray();
    kernelChecks.Add(new{HeldMaterial=heldMaterial,Rank=factor.SingularSquared.Count(v=>v>factor.Threshold),MaximumAbsolute=errors.SelectMany(v=>v).Max(Math.Abs),Rms=Math.Sqrt(errors.SelectMany(v=>v).Average(v=>v*v)),Weights=weights});
}
var output=new{Sources=sourceHashes,Cmf=Sha(cmfPath),D65=Sha(d65Path),MaximumOneVsHalfNmDifference=convergence,Models=modelChecks,RecoveredPublishedKernels=kernelChecks,ApprovedCalibrationExported=false};
File.WriteAllText(Path.Combine(root,"audit.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{MaximumIntegrationDifference=convergence,Models=modelChecks,Kernels=kernelChecks.Select(r=>r)}));
internal sealed record Row(string Material,string Id,double[] Spectral,double[] Published,double[] Derived,bool Validation);
