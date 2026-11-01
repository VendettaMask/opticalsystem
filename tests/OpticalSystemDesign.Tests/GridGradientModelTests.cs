using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class GridGradientModelTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","grid-gradient-native-v1.json")));

    [Fact]
    public void ConvergedSampleRaysUseTheOriginalExplicitRayBoundsAndPortableGrid()
    {
        using var fixture=Fixture();var data=fixture.RootElement.GetProperty("sample");
        var source=data.GetProperty("source").GetString()!;var imported=OpticalFormatCatalog.Import(source,".zmx");
        var original=Assert.IsType<GradientIndexMaterial>(imported.SurfaceGroup.Items[2].MaterialAfter);
        // Both integrators converge independently. Maximum step is a numerical
        // setting, not a change to the physical GGD or the original ray gate.
        var adaptive=new GradientIndexMaterial(original.Name,original.Profile,original.MaximumPathLength,
            original.IntegrationOptions with {MaximumStep=.05});
        imported.SurfaceGroup.Items[2].MaterialAfter=adaptive;imported.SurfaceGroup.Items[3].MaterialBefore=adaptive.Clone();
        foreach(var optic in new[]{imported,Optic.FromSnapshot(imported.ToSnapshot())})
        {
            Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
            var grid=Assert.IsType<GridGradientIndexProfile>(Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[2].MaterialAfter).Profile);
            Assert.Equal("d025dcc149a61dae8a368559ae31e1932b0164611accf90bf3c9403b96025e55",grid.SourceSha256);
            foreach(var beam in data.GetProperty("rows").EnumerateArray().GroupBy(r=>
                (Wave:r.GetProperty("input").GetProperty("int2").GetInt32(),Hy:r.GetProperty("input").GetProperty("data2").GetDouble(),
                 Px:r.GetProperty("input").GetProperty("data3").GetDouble(),Py:r.GetProperty("input").GetProperty("data4").GetDouble())))
            {
                var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,beam.Key.Hy,beam.Key.Px,beam.Key.Py,optic.Wavelengths[beam.Key.Wave-1].Micrometers);
                using var trace=optic.SequentialRayTracer.Trace(bundle,TraceRequest.Selected([1,2,3]));
                double NativePath(int surface)=>beam.Single(r=>r.GetProperty("input").GetProperty("code").GetString()=="OPTH"
                    &&r.GetProperty("input").GetProperty("int1").GetInt32()==surface).GetProperty("value").GetDouble();
                foreach(var row in beam)
                {
                    var p=row.GetProperty("input");var index=p.GetProperty("int1").GetInt32();var code=p.GetProperty("code").GetString();
                    Assert.True(trace.TryGetSample(0,index,out var sample));Assert.False(sample.Vignetted);
                    var coordinates=optic.SurfaceGroup.Items[index].CoordinateSystem;
                    var point=coordinates.ToLocalPoint(sample.Position);var direction=coordinates.ToLocalDirection(sample.Direction);
                    var actual=code switch{"REAX"=>point.X,"REAY"=>point.Y,"REAZ"=>point.Z,"REAA"=>direction.X,"REAB"=>direction.Y,"REAC"=>direction.Z,
                        "OPTH"=>sample.PhaseInclusiveOpticalPathLength!.Value-trace.GetSurfaceSamples(1)[0]!.Value.PhaseInclusiveOpticalPathLength!.Value,_=>throw new InvalidDataException(code)};
                    var expected=row.GetProperty("value").GetDouble();if(code=="OPTH")expected-=NativePath(1);
                    Assert.InRange(Math.Abs(actual-expected),0,1e-8+1e-6*Math.Abs(expected));
                }
            }
            Assert.Throws<NotSupportedException>(()=>OpticalFormatCatalog.Export(optic,".zmx"));
        }
    }

    [Fact]
    public void InterfaceDispersionMatchesTheNativeQueryAndVolumeGradientIsConsistent()
    {
        using var fixture=Fixture();var data=fixture.RootElement.GetProperty("dispersion");
        var bytes=Convert.FromBase64String(data.GetProperty("ggdBytes").GetString()!);
        var grid=GridGradientCatalog.Read(bytes,"dispersion-grid.ggd",1);
        foreach(var row in data.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("input").GetProperty("code").GetString()=="INDX"))
        {
            var wave=row.GetProperty("input").GetProperty("int2").GetInt32();var wavelength=wave==1?550:650;
            Assert.InRange(Math.Abs(grid.InterfaceRefractiveIndex(Vector3D.Zero,wavelength)-row.GetProperty("value").GetDouble()),0,1e-12);
        }
        var point=new Vector3D(.02,.03,.37);const double h=1e-5;var sample=grid.Evaluate(point,550);
        foreach(var axis in new[]{new Vector3D(1,0,0),new Vector3D(0,1,0),new Vector3D(0,0,1)})
        {
            var derivative=(grid.RefractiveIndex(point+h*axis,550)-grid.RefractiveIndex(point-h*axis,550))/(2*h);
            var expected=sample.Gradient.X*axis.X+sample.Gradient.Y*axis.Y+sample.Gradient.Z*axis.Z;
            Assert.InRange(Math.Abs(derivative-expected),0,1e-8);
        }
        Assert.Throws<SpatialIndexDomainException>(()=>grid.Evaluate(new(-2,0,.1),550));
        var optic=Optic.CreateBlank();var material=new GradientIndexMaterial("Grid control",grid,10);
        optic.SurfaceGroup.Items[1].MaterialAfter=material;
        var restored=Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(grid.Values,Assert.IsType<GridGradientIndexProfile>(Assert.IsType<GradientIndexMaterial>(restored.SurfaceGroup.Items[1].MaterialAfter).Profile).Values);
    }

    [Theory] [InlineData(2)] [InlineData(3)]
    public void UnverifiedCrystalPolarizationModesStayOpaque(int mode)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","birefringent-native-v1.json")));
        var source=fixture.RootElement.GetProperty("cases")[0].GetProperty("source").GetString()!.Replace("PARM 1 0","PARM 1 "+mode);
        Assert.NotEmpty(OpticCapabilityPreflight.Inspect(OpticalFormatCatalog.Import(source,".zmx")));
    }
}
