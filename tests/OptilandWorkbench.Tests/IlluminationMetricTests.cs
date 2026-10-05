using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class IlluminationMetricTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0.9, 0)]
    [InlineData(0.5, 0)]
    [InlineData(1, 0.3)]
    [InlineData(0.8, 0.4)]
    public void FullAndObscuredConesMatchIndependentSolidAngleIntegral(double outer, double inner)
    {
        var optic = Cone();
        var aperture = inner == 0 ? (IPhysicalAperture)new CircularAperture(outer) : new AnnularAperture(outer, inner);
        optic.SurfaceGroup.Items[1].PhysicalAperture = aperture;
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        var expected = Disk(outer) - Disk(inner);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / expected - 1), 0, 0.0005);
        Assert.InRange(Math.Abs(result.EffectiveFNumber / (0.5 * Math.Sqrt(Math.PI / expected)) - 1), 0, 0.00025);
        Assert.Same(aperture, optic.SurfaceGroup.Items[1].PhysicalAperture);
        Assert.True(result.ValidRays > 0 && result.SampledPupilNodes >= result.ValidRays);
    }

    [Theory]
    [InlineData("gaussian")]
    [InlineData("polynomial")]
    [InlineData("cosine")]
    [InlineData("hann")]
    public void NonuniformAndDarkRimPupilsIntegrateInteriorTransmission(string model)
    {
        var optic = Cone();
        optic.Apodization = model switch
        {
            "gaussian" => new GaussianApodization(0.45),
            "polynomial" => new PolynomialApodization(1, 2),
            "cosine" => new CosineSquaredApodization(),
            _ => new HannApodization()
        };
        // Independent analytical cone Jacobian with a high-resolution Simpson rule.
        // This reference is test-only and never provides production illumination.
        var expected = Simpson(r => 2 * Math.PI * r * 100 / Math.Pow(100 + r * r, 2)
            * (model switch
            {
                "gaussian" => Math.Exp(-r * r / (2 * 0.45 * 0.45)),
                "polynomial" => Math.Pow(1 - r * r, 2),
                "cosine" => Math.Pow(Math.Cos(Math.PI * r / 2), 2),
                _ => 0.5 * (1 - Math.Cos(Math.PI * r))
            }), 0, 1, 10000);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / expected - 1), 0, 0.0005);
        Assert.True(double.IsFinite(result.EffectiveFNumber));
    }

    [Fact]
    public void OffCenterInteriorObscurationSubtractsItsOwnAngularArea()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new DifferenceAperture(
            new CircularAperture(1), new OffsetRadialAperture(0.2, offsetX: 0.45));
        var hole = Simpson(r => Simpson(angle =>
        {
            var x = 0.45 + r * Math.Cos(angle);
            var y = r * Math.Sin(angle);
            return r * 100 / Math.Pow(100 + x * x + y * y, 2);
        }, 0, 2 * Math.PI, 240), 0, 0.2, 240);
        var actual = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55).ProjectedCosineArea;
        Assert.InRange(Math.Abs(actual / (Disk(1) - hole) - 1), 0, 0.0005);
    }

    [Fact]
    public void DisconnectedTransmittingRegionsDoNotRequireAVisibleChiefRay()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new UnionAperture(
            new OffsetRadialAperture(0.15, offsetX: 0.5), new OffsetRadialAperture(0.15, offsetX: -0.5));
        var expected = 2 * Simpson(r => Simpson(angle =>
        {
            var x = 0.5 + r * Math.Cos(angle);
            var y = r * Math.Sin(angle);
            return r * 100 / Math.Pow(100 + x * x + y * y, 2);
        }, 0, 2 * Math.PI, 240), 0, 0.15, 240);
        var actual = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55).ProjectedCosineArea;
        Assert.InRange(Math.Abs(actual / expected - 1), 0, 0.001);
    }

    [Fact]
    public void TwoDimensionalTransmissionUsesInteriorWeights()
    {
        var optic = Cone();
        optic.Apodization = new QuadraticTransmission();
        var expected = Simpson(r => 2 * Math.PI * r * 100 / Math.Pow(100 + r * r, 2)
            * (1 - 0.45 * r * r), 0, 1, 10000);
        var actual = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55).ProjectedCosineArea;
        Assert.InRange(Math.Abs(actual / expected - 1), 0, 0.0005);
    }

    private sealed class QuadraticTransmission : IApodizationModel
    {
        public string Kind => "test-quadratic-transmission";
        public double Intensity(double normalizedPupilX, double normalizedPupilY) =>
            1 - 0.7 * normalizedPupilX * normalizedPupilX - 0.2 * normalizedPupilY * normalizedPupilY;
        public IApodizationModel Clone() => new QuadraticTransmission();
    }

    [Fact]
    public void ConstantWeightShortcutMatchesGeneralInteriorQuadrature()
    {
        var optic = Cone();
        var optimized = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        optic.Apodization = new ConstantTransmission();
        var general = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        Assert.Equal(general.ProjectedCosineArea, optimized.ProjectedCosineArea, 12);
        Assert.True(optimized.SampledPupilNodes < general.SampledPupilNodes / 2);
    }

    private sealed class ConstantTransmission : IApodizationModel
    {
        public string Kind => "test-unit-transmission";
        public double Intensity(double normalizedPupilX, double normalizedPupilY) => 1;
        public IApodizationModel Clone() => new ConstantTransmission();
    }

    [Fact]
    public void BulkAbsorptionRetainsAngleDependentPathTransmission()
    {
        var optic = Cone();
        var material = new ConstantIndexMaterial("Absorber", 1, 1e-6);
        optic.SurfaceGroup.Items[1].MaterialAfter = material;
        optic.SurfaceGroup.Items[^1].MaterialBefore = material;
        var expected = Simpson(r => 2 * Math.PI * r * 100 / Math.Pow(100 + r * r, 2)
            * Math.Exp(-4 * Math.PI * 1e-6 * 1000 / 0.55 * Math.Sqrt(100 + r * r)), 0, 1, 10000);
        var actual = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55).ProjectedCosineArea;
        Assert.InRange(Math.Abs(actual / expected - 1), 0, 0.0005);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.25)]
    [InlineData(0)]
    public void ScalarTransmissionControlsEqualIrradianceFNumber(double transmission)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        if (transmission == 0)
        {
            Assert.Equal(0, result.ProjectedCosineArea);
            Assert.True(double.IsPositiveInfinity(result.EffectiveFNumber));
            Assert.Equal(0, result.ValidRays);
        }
        else
        {
            Assert.InRange(Math.Abs(result.ProjectedCosineArea / (Disk(1) * transmission) - 1), 0, 0.0002);
            Assert.InRange(Math.Abs(result.EffectiveFNumber / (Math.Sqrt(101 / transmission) / 2) - 1), 0, 0.0001);
        }
    }

    [Fact]
    public void DiagnosticMaskDoesNotTurnBlockedChiefIntoAcceptedImageRay()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(1, 0.3);
        var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, 0.55, aimAtStop: true);
        var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(source.Rays.Single());
        Assert.NotNull(diagnostic.UnclippedImage);
        Assert.False(diagnostic.InsideAllApertures);
        Assert.Equal(1, diagnostic.FirstBlockedSurfaceIndex);
        Assert.Null(optic.SequentialRayTracer.TraceFinalSamples(source).Single());
        var dark = optic.SequentialRayTracer.DiagnoseApertures(source.Rays.Single() with { Intensity = 0 });
        Assert.NotNull(dark.UnclippedImage);
        Assert.Equal(0, dark.UnclippedImage.Intensity);
    }

    [Fact]
    public void CancellationAndInvalidMetricSettingsDoNotReturnPlausibleValues()
    {
        var optic = Cone();
        Assert.Throws<ArgumentOutOfRangeException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => IlluminationMetrics.Evaluate(optic, (double.NaN, 0), 0.55));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55,
            cancellationToken: cancelled.Token));
        using var scope = ComputationCancellation.Push(cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), 0.55));
        Assert.ThrowsAny<OperationCanceledException>(() => IlluminationMetrics.EvaluateFields(optic, [(0, 0)], 0.55));
    }

    [Fact]
    public void ParallelFieldsPreserveOrderValuesAndOriginalErrors()
    {
        var optic = Cone();
        optic.Fields[0].Y = 5;
        var fields = new[] { (0.0, 0.0), (0.0, 0.5), (0.0, 1.0), (0.0, -0.5) };
        var results = IlluminationMetrics.EvaluateFields(optic, fields, 0.55);
        for (var index = 0; index < fields.Length; index++)
            Assert.Equal(IlluminationMetrics.Evaluate(optic, fields[index], 0.55), results[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => IlluminationMetrics.EvaluateFields(optic, [(0, 0), (double.NaN, 0)], 0.55));
    }

    [Fact]
    public void InvalidAnalysisWavelengthIsNotSilentlyClamped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelativeIlluminationAnalysis(Cone(), wavelengthNumber: -1));
        Assert.Throws<AnalysisDataUnavailableException>(() => new RelativeIlluminationAnalysis(Cone(), wavelengthNumber: 2).GenerateData());
    }

    [Fact]
    public void FullyDarkPupilProducesUnavailableCurveAndBlackSimulatedImage()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0);
        var data = new RelativeIlluminationAnalysis(optic, fieldDensity: 2).GenerateData();
        Assert.Empty(data.PlotSeries);
        var source = new double[,] { { 1, 0.5 }, { 0.25, 0.75 } };
        ImageSimulationEngine.ApplyRelativeIllumination(source, optic, optic.Wavelengths[0], 0, 0, 10, 0, 0);
        Assert.All(source.Cast<double>(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void ImmersionUsesIncidentImageConeWithoutApplyingRefractiveIndexTwice()
    {
        var optic = Cone();
        optic.Materials.Register(new ConstantIndexMaterial("Immersion", 1.5));
        optic.SurfaceGroup.Items[1].MaterialAfter = optic.Materials.Resolve("Immersion");
        optic.SurfaceGroup.Items[^1].MaterialBefore = optic.Materials.Resolve("Immersion");
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / (Disk(1) / 2.25) - 1), 0, 0.0002);
        Assert.InRange(Math.Abs(result.EffectiveFNumber / (1.5 * Math.Sqrt(101) / 2) - 1), 0, 0.0001);
    }

    [Fact]
    public void ImageTiltIncludesIncidenceProjection()
    {
        var optic = Cone();
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with { RotationYDegrees = 30 };
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        var expected = Disk(1) * Math.Cos(Math.PI / 6);
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / expected - 1), 0, 0.0002);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedPupilSamplerMatchesFormalIndividualGeneration(bool infinite)
    {
        var optic = infinite ? Optic.CreateCookeTriplet() : Cone();
        foreach (var field in optic.Fields)
        {
            field.VignetteFactorX = 0.1;
            field.VignetteFactorY = 0.2;
            field.VignetteDecenterX = 0.05;
            field.VignetteDecenterY = -0.03;
            field.VignetteAngleDegrees = 12;
        }
        var generator = optic.SequentialRayTracer.RayGenerator;
        var sample = generator.CreatePupilRaySampler(0, 0.5, 0.55, aimAtStop: true);
        foreach (var (x, y) in new[] { (0.0, 0.0), (0.5, -0.5), (-0.3, 0.4), (0.0, 0.9) })
        {
            var expected = generator.GenerateGeneric(0, 0.5, x, y, 0.55, aimAtStop: true).Rays.Single();
            var actual = sample(x, y);
            Assert.InRange((actual.Origin - expected.Origin).Length, 0, 1e-10);
            Assert.InRange((actual.Direction - expected.Direction).Length, 0, 1e-12);
            Assert.Equal(expected.Intensity, actual.Intensity, 12);
        }
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

    private static double Disk(double radius) => Math.PI * radius * radius / (100 + radius * radius);

    private static double Simpson(Func<double, double> function, double from, double to, int intervals)
    {
        var sum = function(from) + function(to);
        var step = (to - from) / intervals;
        for (var index = 1; index < intervals; index++) sum += (index % 2 == 0 ? 2 : 4) * function(from + index * step);
        return sum * step / 3;
    }
}
