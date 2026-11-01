using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class XyzGamutCalibrationTests
{
    internal static readonly XyzCalibrationColor[] Colors = [new(.2,.6,.4),new(.1,.5,.6),new(.3,.7,.2),new(.25,.8,.4),new(.16,.64,.5),new(.24,.72,.3)];
    internal static XyzRadiometricObservation[] Observations(double scale=1) => new[]{400d,700}.SelectMany(w=>Colors.Select((p,i)=>
        new XyzRadiometricObservation("colour-"+i,w,p.X,p.Y,p.Z,scale*(w==400?-.2*p.X+.8*p.Y-.1*p.Z:-.1*p.X+.5*p.Y-.1*p.Z),i>=4))).ToArray();
    internal static XyzCalibrationGamut Gamut(XyzCalibrationColor[]? colors=null) => XyzCalibrationGamut.Create("Synthetic input feature domain, not measurement",new string('1',64),colors??Colors);
    internal static XyzRadiometricFitResult Fit(double scale=1) => XyzRadiometricCalibrator.FitWithinGamut(Observations(scale),CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),Gamut());
    internal static string CalibrationJson(double scale=1) => JsonSerializer.Serialize(Fit(scale).CreateCalibration("Synthetic signed/domain calibration","Synthetic observer","Synthetic source"));
    internal static MeasuredBsdfData Read(bool transmission=false,bool fixedAxes=false,double scale=1,string? text=null,string? calibrationJson=null)
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var source=files.Path("domain.BSDF");var calibration=files.Path("domain.json");
        File.WriteAllText(source,text??SpeosV11XyzTests.XyzText(transmission,fixedAxes));File.WriteAllText(calibration,calibrationJson??CalibrationJson(scale));
        return BsdfFileImporter.Read(source,new(BsdfImportSides.BothDeclaredIdentical,ZemaxXyzCalibrationPath:calibration));
    }
    [Fact]
    public void SignedCoefficientsRecoverKnownTransferInsideAnExplicitImmutableDomain()
    {
        var fit=Fit();Assert.True(fit.ModelValidationPassed);Assert.Equal(-.2,fit.Bands[0].X,12);Assert.Equal(.8,fit.Bands[0].Y,12);Assert.Equal(-.1,fit.Bands[0].Z,12);
        var domain=fit.Evidence.Gamut!;Assert.True(domain.Contains(.2,.6,.4));Assert.True(domain.Contains(0,0,0));Assert.False(domain.Contains(1,0,0));Assert.False(domain.Contains(-1,1,1));
        Assert.Throws<NotSupportedException>(()=>((IList<XyzCalibrationColor>)domain.Vertices)[0]=Colors[0]);
        var legacy=XyzRadiometricCalibrator.Fit(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9));Assert.False(legacy.ModelValidationPassed);
        Assert.All(fit.Evidence.Bands,b=>Assert.InRange(b.ValidationMaximumAbsoluteError,0,1e-12));
        var modified=Observations().Select(p=>p.Validation?p with{RadiometricBsdfPerSteradian=p.RadiometricBsdfPerSteradian+.1}:p).ToArray();
        var held=XyzRadiometricCalibrator.FitWithinGamut(modified,CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),Gamut());
        Assert.Equal(fit.Bands,held.Bands);Assert.False(held.ModelValidationPassed);Assert.Throws<InvalidDataException>(()=>held.CreateCalibration("bad","o","i"));
    }
    [Fact]
    public void OrthantDomainMatchesIndependentLegacyNnlsBoundarySolution()
    {
        var rows=CommercialBsdfCalibrationTests.Observations();var orthant=Gamut([new(1,0,0),new(0,1,0),new(0,0,1)]);
        var positive=XyzRadiometricCalibrator.Fit(rows,CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9));
        var constrained=XyzRadiometricCalibrator.FitWithinGamut(rows,CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),orthant);
        for(var i=0;i<positive.Bands.Count;i++){Assert.Equal(positive.Bands[i].X,constrained.Bands[i].X,12);Assert.Equal(positive.Bands[i].Y,constrained.Bands[i].Y,12);Assert.Equal(positive.Bands[i].Z,constrained.Bands[i].Z,12);}
        // Nonnegative target observations whose unconstrained optimum has a negative X.
        var v=new[]{new[]{1d,.6,.3},new[]{.2,1d,.4},new[]{.3,.3,1d},new[]{.6,.8,.7},new[]{.4,.7,.8},new[]{.5,.9,.2}};
        var active=v.Select((p,i)=>new XyzRadiometricObservation("a"+i,550,p[0],p[1],p[2],-.2*p[0]+.5*p[1]+.4*p[2],i>=4)).ToArray();
        var a=XyzRadiometricCalibrator.Fit(active,CommercialBsdfCalibrationTests.Synthetic(),new(0,0,1e-9));
        var b=XyzRadiometricCalibrator.FitWithinGamut(active,CommercialBsdfCalibrationTests.Synthetic(),new(0,0,1e-9),orthant);
        Assert.InRange(Math.Abs(a.Bands[0].X-b.Bands[0].X),0,1e-12);Assert.Equal(a.Bands[0].Y,b.Bands[0].Y,12);Assert.Equal(a.Bands[0].Z,b.Bands[0].Z,12);
    }
    [Fact]
    public void ChangedDomainUnknownConditionsOutOfDomainAndCancellationCannotExport()
    {
        var fit=Fit();var changed=fit with{Evidence=fit.Evidence with{Gamut=Gamut([..Colors,new(.01,.98,.01)])}};
        Assert.Throws<InvalidDataException>(()=>changed.CreateCalibration("tampered","observer","source"));
        var unknown=XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(false),new(1e-10,1e-10,1e-9),Gamut());
        Assert.True(unknown.ModelValidationPassed);Assert.Throws<InvalidDataException>(()=>unknown.CreateCalibration("unknown","unknown","unknown"));
        Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),Gamut() with{SourceSha256=new string('a',64)}));
        var outside=Observations();outside[0]=outside[0] with{X=1,Y=0,Z=0};Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibrator.FitWithinGamut(outside,CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),Gamut()));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),Gamut(),cancel.Token));
    }
    [Fact]
    public void InvalidConcaveCollinearOversizedAndSelfCrossingDomainsAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>Gamut([new(1,1,1),new(2,2,2),new(3,3,3)]));
        Assert.Throws<InvalidDataException>(()=>Gamut([new(-1,1,1),..Colors]));
        var bad=Gamut() with{Vertices=new[]{new XyzCalibrationColor(1,0,0),new(0,1,0),new(.4,.4,.2),new(0,0,1)}};
        Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),bad));
        var crossed=Gamut() with{Vertices=new[]{new XyzCalibrationColor(.1,.1,.8),new(.7,.2,.1),new(.1,.7,.2),new(.7,.1,.2)}};
        Assert.Throws<InvalidDataException>(()=>XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1,1,1e-9),crossed));
        var ring=Enumerable.Range(0,65).Select(i=>new XyzCalibrationColor(.3+.05*Math.Cos(i*2*Math.PI/65),.3+.05*Math.Sin(i*2*Math.PI/65),.4-.05*Math.Cos(i*2*Math.PI/65)-.05*Math.Sin(i*2*Math.PI/65))).ToArray();
        Assert.Throws<InvalidDataException>(()=>Gamut(ring));
    }
    [Theory] [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)]
    public void RadiometricDistributionUsesPositivePowerAndPdfForSignedTransfer(bool transmission,bool fixedAxes)
    {
        var data=Read(transmission,fixedAxes);Assert.Equal(MeasuredBsdfData.GamutXyzProtocol,data.ComputationProtocol);var model=new MeasuredBsdf(data);
        var context=SpeosV11XyzTests.Context(0,550,axis:new Vector3D(1,0,0));var energy=model.Integrate(context);
        Assert.Equal(transmission?0:.32,energy.Reflection,11);Assert.Equal(transmission?.32:0,energy.Transmission,11);
        var random=new Random(39217);double mean=0;const int count=4096;
        for(var i=0;i<count;i++){var sample=model.Sample(context,random.NextDouble(),random.NextDouble());var mu=Math.Abs(sample.Direction.Z);Assert.True(sample.Value>=0&&sample.Pdf>0);Assert.Equal(.32,sample.Value*mu/sample.Pdf,11);mean+=mu;Assert.Equal(transmission?BsdfLobe.DiffuseTransmission:BsdfLobe.DiffuseReflection,sample.Lobe);}
        Assert.InRange(Math.Abs(mean/count-2d/3),0,6*Math.Sqrt(1d/(18*count)));
        Assert.Throws<InvalidDataException>(()=>model.Integrate(SpeosV11XyzTests.Context(0,399)));
    }
    [Fact]
    public void IndependentTisNormalizationPrecedesTransferAndBadPowerIsRejected()
    {
        var text=SpeosV11XyzTests.XyzText();var marker=text.IndexOf("TristimulusY",StringComparison.Ordinal);
        var changed=text[..marker].Replace("1 1 1 1 1","17 17 17 17 17")+text[marker..];
        var a=new MeasuredBsdf(Read());var b=new MeasuredBsdf(Read(text:changed));var c=SpeosV11XyzTests.Context(20,550);
        foreach(var t in new[]{0d,20,60,89}){var d=new Vector3D(Math.Sin(t*Math.PI/180),0,Math.Cos(t*Math.PI/180));Assert.Equal(a.Evaluate(c,d),b.Evaluate(c,d),11);}
        Assert.Throws<InvalidDataException>(()=>Read(scale:4));
        Assert.Throws<InvalidDataException>(()=>Read(text:text.Replace("TIS 0.2","TIS 0.9")));
    }
    [Fact]
    public void NonconstantSignedChannelsMatchIndependentDensityMomentsAndQuadrants()
    {
        double[] slopes=[.2,-.1,.05];double[] tis=[.2,.6,.4];double[] radial=[0,60,90,120,180];
        var features=radial.Select(t=>{var mu=Math.Cos(t*Math.PI/180);var v=Enumerable.Range(0,3).Select(j=>tis[j]*(1+slopes[j]*mu)/(Math.PI*(1+2*slopes[j]/3))).ToArray();return new XyzCalibrationColor(v[0],v[1],v[2]);}).ToArray();
        var domain=Gamut([..Colors,..features]);var fit=XyzRadiometricCalibrator.FitWithinGamut(Observations(),CommercialBsdfCalibrationTests.Synthetic(),new(1e-10,1e-10,1e-9),domain);
        var json=JsonSerializer.Serialize(fit.CreateCalibration("Signed analytic angular control","Synthetic observer","Synthetic source"));
        var text=SpeosV11XyzTests.XyzText(incidences:[0]);
        for(int j=0;j<3;j++)
        {
            var begin=text.IndexOf(new[]{"TristimulusX","TristimulusY","TristimulusZ"}[j],StringComparison.Ordinal);var end=text.IndexOf("DataEnd",begin,StringComparison.Ordinal);
            var values=string.Join(" ",radial.Select(t=>(1+slopes[j]*Math.Cos(t*Math.PI/180)).ToString("G17",System.Globalization.CultureInfo.InvariantCulture)));
            text=text[..begin]+text[begin..end].Replace("1 1 1 1 1",values)+text[end..];
        }
        var model=new MeasuredBsdf(Read(text:text,calibrationJson:json));var c=SpeosV11XyzTests.Context(0,550);double[] coefficient=[-.15,.65,-.1];
        double Expected(double mu)=>Enumerable.Range(0,3).Sum(j=>coefficient[j]*tis[j]*(1+slopes[j]*mu)/(Math.PI*(1+2*slopes[j]/3)));
        foreach(var mu in new[]{.01,.2,.5,.9,1})Assert.InRange(Math.Abs(Expected(mu)-model.Evaluate(c,new(Math.Sqrt(1-mu*mu),0,mu))),0,1e-10);
        var expectedMean=Enumerable.Range(0,3).Sum(j=>coefficient[j]*tis[j]*(2d/3+slopes[j]/2)/(1+2*slopes[j]/3))/.32;
        var expectedSecond=Enumerable.Range(0,3).Sum(j=>coefficient[j]*tis[j]*(.5+2*slopes[j]/5)/(1+2*slopes[j]/3))/.32;
        var random=new Random(81931);double sum=0;const int count=8192;var quadrants=new int[4];
        for(int i=0;i<count;i++){var s=model.Sample(c,random.NextDouble(),random.NextDouble());Assert.Equal(.32,s.Value*Math.Abs(s.Direction.Z)/s.Pdf,11);sum+=Math.Abs(s.Direction.Z);var phi=Math.Atan2(s.Direction.Y,s.Direction.X);if(phi<0)phi+=2*Math.PI;quadrants[Math.Min(3,(int)(phi/(Math.PI/2)))]++;}
        Assert.InRange(Math.Abs(sum/count-expectedMean),0,6*Math.Sqrt((expectedSecond-expectedMean*expectedMean)/count));
        Assert.All(quadrants,q=>Assert.InRange(Math.Abs(q-count/4d),0,6*Math.Sqrt(count*.25*.75)));
    }
}
