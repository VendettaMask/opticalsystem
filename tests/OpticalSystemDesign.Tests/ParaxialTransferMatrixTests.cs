using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class ParaxialTransferMatrixTests
{
    [Fact]
    public void FullMatrixMatchesIndependentThickLensProductAndExcludesImagePlaceholderThickness()
    {
        var optic = Lens();
        var n = optic.SurfaceGroup.Items[3].MaterialAfter.RefractiveIndex(587.6);
        double[,] matrix = { { 1, 7 }, { 0, 1 } };
        matrix = Product(new double[,] { { 1, 0 }, { -(n - 1) / (n * 40), 1 / n } }, matrix);
        matrix = Product(new double[,] { { 1, 2 }, { 0, 1 } }, matrix);
        matrix = Product(new double[,] { { 1, 0 }, { -(1 - n) / -60, n } }, matrix);
        matrix = Product(new double[,] { { 1, 50 }, { 0, 1 } }, matrix);
        var actual = optic.Paraxial.GetTransferMatrix(1, 5);
        Assert.Equal(matrix[0, 0], actual.A, 12); Assert.Equal(matrix[0, 1], actual.B, 12);
        Assert.Equal(matrix[1, 0], actual.C, 12); Assert.Equal(matrix[1, 1], actual.D, 12);
        Assert.Equal(1, actual.InputIndex); Assert.Equal(1, actual.OutputIndex);
        Assert.Equal(0, actual.InputZMillimeters); Assert.Equal(59, actual.OutputZMillimeters);
        Assert.Equal(1, actual.A * actual.D - actual.B * actual.C, 12);
        optic.SurfaceGroup.Items[^1].Thickness = 500;
        Assert.Equal(actual, optic.Paraxial.GetTransferMatrix(1, 5));
    }

    [Fact]
    public void MatrixMapsBothIndependentBasisRaysAndSmallPhysicalRaysUsingHeightAndSlopeOrder()
    {
        var optic = Lens();
        var matrix = optic.Paraxial.GetTransferMatrix(1, 5);
        var firstOrder = optic.Paraxial.TraceGeneric([1, 0], [0, 1], 0, .5876, 1);
        Assert.Equal(matrix.A, firstOrder.Heights[^1][0], 12); Assert.Equal(matrix.B, firstOrder.Heights[^1][1], 12);
        Assert.Equal(matrix.C, firstOrder.Slopes[^1][0], 12); Assert.Equal(matrix.D, firstOrder.Slopes[^1][1], 12);
        foreach (var input in new[] { (Height: 1e-4, Slope: 0.0), (Height: 0.0, Slope: 1e-6) })
        {
            var ray = new RealRay(new(0, input.Height, 0), new(0, input.Slope, 1), 587.6).Normalize();
            using var trace = optic.SequentialRayTracer.Trace(new RealRayBundle([ray]), TraceRequest.FinalOnly(false));
            var hit = trace.GetSurfaceSamples(5).Single()!.Value;
            var expected = matrix.Map(input.Height, input.Slope);
            Assert.InRange(Math.Abs(hit.Position.Y - expected.HeightMillimeters), 0, 1e-10);
            Assert.InRange(Math.Abs(hit.Direction.Y / hit.Direction.Z - expected.Slope), 0, 1e-11);
        }
    }

    [Fact]
    public void EndpointMediaAndFixedReferenceCoordinatesAreExplicitAndUnsupportedPathsAreRejected()
    {
        var optic = Lens();
        var front = optic.Paraxial.GetTransferMatrix(3, 3);
        var n = optic.SurfaceGroup.Items[3].MaterialAfter.RefractiveIndex(587.6);
        Assert.Equal(1, front.InputIndex); Assert.Equal(n, front.OutputIndex);
        Assert.Equal(1 / n, front.D, 12); Assert.Equal(0, front.B);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Paraxial.GetTransferMatrix(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Paraxial.GetTransferMatrix(3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Paraxial.GetTransferMatrix(1, 5, double.NaN));
        optic.SurfaceGroup.Items[3].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 7), RotationYDegrees: 1);
        Assert.Throws<NotSupportedException>(() => optic.Paraxial.GetTransferMatrix(1, 5));
    }

    private static Optic Lens()
    {
        var optic = new Optic("Matrix contract thick lens");
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Material = "Air", Thickness = double.PositiveInfinity },
            new() { Material = "Air", Thickness = 3 },
            new() { Material = "Air", Thickness = 4, IsStop = true, SemiDiameter = 2 },
            new() { Material = "N-BK7", Thickness = 2, Radius = 40, SemiDiameter = 5 },
            new() { Material = "Air", Thickness = 50, Radius = -60, SemiDiameter = 5 },
            new() { Material = "Air", Thickness = 17 }
        ]);
        optic.Wavelengths.Add(new() { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.Fields.Add(new() { Weight = 1 });
        return optic;
    }

    private static double[,] Product(double[,] a, double[,] b) => new double[,]
    {
        { a[0,0]*b[0,0]+a[0,1]*b[1,0], a[0,0]*b[0,1]+a[0,1]*b[1,1] },
        { a[1,0]*b[0,0]+a[1,1]*b[1,0], a[1,0]*b[0,1]+a[1,1]*b[1,1] }
    };
}
