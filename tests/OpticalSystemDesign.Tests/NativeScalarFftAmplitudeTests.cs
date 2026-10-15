using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.FileIO;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class NativeScalarFftAmplitudeTests
{
    private readonly ITestOutputHelper output;
    public NativeScalarFftAmplitudeTests(ITestOutputHelper output) => this.output = output;
    [Theory]
    [InlineData("ms-l7-32", 32)]
    [InlineData("ms-l7-64", 64)]
    [InlineData("cooke-32", 32)]
    [InlineData("tessar-32", 32)]
    public void FormalScalarPupilMatchesFrozenNativeFftWithoutImportedAmplitudes(string name, int sampling)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Validation/Zemax/FftPupilPhase", name);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(directory, "source.ZMX")), ".zmx");
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "probe/model.json")));
        var aiming = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        optic.RayAimingEnabled = aiming;
        var psf = Compute(optic, sampling, compatible: true, aiming: aiming);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"psf-{sampling}/data.json")));
        var grid = expected.RootElement.GetProperty("dataGrids")[0];
        var squared = 0d; var referenceSquared = 0d; var maximum = 0d;
        for (var y = 0; y < grid.GetProperty("ny").GetInt32(); y++)
            for (var x = 0; x < grid.GetProperty("nx").GetInt32(); x++)
            {
                var reference = grid.GetProperty("values")[y][x].GetDouble();
                var actual = PsfAnalysis.BilinearSample(psf,
                    grid.GetProperty("minX").GetDouble() + x * grid.GetProperty("dx").GetDouble(),
                    grid.GetProperty("minY").GetDouble() + y * grid.GetProperty("dy").GetDouble()) / 100;
                var difference = actual - reference;
                squared += difference * difference; referenceSquared += reference * reference;
                maximum = Math.Max(maximum, Math.Abs(difference));
            }
        var relativeL2 = Math.Sqrt(squared / referenceSquared);
        output.WriteLine($"{name}: formal scalar FFT L2={relativeL2:R}, max={maximum:R}");
        // Regression budget for the formal traced phase; this is not the
        // tighter native-input FFT check or a DEE acceptance tolerance.
        Assert.True(relativeL2 <= 2e-5 && maximum <= 1e-5,
            $"{name}: formal scalar FFT L2={relativeL2:R}, max={maximum:R}; original grid, no native pupil substitution.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GeneralAndPolarizedFftRetainTracedTransmission(bool compatible, bool polarized)
    {
        var optic = Lens();
        var pupil = Prepare(optic, compatible);
        Assert.Contains(pupil.Samples, s => s.Intensity > 0 && s.Intensity < s.LaunchIntensity);
        var expected = Compute(optic, 32, compatible, polarized, pupil);
        var automatic = Compute(optic, 32, compatible, polarized);
        Assert.Equal(expected.Values.Cast<double>(), automatic.Values.Cast<double>());
    }

    [Fact]
    public void ScalarCompatibilityPreservesGaussianLaunchPower()
    {
        var optic = Lens(); optic.Apodization = new GaussianApodization(.5);
        var pupil = Prepare(optic, true);
        var accepted = pupil with { Samples = pupil.Samples.Select(s => s.Intensity > 0
            ? s with { Intensity = optic.Apodization.Intensity(s.NormalizedPupilX, s.NormalizedPupilY) }
            : s).ToArray() };
        Assert.True(accepted.Samples.Where(s => s.Intensity > 0).Max(s => s.Intensity)
            > 2 * accepted.Samples.Where(s => s.Intensity > 0).Min(s => s.Intensity));
        var expected = Compute(optic, 32, true, pupil: accepted);
        var actual = Compute(optic, 32, true);
        Assert.Equal(expected.Values.Cast<double>(), actual.Values.Cast<double>());
        var spectrum = DiffractionEngine.ComputeFftPsfSpectrum(optic, (0, 0), [optic.Wavelengths[0]],
            32, 64, cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true,
            referenceWavelength: optic.Wavelengths[0], ignoreScalarTransmission: true);
        Assert.Equal(actual.Values.Cast<double>(), spectrum.Single().Result.Values.Cast<double>());
    }

    [Fact]
    public void PreparedAmplitudesRemainAuthoritativeIncludingZeroPower()
    {
        var optic = Lens(); var pupil = Prepare(optic, true);
        var masked = pupil with { Samples = pupil.Samples.Select(s => s with
            { Intensity = s.NormalizedPupilX < 0 ? 0 : s.Intensity * .5 }).ToArray() };
        var original = masked.Samples.ToArray();
        var maskedPsf = Compute(optic, 32, true, pupil: masked);
        Assert.Equal(original, masked.Samples);
        Assert.False(maskedPsf.Values.Cast<double>().SequenceEqual(Compute(optic, 32, true).Values.Cast<double>()));
    }

    private static Optic Lens() => OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures/zemax-ms-l7-high-na.ZMX")), ".zmx");
    private static WavefrontResult Prepare(Optic optic, bool compatible) => WavefrontEngine.GenerateChiefRayUniform(
        optic, (0, 0), optic.Wavelengths[0], 32, cellCentered: true, aimAtStop: true,
        zemaxCentered: compatible, referenceWavelength: optic.Wavelengths[0]);
    private static PsfResult Compute(Optic optic, int sampling, bool compatible, bool polarized = false,
        WavefrontResult? pupil = null, bool aiming = true) => DiffractionEngine.ComputeFftPsf(
        optic, (0, 0), optic.Wavelengths[0], sampling, 2 * sampling, usePolarization: polarized,
        cellCenteredPupil: true, zemaxFftSampling: compatible, preparedWavefront: pupil,
        aimAtStop: aiming, referenceWavelength: optic.Wavelengths[0],
        ignoreScalarTransmission: compatible);
}
