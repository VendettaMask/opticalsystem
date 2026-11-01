using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class DisplayPolarizationTests
{
    [Theory]
    [InlineData(0, ThinFilmPolarization.S)]
    [InlineData(35, ThinFilmPolarization.S)]
    [InlineData(57, ThinFilmPolarization.P)]
    public void DisplayUsesActualCorePolarizationsWithoutChangingTheOriginalResult(double angle, ThinFilmPolarization polarization)
    {
        var d = Examples.Create(DesignKind.HighReflector);
        d = d with { Target = d.Target with { AngleDegrees = angle, Polarization = polarization },
            Layers = [new(d.HighId, 65), new(d.LowId, 95), new(d.HighId, 70)],
            Settings = d.Settings with { PreviewIntervals = 40 } };
        var service = new DesignService(); d = service.Evaluate(d);
        var frozen = System.Text.Json.JsonSerializer.Serialize(d, Experiment.JsonOptions);
        var display = service.EvaluateDisplayPolarizations(d);
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        Assert.Equal(d.Result!.Spectrum.Select(s => s.WavelengthNanometers), display.Average.Select(s => s.WavelengthNanometers));
        for (var i = 0; i < display.Average.Length; i++)
        {
            var wave = display.Average[i].WavelengthNanometers;
            Assert.Equal(solver.Evaluate(wave, angle, ThinFilmPolarization.S), display.S[i].Power);
            Assert.Equal(solver.Evaluate(wave, angle, ThinFilmPolarization.P), display.P[i].Power);
            Assert.Equal(solver.Evaluate(wave, angle, ThinFilmPolarization.Unpolarized), display.Average[i].Power);
            Assert.Equal((display.S[i].Power.Transmittance + display.P[i].Power.Transmittance) / 2, display.Average[i].Power.Transmittance);
        }
        if (angle > 0)
        {
            Assert.NotEqual(display.S[0].Power.Transmittance, display.P[0].Power.Transmittance);
            var meanOd = display.Average[0].Power.OpticalDensity;
            Assert.True(Math.Abs(meanOd - (display.S[0].Power.OpticalDensity + display.P[0].Power.OpticalDensity) / 2) > 1e-6);
        }
        Assert.Equal(frozen, System.Text.Json.JsonSerializer.Serialize(d, Experiment.JsonOptions));
        Assert.Throws<OperationCanceledException>(() => service.EvaluateDisplayPolarizations(d, new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => service.EvaluateDisplayPolarizations(d with { Layers = [] }));
    }
}
