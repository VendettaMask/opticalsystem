using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class XyzPositiveCalibrationTests
{
    internal static XyzSpectralProjector Projector(double sourceScale=1) => new("Analytic observer","Analytic illuminant",new string('a',64),new string('b',64),
        [new(200,0,1,1),new(300,.5,1,.5),new(400,1,1,0)], [new(200,sourceScale),new(275,1.375*sourceScale),new(400,2*sourceScale)],200,400);
    internal static XyzColorimetricConditions Conditions => new("Synthetic calibrated observer","Synthetic calibrated source",new string('a',64),new string('b',64),new string('c',64),400,700,XyzColorimetricConditions.ExactMethod);
    internal static double Ratio(double wave,XyzCalibrationColor p)
    { var s=p.X+p.Y+p.Z;var x=p.X/s;var y=p.Y/s;var z=p.Z/s;return wave==400?.1+.2*x*x+.1*y:.2+.15*y*y+.05*z; }
    internal static XyzRadiometricObservation[] Observations() => new[]{400d,700}.SelectMany(w=>XyzGamutCalibrationTests.Colors.SelectMany((p,i)=>new[]{
        new XyzRadiometricObservation("train-"+i,w,p.X,p.Y,p.Z,(p.X+p.Y+p.Z)*Ratio(w,p),false),
        new XyzRadiometricObservation("hold-"+i,w,2*p.X,2*p.Y,2*p.Z,2*(p.X+p.Y+p.Z)*Ratio(w,p),true)})).ToArray();
    internal static XyzRadiometricFitResult Fit(bool known=true,double scale=1)
    {
        var obs=Observations().Select(p=>p with{RadiometricBsdfPerSteradian=p.RadiometricBsdfPerSteradian*scale}).ToArray();
        return XyzRadiometricCalibrator.FitPositiveColorTransfer(obs,CommercialBsdfCalibrationTests.Synthetic(known),new(1e-10,1e-10,1e-9),XyzGamutCalibrationTests.Gamut(),Conditions,6);
    }
    internal static string CalibrationJson(double scale=1)=>JsonSerializer.Serialize(Fit(scale:scale).CreateCalibration("Synthetic positive colour transfer",Conditions.Observer,Conditions.Illuminant));
    internal static MeasuredBsdfData Read(bool transmission=false,bool fixedAxes=false,double scale=1,string? text=null)=>XyzGamutCalibrationTests.Read(transmission,fixedAxes,text:text,calibrationJson:CalibrationJson(scale));
    internal static double Energy(double wave)=>1.2*((700-wave)/300*Ratio(400,XyzGamutCalibrationTests.Colors[0])+(wave-400)/300*Ratio(700,XyzGamutCalibrationTests.Colors[0]));

    [Fact]
    public void ExactProjectionMatchesIndependentCubicIntegralAndSourceScaling()
    {
        var projector=Projector();XyzSpectralSample[] spectrum=[new(200,0),new(400,1)];var xyz=projector.Project(spectrum);
        Assert.Equal(7d/18,xyz.X,13);Assert.Equal(5d/9,xyz.Y,13);Assert.Equal(1d/6,xyz.Z,13);
        var refined=projector.Project([new(200,0),new(250,.25),new(300,.5),new(350,.75),new(400,1)]);
        Assert.Equal(xyz.X,refined.X,13);Assert.Equal(xyz.Y,refined.Y,13);Assert.Equal(xyz.Z,refined.Z,13);
        var scaled=Projector(10).Project(spectrum);Assert.Equal(xyz.X,scaled.X,13);Assert.Equal(xyz.Y,scaled.Y,13);Assert.Equal(xyz.Z,scaled.Z,13);
        Assert.NotEqual(projector.Conditions.ProjectionTablesSha256,Projector(10).Conditions.ProjectionTablesSha256);
    }
    [Fact]
    public void SpectralConditionsRejectUnmeasuredBandNegativeOrInvalidDataAndCancellation()
    {
        var p=Projector();Assert.Throws<InvalidDataException>(()=>p.Project([new(201,1),new(400,1)]));Assert.Throws<InvalidDataException>(()=>p.Project([new(200,-1),new(400,1)]));
        Assert.Throws<InvalidDataException>(()=>p.Project([new(400,1),new(200,1)]));Assert.Throws<InvalidDataException>(()=>Projector(0));
        Assert.Throws<InvalidDataException>(()=>new XyzSpectralProjector("o","s",new string('a',64),new string('b',64),[new(200,1,1,1),new(400,1,1,1)],[new(250,1),new(400,1)],200,400));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>p.Project([new(200,1),new(400,1)],cancel.Token));
    }
    [Fact]
    public void PositiveFitIsHomogeneousAndNeverStoresValidationTargets()
    {
        var fit=Fit();Assert.True(fit.ModelValidationPassed);Assert.Equal(6,fit.PositiveModel!.Nodes.Count);Assert.NotNull(fit.Evidence.Conditions);
        var p=XyzGamutCalibrationTests.Colors[0];var expected=(p.X+p.Y+p.Z)*Ratio(400,p);Assert.Equal(expected,fit.PositiveModel.Evaluate(p.X,p.Y,p.Z)[0],13);
        Assert.Equal(3*expected,fit.PositiveModel.Evaluate(3*p.X,3*p.Y,3*p.Z)[0],12);Assert.All(fit.PositiveModel.Evaluate(0,0,0),v=>Assert.Equal(0,v));
        Assert.Throws<NotSupportedException>(()=>((IList<double>)fit.PositiveModel.Nodes[0].Ratios)[0]=1);
        var obs=Observations().Select(q=>q.Validation?q with{RadiometricBsdfPerSteradian=q.RadiometricBsdfPerSteradian+.1}:q).ToArray();
        var failed=XyzRadiometricCalibrator.FitPositiveColorTransfer(obs,CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),XyzGamutCalibrationTests.Gamut(),Conditions,6);
        Assert.False(failed.ModelValidationPassed);Assert.Equal(fit.Evidence.PositiveModelSha256,failed.Evidence.PositiveModelSha256);Assert.Throws<InvalidDataException>(()=>failed.CreateCalibration("bad",Conditions.Observer,Conditions.Illuminant));
    }
    [Fact]
    public void UnknownOrRelabelledConditionsAndChangedModelsCannotExport()
    {
        var fit=Fit();Assert.Throws<InvalidDataException>(()=>Fit(false).CreateCalibration("unknown",Conditions.Observer,Conditions.Illuminant));
        Assert.Throws<InvalidDataException>(()=>fit.CreateCalibration("bad","different observer",Conditions.Illuminant));
        var changed=fit with{PositiveModel=fit.PositiveModel! with{NeighbourCount=5}};Assert.Throws<InvalidDataException>(()=>changed.CreateCalibration("bad",Conditions.Observer,Conditions.Illuminant));
        var altered=fit with{Evidence=fit.Evidence with{Conditions=Conditions with{IlluminantSourceSha256=new string('f',64)}}};Assert.Throws<InvalidDataException>(()=>altered.CreateCalibration("bad",Conditions.Observer,Conditions.Illuminant));
        var obs=Observations().Select(p=>p.SampleId=="train-0"?p with{X=0,Y=0,Z=0}:p).ToArray();Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibrator.FitPositiveColorTransfer(obs,CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),XyzGamutCalibrationTests.Gamut(),Conditions,6));
    }
    [Fact]
    public void ExactChromaticityTiesAreDeterministicAndNonnegative()
    {
        var model=new XyzPositiveColorModel(1,[new("a",.2,.5,.3,[.1]),new("b",.2,.5,.3,[.3]),new("c",.3,.4,.3,[.7])]).Freeze(1);
        Assert.Equal(.2,model.Evaluate(.2,.5,.3)[0],13);Assert.True(model.Evaluate(.21,.48,.31)[0]>=0);
        Assert.Throws<InvalidDataException>(()=>(model with{NeighbourCount=17}).Freeze(1));Assert.Throws<InvalidDataException>(()=>(model with{Nodes=[new("a",.2,.5,.3,[-1]),new("b",.2,.5,.3,[1]),new("c",.3,.4,.3,[1])]}).Freeze(1));
    }
    [Theory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)]
    public void PositiveCalibrationUsesActualProjectedEnergyAndValidDirectionalSampling(bool transmission,bool fixedAxes)
    {
        var data=Read(transmission,fixedAxes);Assert.Equal("MeasuredBsdfGrid/8",data.ComputationProtocol);var model=new MeasuredBsdf(data);var context=SpeosV11XyzTests.Context(0,550);
        var expected=Energy(550);Assert.Equal(expected,model.Integrate(context).Total,11);
        var random=new Random(28715);double mean=0;const int count=4096;
        for(var i=0;i<count;i++){var s=model.Sample(context,random.NextDouble(),random.NextDouble());var mu=Math.Abs(s.Direction.Z);Assert.True(s.Value>=0&&s.Pdf>0);Assert.Equal(expected,s.Value*mu/s.Pdf,11);mean+=mu;}
        Assert.InRange(Math.Abs(mean/count-2d/3),0,6*Math.Sqrt(1d/(18*count)));
        Assert.Throws<InvalidDataException>(()=>Read(transmission,scale:100));
    }
    [Fact]
    public void NonlinearConversionIntegratesConvertedKnotsInsteadOfMappingChannelTis()
    {
        double[] slopes=[.2,-.1,.05],tis=[.2,.6,.4],radial=[0,60,90,120,180];
        double[] Color(double mu)=>Enumerable.Range(0,3).Select(j=>tis[j]*(1+slopes[j]*mu)/(Math.PI*(1+2*slopes[j]/3))).ToArray();
        var extra=radial.Select(t=>{var v=Color(Math.Cos(t*Math.PI/180));return new XyzCalibrationColor(v[0],v[1],v[2]);}).ToArray();
        var domain=XyzGamutCalibrationTests.Gamut([..XyzGamutCalibrationTests.Colors,..extra]);
        var fit=XyzRadiometricCalibrator.FitPositiveColorTransfer(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),domain,Conditions,6);
        var text=SpeosV11XyzTests.XyzText(incidences:[0]);
        for(int j=0;j<3;j++){var at=text.IndexOf(new[]{"TristimulusX","TristimulusY","TristimulusZ"}[j],StringComparison.Ordinal);var end=text.IndexOf("DataEnd",at,StringComparison.Ordinal);var values=string.Join(" ",radial.Select(t=>(1+slopes[j]*Math.Cos(t*Math.PI/180)).ToString("G17",System.Globalization.CultureInfo.InvariantCulture)));text=text[..at]+text[at..end].Replace("1 1 1 1 1",values)+text[end..];}
        var json=JsonSerializer.Serialize(fit.CreateCalibration("Nonlinear angular control",Conditions.Observer,Conditions.Illuminant));
        var model=new MeasuredBsdf(XyzGamutCalibrationTests.Read(text:text,calibrationJson:json));var context=SpeosV11XyzTests.Context(0,550);
        double Reference(double[] color)
        {
            var sum=color.Sum();var list=fit.PositiveModel!.Nodes.Select(p=>(Node:p,D:Math.Pow(color[0]/sum-p.X,2)+Math.Pow(color[1]/sum-p.Y,2)+Math.Pow(color[2]/sum-p.Z,2))).OrderBy(p=>p.D).ThenBy(p=>p.Node.SampleId,StringComparer.Ordinal).ToArray();
            var exact=list.Where(p=>p.D==0).ToArray();if(exact.Length>0)return sum*exact.Average(p=>(p.Node.Ratios[0]+p.Node.Ratios[1])/2);
            var nearest=list.Take(6).ToArray();return sum*nearest.Sum(p=>(p.Node.Ratios[0]+p.Node.Ratios[1])/2/p.D)/nearest.Sum(p=>1/p.D);
        }
        double[] mus=[0,.5,1];var valuesAt=mus.Select(mu=>Reference(Color(mu))).ToArray();
        double Integral(int power)
        {
            double result=0;for(int i=0;i<2;i++){var a=mus[i];var b=mus[i+1];var p=(Math.Pow(b,power+1)-Math.Pow(a,power+1))/(power+1);var q=(Math.Pow(b,power+2)-Math.Pow(a,power+2))/(power+2)-a*p;result+=valuesAt[i]*p+(valuesAt[i+1]-valuesAt[i])/(b-a)*q;}return 2*Math.PI*result;
        }
        var energy=Integral(1);Assert.Equal(energy,model.Integrate(context).Total,10);Assert.True(Math.Abs(energy-Reference(tis))>1e-7);
        foreach(var mu in new[]{.01,.2,.5,.9,1}){var i=mu<.5?0:1;var t=(mu-mus[i])/.5;var expected=valuesAt[i]*(1-t)+valuesAt[i+1]*t;Assert.Equal(expected,model.Evaluate(context,new(Math.Sqrt(1-mu*mu),0,mu)),10);}
        const int count=8192;var random=new Random(57843);double mean=0;
        for(int i=0;i<count;i++){var s=model.Sample(context,random.NextDouble(),random.NextDouble());Assert.Equal(energy,s.Value*Math.Abs(s.Direction.Z)/s.Pdf,10);mean+=Math.Abs(s.Direction.Z);}
        var expectedMean=Integral(2)/energy;var second=Integral(3)/energy;Assert.InRange(Math.Abs(mean/count-expectedMean),0,6*Math.Sqrt((second-expectedMean*expectedMean)/count));
    }
    [Fact]
    public void FileBudgetsAndPersistedPositiveEvidenceCannotBeBypassed()
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("positive.json");File.WriteAllText(path,CalibrationJson());var original=XyzRadiometricCalibration.Read(path);Assert.NotNull(original.PositiveModel);
        File.WriteAllText(path,JsonSerializer.Serialize(original with{PositiveModel=original.PositiveModel! with{NeighbourCount=5}}));Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibration.Read(path));
        File.WriteAllText(path,SpeosV11XyzTests.Calibration()+new string(' ',65536));Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibration.Read(path));
        using(var file=File.Create(path))file.SetLength(4*1024*1024+1);Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibration.Read(path));
    }
}
