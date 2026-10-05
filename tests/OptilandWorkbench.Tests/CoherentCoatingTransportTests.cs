using System.Numerics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CoherentCoatingTransportTests
{
    [Theory]
    [InlineData(1, 1.5, 0)]
    [InlineData(1, 1.5, 40)]
    [InlineData(1, 1.5, 56.309932474020215)]
    [InlineData(1.5, 1, 30)]
    [InlineData(1.5, 1, 60)]
    public void EmptyStackAgreesWithPowerFresnelIncludingSignsAndTir(double n1, double n2, double degrees)
    {
        var solver = new CoherentThinFilmSolver(N(n1), N(n2), []);
        var expected = FresnelPower.Evaluate(n1, n2, Math.Cos(degrees * Math.PI / 180));
        var s = solver.EvaluateAmplitude(550, degrees, ThinFilmPolarization.S);
        var p = solver.EvaluateAmplitude(550, degrees, ThinFilmPolarization.P);
        Close(expected.ReflectionS, s.Reflection);
        Close(expected.ReflectionP, p.Reflection);
        Close(expected.TransmissionS, s.PowerTransmission!.Value);
        Close(expected.TransmissionP, p.PowerTransmission!.Value);
        Assert.Equal(s.Power, solver.Evaluate(550, degrees, ThinFilmPolarization.S));
        Assert.Equal(p.Power, solver.Evaluate(550, degrees, ThinFilmPolarization.P));
    }

    [Theory]
    [InlineData(ThinFilmPolarization.S)]
    [InlineData(ThinFilmPolarization.P)]
    public void MatchedFilmRetainsItsPropagationPhase(ThinFilmPolarization polarization)
    {
        const double n = 1.5, thickness = 111, angle = 32, wavelength = 550;
        var solver = new CoherentThinFilmSolver(N(n), N(n), [new(N(n), thickness)]);
        var response = solver.EvaluateAmplitude(wavelength, angle, polarization);
        var phase = 2 * Math.PI * n * thickness * Math.Cos(angle * Math.PI / 180) / wavelength;
        Close(Complex.Zero, response.Reflection);
        Close(Complex.FromPolarCoordinates(1, phase), response.PowerTransmission!.Value);
    }

    [Theory]
    [InlineData(ThinFilmPolarization.S, 0)]
    [InlineData(ThinFilmPolarization.P, 0)]
    [InlineData(ThinFilmPolarization.S, 45)]
    [InlineData(ThinFilmPolarization.P, 45)]
    public void AbsorbingSingleFilmMatchesIndependentAiryAmplitudes(ThinFilmPolarization polarization, double angle)
    {
        var film = new Complex(2.1, 0.15);
        var solver = new CoherentThinFilmSolver(N(1), N(1.5), [new(N(film.Real, film.Imaginary), 87)]);
        var actual = solver.EvaluateAmplitude(550, angle, polarization);
        var expected = Airy(film, 87, angle, polarization);
        Close(expected.R, actual.Reflection);
        Close(expected.T, actual.PowerTransmission!.Value);
        Assert.True(actual.Power.Absorptance > 0);
    }

    [Fact]
    public void QuarterWaveAntireflectionRetainsQuarterCycleTransmissionPhase()
    {
        var n = Math.Sqrt(1.52);
        var solver = new CoherentThinFilmSolver(N(1), N(1.52), [new(N(n), 550 / (4 * n))]);
        foreach (var pol in new[] { ThinFilmPolarization.S, ThinFilmPolarization.P })
        {
            var value = solver.EvaluateAmplitude(550, 0, pol);
            Close(Complex.Zero, value.Reflection);
            Close(Complex.ImaginaryOne, value.PowerTransmission!.Value);
        }
    }

    [Fact]
    public void AbsorbingSubstrateKeepsReflectionButDoesNotInventTransverseTransmission()
    {
        var solver = new CoherentThinFilmSolver(N(1), N(0.2, 4), []);
        var expected = (Complex.One - new Complex(0.2, 4)) / (Complex.One + new Complex(0.2, 4));
        var s = solver.EvaluateAmplitude(550, 0, ThinFilmPolarization.S);
        var p = solver.EvaluateAmplitude(550, 0, ThinFilmPolarization.P);
        Close(expected, s.Reflection);
        Close(-expected, p.Reflection);
        Assert.Null(s.PowerTransmission);
        Assert.Null(p.PowerTransmission);
        Assert.True(s.Power.Transmittance > 0);
        Assert.Throws<ArgumentException>(() => solver.EvaluateAmplitude(550, 0, ThinFilmPolarization.Unpolarized));
    }

    [Fact]
    public void ThickAbsorberKeepsFiniteLogTransmissionAndPhaseAfterAmplitudeUnderflow()
    {
        var solver = new CoherentThinFilmSolver(N(1), N(1.5), [new(N(0.2, 4), 100000)]);
        var response = solver.EvaluateAmplitude(550, 20, ThinFilmPolarization.P);
        Assert.True(double.IsFinite(response.Power.LogTransmittance));
        Assert.True(response.Power.OpticalDensity > 1000);
        Assert.True(double.IsFinite(response.TransmissionPhaseRadians));
        Assert.Equal(Complex.Zero, response.PowerTransmission);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(60)]
    public void CoatedPlateUsesIncidentAngleAndDoesNotMultiplyScalarLossTwice(double degrees)
    {
        var optic = Plate();
        var ray = Source(degrees);
        var actual = optic.SequentialRayTracer.DiagnoseApertures(ray, stopAtIncidentImage: true, usePolarization: true);
        var scalar = optic.SequentialRayTracer.DiagnoseApertures(ray, stopAtIncidentImage: true);
        var (ts, tp) = BarePowers(degrees);
        Assert.Equal((ts * ts + tp * tp) / 2, actual.UnpolarizedIntensity!.Value, 12);
        Assert.Equal(Math.Pow((ts + tp) / 2, 2), scalar.UnclippedImage!.Intensity, 12);
        Assert.Equal(1, actual.UnclippedImage!.Intensity);
        if (degrees == 60) Assert.True(actual.UnpolarizedIntensity > scalar.UnclippedImage.Intensity);
        using var traced = optic.SequentialRayTracer.Trace(new RealRayBundle([ray]), TraceRequest.FullHistory());
        Assert.True(traced.TryGetSample(0, 3, out var final));
        Assert.Equal(scalar.UnclippedImage.Intensity, final.Intensity, 12);
    }

    [Fact]
    public void AbsorbingFilmTransportsItsComplexJonesResponseThroughTheNextInterface()
    {
        var optic = Plate();
        var film = new Complex(2.1, 0.15);
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(N(film.Real, film.Imaginary), 87)]);
        var actual = optic.SequentialRayTracer.DiagnoseApertures(Source(45), stopAtIncidentImage: true, usePolarization: true);
        var s = Airy(film, 87, 45, ThinFilmPolarization.S).T;
        var p = Airy(film, 87, 45, ThinFilmPolarization.P).T;
        var (ts, tp) = BarePowers(45);
        Assert.Equal((Squared(s) * ts + Squared(p) * tp) / 2, actual.UnpolarizedIntensity!.Value, 12);
    }

    [Fact]
    public void ReflectionFromAbsorbingSubstrateUsesPhysicalCoatingInsteadOfIdealMirrorPower()
    {
        var optic = new Optic();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsReflective = true, Thickness = -2, MaterialAfter = N(0.2, 4), CoatingModel = new CoherentMultilayerCoating([]) },
            new OpticalSurface()
        ]);
        var actual = optic.SequentialRayTracer.DiagnoseApertures(Source(0), stopAtIncidentImage: true, usePolarization: true);
        var expected = Squared((Complex.One - new Complex(0.2, 4)) / (Complex.One + new Complex(0.2, 4)));
        Assert.True(actual.InsideAllApertures);
        Assert.Equal(expected, actual.UnpolarizedIntensity!.Value, 12);
        Assert.True(actual.UnpolarizedIntensity < 1);
    }

    [Fact]
    public void ImagePlaneCoatingIsExcludedFromIncidentIrradiance()
    {
        var optic = Plate();
        var before = optic.SequentialRayTracer.DiagnoseApertures(Source(40), stopAtIncidentImage: true, usePolarization: true);
        optic.SurfaceGroup.Items[^1].CoatingModel = new CoherentMultilayerCoating([new(N(0.2, 4), 100000)]);
        var after = optic.SequentialRayTracer.DiagnoseApertures(Source(40), stopAtIncidentImage: true, usePolarization: true);
        Assert.Equal(before, after);
    }

    [Fact]
    public void SharedIlluminationUsesThePersistedPhysicalCoatingModel()
    {
        var optic = Plate();
        optic.Aperture.Value = 2;
        optic.SurfaceGroup.Items[0].Thickness = 10;
        var empty = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, usePolarization: true);
        foreach (var surface in optic.SurfaceGroup.Items) surface.CoatingModel = new NoneCoatingModel();
        var bare = IlluminationMetrics.Evaluate(optic, (0, 0), 0.55, usePolarization: true);
        Assert.Equal(bare.ProjectedCosineArea, empty.ProjectedCosineArea, 12);
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(N(0.3, 3.5), 50)]);
        var absorbing = IlluminationMetrics.Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), (0, 0), 0.55, usePolarization: true);
        Assert.True(absorbing.ProjectedCosineArea > 0 && absorbing.ProjectedCosineArea < bare.ProjectedCosineArea / 2);
        Assert.True(absorbing.EffectiveFNumber > bare.EffectiveFNumber);
    }

    [Fact]
    public void LegacyElectricFieldPupilDoesNotSilentlyReplacePhysicalStackWithBareFresnel()
    {
        var optic = Plate();
        Assert.Throws<NotSupportedException>(() => JonesPupilEngine.Generate(optic, (0, 0), optic.Wavelengths[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDocumentRoundTripPreservesLayerPhysicsAndPower(bool project)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([
            new(new CauchyMaterial("film A", 1.7, 0.01), 79), new(N(0.3, 3.5), 12)]);
        var expected = optic.SequentialRayTracer.DiagnoseApertures(Source(35), stopAtIncidentImage: true, usePolarization: true);
        var path = Path.Combine(Path.GetTempPath(), "coherent-coating-" + Guid.NewGuid() + (project ? ".staropt" : ".json"));
        try
        {
            Optic loaded;
            if (project)
            {
                await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
                loaded = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            }
            else
            {
                await OpticJsonStore.SaveAsync(optic, path);
                loaded = await OpticJsonStore.LoadAsync(path);
            }
            var coating = Assert.IsType<CoherentMultilayerCoating>(loaded.SurfaceGroup.Items[1].CoatingModel);
            Assert.Equal(2, coating.Layers.Count);
            Assert.IsType<CauchyMaterial>(coating.Layers[0].Material);
            Assert.Equal(3.5, coating.Layers[1].Material.ExtinctionCoefficient(550));
            var actual = loaded.SequentialRayTracer.DiagnoseApertures(Source(35), stopAtIncidentImage: true, usePolarization: true);
            Assert.Equal(expected.UnpolarizedIntensity!.Value, actual.UnpolarizedIntensity!.Value, 12);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("material")]
    [InlineData("thickness")]
    [InlineData("negative")]
    [InlineData("count")]
    [InlineData("index")]
    public void IncompletePhysicalCoatingPayloadIsRejectedRatherThanDefaulted(string mutation)
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([new(N(1.4), 91)]);
        var snapshot = optic.ToSnapshot();
        var data = snapshot.Surfaces[1].Components!.Coating!;
        if (mutation == "material") data.Children!.Clear();
        if (mutation == "thickness") data.Numbers.Remove("thickness_0");
        if (mutation == "negative") data.Numbers["thickness_0"] = -1;
        if (mutation == "count") data.Numbers["count"] = 5000;
        if (mutation == "index") data.Children!["material_0"].Numbers.Remove("index");
        Assert.ThrowsAny<ArgumentException>(() => ComponentSnapshotFactory.ToCoating(data));
        Assert.ThrowsAny<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot));
    }

    private static Optic Plate()
    {
        var optic = new Optic();
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        var glass = N(1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 2, IsStop = true, SemiDiameter = 1, MaterialAfter = glass, CoatingModel = new CoherentMultilayerCoating([]) },
            new OpticalSurface { Thickness = 3, MaterialBefore = glass, CoatingModel = new CoherentMultilayerCoating([]) },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static ConstantIndexMaterial N(double n, double k = 0) => new("n=" + n, n, k);
    private static double Squared(Complex value) => value.Magnitude * value.Magnitude;
    private static void Close(Complex expected, Complex actual) => Assert.InRange((actual - expected).Magnitude, 0, 2e-12);
    private static RealRay Source(double angle)
    {
        angle *= Math.PI / 180;
        return new(new Vector3D(-Math.Tan(angle), 0, -1), new Vector3D(Math.Sin(angle), 0, Math.Cos(angle)), 550);
    }
    private static (double Ts, double Tp) BarePowers(double degrees)
    {
        var cosine = Math.Cos(degrees * Math.PI / 180);
        var transmitted = Math.Sqrt(1 - (1 - cosine * cosine) / 2.25);
        return (1 - Math.Pow((cosine - 1.5 * transmitted) / (cosine + 1.5 * transmitted), 2),
            1 - Math.Pow((1.5 * cosine - transmitted) / (1.5 * cosine + transmitted), 2));
    }

    // Independent test-only two-interface Airy sum, using full electric-field amplitudes.
    private static (Complex R, Complex T) Airy(Complex film, double thickness, double degrees, ThinFilmPolarization pol)
    {
        var sin = Math.Sin(degrees * Math.PI / 180);
        Complex n0 = 1, ns = 1.5, c0 = Math.Cos(degrees * Math.PI / 180);
        var c1 = Complex.Sqrt(1 - sin * sin / (film * film));
        var cs = Complex.Sqrt(1 - sin * sin / (ns * ns));
        var first = Interface(n0, film, c0, c1);
        var second = Interface(film, ns, c1, cs);
        var e = Complex.Exp(Complex.ImaginaryOne * (2 * Math.PI * film * c1 * thickness / 550));
        var denominator = 1 + first.R * second.R * e * e;
        var flux = Math.Sqrt((ns * cs).Real / (n0 * c0).Real);
        return ((first.R + second.R * e * e) / denominator, first.T * second.T * e / denominator * flux);

        (Complex R, Complex T) Interface(Complex a, Complex b, Complex ca, Complex cb) => pol == ThinFilmPolarization.S
            ? ((a * ca - b * cb) / (a * ca + b * cb), 2 * a * ca / (a * ca + b * cb))
            : ((b * ca - a * cb) / (b * ca + a * cb), 2 * a * ca / (b * ca + a * cb));
    }
}
