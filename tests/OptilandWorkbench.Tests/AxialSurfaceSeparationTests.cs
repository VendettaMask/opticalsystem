using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class AxialSurfaceSeparationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void SphericalLensEdgeAgreesWithIndependentCircleIntersection(double height)
    {
        var optic = new Optic();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Radius = 20, Thickness = 4 },
            new OpticalSurface { Radius = -30, Thickness = 10 },
            new OpticalSurface { Thickness = 0 }
        ]);
        var frontSag = 20 - Math.Sqrt(400 - height * height);
        var backSag = -30 + Math.Sqrt(900 - height * height);
        Assert.Equal(4 + backSag - frontSag, AxialSurfaceSeparation.Evaluate(optic, 1, 0, height), 12);
    }
}
