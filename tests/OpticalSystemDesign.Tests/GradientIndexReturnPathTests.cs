using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class GradientIndexReturnPathTests
{
    [Theory] [InlineData(1,0)] [InlineData(0,1)]
    public void ReflectedRadialRodMatchesClosedAxisMatrixRealRayLimitAndReverseTrace(double height,double slope)
    {
        var optic=Rod(); const double n0=1.5;var k=Math.Sqrt(.04/n0);const double length=4;
        var c=Math.Cos(k*length);var b=Math.Sin(k*length)/k;var a=-k*Math.Sin(k*length);
        var y=height+slope;var u=slope/n0;
        (y,u)=(c*y+b*u,a*y+c*u);u=-u;
        (y,u)=(c*y-b*u,-a*y+c*u);u*=n0;y-=u;
        var trace=optic.Paraxial.TraceGeneric([height],[slope],-1,.55);
        Assert.InRange(Math.Abs(trace.Heights[^1][0]-y),0,2e-9);
        Assert.InRange(Math.Abs(trace.Slopes[^1][0]-u),0,2e-9);
        var reverse=optic.Paraxial.TraceGenericReverse([y],[-u],0,.55);
        Assert.InRange(Math.Abs(reverse.Heights[^1][0]-height),0,3e-9);
        Assert.InRange(Math.Abs(reverse.Slopes[^1][0]+slope),0,3e-9);
        const double epsilon=1e-6;var direction=new Vector3D(epsilon*slope,0,1);direction/=direction.Length;
        using var real=optic.SequentialRayTracer.Trace(new RealRayBundle([new(new(epsilon*height,0,-1),direction,550)]),TraceRequest.Selected([4]));
        Assert.True(real.TryGetSample(0,4,out var sample));Assert.False(sample.Vignetted);
        Assert.InRange(Math.Abs(sample.Position.X/epsilon-y),0,3e-8);
        Assert.InRange(Math.Abs(sample.Direction.X/sample.Direction.Z/epsilon-u),0,3e-8);
    }

    [Fact]
    public void ReflectionRetainsTheEntranceFrameOfAnAxialGradient()
    {
        var optic=Rod();var material=new GradientIndexMaterial("axial return",new Gradient3IndexProfile(1.5,axial1:.02),100);
        optic.SurfaceGroup.Items[1].MaterialAfter=material;
        optic.SurfaceGroup.Items[2].MaterialBefore=material.Clone();optic.SurfaceGroup.Items[2].MaterialAfter=material.Clone();
        optic.SurfaceGroup.Items[3].MaterialBefore=material.Clone();
        var integral=Math.Log((1.5+.02*4)/1.5)/.02;
        var trace=optic.Paraxial.TraceGeneric([0],[1],-1,.55);
        Assert.InRange(Math.Abs(trace.Heights[^1][0]-(2+2*integral)),0,2e-9);
        Assert.InRange(Math.Abs(trace.Slopes[^1][0]+1),0,2e-9);
    }

    private static Optic Rod()=>OpticalFormatCatalog.Import("MODE SEQ\nUNIT MM\nENPD .2\nWAVM 1 .55 1\nPWAV 1\nSURF 0\nCURV 0\nDISZ 1\nSURF 1\nTYPE GRINSUR1\nCURV 0\nPARM 1 .1\nPARM 2 1.5\nPARM 3 -.02\nPARM 4 0\nDISZ 4\nDIAM 10 1\nSURF 2\nCURV 0\nGLAS MIRROR\nDISZ -4\nDIAM 10 1\nSURF 3\nCURV 0\nDISZ -1\nDIAM 10 1\nSURF 4\nCURV 0\nDIAM 10 1",".zmx");
}
