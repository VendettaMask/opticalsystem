using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class DelanoDiagramTests
{
    [Fact]
    public void TwoRayDiagramRecoversKnownSeparatedPowersAndImageWithoutTracing()
    {
        var result = DelanoDiagram.Construct([2, 1.6, 0], [0, 1, 25.0 / 3], .2);
        Assert.Equal(10, result.DistancesMillimeters[0], 12);
        Assert.Equal(200.0 / 3, result.DistancesMillimeters[1], 11);
        Assert.Equal(.02, result.PowersPerMillimeter[0], 12);
        Assert.Equal(-.01, result.PowersPerMillimeter[1], 12);
        double y = 2, u = 0, chief = 0, chiefSlope = .1;
        for (var i = 0; i < 2; i++)
        {
            u -= result.PowersPerMillimeter[i] * y;
            chiefSlope -= result.PowersPerMillimeter[i] * chief;
            Assert.Equal(.2, y * chiefSlope - chief * u, 12);
            y += result.DistancesMillimeters[i] * u;
            chief += result.DistancesMillimeters[i] * chiefSlope;
        }
        Assert.Equal(0, y, 12); Assert.Equal(25.0 / 3, chief, 11);
    }

    [Theory]
    [InlineData(.02)]
    [InlineData(-.02)]
    [InlineData(0)]
    public void SymmetricPhysicalSeedMatchesActualCoreThickLensPower(double power)
    {
        var optic = new Optic("Delano realization");
        var n = optic.Materials.Resolve("N-BK7").RefractiveIndex(587.6);
        var c = DelanoDiagram.SymmetricSingletCurvature(power, n, 3);
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Material = "Air", Thickness = double.PositiveInfinity },
            new() { Material = "N-BK7", Thickness = 3, Radius = c == 0 ? double.PositiveInfinity : 1 / c, SemiDiameter = 2 },
            new() { Material = "Air", Thickness = 20, Radius = c == 0 ? double.PositiveInfinity : -1 / c, SemiDiameter = 2 },
            new() { Material = "Air" }
        ]);
        optic.Wavelengths.Add(new() { Nanometers = 587.6, IsPrimary = true, Weight = 1 });
        optic.Fields.Add(new() { Weight = 1 });
        Assert.Equal(power, -optic.Paraxial.GetTransferMatrix(1, 2).C, 12);
    }

    [Fact]
    public void SingularBackwardAndNonfiniteDiagramsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() => DelanoDiagram.Construct([2, 1, 0], [0, 1, 2], 0));
        Assert.Throws<ArithmeticException>(() => DelanoDiagram.Construct([2, 1, 0], [0, -1, 2], .2));
        Assert.Throws<ArgumentException>(() => DelanoDiagram.Construct([2, double.NaN, 0], [0, 1, 2], .2));
        Assert.Throws<ArithmeticException>(() => DelanoDiagram.SymmetricSingletCurvature(10, 1.5, 3));
        Assert.Throws<ArithmeticException>(() => DelanoDiagram.SymmetricSingletCurvature(-double.MaxValue, 1.5, double.MaxValue));
    }
}
