using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalHuygensQuadratureTests(ITestOutputHelper output)
{
    private static Optic Disk(double diameter = 2, double wavelength = .55) => OpticalFormatCatalog.Import(
        FormattableString.Invariant($"MODE SEQ\nUNIT MM X W X CM MR CPMM\nENPD {diameter}\nFTYP 0 0 1 1 0 0 1\nWAVM 1 {wavelength} 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM {diameter / 2}\nDISZ 10\nSURF 2\nDIAM 20"), ".zmx");

    [Theory]
    [InlineData(16, 2, .55, .02)]
    [InlineData(32, 2, .55, .02)]
    [InlineData(64, 2, .55, .02)]
    [InlineData(128, 2, .55, .02)]
    [InlineData(32, 1, .45, .03)]
    [InlineData(32, 4, .7, .01)]
    public void TracedContinuousDiskMeetsAnIndependentStrictAiryGate(int n, double diameter, double wavelength, double pitch)
    {
        var optic = Disk(diameter, wavelength);
        var data = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], n, 32, pitch,
            AfocalHuygensIntegration.GaussianPupil);
        Assert.Equal(AfocalHuygensIntegration.GaussianPupil, data.AfocalIntegration);
        Assert.False(data.FiniteCellHuygensUsed);
        Assert.Equal(n * n, data.PupilQuadratureNodeCount);
        Assert.Equal(AnalysisAxisUnit.Milliradian, data.SampleSpacingUnit);
        Assert.Equal(pitch, data.SampleSpacingMicrometers);
        var maximum = 0d;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var radius = pitch * Math.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16));
            var expected = Airy(Math.PI * diameter * radius / wavelength);
            maximum = Math.Max(maximum, Math.Abs(data.Values[y, x] / 100 - expected));
        }
        // Additional continuum-accuracy gate. The original native gates and the
        // older O(h) diagnostic budget are retained without modification.
        Assert.InRange(maximum, 0, 1e-10);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "gaussian-airy", n, diameter, wavelength, pitch, maximum, gate = 1e-10 }));
    }

    [Theory]
    [InlineData(0, -.2)]
    [InlineData(0, 0)]
    [InlineData(0, .2)]
    [InlineData(.7, -.2)]
    [InlineData(.7, 0)]
    [InlineData(.7, .2)]
    [InlineData(2, -.2)]
    [InlineData(2, 0)]
    [InlineData(2, .2)]
    public void ActualApodizationAndDefocusHaveTheClosedFormComplexDiskIntegral(double attenuation, double defocus)
    {
        var optic = Disk(); optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, attenuation);
        // sqrt(I)=exp(-a*r²); phase=b*r², with the declared 1 mm pupil radius.
        var b = Math.PI * defocus / .55;
        var z = new Complex(-attenuation, b);
        var integral = z.Magnitude == 0 ? Complex.One : (Complex.Exp(z) - 1) / z;
        var reference = attenuation == 0 ? 1 : (1 - Math.Exp(-attenuation)) / attenuation;
        var expected = Math.Pow(integral.Magnitude / reference, 2);
        foreach (var paraxial in new[] { false, true })
        {
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 1, .02,
                AfocalHuygensIntegration.GaussianPupil, defocus: defocus, useParaxialPupilDefocus: paraxial);
            Assert.InRange(Math.Abs(psf.Values[0, 0] / 100 - expected), 0, 1e-10);
        }
    }

    [Fact]
    public void ShiftedGridUsesTheSamePhysicalAnglesWithoutWindowRenormalization()
    {
        var optic = Disk();
        var centered = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 32, .02,
            AfocalHuygensIntegration.GaussianPupil);
        var shifted = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 16, .02,
            AfocalHuygensIntegration.GaussianPupil, imageCenterOffset: (.08, -.04));
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            Assert.InRange(Math.Abs(shifted.Values[y, x] - centered.Values[y + 6, x + 12]), 0, 1e-10);
        Assert.True(centered.Values.Cast<double>().Sum() > shifted.Values.Cast<double>().Sum());
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("Huygens MTF")]
    public void ApplicationSelectionUsesSharedCoreAndNamesTheChosenProtocol(string name)
    {
        var optic = Disk(); var runtime = new WorkbenchRuntime(optic);
        var settings = new Dictionary<string, string> { ["PupilSampling"] = "32", ["ImageSampling"] = "32",
            ["ImageDeltaMilliradians"] = ".02", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["ProfileType"] = "Both" };
        var legacy = runtime.BuildAnalysisData(name, settings);
        Assert.Equal("NativeFiniteCell", legacy.Values["AfocalIntegration"]);
        settings["AfocalIntegration"] = "高阶圆瞳积分";
        var improved = runtime.BuildAnalysisData(name, settings);
        Assert.Equal("GaussianPupil", improved.Values["AfocalIntegration"]);
        var expectedPsf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 32, .02,
            AfocalHuygensIntegration.GaussianPupil);
        if (name != "Huygens MTF")
        {
            Assert.False((bool)improved.Values["FiniteCellHuygensUsed"]);
            Assert.Equal(1024, improved.Values["PupilQuadratureNodeCount"]);
            if (name == "Huygens PSF")
                Assert.Equal(expectedPsf.Values.Cast<double>().Select(v => v / 100), Assert.Single(improved.PlotSeries).Points.Select(p => p.Value!.Value));
            else
                for (var index = 0; index < improved.PlotSeries.Count; index++)
                {
                    var curve = Enumerable.Range(0, 32).Select(i => (index == 0
                        ? expectedPsf.Values[16, i] : expectedPsf.Values[i, 16]) / 100).ToArray();
                    // Existing Both presentation peak-normalizes each profile.
                    // Compare its declared presentation, not a different PSF datum.
                    var peak = curve.Max();
                    Assert.Equal(curve.Select(v => v / peak), improved.PlotSeries[index].Points.Select(p => p.Y));
                }
        }
        else
        {
            Assert.False((bool)improved.Values["ZemaxCompatible"]);
            Assert.Equal("LinearDftPeriod", improved.Values["FrequencySampling"]);
            Assert.Equal(32, improved.Values["MtfTransformSize"]);
            var total = expectedPsf.Values.Cast<double>().Sum();
            foreach (var series in improved.PlotSeries)
                foreach (var point in series.Points)
                {
                    var field = Complex.Zero;
                    for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                        field += expectedPsf.Values[y, x] * Complex.FromPolarCoordinates(1, 2 * Math.PI * point.X * .02 * (x - 16));
                    Assert.InRange(Math.Abs(point.Y - field.Magnitude / total), 0, 1e-12);
                }
        }
        Assert.Contains(runtime.GetAnalysisParameters(name), p => p.Key == "AfocalIntegration");
    }

    [Fact]
    public void ExistingApiKeepsItsExactNativeConvention()
    {
        var optic = Disk();
        var legacy = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 32, .02);
        var explicitNative = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 32, .02,
            AfocalHuygensIntegration.NativeFiniteCell);
        Assert.Equal(legacy.Values.Cast<double>(), explicitNative.Values.Cast<double>());
        Assert.Equal(AfocalHuygensIntegration.NativeFiniteCell, legacy.AfocalIntegration);
        Assert.True(legacy.FiniteCellHuygensUsed);
    }

    [Fact]
    public void UnsupportedModesAndDarkReferenceCannotSilentlyBecomeValidPsf()
    {
        var optic = Disk(); var wave = optic.Wavelengths[0];
        Assert.Throws<NotSupportedException>(() => DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 1, .02,
            AfocalHuygensIntegration.GaussianPupil, usePolarization: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 1024, 512, .02,
            AfocalHuygensIntegration.GaussianPupil));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 1, .02,
            (AfocalHuygensIntegration)99));
        // The chief ray remains available, while every finite quadrature node
        // is blocked. This reaches the new reference-integral precondition.
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(1e-6);
        Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 32, 1, .02,
            AfocalHuygensIntegration.GaussianPupil));
    }

    [Theory]
    [InlineData(.4, .3, .12, .08)]
    [InlineData(1.2, -.3, .2, -.16)]
    [InlineData(.7, .2, .04, .1)]
    public void NonuniformDefocusedPupilAgreesWithIndependentRadialHankelIntegration(double a, double defocus, double x, double y)
    {
        var optic = Disk(); optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, a);
        var v = Math.PI * 2 * Math.Sqrt(x * x + y * y) / .55;
        var b = Math.PI * defocus / .55;
        // Composite Simpson in r², independent of the production Gauss nodes
        // and azimuthal sums. Angular integration has the analytic J0 kernel.
        const int intervals = 2048; var integral = Complex.Zero;
        for (var i = 0; i <= intervals; i++)
        {
            var t = (double)i / intervals; var z = v * Math.Sqrt(t);
            double term = 1, j0 = 1;
            for (var k = 1; k < 50; k++) { term *= -z * z / (4 * k * k); j0 += term; if (Math.Abs(term) < 1e-18) break; }
            integral += (i == 0 || i == intervals ? 1 : i % 2 == 0 ? 2 : 4) * Complex.Exp(new Complex(-a * t, b * t)) * j0;
        }
        integral /= 3 * intervals;
        var expected = Math.Pow(integral.Magnitude / ((1 - Math.Exp(-a)) / a), 2);
        var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 1, .02,
            AfocalHuygensIntegration.GaussianPupil, defocus: defocus, imageCenterOffset: (x, y));
        Assert.InRange(Math.Abs(psf.Values[0, 0] / 100 - expected), 0, 1e-10);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(2, true)]
    public void PupilDistanceInUniformObjectMediumIsTheActualStopDistance(double index, bool infinite)
    {
        var optic = new Optic(); var medium = new ConstantIndexMaterial("Uniform medium", index);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.Fields.Add(new FieldPoint { Y = 1 }); optic.Aperture.Value = 2;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Label = "Object", Thickness = infinite ? double.PositiveInfinity : 10, MaterialAfter = medium },
            new OpticalSurface { Thickness = 12, MaterialAfter = medium },
            new OpticalSurface { IsStop = true, Thickness = 5, MaterialAfter = medium },
            new OpticalSurface { Label = "Image", MaterialAfter = medium }
        ]);
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateEntrancePupilLocation() - (infinite ? 12 : 22)), 0, 1e-12);
        if (infinite)
        {
            var chief = optic.TraceGenericFinalSample(0, 1, 0, 0, .55, aimAtStop: false)!;
            var stopZ = optic.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z;
            var atStopY = chief.Position.Y + (stopZ - chief.Position.Z) * chief.Direction.Y / chief.Direction.Z;
            Assert.InRange(Math.Abs(atStopY), 0, 1e-12);
            optic.ImageSpaceAfocal = true; optic.Wavelengths[0].Nanometers *= index;
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 1, .02,
                AfocalHuygensIntegration.GaussianPupil, imageCenterOffset: (.14, .06));
            var expected = Airy(Math.PI * 2 * Math.Sqrt(.14 * .14 + .06 * .06) / .55);
            Assert.InRange(Math.Abs(psf.Values[0, 0] / 100 - expected), 0, 1e-10);
        }
    }

    private static double Airy(double z)
    {
        if (z == 0) return 1;
        var term = z / 2; var sum = term;
        for (var k = 1; k < 100; k++) { term *= -z * z / (4 * k * (k + 1)); sum += term; if (Math.Abs(term) < 1e-17) break; }
        return Math.Pow(2 * sum / z, 2);
    }
}
