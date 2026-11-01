using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class SpecialSagSurfaceTests
{
    [Theory]
    [InlineData("fringe-nonzero","MM",1)] [InlineData("fringe-nonzero","CM",10)] [InlineData("fringe-nonzero","IN",25.4)]
    [InlineData("fringe-mirror","MM",1)] [InlineData("fringe-mirror","CM",10)] [InlineData("fringe-mirror","IN",25.4)]
    [InlineData("cheb-nonzero","MM",1)] [InlineData("cheb-nonzero","CM",10)] [InlineData("cheb-nonzero","IN",25.4)]
    [InlineData("mems-rows","MM",1)] [InlineData("mems-rows","CM",10)] [InlineData("mems-rows","IN",25.4)]
    public void ImportedSagAndPhysicalRaysMatchFrozenNativeOperandsAcrossUnitsAndRoundtrips(string id,string unit,double scale)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","special-sag-native-v1.json")));
        var data=fixture.RootElement.GetProperty("cases").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==id);
        var source=data.GetProperty("source").GetString()!.Replace("UNIT MM","UNIT "+unit);
        var optic=OpticalFormatCatalog.Import(source,".zmx");
        foreach(var candidate in new[]{optic,Optic.FromSnapshot(optic.ToSnapshot()),OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(optic,".zmx"),".zmx")})
        {
            var surface=candidate.SurfaceGroup.Items[1];
            foreach(var row in data.GetProperty("rows").EnumerateArray())
            {
                var p=row.GetProperty("input");var code=p.GetProperty("code").GetString()!;
                double N(string key)=>p.GetProperty(key).GetDouble();
                var expected=row.GetProperty("value").GetDouble();double actual;
                if(code=="SSAG") actual=surface.Geometry.Sag(scale*N("data1"),scale*N("data2"));
                else
                {
                    var bundle=candidate.SequentialRayTracer.RayGenerator.GenerateGeneric(N("data1"),N("data2"),N("data3"),N("data4"),.55);
                    using var trace=candidate.SequentialRayTracer.Trace(bundle,TraceRequest.Selected([1]));
                    Assert.True(trace.TryGetSample(0,1,out var sample));Assert.False(sample.Vignetted);
                    var position=surface.CoordinateSystem.ToLocalPoint(sample.Position);
                    var direction=surface.CoordinateSystem.ToLocalDirection(sample.Direction);
                    actual=code switch{"REAX"=>position.X,"REAY"=>position.Y,"REAZ"=>position.Z,
                        "REAA"=>direction.X,"REAB"=>direction.Y,"REAC"=>direction.Z,_=>throw new InvalidDataException(code)};
                }
                var isDirection=code is "REAA" or "REAB" or "REAC";
                expected*=isDirection?data.GetProperty("reflectionParity").GetInt32():scale;
                Assert.InRange(Math.Abs(actual-expected),0,isDirection?1e-12:1e-10*scale);
            }
        }
    }

    [Fact]
    public void ExtrapolationAndRectangularNormalizationKeepTheirOwnDomains()
    {
        var truncated=new FringeSagGeometry(0,0,[],1,[.2],extrapolate:false);
        var extrapolated=new FringeSagGeometry(0,0,[],1,[.2],extrapolate:true);
        Assert.Equal(.2,truncated.Sag(1,0));Assert.Equal(0,truncated.Sag(1.01,0));Assert.Equal(.2,extrapolated.Sag(1.01,0));
        var cheb=new ChebyshevSagGeometry(0,2,2,4,5,[.03,.01,.04,0,0,.02,0,0]);
        Assert.True(double.IsFinite(cheb.Sag(4,5)));Assert.True(double.IsNaN(cheb.Sag(4.01,0)));
        var normal=cheb.SurfaceNormal(new(.5,.25,cheb.Sag(.5,.25)));
        var expected=new Vector3D(-(.15+.05*.5),-(.16+.064*.25),1);expected/=expected.Length;
        Assert.InRange((normal-expected).Length,0,1e-14);
    }

    [Fact]
    public void ExtendedMultiConfigurationParametersChangeTheFringeCoefficientWithoutChangingOtherConfigurations()
    {
        const string source="MODE SEQ\nUNIT CM\nENPD 1\nMNUM 2\nPRAM 1 2 .08 0 18\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE FZERNSAG\nPARM 13 4\nPARM 14 2\nPARM 18 .04\nDISZ 10\nSURF 2";
        var configurations=new ZemaxZmxImporter().ImportConfigurationSet(source).Configurations;
        Assert.Equal(.4,Assert.IsType<FringeSagGeometry>(configurations[0].SurfaceGroup.Items[1].Geometry).FringeCoefficients[3]);
        Assert.Equal(.8,Assert.IsType<FringeSagGeometry>(configurations[1].SurfaceGroup.Items[1].Geometry).FringeCoefficients[3]);
    }

    [Theory] [InlineData(".txt")] [InlineData(".seq")] [InlineData(".len")]
    public void ExportersWithoutSpecialSurfaceColumnsRejectLossyFlattening(string extension)
    {
        var optic=OpticalFormatCatalog.Import("MODE SEQ\nENPD 2\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE TILTSURF\nPARM 2 .4\nDISZ 10\nSURF 2",".zmx");
        Assert.Throws<NotSupportedException>(()=>OpticalFormatCatalog.Export(optic,extension));
    }
}
