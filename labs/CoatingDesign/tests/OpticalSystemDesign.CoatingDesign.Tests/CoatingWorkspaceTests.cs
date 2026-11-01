using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingWorkspaceTests
{
    [Fact]
    public void ReusedRealMaterialWorkspaceMatchesIndependentSolversAtEveryThicknessAngleWavelengthAndPolarization()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/margin-refinement-20261031.json")));
        var d = json.RootElement.GetProperty("Cases").EnumerateArray().Select(c => c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!)
            .Single(d => d.Name == "real-nbf2-96");
        var mats = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        CoherentThinFilmSolver Independent(double[] values) => new(mats[d.IncidentId], mats[d.SubstrateId], d.Layers.Select((l,i) => new CoherentFilm(mats[l.MaterialId], values[i])));
        var original = d.Layers.Select(l => l.ThicknessNm).ToArray(); var basis = Independent(original);
        foreach (var factor in new[] { .8, 1.0, 1.2 })
        {
            var values = original.Select(v => v * factor).ToArray(); var reused = basis.WithThicknesses(values); var independent = Independent(values);
            foreach (var angle in new[] { 0.0, 27.0, 43.0 }) foreach (var wave in new[] { 500.0, 550.0, 600.0 }) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
            {
                Assert.Equal(independent.Evaluate(wave, angle, pol), reused.Evaluate(wave, angle, pol));
                var expected = independent.EvaluateThicknessSensitivity(wave, angle, pol); var actual = reused.EvaluateThicknessSensitivity(wave, angle, pol);
                Assert.Equal(expected.Power, actual.Power); Assert.Equal(expected.Layers, actual.Layers);
            }
        }
        Assert.Equal(Independent(original).Evaluate(550, 0, ThinFilmPolarization.S), basis.Evaluate(550, 0, ThinFilmPolarization.S));
        Assert.Throws<ArgumentException>(() => basis.WithThicknesses([1]));
        var invalid = original.ToArray(); invalid[0] = double.NaN;
        Assert.Throws<ArgumentException>(() => basis.WithThicknesses(invalid));
    }
}
