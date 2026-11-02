using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class ThinFilmSubstrateTests
{
    [Fact]
    public void CompletePlateMatchesIndependentMixedCoherenceReferenceWithRealSubstrateLoss()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/incoherent-substrate-20261102.json")));
        foreach (var sample in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            static IMaterial Material(JsonElement n) => new ConstantIndexMaterial("independent physical input", n[0].GetDouble(), n[1].GetDouble());
            var films = sample.GetProperty("layers").EnumerateArray().Select(l => new CoherentFilm(Material(l.GetProperty("n")), l.GetProperty("d").GetDouble()));
            var solver = new CoherentThinFilmSolver(Material(sample.GetProperty("n0")), Material(sample.GetProperty("ns")), films);
            var actual = solver.EvaluateIncoherentSubstrate(Material(sample.GetProperty("ne")), sample.GetProperty("thickness").GetDouble(),
                sample.GetProperty("wavelength").GetDouble(), sample.GetProperty("angle").GetDouble(),
                sample.GetProperty("polarization").GetString() == "s" ? ThinFilmPolarization.S : ThinFilmPolarization.P);
            Assert.InRange(Math.Abs(actual.Reflectance - sample.GetProperty("r").GetDouble()), 0, 2e-11);
            Assert.InRange(Math.Abs(actual.Transmittance - sample.GetProperty("t").GetDouble()), 0, 2e-11);
            Assert.InRange(Math.Abs(actual.Absorptance - sample.GetProperty("a").GetDouble()), 0, 2e-11);
        }
    }

    [Fact]
    public void BarePlateIncludesRearReflectionAndOpaqueSubstrateRetainsUntruncatedLogTransmission()
    {
        var air = new AirMaterial(); var plate = new CoherentThinFilmSolver(air, new ConstantIndexMaterial("glass", 1.5), []);
        var power = plate.EvaluateIncoherentSubstrate(air, 1e6, 550, 0, ThinFilmPolarization.Unpolarized);
        Assert.Equal(1.0 / 13, power.Reflectance, 12); Assert.Equal(12.0 / 13, power.Transmittance, 12);
        Assert.InRange(Math.Abs(power.Absorptance), 0, 1e-12);
        var opaque = new CoherentThinFilmSolver(air, new ConstantIndexMaterial("absorbing glass", 1.5, .1), [])
            .EvaluateIncoherentSubstrate(air, 1e6, 550, 35, ThinFilmPolarization.P);
        Assert.Equal(0, opaque.Transmittance); Assert.True(double.IsFinite(opaque.LogTransmittance)); Assert.True(opaque.LogTransmittance < -1000);
        Assert.Throws<ArgumentException>(() => plate.EvaluateIncoherentSubstrate(air, 0, 550, 0, ThinFilmPolarization.S));
        var tir = new CoherentThinFilmSolver(new ConstantIndexMaterial("incident", 1.7), air, []);
        Assert.Throws<NotSupportedException>(() => tir.EvaluateIncoherentSubstrate(air, 1e6, 550, 65, ThinFilmPolarization.S));
    }

    [Fact]
    public async Task GeometryAndWholePlateDisplayRoundTripWithoutRedefiningFrontSurfaceAcceptance()
    {
        var service = new DesignService(); var d = ReferenceExamples.Create("demo-mgf2-ar");
        var front = service.Evaluate(d); var fingerprint = front.Fingerprint();
        var plate = front with { StackGeometry = new("Air", 2) };
        Assert.Equal(fingerprint, plate.Fingerprint()); Assert.Equal(front.Result!.Metrics, service.Evaluate(plate).Result!.Metrics);
        var options = new AnalysisSpectrumOptions(500, 600, 25, [0, 35]);
        var a = service.EvaluateAnalysisDisplay(front, options); var b = service.EvaluateAnalysisDisplay(plate, options);
        Assert.Contains(a[0].Spectrum.Average.Zip(b[0].Spectrum.Average), p => Math.Abs(p.First.Power.Transmittance - p.Second.Power.Transmittance) > 1e-3);
        var noRear = service.EvaluateAnalysisDisplay(plate with { StackGeometry = plate.StackGeometry! with { ExitMediumId = plate.SubstrateId } }, options);
        Assert.Contains(b[0].Spectrum.Average.Zip(noRear[0].Spectrum.Average), p => Math.Abs(p.First.Power.Reflectance - p.Second.Power.Reflectance) > 1e-4);
        var path = Path.Combine(Path.GetTempPath(), "coating-plate-" + Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(plate, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(plate.StackGeometry, reopened.StackGeometry); Assert.Equal(fingerprint, reopened.Fingerprint());
            Assert.Equal(b[1].Spectrum.P, service.EvaluateAnalysisDisplay(reopened, options)[1].Spectrum.P);
        }
        finally { File.Delete(path); }
        Assert.Throws<InvalidDataException>(() => (plate with { StackGeometry = new("missing", 1) }).Validate());
    }
}
