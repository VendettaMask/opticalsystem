using OpticalSystemDesign.Validation;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;
using System.Text.Json;

namespace OpticalSystemDesign.Tests;

public sealed class Gradient9MaterialTests
{
    [Theory]
    [InlineData("gradient9-native-v1.json")]
    [InlineData("gradient9-return-native-v1.json")]
    public void ImportedExampleMatchesFrozenAuthoritativeNativeStates(string fixture)
    {
        using var data=JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",fixture)));
        var optic=OpticalFormatCatalog.Import(data.RootElement.GetProperty("source").GetString()!,".zmx");
        var nonAuthority=0;
        foreach(var row in data.RootElement.GetProperty("rows").EnumerateArray())
        {
            if(row.TryGetProperty("authority",out var authority) && !authority.GetBoolean()){nonAuthority++;continue;}
            var input=row.GetProperty("input");var code=input.GetProperty("code").GetString()!;
            var wave=input.GetProperty("int2").GetInt32();var wavelength=optic.Wavelengths[wave-1].Nanometers;
            var surface=input.GetProperty("int1").GetInt32();double actual;
            if(code=="INDX") actual=((GradientIndexMaterial)optic.SurfaceGroup.Items[surface].MaterialAfter).RefractiveIndex(Vector3D.Zero,wavelength);
            else
            {
                var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(input.GetProperty("data1").GetDouble(),input.GetProperty("data2").GetDouble(),input.GetProperty("data3").GetDouble(),input.GetProperty("data4").GetDouble(),wavelength/1000);
                using var trace=optic.SequentialRayTracer.Trace(bundle,TraceRequest.Selected([surface]));
                Assert.True(trace.TryGetSample(0,surface,out var sample));Assert.False(sample.Vignetted);
                var position=optic.SurfaceGroup.Items[surface].CoordinateSystem.ToLocalPoint(sample.Position);
                var direction=optic.SurfaceGroup.Items[surface].CoordinateSystem.ToLocalDirection(sample.Direction);
                actual=code switch{"REAX"=>position.X,"REAY"=>position.Y,"REAZ"=>position.Z,
                    "REAA"=>direction.X,"REAB"=>direction.Y,"REAC"=>direction.Z,"OPTH"=>sample.PhaseInclusiveOpticalPathLength!.Value,_=>throw new InvalidDataException(code)};
            }
            var expected=row.GetProperty("value").GetDouble()*(row.TryGetProperty("normalizationSign",out var sign)?sign.GetInt32():1);
            Assert.InRange(Math.Abs(actual-expected),0,(code=="INDX"?1e-12:1e-8)+1e-6*Math.Abs(expected));
        }
        Assert.Equal(data.RootElement.TryGetProperty("nonAuthorityIntermediateDirections",out var count)?count.GetInt32():0,nonAuthority);
    }

    [Theory] [InlineData(500)] [InlineData(550)] [InlineData(600)]
    public void DispersionGradientAndAxisTransportFollowTheDeclaredProfile(double wavelength)
    {
        var profile=new Gradient9IndexProfile(1.5477,.00637,.4785,.007157,.0003749);
        var l=wavelength*.001;var n0=1.5477+.00637/(l*l);
        var k=.4785+.007157/(l*l)+.0003749/Math.Pow(l,4);
        var sample=profile.Evaluate(new(.2,-.3,1),wavelength);
        Assert.Equal(n0*(1-.5*k*k*(.2*.2+.3*.3)),sample.Index,14);
        Assert.Equal(-n0*k*k*.2,sample.Gradient.X,14);Assert.Equal(n0*k*k*.3,sample.Gradient.Y,14);
        var material=new GradientIndexMaterial("SLS-1.0",profile,100);
        const double length=3.8;var matrix=GradientIndexParaxialTransport.Between(material,0,length,wavelength);
        Assert.InRange(Math.Abs(matrix.A-Math.Cos(k*length)),0,2e-10);
        Assert.InRange(Math.Abs(matrix.B-Math.Sin(k*length)/k),0,2e-10);
        Assert.InRange(Math.Abs(matrix.C+k*Math.Sin(k*length)),0,2e-10);
        Assert.InRange(Math.Abs(matrix.D-Math.Cos(k*length)),0,2e-10);
    }

    [Theory] [InlineData("MM",1)] [InlineData("CM",10)] [InlineData("IN",25.4)]
    public void ImportAndSnapshotRetainActualDispersionAndUnitScaledSpatialCoefficients(string unit,double scale)
    {
        var optic=OpticalFormatCatalog.Import(Source(unit,"SLS-1.0"),".zmx");
        foreach(var candidate in new[]{optic,Optic.FromSnapshot(optic.ToSnapshot())})
        {
            Assert.Empty(OpticCapabilityPreflight.Inspect(candidate));
            var material=Assert.IsType<GradientIndexMaterial>(candidate.SurfaceGroup.Items[1].MaterialAfter);
            var profile=Assert.IsType<Gradient9IndexProfile>(material.Profile);
            Assert.Equal(.4785/scale,profile.K0);Assert.Equal(.007157/scale,profile.K1);Assert.Equal(.0003749/scale,profile.K2);
            var original=new Gradient9IndexProfile(1.5477,.00637,.4785,.007157,.0003749);
            Assert.Equal(original.RefractiveIndex(new(.2,-.3,0),550),profile.RefractiveIndex(new(.2*scale,-.3*scale,0),550),14);
            Assert.Throws<NotSupportedException>(()=>material.RefractiveIndex(550));
        }
    }

    [Fact]
    public void TiltedPlanoEntrancePreservesTheGradient9MaterialAndSlope()
    {
        var optic = OpticalFormatCatalog.Import(Source("MM", "SLW-1.8").Replace("PARM 3 0", "PARM 3 -.14054", StringComparison.Ordinal), ".zmx");
        var surface = optic.SurfaceGroup.Items[1];
        Assert.IsType<GradientIndexMaterial>(surface.MaterialAfter);
        Assert.Equal(-.07027, surface.Geometry.Sag(0, .5), 14);
        Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
        using var workspace = new WorkspaceCoordinator(new OpticContext(optic));
        var document = workspace.GetDocumentSnapshot();
        Assert.True(double.IsNaN(document.EffectiveFocalLength));
        Assert.Equal(optic.SurfaceGroup.Items.Count, document.SurfaceCount);
    }

    [Fact]
    public async Task AdjacentCatalogIsAuthoritativeAndSnapshotDoesNotDependOnItAfterImport()
    {
        var directory=Path.Combine(Path.GetTempPath(),"gradient9-import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var lens=Path.Combine(directory,"lens.zmx");var catalog=Path.Combine(directory,"GRADIENT_9.DAT");
        try
        {
            await File.WriteAllTextAsync(lens,Source("MM","SLS-1.0"));
            await File.WriteAllTextAsync(catalog,"SLS-1.0\n1.7\n.002\n.3\n.001\n.0001\n");
            var optic=await new ZemaxZmxImporter().ImportFileAsync(lens);
            var snapshot=optic.ToSnapshot();File.Delete(catalog);
            var restored=Optic.FromSnapshot(snapshot);
            var material=Assert.IsType<GradientIndexMaterial>(restored.SurfaceGroup.Items[1].MaterialAfter);
            var profile=Assert.IsType<Gradient9IndexProfile>(material.Profile);
            Assert.Equal(1.7,profile.B);Assert.Equal(.3,profile.K0);
        }
        finally {File.Delete(lens);File.Delete(catalog);Directory.Delete(directory);}
        var unresolved=OpticalFormatCatalog.Import(Source("MM","MISSING"),".zmx");
        Assert.Contains(OpticCapabilityPreflight.Inspect(unresolved),issue=>issue.OriginalType.Contains("MISSING"));
    }

    private static string Source(string unit,string name)=>$"MODE SEQ\nUNIT {unit}\nENPD .2\nSURF 0\nCURV 0\nDISZ INFINITY\nSURF 1\nTYPE GRINSUR9\nCURV 0\nPARM 1 .1\nPARM 2 0\nPARM 3 0\nGLAS {name} 0 0 1.5 40\nDISZ 3.8\nSURF 2\nCURV 0\nDISZ 1\nSURF 3\nCURV 0";
}
