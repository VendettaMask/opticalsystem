using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class SearchSeedRankingTests
{
    private static Experiment Input(string name)
    {
        using var json = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/search-seed-ranking-20261031.json")));
        return json.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Name").GetString() == name).Deserialize<Experiment>(Experiment.JsonOptions)!;
    }

    [Theory]
    [InlineData("correction-real-nbf-red")]
    [InlineData("new-real-nbf-green")]
    [InlineData("new-real-nbf-nearir-fixed")]
    public async Task ShortlistCertificatesMatchCoreAndRespectDeclaredBudgetsAndFixedLayers(string name)
    {
        var input = Input(name); var d = input with { Settings = input.Settings with { MaximumIterations = 1, Search = new(1, 12, true) } };
        var service = new DesignService(); var before = service.Evaluate(d); var output = service.Generate(d); var run = output.Result!.Run;
        Assert.Equal(DesignService.SeedRankingProtocol, run.SeedRankingProtocol);
        Assert.NotEmpty(run.SearchSeedValidations!); Assert.InRange(run.SearchSeedValidations!.Length, 1, 12);
        Assert.True(run.SearchSeedValidations.Count(v => v.RefinementRun is not null) <= 1);
        TargetPreservationTests.AssertTargetsPreserved(before, output);
        foreach (var v in run.SearchSeedValidations)
        {
            var checkedValue = service.Evaluate(d with { Layers = v.Layers, Result = null, Candidates = null }); var r = checkedValue.Result!;
            Assert.Equal(v.Metrics, r.Metrics); Assert.Equal(v.Passed, r.Passed); Assert.Equal(v.SamplingConverged, r.SamplingConverged);
            Assert.Equal(d.Target.StopBands!.Select(b => r.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)), v.OpticalDensities);
            if (v.RefinementRun is { } refinement) Assert.InRange(refinement.Iterations, 0, 1);
            if (d.Layers.Any(l => !l.Variable)) for (var i = 0; i < d.Layers.Length; i++) if (!d.Layers[i].Variable) Assert.Equal(d.Layers[i], v.Layers[i]);
        }
        Assert.Equal(JsonSerializer.Serialize(d.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try { await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path); Assert.Equal(JsonSerializer.Serialize(run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run, Experiment.JsonOptions)); Assert.Equal(output.Result.Metrics, service.Evaluate(reopened).Result!.Metrics); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RealDoubleCavityGenerationUsesTheOriginalCompleteAcceptanceGates()
    {
        var d = Input("correction-real-nbf-red"); var service = new DesignService(); var output = service.Generate(d); var r = output.Result!;
        Assert.True(r.SamplingConverged); Assert.All(r.Checks, c => Assert.True(c.Passed, c.Name + ": " + c.Actual)); Assert.True(r.Passed);
        Assert.True(r.Metrics.PeakTransmittance >= d.Target.PeakTransmittance);
        Assert.True(Math.Abs(r.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) <= d.Target.FwhmToleranceNm);
        Assert.True(Math.Abs(r.Metrics.PeakWavelengthNanometers - d.Target.CenterNm) <= d.Target.CenterToleranceNm);
        Assert.True(output.Layers.Length <= d.Target.MaximumLayers);
        Assert.Contains(r.Run.SearchSeedValidations!, v => v.Passed);
    }
}
