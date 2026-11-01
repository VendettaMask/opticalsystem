using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Capabilities;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxGradient1ImportTests
{
    [Theory] [InlineData("MM",1)] [InlineData("CM",10)] [InlineData("IN",25.4)]
    public void ImportUsesContinuousCoreTransportAndRetainsTheSpatialProfile(string unit,double scale)
    {
        var source=$"MODE SEQ\nUNIT {unit}\nENPD .2\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE GRINSUR1\nCURV 0\nPARM 1 .1\nPARM 2 1.5\nPARM 3 -.083\nPARM 4 0\nDISZ 4\nSURF 2\nCURV 0\nDISZ 1\nSURF 3\nCURV 0";
        var original=OpticalFormatCatalog.Import(source,".zmx");
        foreach(var optic in new[]{original,Optic.FromSnapshot(original.ToSnapshot())})
        {
            Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
            var material=Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[1].MaterialAfter);
            var profile=Assert.IsType<Gradient1IndexProfile>(material.Profile);
            Assert.Equal(1.5,profile.BaseIndex);Assert.Equal(-.083/(scale*scale),profile.RadialQuadratic);
            Assert.Equal(.1*scale,material.IntegrationOptions.MaximumStep);
            Assert.Equal(1.5-.083*.1*.1,profile.RefractiveIndex(new(.1*scale,0,0),550),14);
            using var trace=optic.SequentialRayTracer.Trace(new RealRayBundle([new RealRay(new(.1*scale,0,-scale),new(0,0,1),550)]),TraceRequest.Selected([1,2]));
            Assert.True(trace.TryGetSample(0,1,out var entrance));Assert.True(trace.TryGetSample(0,2,out var exit));
            Assert.False(exit.Vignetted);Assert.True(exit.Position.X<entrance.Position.X);
            Assert.True(exit.SegmentLength>4*scale);Assert.True(exit.SegmentOpticalPathLength>4*scale);
        }
        Assert.Throws<NotSupportedException>(()=>OpticalFormatCatalog.Export(original,".zmx"));
    }
    [Fact]
    public void NonDifferentiableAxisIsNotPromotedToSupportedByTheImporter()
    {
        var optic=OpticalFormatCatalog.Import("MODE SEQ\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\nTYPE GRINSUR1\nPARM 1 .1\nPARM 2 1.5\nPARM 3 -.083\nPARM 4 .1\nDISZ 4\nSURF 2",".zmx");
        Assert.Contains(OpticCapabilityPreflight.Inspect(optic),i=>i.OriginalType.Contains("GRINSUR1"));
    }
}
