using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class XyzAbsoluteRbfTests
{
    internal static double Expected(double wavelength,double x,double y,double z)=>
        (wavelength==400?.01:.02)*Math.Pow(x,.2)*Math.Pow(y,.3)*Math.Pow(z,.1);
    internal static XyzRadiometricObservation[] Observations()
    {
        var training=XyzGamutCalibrationTests.Colors.SelectMany(p=>new[]{.1,.3,1}.Select(s=>new XyzCalibrationColor(s*p.X,s*p.Y,s*p.Z))).ToArray();
        XyzCalibrationColor[] validation=[new(.064,.19,.126),new(.09,.23,.15)];
        return new[]{400d,700}.SelectMany(w=>training.Concat(validation).Select((p,i)=>new XyzRadiometricObservation("point-"+i,w,p.X,p.Y,p.Z,Expected(w,p.X,p.Y,p.Z)-1e-6,i>=training.Length))).ToArray();
    }
    internal static XyzRadiometricFitResult Fit(bool known=true,IReadOnlyList<XyzRadiometricObservation>? observations=null)
    {
        var obs=observations??Observations();var proof=CommercialBsdfCalibrationTests.Synthetic(known);
        var gamut=XyzCalibrationGamut.Create("Analytic input domain",proof.XyzSha256,obs.Select(p=>new XyzCalibrationColor(p.X,p.Y,p.Z)).ToArray());
        // The positive exp response retains its declared 1e-6 offset. Validate
        // that exact bias explicitly, rather than pretending the targets changed.
        return XyzRadiometricCalibrator.FitAbsoluteRbf(obs,proof,new(.001,2e-6,1e-6),gamut,XyzPositiveCalibrationTests.Conditions,XyzAbsoluteRbfFeatures.LogXyz,12,.01);
    }
    internal static string CalibrationJson()=>JsonSerializer.Serialize(Fit().CreateCalibration("Analytic absolute XYZ",XyzPositiveCalibrationTests.Conditions.Observer,XyzPositiveCalibrationTests.Conditions.Illuminant));
    internal static MeasuredBsdfData Read(bool transmission=false,bool fixedAxes=false)=>XyzGamutCalibrationTests.Read(transmission,fixedAxes,calibrationJson:CalibrationJson());
    internal static double Energy(double wavelength)
    {
        var x=.2/Math.PI;var y=.6/Math.PI;var z=.4/Math.PI;
        return Math.PI*((700-wavelength)/300*Expected(400,x,y,z)+(wavelength-400)/300*Expected(700,x,y,z));
    }
    [Fact]
    public void AnalyticPowerLawIsRecoveredAtUnseenAbsoluteInputsWithoutHeldOutTargets()
    {
        var fit=Fit();Assert.True(fit.ModelValidationPassed);Assert.Equal(18,fit.AbsoluteRbfModel!.Nodes.Count);
        Assert.All(fit.Evidence.Bands,b=>Assert.InRange(Math.Abs(b.ValidationMaximumAbsoluteError-1e-6),0,1e-12));
        var prediction=fit.AbsoluteRbfModel.Evaluate(.064,.19,.126);Assert.Equal(Expected(400,.064,.19,.126),prediction[0],12);
        Assert.All(fit.AbsoluteRbfModel.Evaluate(0,0,0),v=>Assert.Equal(0,v));
        Assert.Throws<InvalidDataException>(()=>fit.AbsoluteRbfModel.Evaluate(10,10,10));
        Assert.Throws<InvalidDataException>(()=>fit.AbsoluteRbfModel.Evaluate(-1,1,1));
        Assert.Throws<NotSupportedException>(()=>((IList<double>)fit.AbsoluteRbfModel.Nodes[0].RadiometricValues)[0]=1);
        var changed=Observations().Select(p=>p.Validation?p with{RadiometricBsdfPerSteradian=p.RadiometricBsdfPerSteradian+.1}:p).ToArray();
        var failed=Fit(observations:changed);Assert.Equal(fit.Evidence.AbsoluteRbfModelSha256,failed.Evidence.AbsoluteRbfModelSha256);Assert.False(failed.ModelValidationPassed);
        Assert.Throws<InvalidDataException>(()=>failed.CreateCalibration("bad",XyzPositiveCalibrationTests.Conditions.Observer,XyzPositiveCalibrationTests.Conditions.Illuminant));
    }
    [Fact]
    public void ConditionsModelBoundsAndTrainingTargetsAreDigestBound()
    {
        var fit=Fit();var c=XyzPositiveCalibrationTests.Conditions;
        Assert.Throws<InvalidDataException>(()=>Fit(false).CreateCalibration("unknown",c.Observer,c.Illuminant));
        Assert.Throws<InvalidDataException>(()=>fit.CreateCalibration("bad","wrong observer",c.Illuminant));
        Assert.Throws<InvalidDataException>(()=>(fit with{AbsoluteRbfModel=fit.AbsoluteRbfModel! with{Smoothing=.02}}).CreateCalibration("bad",c.Observer,c.Illuminant));
        Assert.Throws<InvalidDataException>(()=>(fit with{AbsoluteRbfModel=fit.AbsoluteRbfModel! with{MaximumStrength=2}}).CreateCalibration("bad",c.Observer,c.Illuminant));
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("model.json");File.WriteAllText(path,CalibrationJson());var cal=XyzRadiometricCalibration.Read(path);
        File.WriteAllText(path,JsonSerializer.Serialize(cal with{AbsoluteRbfModel=cal.AbsoluteRbfModel! with{ResponseOffsetPerSteradian=1e-5}}));Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibration.Read(path));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>XyzRadiometricCalibrator.FitAbsoluteRbf(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(.01,.01,1e-6),fit.Evidence.Gamut!,c,XyzAbsoluteRbfFeatures.LogXyz,12,.01,cancel.Token));
    }
    [Fact]
    public void LocalConstantColourCloudUsesItsActualAffineRankAndRejectsInvalidBudgets()
    {
        var nodes=new[]{.1,.2,.3,.4}.Select((s,i)=>new XyzAbsoluteRbfNode("n"+i,s,2*s,3*s,[s-1e-6])).ToArray();
        var model=new XyzAbsoluteRbfModel(XyzAbsoluteRbfFeatures.ChromaticityLogStrength,4,.01,1e-6,[1,1,1],.6,2.4,nodes).Freeze(1);
        Assert.Equal(.25,model.Evaluate(.25,.5,.75)[0],12);
        Assert.Throws<InvalidDataException>(()=>(model with{Smoothing=0}).Freeze(1));Assert.Throws<InvalidDataException>(()=>(model with{NeighbourCount=257}).Freeze(1));
        Assert.Throws<InvalidDataException>(()=>(model with{CoordinateScales=[1,0,1]}).Freeze(1));
        Assert.Throws<InvalidDataException>(()=>(model with{Nodes=[nodes[0],nodes[0],nodes[2],nodes[3]]}).Freeze(1));
    }
    [Theory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)]
    public void ConvertedAbsoluteKnotsConserveActualPowerAndSampleTheSameDensity(bool transmission,bool fixedAxes)
    {
        var data=Read(transmission,fixedAxes);Assert.Equal("MeasuredBsdfGrid/11",data.ComputationProtocol);var model=new MeasuredBsdf(data);var context=SpeosV11XyzTests.Context(0,550);
        var expected=Energy(550);Assert.Equal(expected,model.Integrate(context).Total,11);var random=new Random(73128);double mean=0;const int count=4096;
        for(int i=0;i<count;i++){var s=model.Sample(context,random.NextDouble(),random.NextDouble());var mu=Math.Abs(s.Direction.Z);Assert.Equal(expected,s.Value*mu/s.Pdf,11);Assert.True(s.Pdf>0&&s.Value>=0);mean+=mu;}
        Assert.InRange(Math.Abs(mean/count-2d/3),0,6*Math.Sqrt(1d/(18*count)));
    }
    [Fact]
    public void SharedPivotedSolveHandlesRowExchangeAndRejectsSingularityOrNonfiniteData()
    {
        double[,] matrix={{0,2,1},{3,1,0},{1,0,4}};double[] target=[7,5,13];var result=PivotedLinearSystem.Solve(matrix,target);
        Assert.Equal(1,result[0],12);Assert.Equal(2,result[1],12);Assert.Equal(3,result[2],12);Assert.Equal(0,matrix[0,0]);Assert.Equal(7,target[0]);
        Assert.Throws<InvalidDataException>(()=>PivotedLinearSystem.Solve(new double[,]{{1,1},{2,2}},[1,2]));
        Assert.Throws<InvalidDataException>(()=>PivotedLinearSystem.Solve(new double[,]{{double.NaN}},[1]));
        Assert.Throws<ArgumentException>(()=>PivotedLinearSystem.Solve(new double[1,2],[1]));
    }
}
