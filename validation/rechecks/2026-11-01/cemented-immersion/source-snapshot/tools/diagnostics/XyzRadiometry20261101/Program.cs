using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

if(args[0]=="--derived-gamut"){DerivedGamutRun.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]),Path.GetFullPath(args[4]));return;}
var root = Path.GetFullPath(args[0]);
var spectralPath = Path.GetFullPath(args[1]);
var xyzPath = Path.GetFullPath(args[2]);
var cmfPath = Path.GetFullPath(args[3]);
var d65Path = Path.GetFullPath(args[4]);
Directory.CreateDirectory(root);
static string Sha(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
static double[] Numbers(string s) => s.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
static (string[] Columns, double[][] Rows) Read(string p)
{
    var lines=File.ReadAllLines(p); var columns=lines.Single(s=>s.StartsWith("VARS "))[5..].Split(',');
    var rows=lines.Where(s=>s.Length>0&&(char.IsDigit(s[0])||s[0]=='-')).Select(Numbers).ToArray();
    var count=int.Parse(lines.Single(s=>s.StartsWith("NUM_POINTS "))[11..],CultureInfo.InvariantCulture);
    if(rows.Length!=count||rows.Any(r=>r.Length!=columns.Length||r.Any(v=>!double.IsFinite(v))))throw new InvalidDataException("ASTM count or finite-value mismatch");
    return(columns,rows);
}
static string Key(double[] r)=>string.Join("/",r.Take(4).Select(v=>v.ToString("G17",CultureInfo.InvariantCulture)));
var spectral=Read(spectralPath); var xyz=Read(xyzPath); var byKey=xyz.Rows.ToDictionary(Key);
if(!spectral.Rows.Select(Key).ToHashSet().SetEquals(byKey.Keys))throw new InvalidDataException("Paired geometry mismatch");
var held=spectral.Rows.Select(Key).Order(StringComparer.Ordinal).Where((_,i)=>i%3==0).ToHashSet();
var band=Array.IndexOf(spectral.Columns,"550nm"); if(band<4)throw new InvalidDataException("Missing prescribed 550 nm");
var observations=spectral.Rows.Select(r=>{var v=byKey[Key(r)];return new XyzRadiometricObservation(Key(r),550,v[4],v[5],v[6],r[band],held.Contains(Key(r)));}).ToArray();
var fit=XyzRadiometricCalibrator.Fit(observations,new(XyzCalibrationDataKind.PublishedPairedMeasurements,"Published Cornell paired spectra/XYZ. Illuminant D65; observer not verified.",Sha(xyzPath),Sha(spectralPath),false),new(.01,.01,1e-6));
var coefficient=fit.Bands.Single();
var gamut=GamutExploration.Run(observations);
var declared=XyzCalibrationGamut.Create("Unlabelled published XYZ input colours; no validation radiometric labels",Sha(xyzPath),observations.Select(p=>new XyzCalibrationColor(p.X,p.Y,p.Z)).ToArray());
var constrained=XyzRadiometricCalibrator.FitWithinGamut(observations,new(XyzCalibrationDataKind.PublishedPairedMeasurements,"Cornell published XYZ; D65 declared, observer unknown",Sha(xyzPath),Sha(spectralPath),false),new(.01,.01,1e-6),declared);
bool exportRejected=false;string? exportReason=null;
try{_ = constrained.CreateCalibration("Published pair candidate","Observer unverified","D65");}catch(InvalidDataException ex){exportRejected=true;exportReason=ex.Message;}
if(!exportRejected)throw new InvalidDataException("Unknown-observer calibration must not export");
// Feasibility diagnostic only: even fitting the validation set itself cannot beat its
// nonnegative least-squares minimum. Never export those coefficients or use them in the fit.
var oracle=XyzRadiometricCalibrator.Fit(observations.Select(p=>p with{Validation=!p.Validation}).ToArray(),
    new(XyzCalibrationDataKind.SyntheticControl,"Diagnostic lower bound on validation data, never a calibration",Sha(xyzPath),Sha(spectralPath),false),new(.01,.01,1e-6));
var relative=XyzRadiometricCalibrator.Fit(observations.Select(p=>{var s=Math.Max(1e-6,p.RadiometricBsdfPerSteradian);return p with{X=p.X/s,Y=p.Y/s,Z=p.Z/s,RadiometricBsdfPerSteradian=p.RadiometricBsdfPerSteradian/s};}).ToArray(),
    new(XyzCalibrationDataKind.SyntheticControl,"Diagnostic relative-error objective, never measurement uncertainty",Sha(xyzPath),Sha(spectralPath),false),new(.01,.01,1e-6));
var relativeBand=relative.Bands.Single();var relativeErrors=observations.Where(p=>p.Validation).Select(p=>relativeBand.X*p.X+relativeBand.Y*p.Y+relativeBand.Z*p.Z-p.RadiometricBsdfPerSteradian).ToArray();
var feasibility=new{ValidationInSampleNnlsMinimumNrmse=oracle.Evidence.Bands.Single().TrainingNormalizedRms,ValidationCoefficientsAreNotExported=true,
    RelativeObjectiveOriginalValidationNrmse=Math.Sqrt(relativeErrors.Sum(v=>v*v)/observations.Where(p=>p.Validation).Sum(p=>p.RadiometricBsdfPerSteradian*p.RadiometricBsdfPerSteradian)),RelativeObjectiveOriginalMaximumAbsolute=relativeErrors.Max(Math.Abs)};
var residuals=observations.Select(p=>new{p.SampleId,p.Validation,Target=p.RadiometricBsdfPerSteradian,Prediction=coefficient.X*p.X+coefficient.Y*p.Y+coefficient.Z*p.Z}).ToArray();
var profiles=new[]{false,true}.Select(validation=>
{
    var set=residuals.Where(r=>r.Validation==validation).ToArray();var squares=set.Sum(r=>r.Target*r.Target);
    return new{Validation=validation,Count=set.Length,TargetSquaredSum=squares,MaximumTarget=set.Max(r=>r.Target),LargestTargetSquaredShare=set.Max(r=>r.Target*r.Target)/squares,
        LargestResiduals=set.OrderByDescending(r=>Math.Abs(r.Prediction-r.Target)).Take(8).ToArray()};
}).ToArray();
var cmf=File.ReadAllLines(cmfPath).Select(Numbers).ToDictionary(r=>(int)r[0]);
var source=File.ReadAllLines(d65Path).Select(Numbers).ToDictionary(r=>(int)r[0],r=>r[1]);
var forward=new List<object>();
foreach(var illuminant in new[]{"D65","E"})
foreach(var step in new[]{1,10})
foreach(var trapezoid in step==1?new[]{true}:new[]{true,false})
{
    double normal=0;var weights=new double[301,3];
    for(var w=400;w<=700;w+=step)
    {
        var s=illuminant=="D65"?source[w]:1d;var trap=trapezoid&&w is 400 or 700?.5:1;
        normal+=s*cmf[w][2]*trap;for(var c=0;c<3;c++)weights[w-400,c]=s*cmf[w][c+1]*trap;
    }
    var rows=spectral.Rows.Select(r=>
    {
        var computed=new double[3];for(var w=400;w<=700;w+=step)
        {
            var lo=Math.Min(29,(w-400)/10);var t=(w-400-lo*10)/10d;var value=r[4+lo]*(1-t)+r[5+lo]*t;
            for(var c=0;c<3;c++)computed[c]+=value*weights[w-400,c]/normal;
        }
        return (Computed:computed,Published:byKey[Key(r)][4..]);
    }).ToArray();
    // Scale is diagnostic only: a documented normalization mismatch is not silently repaired.
    var scale=Enumerable.Range(0,3).Select(c=>rows.Sum(r=>r.Computed[c]*r.Published[c])/rows.Sum(r=>r.Computed[c]*r.Computed[c])).ToArray();
    var commonScale=rows.Sum(r=>Enumerable.Range(0,3).Sum(c=>r.Computed[c]*r.Published[c]))/rows.Sum(r=>r.Computed.Sum(v=>v*v));
    var errors=Enumerable.Range(0,3).Select(c=>new{Channel="XYZ"[c].ToString(),Scale=scale[c],UnscaledNrmse=Math.Sqrt(rows.Sum(r=>Math.Pow(r.Computed[c]-r.Published[c],2))/rows.Sum(r=>r.Published[c]*r.Published[c])),ScaledNrmse=Math.Sqrt(rows.Sum(r=>Math.Pow(scale[c]*r.Computed[c]-r.Published[c],2))/rows.Sum(r=>r.Published[c]*r.Published[c])),ScaledMaximumAbsolute=rows.Max(r=>Math.Abs(scale[c]*r.Computed[c]-r.Published[c]))}).ToArray();
    forward.Add(new{Illuminant=illuminant,Observer="CIE 1931 2-degree hypothesis only",Band="400-700 nm zero outside",StepNm=step,Trapezoid=trapezoid,CommonScale=commonScale,CommonScaleMaximumError=rows.Max(r=>Enumerable.Range(0,3).Max(c=>Math.Abs(commonScale*r.Computed[c]-r.Published[c]))),Errors=errors});
}
var report=new{Execution=new{CoreSha256=Sha(typeof(XyzRadiometricCalibrator).Assembly.Location),DiagnosticSha256=Sha(System.Reflection.Assembly.GetExecutingAssembly().Location)},Sources=new{Spectral=Sha(spectralPath),Xyz=Sha(xyzPath),Cmf=Sha(cmfPath),D65=Sha(d65Path)},fit,Profiles=profiles,Feasibility=feasibility,Gamut=gamut,CoreConstrainedFit=constrained,Forward=forward,ApprovedExport=false,ApprovedExportRejected=exportRejected,ExportRejection=exportReason};
File.WriteAllText(Path.Combine(root,"inspection.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{Fit=fit.Evidence.Bands,Profiles=profiles.Select(p=>new{p.Validation,p.LargestTargetSquaredShare,p.MaximumTarget}),Feasibility=feasibility,Forward=forward}));
