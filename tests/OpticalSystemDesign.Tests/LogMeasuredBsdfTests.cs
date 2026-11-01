using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;
using OpticalSystemDesign.Core.Serialization;
using CoreNsc=OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class LogMeasuredBsdfTests
{
    internal static MeasuredBsdfData Analytic(bool transmission=false)
    {
        const double scale=.01;
        var points=(from side in new[]{BsdfSide.Front,BsdfSide.Back} from mu in new[]{.25,.5,.75} from phi in new[]{0d,180,360}
                    let value=scale*(Math.Exp(2*mu+(phi==180?1:0))-1)
                    select new MeasuredBsdfPoint(side,550,20,Math.Acos(mu)*180/Math.PI,phi,transmission?0:value,transmission?value:0)).ToArray();
        return new("Synthetic separable log density","analytic.log.csv",new string('a',64),points,
            angularSupport:BsdfAngularSupport.MeasuredPolarRangeZeroOutside,logInterpolation:new(scale));
    }
    internal static double Integral(int power)
    {
        double Primitive(int n,double x)=>n==0?Math.Exp(2*x)/2:Math.Exp(2*x)*Math.Pow(x,n)/2-n*Primitive(n-1,x)/2;
        var exponential=Primitive(power,.75)-Primitive(power,.25);
        var constant=(Math.Pow(.75,power+1)-Math.Pow(.25,power+1))/(power+1);
        return .01*(2*Math.PI*(Math.E-1)*exponential-2*Math.PI*constant);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void EvaluationPowerAndSamplingUseTheSameAnalyticLogDensity(bool transmission)
    {
        var data=Analytic(transmission);var model=new MeasuredBsdf(data);var c=MeasuredAngularCoverageTests.Context();var expected=Integral(1);
        Assert.Equal("MeasuredBsdfGrid/10",data.ComputationProtocol);Assert.InRange(Math.Abs(model.Integrate(c).Total-expected),0,1e-14);
        var direction=MeasuredAngularCoverageTests.Direction(transmission?180-Math.Acos(.6)*180/Math.PI:Math.Acos(.6)*180/Math.PI,90);
        Assert.InRange(Math.Abs(model.Evaluate(c,direction)-.01*(Math.Exp(1.7)-1)),0,1e-14);
        Assert.Equal(0,model.Evaluate(c,MeasuredAngularCoverageTests.Direction(90)));
        Assert.Equal(0,model.Evaluate(c,new(0,0,transmission?-1:1)));
        const int count=8192;var random=new Random(218811);double sum=0;
        for(int i=0;i<count;i++)
        {var sample=model.Sample(c,random.NextDouble(),random.NextDouble());var mu=Math.Abs(sample.Direction.Z);sum+=mu;Assert.True(double.IsFinite(sample.Pdf)&&sample.Pdf>0);Assert.InRange(mu,.25-1e-15,.75+1e-15);Assert.InRange(Math.Abs(sample.Value*mu/sample.Pdf-expected),0,1e-13);}
        var mean=Integral(2)/expected;var variance=Integral(3)/expected-mean*mean;
        Assert.InRange(Math.Abs(sum/count-mean),0,6*Math.Sqrt(variance/count));
        foreach(var u in new[]{0d,Math.BitDecrement(1d)})foreach(var v in new[]{0d,Math.BitDecrement(1d)})
        {var sample=model.Sample(c,u,v);Assert.True(double.IsFinite(sample.Pdf)&&sample.Pdf>0);Assert.InRange(Math.Abs(sample.Value*Math.Abs(sample.Direction.Z)/sample.Pdf-expected),0,1e-13);}
    }
    [Fact]
    public void HighDynamicRangeKnotsArePreservedWithoutPassivePowerRescaling()
    {
        var points=(from theta in new[]{0d,60,90} from phi in new[]{0d,180,360}
                    select new MeasuredBsdfPoint(BsdfSide.Front,550,20,theta,phi,theta==60&&phi==180?20:1e-6,0)).ToArray();
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData("linear","sharp.csv",new string('a',64),points));
        var data=new MeasuredBsdfData("logarithmic","sharp.csv",new string('a',64),points,logInterpolation:new(1e-6));var model=new MeasuredBsdf(data);var c=MeasuredAngularCoverageTests.Context();
        Assert.InRange(model.Integrate(c).Reflection,0,1);
        foreach(var p in points)Assert.InRange(Math.Abs(model.Evaluate(c,MeasuredAngularCoverageTests.Direction(p.OutgoingThetaDegrees,p.OutgoingPhiDegrees))-p.BrdfPerSteradian),0,1e-11);
        Assert.Equal(points.Select(p=>p.BrdfPerSteradian),data.Points.OrderBy(p=>p.OutgoingThetaDegrees).ThenBy(p=>p.OutgoingPhiDegrees).Select(p=>p.BrdfPerSteradian));
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidLogScaleIsRejected(double scale)
    {var data=Analytic();Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,angularSupport:data.AngularSupport,logInterpolation:new(scale)));}
    [Fact]
    public void ScaleAndMethodAreDigestBoundAndCannotBeCombinedWithProjectedQuantity()
    {
        var data=Analytic();var json=JsonSerializer.Serialize(data);Assert.Equal(data.DataSha256,JsonSerializer.Deserialize<MeasuredBsdfData>(json)!.DataSha256);
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,data.DataSha256,angularSupport:data.AngularSupport,logInterpolation:new(.02)));
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,angularSupport:data.AngularSupport,logInterpolation:new(.01,"unknown")));
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,storedQuantity:BsdfStoredQuantity.ProjectedBsdf,logInterpolation:new(.01)));
    }
    [Fact]
    public void ExplicitCornellLogImportRetainsKnotsAndTheOriginalLinearModelIdentity()
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("cornell.astm");File.WriteAllText(path,MeasuredAngularCoverageTests.Cornell());
        var linear=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550));
        var log=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550,AstmLogInterpolationScalePerSteradian:1e-6));
        Assert.Equal("MeasuredBsdfGrid/9",linear.ComputationProtocol);Assert.Equal("MeasuredBsdfGrid/10",log.ComputationProtocol);Assert.NotEqual(linear.DataSha256,log.DataSha256);Assert.Equal(linear.SourceSha256,log.SourceSha256);
        Assert.Contains("not measurement uncertainty",log.ImportNotes!);
        var model=new MeasuredBsdf(log);foreach(var incidence in new[]{20d,40})foreach(var theta in new[]{30d,60})foreach(var phi in theta==30?new[]{0d,180}:new[]{-90d,0,90,180})
        {var expected=.05+.01*Math.Cos(phi*Math.PI/180);Assert.InRange(Math.Abs(model.Evaluate(MeasuredAngularCoverageTests.Context(incidence),MeasuredAngularCoverageTests.Direction(theta,phi))-expected),0,1e-13);}
    }
    [Fact]
    public async Task DisabledLogarithmicModelCannotDowngradeToV17()
    {
        var scene=CommercialBsdfNscTests.Scene();var plane=scene.Objects[1];scene.Replace(plane.Id,plane with{Enabled=false,Parameters=new CoreNsc.PlaneRectangleParameters(1,1,CoreNsc.NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),SurfaceBsdf=new(CoreNsc.NonSequentialBsdfKind.Measured,0,Analytic())});
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("log.staropt");await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()],0,NonSequentialDocument:scene),path);var bytes=File.ReadAllBytes(path);
        using var raw=new MemoryStream(bytes);raw.Position=52;using var unzip=new BrotliStream(raw,CompressionMode.Decompress);using var json=JsonDocument.Parse(unzip);Assert.Equal(18,json.RootElement.GetProperty("FormatVersion").GetInt32());
        var changed=System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;changed["FormatVersion"]=17;var payload=System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());using var packed=new MemoryStream();using(var zip=new BrotliStream(packed,CompressionLevel.Optimal,true))zip.Write(payload);
        var header=bytes[..52];System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12),payload.Length);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),(int)packed.Length);System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header,20);
        Assert.Contains("version 18",Assert.Throws<InvalidDataException>(()=>StarOptProjectStore.Deserialize(header.Concat(packed.ToArray()).ToArray())).Message);
    }
}
