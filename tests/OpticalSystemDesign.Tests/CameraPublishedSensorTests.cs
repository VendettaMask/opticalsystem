using System.Text.Json;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Tests;

public sealed class CameraPublishedSensorTests
{
    // Each row is a point control at its own measured wavelength, not a reconstructed CFA spectrum.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PublishedQeAtItsActualWavelengthProducesTheExpectedPhotocharge(int row)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-flir-bfs-u3-23s6-points.json")));
        var data = doc.RootElement; var point = data.GetProperty("points")[row];
        var wavelength = point.GetProperty("wavelengthNanometers").GetDouble(); var qe = point.GetProperty("qe").GetDouble();
        const double photons = 1000; const double hc = 6.62607015e-34 * 299792458;
        var sensor = CameraContinuousSpectrumTests.Clean with { PixelPitchMicrometers = data.GetProperty("pixelPitchMicrometers").GetDouble() };
        var irradiance = photons * hc / (wavelength * 1e-9) / (Math.Pow(sensor.PixelPitchMicrometers * 1e-6, 2) * .01);
        var minimum = wavelength - .5; var maximum = wavelength + .5;
        var light = new ScalarSpectrum(ScalarSpectrumKind.Illuminant, [new(minimum, irradiance), new(maximum, irradiance)]);
        var rho = new ScalarSpectrum(ScalarSpectrumKind.Reflectance, [new(minimum, 1), new(maximum, 1)]);
        var response = new SensorSpectralResponse([new(minimum, qe, qe, qe), new(maximum, qe, qe, qe)]);
        var plan = CameraContinuousSpectrum.BuildPlan(light, rho, response, new(minimum, maximum, 1));
        var result = CameraContinuousSpectrum.Integrate(CameraContinuousSpectrumTests.White(), sensor, plan, pixelSampling: new(4, .25));
        Assert.All(result.PhotoElectrons, electrons => Assert.Equal(photons * qe, electrons, 9));
        CameraContinuousSpectrumTests.Record("published-qe-" + row, new { Model = point.GetProperty("model").GetString(), wavelength, qe,
            IncidentPhotons = photons, ExpectedElectrons = photons * qe, ActualElectrons = result.PhotoElectrons[0],
            Scope = "Reported QE point; locally flat 1 nm point-control band; not measured full-spectrum or RAW replay" });
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ReportedReadNoiseAndWellCapacityRemainPhysicalElectronParameters(int row)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-flir-bfs-u3-23s6-points.json")));
        var point = doc.RootElement.GetProperty("points")[row];
        var noise = point.GetProperty("readNoiseElectrons").GetDouble(); var well = point.GetProperty("wellElectrons").GetDouble();
        var sensor = new CameraSensorSettings(NoiseEnabled: true, ReadNoiseElectrons: noise, DarkElectronsPerSecond: 0,
            PrnuPercent: 0, DsnuElectrons: 0, FullWellElectrons: well, GainDnPerElectron: .01, BitDepth: 16, BlackLevel: 1024);
        var result = CameraSensorPipeline.SimulatePhotoElectrons(new double[65536], 256, 256, sensor, new());
        var mean = result.MeasuredElectrons.Average(); var variance = result.MeasuredElectrons.Sum(e => Math.Pow(e - mean, 2)) / 65535;
        Assert.InRange(Math.Abs(mean), 0, noise * .02); Assert.InRange(variance / (noise * noise), .98, 1.02);
        var clipped = CameraSensorPipeline.SimulatePhotoElectrons(Enumerable.Repeat(well * 2, 16).ToArray(), 4, 4,
            sensor with { NoiseEnabled = false }, new());
        Assert.All(clipped.MeasuredElectrons, e => Assert.Equal(well, e)); Assert.Equal(16, clipped.SaturatedPixels);
        CameraContinuousSpectrumTests.Record("published-noise-" + row, new { noise, well, mean, variance,
            Scope = "Parameter-application/statistical control; no measured frame series available" });
    }
}
