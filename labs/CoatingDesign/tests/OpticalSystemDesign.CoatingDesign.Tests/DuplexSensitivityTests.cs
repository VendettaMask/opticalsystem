using System.Text.Json;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class DuplexSensitivityTests
{
    [Fact]
    public void BothFaceAdjointsMatchCentralDifferencesAcross162IndependentPhysicalInputs()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/duplex-substrate-20261102.json")));
        foreach (var sample in json.RootElement.GetProperty("cases").EnumerateArray())
        {
            static IMaterial M(JsonElement n) => new ConstantIndexMaterial("independent input", n[0].GetDouble(), n[1].GetDouble());
            CoherentFilm[] Films(string face) => sample.GetProperty(face).EnumerateArray().Select(l => new CoherentFilm(M(l.GetProperty("n")), l.GetProperty("d").GetDouble())).ToArray();
            var f = Films("front"); var b = Films("back"); var ns = M(sample.GetProperty("ns"));
            CoherentThinFilmSolver Front(CoherentFilm[] films) => new(M(sample.GetProperty("n0")), ns, films);
            CoherentThinFilmSolver Back(CoherentFilm[] films) => new(M(sample.GetProperty("ne")), ns, films);
            var w = sample.GetProperty("wavelength").GetDouble(); var a = sample.GetProperty("angle").GetDouble(); var d = sample.GetProperty("thickness").GetDouble();
            var pol = sample.GetProperty("polarization").GetString() == "s" ? ThinFilmPolarization.S : ThinFilmPolarization.P;
            var s = Front(f).EvaluateIncoherentSubstrateThicknessSensitivity(Back(b), d, w, a, pol);
            Assert.Equal(Front(f).EvaluateIncoherentSubstrate(Back(b), d, w, a, pol), s.Power);
            for (var j = 0; j < f.Length + b.Length; j++)
            {
                const double step = .0001;
                ThinFilmPower At(double shift)
                {
                    var ff = f.ToArray(); var bb = b.ToArray(); var films = j < f.Length ? ff : bb; var i = j < f.Length ? j : j - f.Length;
                    films[i] = films[i] with { ThicknessNanometers = films[i].ThicknessNanometers + shift };
                    return Front(ff).EvaluateIncoherentSubstrate(Back(bb), d, w, a, pol);
                }
                var plus = At(step); var minus = At(-step); var derivative = s.Layers[j];
                Close((plus.Reflectance - minus.Reflectance) / (2 * step), derivative.Reflectance);
                Close((plus.Transmittance - minus.Transmittance) / (2 * step), derivative.Transmittance);
                Close((plus.Absorptance - minus.Absorptance) / (2 * step), derivative.Absorptance);
                if (s.HasLogTransmissionDerivative) Close((plus.LogTransmittance - minus.LogTransmittance) / (2 * step), derivative.LogTransmittance);
            }
        }
    }
    private static void Close(double expected, double actual) => Assert.InRange(Math.Abs(actual - expected), 0, 3e-7 * Math.Max(1, Math.Abs(expected)));
    [Theory]
    [InlineData(ThinFilmPolarization.S)]
    [InlineData(ThinFilmPolarization.P)]
    [InlineData(ThinFilmPolarization.Unpolarized)]
    public void EvanescentExitHasNoTransmissionOrSpuriousLogDerivative(ThinFilmPolarization pol)
    {
        var incident = new ConstantIndexMaterial("dense incident", 1.5); var substrate = new ConstantIndexMaterial("dense substrate", 1.7);
        var front = new CoherentThinFilmSolver(incident, substrate, []);
        var rear = new CoherentThinFilmSolver(new ConstantIndexMaterial("air", 1), substrate, [new(new ConstantIndexMaterial("film", 1.8), 70)]);
        var s = front.EvaluateIncoherentSubstrateThicknessSensitivity(rear, 1e6, 550, 57, pol);
        Assert.Equal(1, s.Power.Reflectance, 12); Assert.Equal(0, s.Power.Transmittance); Assert.False(s.HasLogTransmissionDerivative);
        var derivative = Assert.Single(s.Layers); Assert.InRange(Math.Abs(derivative.Reflectance), 0, 1e-12);
        Assert.Equal(0, derivative.Transmittance); Assert.Equal(0, derivative.LogTransmittance);
    }
    [Theory]
    [InlineData(ThinFilmPolarization.S, 0)]
    [InlineData(ThinFilmPolarization.P, 35)]
    [InlineData(ThinFilmPolarization.Unpolarized, 50)]
    public void OpaqueSubstrateBareBackAdjustmentsAndLosslessMirrorsKeepFiniteDerivatives(ThinFilmPolarization pol, double angle)
    {
        var air = new ConstantIndexMaterial("air", 1); var glass = new ConstantIndexMaterial("glass", 1.5, .1);
        var film = new ConstantIndexMaterial("film", 1.8, .01);
        CoherentThinFilmSolver Front(double thickness) => new(air, glass, [new(film, thickness, new(1.1, .02, .01))]);
        var bare = new CoherentThinFilmSolver(air, glass, []);
        var s = Front(75).EvaluateIncoherentSubstrateThicknessSensitivity(bare, 1e6, 550, angle, pol);
        Assert.Equal(0, s.Power.Transmittance); Assert.True(s.HasLogTransmissionDerivative);
        Close((Front(75.001).EvaluateIncoherentSubstrate(bare, 1e6, 550, angle, pol).LogTransmittance
            - Front(74.999).EvaluateIncoherentSubstrate(bare, 1e6, 550, angle, pol).LogTransmittance) / .002, s.Layers[0].LogTransmittance);
        glass = new("glass", 1.5);
        CoherentThinFilmSolver Mirror(double shift) => new(air, glass, Enumerable.Range(0, 200).Select(i =>
        { var n = i % 2 == 0 ? 3.0 : 1.4; return new CoherentFilm(new ConstantIndexMaterial("mirror", n), 550 / (4 * n) + (i == 17 ? shift : 0)); }));
        var m = Mirror(0).EvaluateIncoherentSubstrateThicknessSensitivity(Mirror(0), 1e6, 555, 0, pol);
        Assert.All(m.Layers, l => Assert.True(double.IsFinite(l.LogTransmittance)));
        var p = Mirror(.0001).EvaluateIncoherentSubstrate(Mirror(0), 1e6, 555, 0, pol);
        var n0 = Mirror(-.0001).EvaluateIncoherentSubstrate(Mirror(0), 1e6, 555, 0, pol);
        Close((p.LogTransmittance - n0.LogTransmittance) / .0002, m.Layers[17].LogTransmittance);
    }
}
