using System.Globalization;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class MeasuredAngularCoverageTests
{
    internal static MeasuredBsdfData Partial(bool transmission=false,double value=.2)
        => new("Synthetic polar support","analytic.csv",new string('a',64),
            (from side in new[]{BsdfSide.Front,BsdfSide.Back} from theta in new[]{30d,60} from phi in new[]{0d,90,180,270,360}
             select new MeasuredBsdfPoint(side,550,20,theta,phi,transmission?0:value,transmission?value:0)).ToArray(),
            angularSupport:BsdfAngularSupport.MeasuredPolarRangeZeroOutside);
    internal static BsdfContext Context(double degrees=20,Vector3D? axis=null)=>new(new(Math.Sin(degrees*Math.PI/180),0,Math.Cos(degrees*Math.PI/180)),new(0,0,1),550,BsdfSide.Front,axis);
    internal static Vector3D Direction(double theta,double phi=0)=>new(Math.Sin(theta*Math.PI/180)*Math.Cos(phi*Math.PI/180),Math.Sin(theta*Math.PI/180)*Math.Sin(phi*Math.PI/180),Math.Cos(theta*Math.PI/180));

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PartialPolarSupportHasExactEnergyPdfAndSamplingMoments(bool transmission)
    {
        var data=Partial(transmission);var bsdf=new MeasuredBsdf(data);var c=Context();var expected=.1*Math.PI;
        Assert.Equal("MeasuredBsdfGrid/9",data.ComputationProtocol);Assert.InRange(Math.Abs(bsdf.Integrate(c).Total-expected),0,1e-14);
        Assert.Equal(0,bsdf.Evaluate(c,Direction(transmission?180:0)));Assert.Equal(0,bsdf.Evaluate(c,Direction(90)));
        const int n=16384;var rng=new Random(7911);double sum=0;
        for(int i=0;i<n;i++){var s=bsdf.Sample(c,rng.NextDouble(),rng.NextDouble());var mu=Math.Abs(s.Direction.Z);Assert.InRange(mu,.5,Math.Sqrt(.75));Assert.Equal(transmission?BsdfLobe.DiffuseTransmission:BsdfLobe.DiffuseReflection,s.Lobe);Assert.InRange(Math.Abs(s.Value*mu/s.Pdf-expected),0,1e-13);sum+=mu;}
        var mean=2d/3*(Math.Pow(Math.Sqrt(.75),3)-.125)/.5;var variance=(.75*.75-.0625)/(2*.5)-mean*mean;
        Assert.InRange(Math.Abs(sum/n-mean),0,6*Math.Sqrt(variance/n));
    }
    [Fact]
    public void SupportMustBeExplicitBoundedPassiveAndDigestBound()
    {
        var data=Partial();Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points));
        Assert.Throws<InvalidDataException>(()=>Partial(value:1));
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,angularSupport:(BsdfAngularSupport)9));
        var json=JsonSerializer.Serialize(data);Assert.Contains("AngularSupport",json);var copy=JsonSerializer.Deserialize<MeasuredBsdfData>(json)!;Assert.Equal(data.DataSha256,copy.DataSha256);
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData(data.Name,data.SourceFileName,data.SourceSha256,data.Points,data.DataSha256,storedQuantity:BsdfStoredQuantity.ProjectedBsdf,angularSupport:data.AngularSupport));
        Assert.Throws<InvalidDataException>(()=>new MeasuredBsdf(data).Integrate(Context(0)));
    }
    internal static string Cornell()
    {
        var rows=new List<string>();
        foreach(var incidence in new[]{20d,40})foreach(var theta in new[]{30d,60})foreach(var phi in theta==30?new[]{0d,180}:new[]{-90d,0,90,180})
        {var value=.05+.01*Math.Cos(phi*Math.PI/180);rows.Add(string.Join(',',new[]{incidence*Math.PI/180,0,theta*Math.PI/180,phi*Math.PI/180,value/2,value,value*2}.Select(v=>v.ToString("G17",CultureInfo.InvariantCulture))));}
        return "LAB_NAME Cornell University, synthetic parser control\nSYSTEM_NAME Cornell/Kodak gonioreflectometer\nSAMPLE_NAME Synthetic rings\nMEAS_NAME BRDF\nNUM_POINTS "+rows.Count+"\nVARS theta_i,phi_i,theta_s,phi_s,400nm,550nm,700nm\n"+string.Join('\n',rows)+"\n";
    }
    [Fact]
    public void CornellRingUnionRetainsEveryMeasuredKnotAndSelectedBand()
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("rings.astm");File.WriteAllText(path,Cornell());
        var data=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550));var bsdf=new MeasuredBsdf(data);
        Assert.Equal("CornellAstmSpectral/1",data.SourceFormat);Assert.Equal(550,Assert.Single(data.Points.Select(p=>p.WavelengthNanometers).Distinct()));
        foreach(var line in Cornell().Split('\n').Where(l=>l.Length>0&&char.IsDigit(l[0])))
        {var row=line.Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();var c=Context(row[0]*180/Math.PI);Assert.InRange(Math.Abs(bsdf.Evaluate(c,Direction(row[2]*180/Math.PI,row[3]*180/Math.PI))-row[5]),0,1e-14);}
        var all=BsdfFileImporter.Read(path,new(BsdfImportSides.Front));Assert.Equal(3,all.Points.Select(p=>p.WavelengthNanometers).Distinct().Count());
        Assert.Throws<InvalidDataException>(()=>BsdfFileImporter.Read(path));
        Assert.Throws<InvalidDataException>(()=>BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:555)));
        var selected=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550,AstmIncidentThetaDegrees:20));Assert.Equal(20,Assert.Single(selected.Points.Select(p=>p.IncidentThetaDegrees).Distinct()),10);
        Assert.Throws<InvalidDataException>(()=>BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550,AstmIncidentThetaDegrees:21)));
    }
    [Theory] [InlineData("MEAS_NAME BRDF","MEAS_NAME BTDF")] [InlineData("550nm","TristimulusY")] [InlineData("NUM_POINTS 12","NUM_POINTS 13")] [InlineData("theta_i,phi_i","theta_i,phi_s")]
    public void CornellRejectsUnverifiedProfilesAndMalformedTables(string oldValue,string newValue)
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("bad.astm");File.WriteAllText(path,Cornell().Replace(oldValue,newValue));
        Assert.Throws<InvalidDataException>(()=>BsdfFileImporter.Read(path,new(BsdfImportSides.Front,AstmWavelengthNanometers:550)));
    }
    internal static string Normal4D(bool invalidTilt=false)=>$"Source Measured\nSymmetry ASymmetrical4D\nSpectralContent Monochrome\nScatterType BTDF\nSampleRotation 1\n0\nAngleOfIncidence 1\n{(invalidTilt?10:0)}\nScatterAzimuth 5\n0 90 180 270 360\nScatterRadial 3\n0 45 85\nMonochrome\nDataBegin\nTIS 0.6\n1 2 1\n1 1 1\n1 0.5 1\n1 1 1\n1 2 1\nDataEnd\n";
    [Fact]
    public void Normal4DSingleRotationRetainsAnisotropyAndRejectsUnmeasuredIncidence()
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var path=files.Path("normal.bsdf");File.WriteAllText(path,Normal4D());
        var data=BsdfFileImporter.Read(path,new(BsdfImportSides.Front,550,true));var bsdf=new MeasuredBsdf(data);var c=Context(0,new(1,0,0));
        Assert.Equal("MeasuredBsdfGrid/3",data.ComputationProtocol);Assert.InRange(Math.Abs(bsdf.Integrate(c).Transmission-.6),0,1e-14);
        Assert.True(Math.Abs(bsdf.Evaluate(c,Direction(135,90))-bsdf.Evaluate(c,Direction(135,270)))>1e-3);
        Assert.Throws<InvalidDataException>(()=>bsdf.Integrate(Context(10,new(1,0,0))));File.WriteAllText(path,Normal4D(true));Assert.Throws<InvalidDataException>(()=>BsdfFileImporter.Read(path,new(BsdfImportSides.Front,550,true)));
    }
}
