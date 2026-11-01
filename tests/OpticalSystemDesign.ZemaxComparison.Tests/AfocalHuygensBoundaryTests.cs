using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Phase;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalHuygensBoundaryTests(ITestOutputHelper output)
{
    private static Optic Disk() => OpticalFormatCatalog.Import(
        "MODE SEQ\nUNIT MM X W X CM MR CPMM\nENPD 2\nFTYP 0 0 1 1 0 0 1\nWAVM 1 .55 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 1\nDISZ 10\nSURF 2\nDIAM 20", ".zmx");

    [Theory]
    [InlineData(.73, 0)]
    [InlineData(1, .31)]
    [InlineData(.81, .57)]
    public void ClippedDisksAndBlockedChiefAnnuliMatchAnalyticFourierIntegral(double outer, double inner)
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(outer, inner);
        var psf = DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 8, 16, .08);
        var maximum = Compare(psf, (kx, ky) =>
        {
            var k = Math.Sqrt(kx * kx + ky * ky);
            return (outer * outer * Jinc(outer * k) - inner * inner * Jinc(inner * k)) / (outer * outer - inner * inner);
        });
        Assert.Equal(inner > 0, psf.GeometricChiefReferenceUsed);
        Assert.InRange(Math.Abs(psf.HuygensReferenceAmplitude!.Value - (outer * outer - inner * inner)), 0, 1e-11);
        Record("annulus", psf, maximum, new { outer, inner });
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void BooleanRectangleWithDecenteredCircleAndRectangleHolesMatchesIndependentClosedForm(int initial)
    {
        var optic = Disk();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new DifferenceAperture(new RectangularAperture(.62, .41),
            new UnionAperture(new OffsetRadialAperture(.12, 0, .1, -.05), new RectangularAperture(.08, .05, -.32, .18)));
        var psf = Compute(optic, initial, 16, .08);
        var area = 4 * .62 * .41 - Math.PI * .12 * .12 - 4 * .08 * .05;
        var maximum = Compare(psf, (kx, ky) =>
            (4 * .62 * .41 * Sinc(.62 * kx) * Sinc(.41 * ky)
                - Math.PI * .12 * .12 * Jinc(.12 * Math.Sqrt(kx * kx + ky * ky)) * Complex.Exp(-Complex.ImaginaryOne * (.1 * kx - .05 * ky))
                - 4 * .08 * .05 * Sinc(.08 * kx) * Sinc(.05 * ky) * Complex.Exp(-Complex.ImaginaryOne * (-.32 * kx + .18 * ky))) / area);
        Assert.True(psf.GeometricChiefReferenceUsed);
        Assert.InRange(Math.Abs(psf.HuygensReferenceAmplitude!.Value - area / Math.PI), 0, 1e-11);
        Record("boolean-holes", psf, maximum, new { initial });
    }

    [Fact]
    public void NarrowDisjointPupilCannotHideBetweenAngularArms()
    {
        var optic = Disk(); const double radius = .003; const double x = .987; const double y = .014;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new OffsetRadialAperture(radius, 0, x, y);
        var psf = Compute(optic, 8, 8, .08);
        var maximum = Compare(psf, (kx, ky) => Jinc(radius * Math.Sqrt(kx * kx + ky * ky))
            * Complex.Exp(-Complex.ImaginaryOne * (x * kx + y * ky)));
        Assert.InRange(Math.Abs(psf.HuygensReferenceAmplitude!.Value - radius * radius), 0, 1e-13);
        Record("narrow-offset-disk", psf, maximum, new { radius, x, y });
    }

    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    [InlineData(180)]
    public void FastRadialPhaseNoLongerExhaustsAngularOrders(double defocus)
    {
        var optic = Disk(); var phase = Math.PI * defocus / .55;
        var expected = Math.Pow(Sinc(phase / 2), 2);
        var psf = Compute(optic, 8, 1, .02, defocus);
        var maximum = Math.Abs(psf.Values[0, 0] / 100 - expected);
        Assert.InRange(maximum, 0, 1e-10);
        Assert.True(Assert.Single(psf.HuygensSamplingChecks!).EffectiveRadialOrder > 64);
        Record("fast-radial-phase", psf, maximum, new { defocus, expected });
    }

    [Fact]
    public void RealPolynomialPhasePlateWithRectangularPupilMatchesSeparableFresnelIntegral()
    {
        var optic = OpticalFormatCatalog.Import(
            "MODE SEQ\nUNIT MM X W X CM MR CPMM\nENPD 2000\nFTYP 0 0 1 1 0 0 1\nWAVM 1 .55 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 1000\nDISZ 0\nSURF 2\nDIAM 20000", ".zmx");
        const double hx = .58, hy = .39, ax = 250, ay = -130, scale = 1000;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(hx * scale, hy * scale);
        // At the image reference plane: test actual Core phase interaction/OPL,
        // without changing the underlying shared propagation law.
        optic.SurfaceGroup.Items[1].InteractionModel = new PhaseInteractionModel(new PolynomialPhaseProfile(
            new Dictionary<(int X, int Y), double> { [(2, 0)] = ax / (scale * scale), [(0, 2)] = ay / (scale * scale) }));
        var psf = Compute(optic, 8, 4, .04 / scale);
        // Independent composite Simpson in Cartesian coordinates (not the polar production rule).
        Complex Integral(double h, double a, double k)
        {
            const int count = 32768; Complex sum = 0;
            for (var i = 0; i <= count; i++)
            {
                var x = -h + 2 * h * i / count;
                sum += (i == 0 || i == count ? 1 : i % 2 == 0 ? 2 : 4) * Complex.Exp(Complex.ImaginaryOne * (a * x * x - k * x));
            }
            return sum / (3 * count);
        }
        var maximum = Compare(psf, (kx, ky) => Integral(hx, ax, kx * scale) * Integral(hy, ay, ky * scale));
        Record("polynomial-phase-plate", psf, maximum, new { hx, hy, ax, ay, scale });
    }

    [Fact]
    public void BoundaryWorkBudgetRejectsBeforeTracingAndNeverReturnsPartialSuccess()
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.62, .41);
        var error = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeCheckedAfocalHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 8, 16, .08, new(MaximumOperations: 100)));
        var check = Assert.IsType<HuygensSamplingDiagnostics>(error.Data["HuygensSamplingDiagnostics"]);
        Assert.Equal("WorkLimitReached", check.Status); Assert.Empty(check.Trials); Assert.Equal(0, check.CumulativeOperations);
    }

    [Fact]
    public void RotatedDecenteredPolygonMatchesIndependentRectangleTransform()
    {
        const double hx = .43, hy = .19, cx = .17, cy = -.12, angle = .47;
        var c = Math.Cos(angle); var s = Math.Sin(angle); var optic = Disk();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new PolygonAperture(new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) }
            .Select(p => (cx + c * p.Item1 - s * p.Item2, cy + s * p.Item1 + c * p.Item2)));
        var psf = Compute(optic, 8, 16, .08);
        var maximum = Compare(psf, (kx, ky) => Sinc(hx * (c * kx + s * ky)) * Sinc(hy * (-s * kx + c * ky))
            * Complex.Exp(-Complex.ImaginaryOne * (cx * kx + cy * ky)));
        Record("rotated-polygon", psf, maximum, new { hx, hy, cx, cy, angle });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffAxisAndOnAxisNormalizationUseTheirOwnPhysicalAperturePreimages(bool aiming)
    {
        var optic = Disk(); optic.Fields.Add(new FieldPoint { X = 3 });
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.6, .4);
        optic.SurfaceGroup.Items[2].PhysicalAperture = new RectangularAperture(.4, .3);
        var psf = DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (1, 0), optic.Wavelengths[0], 8, 1, .08, aimAtStop: aiming);
        var referenceArea = .8 * .6;
        var fieldArea = (.6 + .4 - 10 * Math.Tan(3 * Math.PI / 180)) * .6;
        var expected = Math.Pow(fieldArea / referenceArea, 2);
        var maximum = Math.Abs(psf.Values[0, 0] / 100 - expected);
        Assert.InRange(maximum, 0, 1e-10);
        Assert.InRange(Math.Abs(psf.HuygensReferenceAmplitude!.Value - referenceArea / Math.PI), 0, 1e-11);
        Record("off-axis-reference", psf, maximum, new { aiming, expected, referenceArea, fieldArea });
    }

    [Fact]
    public void GeometricChiefIsOptInAndInsufficientPhaseOrderRemainsUnavailable()
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(1, .31);
        Assert.Throws<InvalidOperationException>(() => WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), optic.Wavelengths[0],
            new[] { (.5, 0d) }, aimAtStop: false));
        var error = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeCheckedAfocalHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 8, 1, .08, new(MaximumRadialOrder: 8), defocus: 40));
        var check = Assert.IsType<HuygensSamplingDiagnostics>(error.Data["HuygensSamplingDiagnostics"]);
        Assert.Equal("OrderLimitReached", check.Status);
        Assert.Equal(1e-10, check.Tolerance); Assert.True(check.MaximumIntensityDisagreement > 1e-10);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "phase-order-rejected", check }));
    }

    [Theory]
    [InlineData(.6, .7)]
    [InlineData(.37, .98)]
    public void CircleIntersectionsPartitionTangenciesAndCrossings(double radius, double distance)
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new OffsetRadialAperture(radius, 0, distance, 0);
        var psf = Compute(optic, 8, 8, .08);
        var area = Math.Acos((distance * distance + 1 - radius * radius) / (2 * distance))
            + radius * radius * Math.Acos((distance * distance + radius * radius - 1) / (2 * distance * radius))
            - Math.Sqrt((-distance + 1 + radius) * (distance + 1 - radius) * (distance - 1 + radius) * (distance + 1 + radius)) / 2;
        var maximum = Math.Abs(psf.HuygensReferenceAmplitude!.Value - area / Math.PI);
        Assert.InRange(maximum, 0, 1e-10);
        Assert.InRange(Math.Abs(psf.Values[4, 4] / 100 - 1), 0, 1e-10);
        Record("circle-intersection-area", psf, maximum, new { radius, distance, area });
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("Huygens MTF")]
    public void BlockedChiefAnnulusIsUsableThroughAllApplicationPaths(string name)
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(1, .31);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(name, new Dictionary<string, string>
        {
            ["AfocalIntegration"] = "CheckedGaussianPupil", ["PupilSampling"] = "8", ["ImageSampling"] = "16",
            ["ImageDeltaMilliradians"] = ".08", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        var checks = Assert.IsAssignableFrom<IReadOnlyList<HuygensSamplingDiagnostics>>(data.Values["SamplingChecks"]);
        Assert.All(checks, c => Assert.Equal("SamplingStable", c.Status));
        Assert.Contains(checks.SelectMany(c => c.Trials), t => t.GeometricChiefReferenceUsed);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "annulus-application", name, checks }));
    }

    private static double Compare(PsfResult psf, Func<double, double, Complex> amplitude)
    {
        var maximum = 0d; var center = psf.GridSize / 2;
        for (var y = 0; y < psf.GridSize; y++) for (var x = 0; x < psf.GridSize; x++)
        {
            var a = amplitude(2 * Math.PI / .55 * (x - center) * psf.SampleSpacingMicrometers,
                2 * Math.PI / .55 * (y - center) * psf.SampleSpacingMicrometers);
            maximum = Math.Max(maximum, Math.Abs(psf.Values[y, x] / 100 - a.Magnitude * a.Magnitude));
        }
        Assert.InRange(maximum, 0, 1e-10);
        return maximum;
    }
    private void Record(string kind, PsfResult psf, double maximum, object parameters)
    {
        var check = Assert.Single(psf.HuygensSamplingChecks!);
        Assert.Equal("SamplingStable", check.Status);
        Assert.InRange(check.CumulativeOperations, 1, AnalysisResourceLimits.MaximumDirectPsfOperations);
        output.WriteLine(JsonSerializer.Serialize(new { kind, parameters, maximum, psf.HuygensReferenceAmplitude,
            psf.GeometricChiefReferenceUsed, check }));
    }
    private PsfResult Compute(Optic optic, int initial, int grid, double pitch, double defocus = 0)
    {
        try { return DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (0, 0), optic.Wavelengths[0], initial, grid, pitch, defocus: defocus); }
        catch (AnalysisDataUnavailableException error)
        {
            output.WriteLine(JsonSerializer.Serialize(new { kind = "boundary-rejected", defocus, check = error.Data["HuygensSamplingDiagnostics"] }));
            throw;
        }
    }
    private static double Sinc(double x) => x == 0 ? 1 : Math.Sin(x) / x;
    private static double Jinc(double z)
    {
        if (z == 0) return 1; var term = z / 2; var sum = term;
        for (var k = 1; k < 100; k++) { term *= -z * z / (4 * k * (k + 1)); sum += term; if (Math.Abs(term) < 1e-17) break; }
        return 2 * sum / z;
    }
}
