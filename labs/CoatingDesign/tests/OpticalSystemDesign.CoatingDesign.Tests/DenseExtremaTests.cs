using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class DenseExtremaTests
{
    [Theory]
    [InlineData(550, 4, .499, true)]
    [InlineData(550, 4, .501, false)]
    [InlineData(940, 3.4, -.499, true)]
    [InlineData(940, 3.4, -.501, false)]
    public void AnalyticSlabResonanceKeepsTheOriginalCentreGateAcrossGrids(double wave, double index, double offset, bool inside)
    {
        var input = GeneralCoatingTests.Fixture(wave, 1.45, index, 0, 0, ThinFilmPolarization.Unpolarized);
        // A lossless nondispersive slab has a transmission maximum at 2*n*d=lambda,
        // independently of the asymmetric real entrance/exit admittances.
        input = input with
        {
            Target = input.Target with { MinimumNm = wave * .65, MaximumNm = wave * 1.6, FwhmNm = wave * .5,
                FwhmToleranceNm = wave * .35, CenterToleranceNm = .5, BlockingOd = .1, MaximumLayers = 1,
                StopBands = [new(wave * .66, wave * .69), new(wave * 1.5, wave * 1.58)] },
            Layers = [new(input.HighId, (wave + offset) / (2 * index), false)]
        };
        var service = new DesignService();
        foreach (var intervals in new[] { 400, 447, 1600, 3200 })
        {
            var d = input with { Settings = input.Settings with { PreviewIntervals = intervals } }; var fingerprint = d.Fingerprint();
            var result = service.Evaluate(d); var r = result.Result!;
            Assert.InRange(Math.Abs(r.Metrics.PeakWavelengthNanometers - (wave + offset)), 0, 2e-5);
            Assert.Equal(inside, r.Checks.Single(c => c.Name == "峰值位置 (nm)").Passed);
            Assert.True(r.SamplingConverged); Assert.True(r.ExtremaRefinementComplete);
            Assert.InRange(r.ExtremaRefinementEvaluations!.Value, 1, 64 * 34 * 5);
            Assert.Equal(fingerprint, d.Fingerprint()); Assert.Equal(d.Layers, result.Layers);
        }
    }

    [Theory]
    [InlineData("Warm")]
    [InlineData("Role")]
    public async Task RealDispersionAndAbsorptionKeepStableVerdictsAndReplayMetadata(string key)
    {
        using var json = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/dense-extrema-20261101.json")));
        var input = json.RootElement.GetProperty(key).Deserialize<Experiment>(Experiment.JsonOptions)!;
        var results = new[] { 400, 1600, 3200 }.Select(n => new DesignService().Evaluate(input with { Settings = input.Settings with { PreviewIntervals = n } })).ToArray();
        Assert.Single(results.Select(x => x.Result!.Checks.Single(c => c.Name == "峰值位置 (nm)").Passed).Distinct());
        Assert.All(results, d =>
        {
            Assert.True(d.Result!.SamplingConverged); Assert.True(d.Result.ExtremaRefinementComplete);
            Assert.Equal(DesignService.DenseSamplingProtocol, d.Result.SamplingProtocol);
            Assert.Equal(JsonSerializer.Serialize(input.Target, Experiment.JsonOptions), JsonSerializer.Serialize(d.Target, Experiment.JsonOptions));
            Assert.Equal(JsonSerializer.Serialize(input.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions));
        });
        Assert.InRange(results.Max(x => x.Result!.Metrics.PeakTransmittance) - results.Min(x => x.Result!.Metrics.PeakTransmittance), 0, 1e-5);
        Assert.InRange(results.Max(x => x.Result!.Metrics.FwhmNanometers!.Value) - results.Min(x => x.Result!.Metrics.FwhmNanometers!.Value), 0, .02);
        var value = results[0]; var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(value, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(value.Result!.ExtremaRefinementEvaluations, reopened.Result!.ExtremaRefinementEvaluations);
            Assert.Equal(value.Result.ExtremaRefinementComplete, reopened.Result.ExtremaRefinementComplete);
            Assert.Equal(value.Fingerprint(), reopened.Fingerprint());
            Assert.Equal(value.Result.Metrics, new DesignService().Evaluate(reopened).Result!.Metrics);
        }
        finally { File.Delete(path); }
    }
}
