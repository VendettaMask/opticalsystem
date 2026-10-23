using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class OpticalDesignDerivativeTests
{
    private static readonly PupilSample[] Pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();

    [Fact]
    public void AutomaticDifferentiationHasExactElementaryTangents()
    {
        var x = DifferentialScalar.Variable(3, 2, 0);
        var y = DifferentialScalar.Variable(4, 2, 1);
        var radius = DifferentialScalar.Sqrt(x * x + y * y);
        Assert.Equal(5, radius.Value);
        Assert.Equal(.6, radius.Derivative(0), 14);
        Assert.Equal(.8, radius.Derivative(1), 14);
        var quotient = (x * y + 2) / (x + 1);
        Assert.Equal(.125, quotient.Derivative(0), 14);
        Assert.Equal(.75, quotient.Derivative(1), 14);
        Assert.Throws<ArgumentException>(() => x + DifferentialScalar.Variable(1, 3, 0));
        Assert.Throws<ArithmeticException>(() => DifferentialScalar.PositivePart(double.NaN));
        Assert.Throws<ArithmeticException>(() => DifferentialScalar.PositivePart(DifferentialScalar.Variable(-1, 1, 0, 1e308) * 1e308));
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void FormalCoreImageAndPowerTangentsAgreeWithIndependentCentralDifferences(int stop, bool asphere, bool relaxed)
    {
        var optic = Lens(stop, asphere, relaxed);
        var parameters = Parameters(asphere, relaxed);
        var template = optic.ToSnapshot();
        var vector = OpticalDesignParameters.Read(optic, parameters);
        var actual = DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0, .7, 1], Pupils);
        Assert.True(actual.TracedRayCount > 0);
        for (var coordinate = 0; coordinate < parameters.Length; coordinate++)
        {
            const double step = 2e-5;
            var plus = vector.ToArray();
            var minus = vector.ToArray();
            plus[coordinate] += step;
            minus[coordinate] -= step;
            var upper = OpticalDesignParameters.Apply(template, parameters, plus);
            var lower = OpticalDesignParameters.Apply(template, parameters, minus);
            PhysicalStopCalibration.Apply(upper);
            PhysicalStopCalibration.Apply(lower);
            var powerDifference = (upper.Paraxial.EstimateOpticalPower() - lower.Paraxial.EstimateOpticalPower()) / (2 * step);
            Close(powerDifference, actual.OpticalPowerPerMillimeter.Derivative(coordinate), 5e-7);
            for (var fieldIndex = 0; fieldIndex < actual.Fields.Count; fieldIndex++)
            {
                var field = actual.Fields[fieldIndex];
                var upperSpot = SpotMetricEvaluator.EvaluatePupilSamples(upper, 0, field.NormalizedFieldY, Pupils,
                    reference: "centroid", includeSurfaceTransmission: false);
                var lowerSpot = SpotMetricEvaluator.EvaluatePupilSamples(lower, 0, field.NormalizedFieldY, Pupils,
                    reference: "centroid", includeSurfaceTransmission: false);
                var nominal = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field.NormalizedFieldY, Pupils,
                    reference: "centroid", includeSurfaceTransmission: false);
                Assert.NotNull(nominal.Metrics);
                Assert.Equal(nominal.Metrics.RmsSpotRadius, field.RmsRadiusMillimeters.Value, asphere ? 7 : 9);
                var difference = (upperSpot.Metrics!.RmsSpotRadius - lowerSpot.Metrics!.RmsSpotRadius) / (2 * step);
                Close(difference, field.RmsRadiusMillimeters.Derivative(coordinate), asphere ? 8e-4 : 2e-5);
            }
        }
    }

    [Fact]
    public void AFlatSeedHasFiniteCurvatureTangentsWithoutPerturbedOptics()
    {
        var optic = Lens(2, false, false);
        optic.SurfaceGroup.Items[1].Radius = 0;
        optic.SurfaceGroup.Items[2].Radius = 0;
        PhysicalStopCalibration.Apply(optic);
        var result = DifferentiableOpticalMetrics.Evaluate(optic, Parameters(false, false), [0, 1], Pupils);
        Assert.Equal(0, result.OpticalPowerPerMillimeter.Value);
        Assert.All(result.Fields, field => Assert.True(field.RmsRadiusMillimeters.IsFinite));
        Assert.Contains(result.OpticalPowerPerMillimeter.Gradient(), value => Math.Abs(value) > .001);
    }

    [Fact]
    public void RelaxedGlassRoundTripsAndPreservesIndependentMaterialCoordinates()
    {
        var material = OpticalDesignParameters.CreateRelaxedGlass("DesignRelaxed-1", 1.6, 55);
        var coordinates = OpticalDesignParameters.RelaxedGlassCoordinates(material);
        Assert.Equal(1.6, coordinates.Nd, 13);
        Assert.Equal(55, coordinates.Vd, 10);
        var optic = Lens(1, true, true);
        var parameters = Parameters(true, true);
        var vector = OpticalDesignParameters.Read(optic, parameters);
        var rebuilt = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, vector);
        Assert.IsType<EvenAsphereGeometry>(rebuilt.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(vector, OpticalDesignParameters.Read(rebuilt, parameters));
    }

    [Fact]
    public void UnsupportedSystemsAndCancellationFailExplicitly()
    {
        var optic = Lens(1, false, false);
        optic.SurfaceGroup.Items[1].IsReflective = true;
        Assert.Throws<NotSupportedException>(() => DifferentiableOpticalMetrics.Evaluate(optic, Parameters(false, false), [0], Pupils));
        optic = Lens(1, false, false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DifferentiableOpticalMetrics.Evaluate(optic, Parameters(false, false), [0], Pupils, cancellation.Token));
        Assert.Throws<ArgumentException>(() => OpticalDesignParameters.Apply(optic.ToSnapshot(), [new(1, OpticalDesignParameterKind.Curvature), new(1, OpticalDesignParameterKind.Curvature)], [0, 0]));
        optic.Wavelengths.Single(wave => wave.IsPrimary).Weight = 0;
        Assert.Throws<NotSupportedException>(() => DifferentiableOpticalMetrics.Evaluate(optic, Parameters(false, false), [0], Pupils));
    }

    [Fact]
    public void StableSeededDiskSamplingIsAreaWeightedAndResamplesWithoutGlobalRandomness()
    {
        var first = ApertureSampler.GenerateSeededUniformDisk(1000, 123);
        Assert.Equal(first, ApertureSampler.GenerateSeededUniformDisk(1000, 123));
        Assert.NotEqual(first[0], ApertureSampler.GenerateSeededUniformDisk(1000, 124)[0]);
        Assert.Equal(1, first.Sum(sample => sample.Weight), 12);
        Assert.InRange(first.Sum(sample => sample.Weight * (sample.X * sample.X + sample.Y * sample.Y)), .499, .501);
        Assert.All(first, sample => Assert.InRange(sample.X * sample.X + sample.Y * sample.Y, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApertureSampler.GenerateSeededUniformDisk(0, 1));
    }

    [Fact]
    public void TiedCementedProfileAndMaterialTangentsAgreeWithPhysicalFiniteDifferences()
    {
        var optic = Lens(2, true, false);
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, Material = "Air", SemiDiameter = 15 },
            new() { Radius = 50, Thickness = 3, Material = "N-BK7", SemiDiameter = 15 },
            new() { Radius = -60, Thickness = 0, Material = "N-F2", SemiDiameter = 15, IsStop = true },
            new() { Radius = -60, Thickness = 4, Material = "N-F2", SemiDiameter = 15 },
            new() { Radius = -90, Thickness = 42, Material = "Air", SemiDiameter = 15 },
            new() { Material = "Air", SemiDiameter = 30 }
        ]);
        optic.SurfaceGroup.Items[2].MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("DesignRelaxed-3", 1.62, 36);
        optic.SurfaceGroup.Items[3].MaterialAfter = optic.SurfaceGroup.Items[2].MaterialAfter.Clone();
        optic.SurfaceGroup.Items[2].Geometry = new EvenAsphereGeometry(-60, -.3, [0, 1e-6]);
        optic.SurfaceGroup.Items[3].Geometry = optic.SurfaceGroup.Items[2].Geometry.Clone();
        PhysicalStopCalibration.Apply(optic);
        OpticalDesignParameter[] parameters = [new(2, OpticalDesignParameterKind.Curvature, .02, LinkedSurfaceIndex: 3),
            new(2, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e-5, 1, 3),
            new(3, OpticalDesignParameterKind.MaterialIndex, LinkedSurfaceIndex: 2),
            new(3, OpticalDesignParameterKind.MaterialAbbe, 50, LinkedSurfaceIndex: 2)];
        var template = optic.ToSnapshot();
        var vector = OpticalDesignParameters.Read(optic, parameters);
        var result = DifferentiableOpticalMetrics.Evaluate(optic, parameters, [.7], Pupils);
        for (var index = 0; index < parameters.Length; index++)
        {
            const double step = 2e-5;
            var plus = vector.ToArray(); plus[index] += step;
            var minus = vector.ToArray(); minus[index] -= step;
            var upper = OpticalDesignParameters.Apply(template, parameters, plus);
            var lower = OpticalDesignParameters.Apply(template, parameters, minus);
            PhysicalStopCalibration.Apply(upper); PhysicalStopCalibration.Apply(lower);
            var upperSpot = SpotMetricEvaluator.EvaluatePupilSamples(upper, 0, .7, Pupils, includeSurfaceTransmission: false);
            var lowerSpot = SpotMetricEvaluator.EvaluatePupilSamples(lower, 0, .7, Pupils, includeSurfaceTransmission: false);
            Close((upperSpot.Metrics!.RmsSpotRadius - lowerSpot.Metrics!.RmsSpotRadius) / (2 * step), result.Fields[0].RmsRadiusMillimeters.Derivative(index), 8e-4);
        }
    }

    [Fact]
    public void CatalogProjectionIsBoundedAndShapeFeaturesSeeAspheresWithTheSameBaseRadius()
    {
        var optic = Lens(1, false, false);
        var original = optic.ToSnapshot().Surfaces[1];
        var coordinates = OpticalDesignParameters.RelaxedGlassCoordinates(optic.Materials.Resolve("N-BK7"));
        var relaxed = OpticalDesignParameters.CreateRelaxedGlass("DesignRelaxed-1", coordinates.Nd, coordinates.Vd);
        var match = OpticalSystemDesign.Core.Materials.DesignGlassMatcher.Match(optic.Materials, relaxed, ["N-F2", "N-BK7"], [486.1, 587.6, 656.3]);
        Assert.Equal("N-BK7", match.Match.CatalogName);
        Assert.InRange(match.Match.Distance, 0, 1e-10);
        Assert.Throws<ArgumentException>(() => OpticalSystemDesign.Core.Materials.DesignGlassMatcher.Match(optic.Materials, relaxed, ["Fused Silica"], [587.6]));
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, 0, [0, 1e-5]);
        var changed = optic.ToSnapshot().Surfaces[1];
        Assert.Equal(original.Radius, changed.Radius);
        Assert.Equal(original.Thickness, changed.Thickness);
        Assert.Equal(0, OpticalDesignParameters.NormalizedProfileDifference(original, original));
        Assert.True(OpticalDesignParameters.NormalizedProfileDifference(original, changed) > 0);
    }

    [Fact]
    public void ExcessiveGradientWorkingSetsAreRejectedBeforeTracingOrAllocatingRayTangents()
    {
        var optic = Lens(1, true, true);
        using var measurement = SequentialTraceMeasurement.Begin();
        Assert.Throws<ArgumentException>(() => DifferentiableOpticalMetrics.Evaluate(optic, Parameters(true, true),
            Enumerable.Repeat(0.0, 20).ToArray(), Enumerable.Repeat(new PupilSample(0, 0, 1), 4000).ToArray()));
        Assert.Equal(0, measurement.RayCount);
    }

    internal static OpticalDesignParameter[] Parameters(bool asphere, bool relaxed)
    {
        var parameters = new List<OpticalDesignParameter>
        {
            new(1, OpticalDesignParameterKind.Curvature, .02), new(2, OpticalDesignParameterKind.Curvature, .02),
            new(1, OpticalDesignParameterKind.Thickness, 5), new(2, OpticalDesignParameterKind.Thickness, 50)
        };
        if (asphere) parameters.AddRange([new(1, OpticalDesignParameterKind.Conic), new(1, OpticalDesignParameterKind.EvenAsphereCoefficient, 1e-5, 1)]);
        if (relaxed) parameters.AddRange([new(1, OpticalDesignParameterKind.MaterialIndex), new(1, OpticalDesignParameterKind.MaterialAbbe, 50)]);
        return parameters.ToArray();
    }

    internal static Optic Lens(int stop, bool asphere, bool relaxed)
    {
        var optic = new Optic("Derivative regression singlet");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 4;
        optic.Fields.Add(new FieldPoint { YAngleDegrees = 7, Weight = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 486.1, Weight = 1, IsPrimary = false });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 587.6, Weight = 2, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 656.3, Weight = 1, IsPrimary = false });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, Material = "Air", SemiDiameter = 15 },
            new() { Radius = 50, Thickness = 5, Material = "N-BK7", SemiDiameter = 15, IsStop = stop == 1 },
            new() { Radius = -50, Thickness = 47, Material = "Air", SemiDiameter = 15, IsStop = stop == 2 },
            new() { Material = "Air", SemiDiameter = 30 }
        ]);
        if (asphere) optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(50, -.3, [0, 1e-5, -1e-9]);
        if (relaxed) optic.SurfaceGroup.Items[1].MaterialAfter = OpticalDesignParameters.CreateRelaxedGlass("DesignRelaxed-1", 1.52, 64);
        optic.SurfaceGroup.Renumber();
        optic.RayAimingEnabled = true;
        PhysicalStopCalibration.Apply(optic);
        return optic;
    }

    private static void Close(double expected, double actual, double tolerance) =>
        Assert.True(double.IsFinite(expected) && double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)),
            $"Independent derivative {expected:R}, automatic derivative {actual:R}, tolerance {tolerance:R}.");
}
