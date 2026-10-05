using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class ImageCosineIlluminationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(128)]
    public void CircularConeUsesUniformImageCosineCells(int density)
    {
        var actual = Uniform(Cone(), density);
        var expected = DiscreteCone(density, (_, _) => 1);
        Assert.Equal(expected.Area, actual.ProjectedCosineArea, 12);
        Assert.Equal(expected.Valid, actual.ValidRays);
        Assert.Equal(density * density, actual.IntegrationSamples);
        Assert.Equal(IlluminationSamplingKind.UniformImageCosine, actual.Sampling);
        Assert.True(actual.SampledPupilNodes > actual.ValidRays);
        if (density == 128)
            Assert.InRange(Math.Abs(actual.ProjectedCosineArea / (Math.PI / 101) - 1), 0, 0.002);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(21)]
    public void InteriorWeightsUseInverseTracedPupilCoordinates(int density)
    {
        var optic = Cone();
        optic.Apodization = new QuadraticTransmission();
        var result = Uniform(optic, density);
        var expected = DiscreteCone(density, (x, y) => 1 - 0.7 * x * x - 0.2 * y * y);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea - expected.Area), 0, 2e-10);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0)]
    public void ScalarAndZeroTransmissionAreNotReplacedByGeometricArea(double transmission)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        var actual = Uniform(optic, 10);
        Assert.Equal(DiscreteCone(10, (_, _) => transmission).Area, actual.ProjectedCosineArea, 12);
        if (transmission == 0)
        {
            Assert.Equal(0, actual.ValidRays);
            Assert.True(double.IsPositiveInfinity(actual.EffectiveFNumber));
        }
    }

    [Theory]
    [InlineData("annulus")]
    [InlineData("offset")]
    [InlineData("disconnected")]
    public void FormalApertureMaskIsEvaluatedAtGridRayIntersections(string shape)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].PhysicalAperture = shape switch
        {
            "annulus" => new AnnularAperture(1, 0.3),
            "offset" => new DifferenceAperture(new CircularAperture(1), new OffsetRadialAperture(0.2, offsetX: 0.45)),
            _ => new UnionAperture(new OffsetRadialAperture(0.15, offsetX: 0.5), new OffsetRadialAperture(0.15, offsetX: -0.5))
        };
        var expected = DiscreteCone(40, (x, y) => shape switch
        {
            "annulus" => x * x + y * y >= 0.09 ? 1 : 0,
            "offset" => Math.Pow(x - 0.45, 2) + y * y > 0.04 ? 1 : 0,
            _ => Math.Pow(Math.Abs(x) - 0.5, 2) + y * y <= 0.0225 ? 1 : 0
        });
        var actual = Uniform(optic, 40);
        Assert.Equal(expected.Valid, actual.ValidRays);
        Assert.Equal(expected.Area, actual.ProjectedCosineArea, 12);
    }

    [Fact]
    public void ImageProjectionUsesItsSurfaceNormal()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { RotationYDegrees = 30 };
        var result = Uniform(optic, 80);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / (Math.PI / 101 * Math.Cos(Math.PI / 6)) - 1), 0, 0.005);
    }

    [Fact]
    public void PhysicalCoatingAndBarePowerChainsAgreeForAnEmptyFilmStack()
    {
        var optic = Cone();
        var glass = new ConstantIndexMaterial("transparent-test-glass", 1.5);
        optic.SurfaceGroup.Items[1].MaterialAfter = glass;
        optic.SurfaceGroup.Items[^1].MaterialBefore = glass;
        var bare = Uniform(optic, 10, usePolarization: true);
        foreach (var surface in optic.SurfaceGroup.Items) surface.CoatingModel = new CoherentMultilayerCoating([]);
        var coated = Uniform(optic, 10, usePolarization: true);
        Assert.Equal(bare.ProjectedCosineArea, coated.ProjectedCosineArea, 11);
    }

    [Fact]
    public void ParallelFieldsMatchSerialAndDoNotChangeTheSource()
    {
        var optic = Optic.CreateCookeTriplet();
        var fields = new[] { (0.0, 0.0), (0.0, 0.4), (0.0, 1.0) };
        var serial = fields.Select(f => IlluminationMetrics.Evaluate(optic, f, 0.55, 10,
            sampling: IlluminationSamplingKind.UniformImageCosine)).ToArray();
        var parallel = IlluminationMetrics.EvaluateFields(optic, fields, 0.55, 10,
            sampling: IlluminationSamplingKind.UniformImageCosine);
        Assert.Equal(serial, parallel);
        Assert.Equal(3, optic.Fields.Count);
    }

    [Fact]
    public void InvalidSamplingAndCancellationDoNotProduceSuccessfulValues()
    {
        var optic = Cone();
        Assert.Throws<ArgumentOutOfRangeException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55,
            sampling: (IlluminationSamplingKind)99));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55,
            cancellationToken: source.Token, sampling: IlluminationSamplingKind.UniformImageCosine));
    }

    [Fact]
    public void ExistingAdaptiveSamplingRemainsTheExplicitDefault()
    {
        var optic = Cone();
        var expected = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, 10);
        Assert.Equal(expected, IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, 10,
            sampling: IlluminationSamplingKind.AdaptivePupil));
        Assert.Equal(0, expected.IntegrationSamples);
        Assert.Equal(IlluminationSamplingKind.AdaptivePupil, expected.Sampling);
    }

    [Fact]
    public void AxisReferenceWorksWithoutAnAxisFieldAndDoesNotClampOffAxisBrightness()
    {
        var optic = Cone();
        optic.Fields[0].X = 5;
        optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { RotationYDegrees = 30 };
        var a = IlluminationMetrics.RelativeToAxis(optic, (1, 0), 0.55, 40, sampling: IlluminationSamplingKind.UniformImageCosine);
        var b = IlluminationMetrics.RelativeToAxis(optic, (-1, 0), 0.55, 40, sampling: IlluminationSamplingKind.UniformImageCosine);
        Assert.True(Math.Max(a, b) > 1.01, $"RI(+x)={a:R}, RI(-x)={b:R}");
        Assert.Equal(5, optic.Fields[0].X);
        Assert.Equal(1, IlluminationMetrics.RelativeToAxis(optic, (0, 0), 0.55, 5,
            sampling: IlluminationSamplingKind.UniformImageCosine), 12);
    }

    [Fact]
    public void AxisReferenceClearsAllVignettingFactorsWithoutMutatingSource()
    {
        var optic = Cone();
        optic.Fields[0].Y = 5;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(0.9, 0.2);
        var expected = IlluminationMetrics.RelativeToAxis(optic, (0, 1), 0.55, 20,
            sampling: IlluminationSamplingKind.UniformImageCosine);
        var field = optic.Fields[0];
        field.VignetteFactorX = 0.2;
        field.VignetteFactorY = 0.3;
        field.VignetteDecenterX = 0.1;
        field.VignetteDecenterY = -0.1;
        field.VignetteAngleDegrees = 23;
        var before = PupilVignetting.FromField(field);
        var actual = IlluminationMetrics.RelativeToAxis(optic, (0, 1), 0.55, 20,
            sampling: IlluminationSamplingKind.UniformImageCosine);
        Assert.Equal(expected, actual, 12);
        Assert.Equal(before, PupilVignetting.FromField(field));
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0);
        Assert.Throws<AnalysisDataUnavailableException>(() => IlluminationMetrics.RelativeToAxis(optic, (0, 1), 0.55,
            sampling: IlluminationSamplingKind.UniformImageCosine));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsedAndFoldedAngularMapsAreExplicitlyRejected(bool folded)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].InteractionModel = new DegenerateMapping(folded);
        Assert.Throws<AnalysisDataUnavailableException>(() => Uniform(optic, 10));
    }

    private sealed class DegenerateMapping(bool folded) : IInteractionModel
    {
        public string Kind => "test-degenerate-angular-mapping";
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var r2 = ray.Origin.X * ray.Origin.X + ray.Origin.Y * ray.Origin.Y;
            var factor = folded ? (1 - 2 * r2) / 10 : 0;
            return new(ray with { Direction = new Vector3D(ray.Origin.X * factor, ray.Origin.Y * factor, 1) }, RayInteractionKind.Transmitted);
        }
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => ray;
        public IInteractionModel Clone() => new DegenerateMapping(folded);
    }

    [Fact]
    public void CapturedFileDensityStudyReportsCurrentGridWithoutClaimingNativeEquivalence()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
        optic = AnalysisTrace.PrepareVignettingFactors(optic, true);
        foreach (var density in new[] { 10, 20, 40 })
        {
            var result = IlluminationMetrics.EvaluateFields(optic, [(0, 0), (0, 0.5), (0, 1)], 0.44, density,
                sampling: IlluminationSamplingKind.UniformImageCosine);
            Assert.All(result, r => Assert.True(double.IsFinite(r.EffectiveFNumber) && r.ProjectedCosineArea > 0));
            output.WriteLine("ImageCosineStudy=" + JsonSerializer.Serialize(new
            {
                Density = density,
                EffectiveFNumbers = result.Select(r => r.EffectiveFNumber),
                RelativeIllumination = result.Select(r => r.ProjectedCosineArea / result[0].ProjectedCosineArea),
                ValidGridRays = result.Select(r => r.ValidRays),
                TraceNodes = result.Select(r => r.SampledPupilNodes)
            }));
        }
    }

    private static IlluminationMetricResult Uniform(Optic optic, int density, bool usePolarization = false) =>
        IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, density, usePolarization: usePolarization,
            sampling: IlluminationSamplingKind.UniformImageCosine);

    private static (double Area, int Valid) DiscreteCone(int density, Func<double, double, double> weight)
    {
        // Independent analytic inverse for a straight air cone, test reference only.
        var scale = 1 / Math.Sqrt(101);
        var sum = 0.0;
        var valid = 0;
        for (var j = 0; j < density; j++)
            for (var i = 0; i < density; i++)
            {
                var u = 2 * (i + 0.5) / density - 1;
                var v = 2 * (j + 0.5) / density - 1;
                if (u * u + v * v > 1) continue;
                var l = scale * u;
                var m = scale * v;
                var z = Math.Sqrt(1 - l * l - m * m);
                var w = weight(10 * l / z, 10 * m / z);
                sum += w;
                if (w > 0) valid++;
            }
        return (sum * Math.Pow(2 * scale / density, 2), valid);
    }

    private static Optic Cone()
    {
        var optic = new Optic();
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Label = "Object", Thickness = 10 },
            new OpticalSurface { Label = "Stop", IsStop = true, Thickness = 10, SemiDiameter = 1 },
            new OpticalSurface { Label = "Image", Thickness = 0 }
        ]);
        return optic;
    }

    private sealed class QuadraticTransmission : IApodizationModel
    {
        public string Kind => "test-image-cosine-quadratic";
        public double Intensity(double x, double y) => 1 - 0.7 * x * x - 0.2 * y * y;
        public IApodizationModel Clone() => new QuadraticTransmission();
    }
}
