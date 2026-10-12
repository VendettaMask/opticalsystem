using System.Globalization;
using System.Numerics;
using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class HuygensPolarizationRepairTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("polarized-field1-linear")]
    [InlineData("polarized-field3-linear")]
    public void CapturedUnpolarizedHuygensIntensityMatchesTheOriginalNativeGrid(string name)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensExtensions");
        var folder = Path.Combine(root, name);
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "request.json")));
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Auto", "captured-settings.json")));
        using var system = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "system-data-settings.json")));
        Assert.True(system.RootElement.GetProperty("Polarization").GetProperty("Unpolarized").GetBoolean());
        var properties = settings.RootElement.GetProperty("properties");
        Assert.True(properties.GetProperty("UsePolarization").GetBoolean());
        Assert.False(properties.GetProperty("Normalize").GetBoolean());
        Assert.False(properties.GetProperty("UseCentroid").GetBoolean());
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        Assert.True(optic.Polarization.Unpolarized);
        var parameters = new Dictionary<string, string>
        {
            ["PupilSampling"] = request.RootElement.GetProperty("pupilSampling").GetInt32().ToString(CultureInfo.InvariantCulture),
            ["ImageSampling"] = "32",
            ["ImageDeltaMicrometers"] = properties.GetProperty("ImageDelta").GetDouble().ToString("R", CultureInfo.InvariantCulture),
            ["WavelengthNumber"] = request.RootElement.GetProperty("wavelength").GetInt32().ToString(CultureInfo.InvariantCulture),
            ["FieldNumber"] = request.RootElement.GetProperty("field").GetInt32().ToString(CultureInfo.InvariantCulture),
            ["UsePolarization"] = "true", ["UseCentroid"] = "false", ["Normalized"] = "false", ["Type"] = "线性"
        };
        var result = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens PSF", parameters);
        result = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(result))!;
        var actual = Assert.Single(result.PlotSeries).Points.Select(point => point.Value!.Value).ToArray();
        Assert.Equal("UnpolarizedMeanPower", ((JsonElement)result.Values["PolarizationMode"]).GetString());
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Auto", "data.json")));
        var grid = native.RootElement.GetProperty("dataGrids")[0];
        var expected = grid.GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
        Assert.Equal(1024, actual.Length);
        Assert.Equal(1024, expected.Length);
        Assert.All(actual, value => Assert.True(double.IsFinite(value) && value >= 0));
        var maximum = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        var relative = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Sum() / expected.Sum(v => v * v));
        output.WriteLine($"{name}: relativeL2={relative:R}; maximumAbsolute={maximum:R}");
        Assert.True(maximum < 1e-5, $"{name}: maximum absolute difference {maximum:R}");
        Assert.True(relative < 5e-5, $"{name}: relative L2 {relative:R}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(60)]
    public void AbsorbingInterfacePowersMatchIndependentTangentialAdmittance(double degrees)
    {
        var c = Math.Cos(degrees * Math.PI / 180);
        Complex n1 = 1, n2 = new(1.8, .7);
        var q1 = n1 * c;
        var q2 = Complex.Sqrt(n2 * n2 - Math.Pow(Math.Sin(degrees * Math.PI / 180), 2));
        var actual = FresnelPower.EvaluateHomogeneousPower(n1, n2, c);
        Check(q1, q2, actual.ReflectionS, actual.TransmissionS);
        Check(n1 * n1 / q1, n2 * n2 / q2, actual.ReflectionP, actual.TransmissionP);
        static void Check(Complex incident, Complex outgoing, Complex r, Complex t)
        {
            var reflected = (incident - outgoing) / (incident + outgoing);
            var tangential = 2 * incident / (incident + outgoing);
            var power = tangential.Magnitude * tangential.Magnitude * outgoing.Real / incident.Real;
            Assert.Equal(reflected.Magnitude * reflected.Magnitude, r.Magnitude * r.Magnitude, 12);
            Assert.Equal(power, t.Magnitude * t.Magnitude, 12);
        }
    }

    [Theory]
    [InlineData(1, 1.5, 0)]
    [InlineData(1, 1.5, 60)]
    [InlineData(1.5, 1, 30)]
    [InlineData(1.5, 1, 60)]
    public void TransparentComplexPowerContractPreservesTheExistingLosslessCoefficients(double n1, double n2, double degrees)
    {
        var c = Math.Cos(degrees * Math.PI / 180);
        Assert.Equal(FresnelPower.Evaluate(n1, n2, c), FresnelPower.EvaluateHomogeneousPower(n1, n2, c));
    }

    [Theory]
    [InlineData(.000001)]
    [InlineData(.1)]
    [InlineData(1)]
    public void AbsorbingExitKeepsForwardPowerAndDoesNotClampItsBoundaryCoefficient(double extinction)
    {
        Complex incident = new(1.5, extinction), outgoing = 1;
        var expected = 2 * incident / (incident + outgoing);
        var power = expected.Magnitude * expected.Magnitude / incident.Real;
        var actual = FresnelPower.EvaluateHomogeneousPower(incident, outgoing, 1);
        Assert.Equal(power, actual.TransmissionS.Magnitude * actual.TransmissionS.Magnitude, 12);
        Assert.Equal(power, actual.TransmissionP.Magnitude * actual.TransmissionP.Magnitude, 12);
        if (extinction == 1) Assert.True(power > 1); // Absorbing incidence is not normalized to net boundary power.
    }

    [Theory]
    [InlineData(.000001)]
    [InlineData(.00001)]
    public void ExplicitMeanPowerTransportIncludesBulkAbsorptionExactlyOnce(double extinction)
    {
        var optic = Plate(extinction);
        var source = new RealRay(new(0, 0, -1), new(0, 0, 1), 550, Intensity: .7);
        var result = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true,
            usePolarization: true, allowHomogeneousAbsorbingPower: true);
        Complex n = new(1.5, extinction);
        var entry = Complex.Abs(2 / (1 + n));
        var exit = Complex.Abs(2 * n / (1 + n));
        var expected = .7 * entry * entry * exit * exit * Math.Exp(-4 * Math.PI * extinction * 2 / .00055);
        Assert.True(result.InsideAllApertures);
        Assert.Equal(expected, result.UnpolarizedIntensity!.Value, 12);
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.TracePolarized(source, new(1, 0)));
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.DiagnoseApertures(source, usePolarization: true));
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(.7)]
    public void ScalarCoatingReplacesBareLossWithoutApplyingItsPowerTwice(double transmission)
    {
        var optic = Plate(.000001);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(transmission);
        var source = new RealRay(new(0, 0, -1), new(0, 0, 1), 550);
        var result = optic.SequentialRayTracer.DiagnoseApertures(source, stopAtIncidentImage: true,
            usePolarization: true, allowHomogeneousAbsorbingPower: true);
        Complex n = new(1.5, .000001);
        var exit = Complex.Abs(2 * n / (1 + n));
        Assert.Equal(transmission * exit * exit / n.Real * Math.Exp(-4 * Math.PI * .000001 * 2 / .00055),
            result.UnpolarizedIntensity!.Value, 12);
    }

    [Fact]
    public void AbsorbingPowerOptInDoesNotAcceptCoherentJonesInput()
    {
        var optic = Plate(.000001);
        optic.Polarization = new(Unpolarized: false);
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.DiagnoseApertures(
            new RealRay(new(0, 0, -1), new(0, 0, 1), 550), usePolarization: true, allowHomogeneousAbsorbingPower: true));
    }

    [Theory]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(0, 1, 0, 0, false)]
    [InlineData(1, 1, 45, -20, false)]
    [InlineData(1, 0, 0, 0, true)]
    [InlineData(0, 1, 0, 0, true)]
    [InlineData(1, 1, 45, -20, true)]
    public void CoherentXyzCenterMatchesIndependentComponentWiseFieldSum(double jx, double jy, double px, double py, bool coated)
    {
        var optic = TransparentReference();
        if (coated) optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([
            new(new ConstantIndexMaterial("reference film", 2.1, .15), 87)]);
        optic.Polarization = new(false, jx, jy, px, py);
        var wave = optic.Wavelengths[0];
        const int count = 8;
        var pupils = Enumerable.Range(0, count).SelectMany(row => Enumerable.Range(0, count)
            .Select(column => (X: (column - 4d) / 3, Y: (row - 4d) / 3)))
            .Where(p => p.X * p.X + p.Y * p.Y <= 25d / 36).ToArray();
        var field = (Hx: 0d, Hy: -1d);
        var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, pupils, aimAtStop: true);
        var center = DiffractionEngine.CreateHuygensImageFrame(optic, field, wave, true).Center;
        var expected = default(ComplexElectricField);
        var idealAmplitude = 0d;
        foreach (var sample in wavefront.Samples.Where(sample => sample.Intensity > 0))
        {
            var source = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy,
                sample.NormalizedPupilX, sample.NormalizedPupilY, wave.Micrometers, true).Rays.Single();
            var e = optic.SequentialRayTracer.TracePolarized(source, optic.Polarization.Input,
                inputCoordinates: optic.SurfaceGroup.Items[0].CoordinateSystem, includePropagationPhase: false).LocalField;
            idealAmplitude += Math.Sqrt(e.SquaredNorm);
            var displacement = center - new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
            var direction = sample.ImageDirection!.Value;
            var dot = displacement.X * direction.X + displacement.Y * direction.Y + displacement.Z * direction.Z;
            var path = -sample.OpdWaves * wave.Micrometers / 1000 + wavefront.ImageRefractiveIndex * (dot - wavefront.Radius);
            expected += e * Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * path / (wave.Micrometers / 1000));
        }
        var actual = DiffractionEngine.ComputeHuygensPsf(optic, field, wave, count, count, .00025,
            usePolarization: true, aimAtStop: true);
        Assert.Equal(HuygensPolarizationMode.CoherentXyz, actual.HuygensPolarization);
        Assert.Equal(expected.SquaredNorm / (idealAmplitude * idealAmplitude) * 100, actual.Values[4, 4], 10);
    }

    [Fact]
    public void CoherentStateChangesThePsfAndIsNotSilentlyAveraged()
    {
        var first = TransparentReference();
        var second = TransparentReference();
        first.Polarization = new(false, 1, 0);
        second.Polarization = new(false, 0, 1);
        var a = DiffractionEngine.ComputeHuygensPsf(first, (0, -1), first.Wavelengths[0], 16, 16, .00025, true, true);
        var b = DiffractionEngine.ComputeHuygensPsf(second, (0, -1), second.Wavelengths[0], 16, 16, .00025, true, true);
        Assert.True(a.Values.Cast<double>().Zip(b.Values.Cast<double>(), (x, y) => Math.Abs(x - y)).Max() > 1e-6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoherentGlobalPhaseAndInputScaleDoNotChangeIntensity(bool phase)
    {
        var first = TransparentReference(); var second = TransparentReference();
        first.Polarization = new(false, 3, 4, 20, 80);
        second.Polarization = phase ? new(false, 3, 4, 70, 130) : new(false, 3e300, 4e300, 20, 80);
        var a = DiffractionEngine.ComputeHuygensPsf(first, (0, -1), first.Wavelengths[0], 8, 8, .00025, true, true);
        var b = DiffractionEngine.ComputeHuygensPsf(second, (0, -1), second.Wavelengths[0], 8, 8, .00025, true, true);
        Assert.InRange(a.Values.Cast<double>().Zip(b.Values.Cast<double>(), (x, y) => Math.Abs(x - y)).Max(), 0, 1e-10);
    }

    [Fact]
    public void CoherentSpectrumAndPresentationKeepDistinctVectorFieldsAndCommonSpectralWeights()
    {
        var optic = TransparentReference(); optic.Polarization = new(false, 1, 2, 20, -40);
        var data = new HuygensPsfAnalysis(optic, 8, 8, .00025, wavelengthNumber: 0,
            fieldNumber: 3, usePolarization: true).GenerateData();
        Assert.Equal("Coherent XYZ Jones Huygens", data.Values["PolarizationModel"]);
        Assert.Equal("CoherentXyz", data.Values["PolarizationMode"]);
        Assert.Contains("Coherent XYZ", data.PlotOptions!.Title);
        Assert.Contains("Absorbing-interface", data.Values["PolarizationLimit"].ToString());
        var weights = optic.Wavelengths.Select(w => w.Weight * Math.Pow(optic.Wavelengths.Min(w => w.Nanometers) / w.Nanometers, 2)).ToArray();
        var expected = new double[64];
        var primary = optic.Wavelengths.First(w => w.IsPrimary);
        for (var index = 0; index < optic.Wavelengths.Count; index++)
        {
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, -1), optic.Wavelengths[index], 8, 8,
                .00025, true, true, referenceWavelength: primary);
            var values = psf.Values.Cast<double>().ToArray();
            for (var i = 0; i < values.Length; i++) expected[i] += weights[index] * values[i] / (weights.Sum() * 100);
        }
        var actual = Assert.Single(data.PlotSeries).Points.Select(p => p.Value!.Value).ToArray();
        Assert.InRange(actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max(), 0, 1e-11);
        var profile = new HuygensPsfCrossSectionAnalysis(optic, 8, 8, .00025, usePolarization: true).GenerateData();
        Assert.Contains("Coherent XYZ", profile.PlotOptions!.Title);
    }

    [Fact]
    public void UnpolarizedModeIgnoresUnusedCoherentInputButPublishesItsMeanPowerModel()
    {
        var first = Reference(); var second = Reference();
        first.Polarization = new(true, 1, 0); second.Polarization = new(true, 0, 1, 80, -20);
        var a = DiffractionEngine.ComputeHuygensPsf(first, (0, -1), first.Wavelengths[0], 8, 8, .00025, true, true);
        var b = DiffractionEngine.ComputeHuygensPsf(second, (0, -1), second.Wavelengths[0], 8, 8, .00025, true, true);
        Assert.Equal(HuygensPolarizationMode.UnpolarizedMeanPower, a.HuygensPolarization);
        Assert.Equal(a.Values.Cast<double>(), b.Values.Cast<double>());
    }

    [Fact]
    public void CoherentAbsorbingFieldStillFailsExplicitlyRatherThanUsingMeanPower()
    {
        var optic = Reference(); optic.Polarization = new(false, 1, 0);
        Assert.Throws<NotSupportedException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 8, 8, .00025, true, true));
    }

    [Fact]
    public void CoherentAfocalFieldDoesNotFallBackToTheLegacyScalarAverage()
    {
        var optic = TransparentReference(); optic.Polarization = new(false, 1, 0); optic.ImageSpaceAfocal = true;
        Assert.Throws<NotSupportedException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 8, 8, .00025, true, true));
    }

    private static Optic Reference()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensExtensions");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        return optic;
    }

    // Declared transparent fixture, not the original absorbing native certificate.
    private static Optic TransparentReference()
    {
        var optic = Reference();
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.MaterialAfter = new ConstantIndexMaterial("transparent-" + surface.Number,
                surface.MaterialAfter.RefractiveIndex(optic.Wavelengths[0].Nanometers));
        return optic;
    }

    private static Optic Plate(double extinction)
    {
        var optic = new Optic("Absorbing mean-power reference plate");
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 2, MaterialAfter = new ConstantIndexMaterial("absorber", 1.5, extinction), IsStop = true, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 5, SemiDiameter = 30, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 30, SemiDiameterFixed = true }]);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
}
