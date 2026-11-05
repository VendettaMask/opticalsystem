using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class SpecialSagSurfaceTests
{
    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(1, 3, 2)]
    public async Task GridSagConfigurationsIgnoreOnlyTheSelectedSurfaceAndPassNeutralOutsideRays(
        int configuration, int ignored, int active)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-closure", "Grid Sag Example.zmx");
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source);
        var optic = Optic.FromSnapshot(imported.Configurations[configuration].ToSnapshot());
        var reference = optic.SurfaceGroup.Items[ignored];
        Assert.IsType<CoordinateReferenceInteractionModel>(reference.InteractionModel);
        Assert.Null(reference.PhysicalAperture);
        Assert.Equal(0, reference.Thickness);

        var grid = Assert.IsType<GridSagGeometry>(optic.SurfaceGroup.Items[active].Geometry);
        Assert.True(double.IsNaN(grid.Sag(0, 4)));
        Assert.False(grid.DistanceToIntersection(new(0, 4, -1), new(0, 0, 1)).IsHit);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .8, .55, false);
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([active, 4]));
        foreach (var surface in new[] { active, 4 })
        {
            Assert.True(trace.TryGetSample(0, surface, out var hit));
            Assert.False(hit.Vignetted);
            Assert.Equal(4, hit.Position.Y, 10);
            Assert.Equal(0, hit.Position.Z, 10);
            Assert.Equal(1, hit.Intensity, 10);
        }
    }

    [Theory]
    [InlineData("fringe-nonzero","MM",1)] [InlineData("fringe-nonzero","CM",10)] [InlineData("fringe-nonzero","IN",25.4)]
    [InlineData("fringe-mirror","MM",1)] [InlineData("fringe-mirror","CM",10)] [InlineData("fringe-mirror","IN",25.4)]
    [InlineData("cheb-nonzero","MM",1)] [InlineData("cheb-nonzero","CM",10)] [InlineData("cheb-nonzero","IN",25.4)]
    [InlineData("mems-rows","MM",1)] [InlineData("mems-rows","CM",10)] [InlineData("mems-rows","IN",25.4)]
    public void ImportedSagAndPhysicalRaysMatchFrozenNativeOperandsAcrossUnitsAndRoundtrips(string id,string unit,double scale)
    {
        using var fixture=JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","special-sag-native-v1.json")));
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
    public void ChebyshevFirstOrderIncludesTheVertexSecondDerivative()
    {
        var coefficients = new double[14];
        coefficients[5] = .02;  // T0(x) T2(y)
        coefficients[7] = .007; // T2(x) T2(y)
        coefficients[11] = .003; // T0(x) T4(y)
        var geometry = new ChebyshevSagGeometry(200, 2, 4, 4, 5, coefficients);
        var expected = 1d / 200 + 4d / 5 * (.02 * 4 - .007 * 4 - .003 * 16);
        Assert.Equal(expected, geometry.VertexMeridionalCurvature, 14);

        const double step = 1e-5;
        var upper = geometry.SurfaceNormal(new(0, step, geometry.Sag(0, step)));
        var lower = geometry.SurfaceNormal(new(0, -step, geometry.Sag(0, -step)));
        Assert.Equal(expected, (-upper.Y / upper.Z + lower.Y / lower.Z) / (2 * step), 10);
    }

    [Fact]
    public async Task MenuChebyshevMirrorReportsTheFiniteFocalLengthOfItsSag()
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src",
            "OpticalSystemDesign.App", "Assets", "SequentialExamples", "Tilted systems & prisms",
            "OAP using Chebyshev Polynomial surface.zmx");
        var optic = await new ZemaxZmxImporter().ImportFileAsync(source);
        var result = new Core.Analysis.FirstOrderAnalysis(optic).GenerateData();
        Assert.Equal("Finite", result.Values["EffectiveFocalLengthStatus"]);
        Assert.InRange(Assert.IsType<double>(result.Values["EffectiveFocalLength"]), 500 - 1e-7, 500 + 1e-7);
        Assert.InRange(Assert.IsType<double>(result.Values["FNumber"]), 500d / 71 - 1e-8, 500d / 71 + 1e-8);
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
