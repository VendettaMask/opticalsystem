using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class DesignPrecisionRepairTests
{
    [Theory]
    [InlineData(1e308)]
    [InlineData(1e200)]
    [InlineData(1e-160)]
    [InlineData(1e-200)]
    [InlineData(1e-308)]
    [InlineData(1e-320)]
    [InlineData(-1e200)]
    [InlineData(-1e-200)]
    [InlineData(-1e-320)]
    public void EqualValueQuotientsRetainUnitTangentsAcrossFiniteScales(double scale)
    {
        var variable = DifferentialScalar.Variable(scale, 1, 0, scale);
        var reciprocal = scale / variable;
        var direct = variable / scale;
        var identity = variable / variable;
        Assert.Equal(1, reciprocal.Value);
        Close(-1, reciprocal.Derivative(0));
        Close(1, direct.Derivative(0));
        Assert.True(identity.IsFinite);
        Assert.Equal(0, identity.Derivative(0));
    }

    [Theory]
    [InlineData(1e308, 1e200, 1e250, -1e158)]
    [InlineData(1e-308, 1e-200, 1e200, -1e292)]
    [InlineData(1e-300, 1e100, 1e308, -1e-192)]
    public void QuotientTangentSurvivesIntermediateOverflowAndValueUnderflow(
        double numerator, double denominator, double tangent, double expected)
    {
        var result = numerator / DifferentialScalar.Variable(denominator, 1, 0, tangent);
        Assert.True(result.IsFinite);
        Close(expected, result.Derivative(0));
    }

    [Fact]
    public void UnrepresentableDerivativesAndZeroDenominatorsRemainUnavailable()
    {
        var result = 1 / DifferentialScalar.Variable(1e-200, 1, 0);
        Assert.False(result.IsFinite);
        Assert.Throws<ArithmeticException>(() => DifferentialScalar.PositivePart(result));
        Assert.False((DifferentialScalar.Variable(1, 1, 0) / 0).IsFinite);
        Assert.Throws<ArgumentException>(() => DifferentialScalar.Variable(1e200, 1, 0)
            / DifferentialScalar.Variable(1e200, 2, 0));
    }

    [Fact]
    public void LargeCorrelatedTangentsCancelWithoutInfinityAndZeroValuesRetainTheirDerivative()
    {
        var correlated = DifferentialScalar.Variable(1e-100, 1, 0, 1e308);
        var identity = correlated / correlated;
        Assert.True(identity.IsFinite);
        Assert.Equal(0, identity.Derivative(0));
        var zero = DifferentialScalar.Variable(0, 1, 0, 1e-320) / 1e-320;
        Assert.True(zero.IsFinite);
        Close(1, zero.Derivative(0));
    }

    [Fact]
    public void QuotientTangentCombinesSubnormalContributionsBeforeFinalRounding()
    {
        var a = DifferentialScalar.Variable(1, 1, 0, double.Epsilon);
        var b = DifferentialScalar.Variable(2, 1, 0, -double.Epsilon);
        // (a' b - a b')/b² = 3/4 of the smallest positive binary64 value,
        // which rounds to one epsilon. Rounding each contribution first loses it.
        Assert.Equal(double.Epsilon, (a / b).Derivative(0));
    }

    [Theory]
    [InlineData(1e200, 1e200)]
    [InlineData(1, 1e200)]
    [InlineData(1, 1e-320)]
    [InlineData(1e-200, 1e-200)]
    public void ApplyingUnrepresentableCurvatureCannotSilentlyCreateAPlane(double scale, double coordinate)
    {
        var optic = Lens();
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Apply(optic.ToSnapshot(),
            [new(1, OpticalDesignParameterKind.Curvature, scale)], [coordinate]));
        Assert.Equal(50, optic.SurfaceGroup.Items[1].Radius);
    }

    [Fact]
    public void ReadingUnrepresentableOptimizerCoordinatesFailsExplicitly()
    {
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Read(Lens(),
            [new(1, OpticalDesignParameterKind.Thickness, double.Epsilon)]));
    }

    [Fact]
    public void ReadingNonzeroAsphereCoordinatesCannotUnderflowToAnUnrelatedZero()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, 0, [0, 1e-200]);
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Read(optic,
            [new(1, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e200, 1)]));
    }

    [Theory]
    [InlineData(OpticalDesignParameterKind.Curvature)]
    [InlineData(OpticalDesignParameterKind.Thickness)]
    public void IrrelevantCoefficientTagsCannotAliasOnePhysicalCoordinate(OpticalDesignParameterKind kind)
    {
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Read(Lens(),
            [new(1, kind), new(1, kind, CoefficientIndex: 1)]));
    }

    [Fact]
    public void InconsistentLinkedCoordinatesCannotDescribeTheNominalDerivativeSystem()
    {
        var optic = Lens();
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Read(optic,
            [new(1, OpticalDesignParameterKind.Curvature, LinkedSurfaceIndex: 2)]));
    }

    [Fact]
    public void QuadraticAsphereTangentCannotSilentlyBypassTheParaxialContract()
    {
        var optic = Lens();
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, 0, [0, 1e-6]);
        OpticalDesignParameter[] parameters = [new(1, OpticalDesignParameterKind.EvenAsphereCoefficient, CoefficientIndex: 0)];
        // The geometry-only derivative is well defined, but the restricted
        // spot/power contract fixes A2 at zero and must reject its optimization.
        Close(4, DifferentiableOpticalMetrics.SurfaceSag(optic, parameters, 1, 2).Derivative(0));
        Assert.Throws<NotSupportedException>(() => DifferentiableOpticalMetrics.Evaluate(optic, parameters,
            [0], [new PupilSample(.2, .3, 1)]));
    }

    private static Optic Lens()
    {
        var optic = new Optic("Update precision control");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 4;
        optic.Fields.Add(new FieldPoint { YAngleDegrees = 7, Weight = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, Material = "Air", SemiDiameter = 15 },
            new() { Radius = 50, Thickness = 5, Material = "N-BK7", SemiDiameter = 15, IsStop = true },
            new() { Radius = -50, Thickness = 47, Material = "Air", SemiDiameter = 15 },
            new() { Material = "Air", SemiDiameter = 30 }
        ]);
        optic.RayAimingEnabled = true;
        PhysicalStopCalibration.Apply(optic);
        return optic;
    }

    private static void Close(double expected, double actual) =>
        Assert.True(double.IsFinite(actual) && Math.Abs((actual - expected) / expected) < 2e-12,
            $"Expected tangent {expected:R}, actual {actual:R}; fixed relative limit 2e-12.");
}
