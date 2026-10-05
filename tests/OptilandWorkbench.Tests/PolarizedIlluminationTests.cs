using System.Numerics;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class PolarizedIlluminationTests
{
    [Theory]
    [InlineData(1, 1.5, 0)]
    [InlineData(1, 1.5, 56.309932474020215)]
    [InlineData(1.5, 1, 30)]
    [InlineData(1.5, 1, 60)]
    [InlineData(1, 1, 70)]
    public void LosslessInterfacesConserveSeparatePolarizationPowers(double n1, double n2, double angle)
    {
        var actual = FresnelPower.Evaluate(n1, n2, Math.Cos(angle * Math.PI / 180));
        Assert.Equal(1, Squared(actual.ReflectionS) + Squared(actual.TransmissionS), 12);
        Assert.Equal(1, Squared(actual.ReflectionP) + Squared(actual.TransmissionP), 12);
    }

    [Fact]
    public void NormalIncidenceUsesPowerFluxAndBrewsterPHasNoReflection()
    {
        var normal = FresnelPower.Evaluate(1, 1.5, 1);
        Assert.Equal(0.96, Squared(normal.TransmissionS), 12);
        Assert.Equal(0.96, Squared(normal.TransmissionP), 12);
        Assert.Equal(-0.2, normal.ReflectionS.Real, 12);
        Assert.Equal(0.2, normal.ReflectionP.Real, 12);
        var brewster = FresnelPower.Evaluate(1, 1.5, Math.Cos(Math.Atan(1.5)));
        Assert.InRange(brewster.ReflectionP.Magnitude, 0, 1e-14);
        Assert.Equal(1, Squared(brewster.TransmissionP), 12);
        Assert.Equal(Math.Pow((2.25 - 1) / (2.25 + 1), 2), Squared(brewster.ReflectionS), 12);
    }

    [Fact]
    public void TotalInternalReflectionRetainsSeparateComplexPhases()
    {
        const double n = 1.5;
        var theta = Math.PI / 3;
        var q = Math.Sqrt(Math.Pow(Math.Sin(theta), 2) - 1 / (n * n));
        var expectedS = Complex.FromPolarCoordinates(1, -2 * Math.Atan(q / Math.Cos(theta)));
        var expectedP = Complex.FromPolarCoordinates(1, -2 * Math.Atan(n * n * q / Math.Cos(theta)));
        var actual = FresnelPower.Evaluate(n, 1, Math.Cos(theta));
        Assert.True(actual.TotalInternalReflection);
        Assert.InRange((actual.ReflectionS - expectedS).Magnitude, 0, 1e-12);
        Assert.InRange((actual.ReflectionP - expectedP).Magnitude, 0, 1e-12);
        Assert.Equal(Complex.Zero, actual.TransmissionS);
        Assert.Equal(Complex.Zero, actual.TransmissionP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void RotatingPlanesRetainPolarizationAcrossTheEntireChain(double angleDegrees)
    {
        var k = new Vector3D(0, 0, 1);
        var power = new UnpolarizedPowerTransport(k);
        var angle = angleDegrees * Math.PI / 180;
        power.Apply(k, k, new Vector3D(1, 0, -1), 0.8, 0.3);
        power.Apply(k, k, new Vector3D(Math.Cos(angle), Math.Sin(angle), -1), 0.4, 0.9);
        var expected = 0.5 * (0.16 * (0.64 * Math.Pow(Math.Cos(angle), 2) + 0.09 * Math.Pow(Math.Sin(angle), 2))
            + 0.81 * (0.64 * Math.Pow(Math.Sin(angle), 2) + 0.09 * Math.Pow(Math.Cos(angle), 2)));
        Assert.Equal(expected, power.Power, 12);
        if (angleDegrees != 45)
            Assert.True(Math.Abs(power.Power - (0.64 + 0.09) * (0.16 + 0.81) / 4) > 0.01);
    }

    [Fact]
    public void ComplexRetardanceAffectsSubsequentDiattenuation()
    {
        var k = new Vector3D(0, 0, 1);
        var normal = new Vector3D(1, 0, -1);
        var power = new UnpolarizedPowerTransport(k);
        power.Apply(k, k, normal, 1, 0.5);
        power.Apply(k, k, new Vector3D(1, 1, -1), Complex.FromPolarCoordinates(1, 0.3), Complex.FromPolarCoordinates(1, -0.7));
        power.Apply(k, k, normal, 0.3, 0.9);
        var c2 = Math.Pow(Math.Cos(0.5), 2);
        var s2 = Math.Pow(Math.Sin(0.5), 2);
        Assert.Equal(0.5 * (0.09 * (c2 + 0.25 * s2) + 0.81 * (s2 + 0.25 * c2)), power.Power, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public void FormalPlateTraceMatchesTwoInterfaceUnpolarizedPower(double angleDegrees)
    {
        var optic = Plate(infinite: true);
        var source = Source(angleDegrees);
        var result = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true, usePolarization: true);
        Assert.True(result.InsideAllApertures);
        Assert.NotNull(result.UnclippedImage);
        Assert.Equal(1, result.UnclippedImage.Intensity);
        var (ts, tp) = IndependentTransmission(Math.Cos(angleDegrees * Math.PI / 180));
        Assert.Equal((ts * ts + tp * tp) / 2, result.UnpolarizedIntensity!.Value, 12);
        if (angleDegrees == 60)
            Assert.True(Math.Abs(result.UnpolarizedIntensity.Value - Math.Pow((ts + tp) / 2, 2)) > 0.005);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(1)]
    public void ScalarCoatingReplacesInterfaceLossAndIsAppliedOnce(double transmission)
    {
        var optic = Plate(infinite: true);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        var result = optic.SequentialRayTracer.DiagnoseApertures(Source(40), stopAtIncidentImage: true, usePolarization: true);
        var (ts, tp) = IndependentTransmission(Math.Cos(40 * Math.PI / 180));
        Assert.True(result.InsideAllApertures);
        Assert.Equal(transmission * (ts + tp) / 2, result.UnpolarizedIntensity!.Value, 12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticUsesTypedTirAndCommandedMirrorInteractions(bool mirror)
    {
        var optic = new Optic();
        var glass = new ConstantIndexMaterial("test glass", 1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, MaterialAfter = mirror ? new AirMaterial() : glass },
            new OpticalSurface { Thickness = -2, IsReflective = mirror },
            new OpticalSurface()
        ]);
        var source = Source(60);
        var result = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true, usePolarization: true);
        Assert.True(result.InsideAllApertures);
        Assert.Equal(1, result.UnpolarizedIntensity!.Value, 12);
        var history = optic.SequentialRayTracer.Trace(new RealRayBundle([source])).RayHistories.Single();
        Assert.Equal(mirror ? RayInteractionKind.Reflected : RayInteractionKind.TotalInternalReflection, history[1].InteractionKind);
    }

    [Fact]
    public void ImageIrradianceStopsBeforeImageCoatingAndRefraction()
    {
        var optic = Plate(infinite: true);
        var source = Source(20);
        var before = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true, usePolarization: true);
        var image = optic.SurfaceGroup.Items[^1];
        image.CoatingModel = new SimpleCoatingModel(0);
        image.MaterialAfter = new ConstantIndexMaterial("image medium", 2);
        var incident = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true, usePolarization: true);
        Assert.Equal(before, incident);
        var outgoing = optic.SequentialRayTracer.DiagnoseApertures(source);
        Assert.Equal(0, outgoing.UnclippedImage!.Intensity);
        Assert.Null(incident.UnclippedImage!.InteractionKind);
        Assert.NotNull(outgoing.UnclippedImage.InteractionKind);
        Assert.Equal(1, incident.UnclippedImage.RefractiveIndexBefore);
        Assert.Null(incident.UnclippedImage.RefractiveIndexAfter);
        Assert.Equal(1, outgoing.UnclippedImage.RefractiveIndexBefore);
        Assert.Equal(2, outgoing.UnclippedImage.RefractiveIndexAfter);
    }

    [Fact]
    public void FullPupilPolarizedIntegralMatchesIndependentPlateRadiometry()
    {
        var optic = Plate(infinite: false);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, usePolarization: true);
        // Independent test-only integration of angular area times two-interface Fresnel power.
        var expected = Simpson(r =>
        {
            var (ts, tp) = IndependentTransmission(10 / Math.Sqrt(100 + r * r));
            return 2 * Math.PI * r * 100 / Math.Pow(100 + r * r, 2) * (ts * ts + tp * tp) / 2;
        });
        Assert.InRange(Math.Abs(result.ProjectedCosineArea / expected - 1), 0, 0.0005);
        var scalar = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55);
        Assert.True(result.EffectiveFNumber > scalar.EffectiveFNumber);
        optic.SurfaceGroup.Items[^1].CoatingModel = new SimpleCoatingModel(0);
        var detector = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, usePolarization: true);
        Assert.Equal(result.ProjectedCosineArea, detector.ProjectedCosineArea, 12);
        Assert.Equal(scalar.ProjectedCosineArea, IlluminationMetrics.Evaluate(optic, (0, 0), 0.55).ProjectedCosineArea, 12);
        var parallel = IlluminationMetrics.EvaluateFields(optic, [(0, 0), (0, 0)], 0.55, usePolarization: true);
        Assert.All(parallel, value => Assert.Equal(detector, value));
    }

    [Theory]
    [InlineData("absorption")]
    [InlineData("ripple")]
    [InlineData("thin-lens")]
    public void UnsupportedPhysicsIsReportedInsteadOfPretendingScalarIsPolarized(string kind)
    {
        var optic = Plate(infinite: true);
        var surface = optic.SurfaceGroup.Items[1];
        if (kind == "absorption") surface.MaterialAfter = new ConstantIndexMaterial("absorbing", 1.5, 1e-6);
        if (kind == "ripple") surface.CoatingModel = new ApproximateTransmissionRippleCoating([]);
        if (kind == "thin-lens") surface.InteractionModel = new ThinLensInteractionModel(100);
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.DiagnoseApertures(
            Source(0), stopAtIncidentImage: true, usePolarization: true));
    }

    [Fact]
    public void AnalysisPublishesPolarizationAndRetainsDefaultScalarBehavior()
    {
        var optic = Plate(infinite: false);
        var scalar = new RelativeIlluminationAnalysis(optic, fieldDensity: 2).GenerateData();
        var polarized = new RelativeIlluminationAnalysis(optic, fieldDensity: 2, usePolarization: true).GenerateData();
        Assert.Equal(false, scalar.Values["UsePolarization"]);
        Assert.Equal(true, polarized.Values["UsePolarization"]);
        var scalarF = Assert.IsType<double[]>(scalar.Values["EffectiveFNumbers"]);
        var polarizedF = Assert.IsType<double[]>(polarized.Values["EffectiveFNumbers"]);
        Assert.True(polarizedF[0] > scalarF[0]);
    }

    [Fact]
    public void ApplicationAnalysisSettingReachesPolarizedCoreEvaluation()
    {
        var optic = Plate(infinite: false);
        var runtime = new WorkbenchRuntime(optic);
        var settings = new Dictionary<string, string> { ["FieldDensity"] = "2", ["UsePolarization"] = "true" };
        var polarized = runtime.BuildAnalysisView("Relative Illumination", settings);
        Assert.Single(polarized.SeriesList);
        Assert.Contains(polarized.Rows, row => row.Metric == "使用偏振" && row.Value.Equals("True", StringComparison.OrdinalIgnoreCase));
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("absorbing", 1.5, 1e-6);
        // If the application silently drops the flag, this unsupported polarized
        // interface would instead produce a plausible scalar curve.
        var unsupported = Assert.Throws<NotSupportedException>(() => runtime.BuildAnalysisView("Relative Illumination", settings));
        Assert.Contains("complex-index", unsupported.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Optic Plate(bool infinite)
    {
        var optic = new Optic();
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        var glass = new ConstantIndexMaterial("test glass", 1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = infinite ? double.PositiveInfinity : 10 },
            new OpticalSurface { IsStop = true, Thickness = 2, SemiDiameter = 1, MaterialAfter = glass },
            new OpticalSurface { Thickness = 3, MaterialBefore = glass },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static RealRay Source(double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180;
        return new(new Vector3D(-Math.Tan(angle), 0, -1), new Vector3D(Math.Sin(angle), 0, Math.Cos(angle)), 550);
    }

    private static (double Ts, double Tp) IndependentTransmission(double cosine)
    {
        var transmitted = Math.Sqrt(1 - (1 - cosine * cosine) / 2.25);
        var rs = (cosine - 1.5 * transmitted) / (cosine + 1.5 * transmitted);
        var rp = (1.5 * cosine - transmitted) / (1.5 * cosine + transmitted);
        return (1 - rs * rs, 1 - rp * rp);
    }

    private static double Squared(Complex value) => value.Magnitude * value.Magnitude;

    private static double Simpson(Func<double, double> function)
    {
        const int intervals = 10000;
        var total = function(0) + function(1);
        for (var i = 1; i < intervals; i++) total += (i % 2 == 0 ? 2 : 4) * function(i / (double)intervals);
        return total / (3 * intervals);
    }
}
