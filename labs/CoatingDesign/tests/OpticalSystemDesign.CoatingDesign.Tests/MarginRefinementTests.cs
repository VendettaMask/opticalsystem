using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class MarginRefinementTests
{
    private static JsonElement Case(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/margin-refinement-20261031.json")));
        return json.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name).Clone();
    }

    [Theory]
    [InlineData("holdout-violet")]
    [InlineData("real-nbf2-96")]
    public async Task PhysicalPeakBlockingAndWidthImproveWithoutSpendingAnyFrozenMargin(string name)
    {
        var c = Case(name); var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var service = new DesignService(); var previous = service.Evaluate(d with { Layers = c.GetProperty("PreviousLayers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! });
        var output = service.Optimize(d); var a = previous.Result!; var b = output.Result!;
        Assert.True(b.Metrics.PeakTransmittance > a.Metrics.PeakTransmittance);
        Assert.True(Math.Abs(b.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) < Math.Abs(a.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm));
        foreach (var band in d.Target.StopBands!)
        {
            double Od(Experiment e) => e.Result!.Spectrum.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).Min(s => s.Power.OpticalDensity);
            Assert.True(Od(output) >= Od(previous));
        }
        Assert.Equal(b.SamplingConverged && b.Checks.All(c => c.Passed), b.Passed);
        Assert.True(b.Run.GeneralWork!.PassbandQualityAcceptedCandidates > 0);
        Assert.Equal(DesignService.PassbandQualityProtocol, b.Run.GeneralWork.PassbandQualityProtocol);
        Assert.True(b.Run.GeneralWork.TargetRepairIterations + b.Run.GeneralWork.TargetParetoPolishIterations + b.Run.GeneralWork.PassbandQualityIterations <= d.Settings.MaximumIterations);
        foreach (var trial in b.Run.PassbandQualityTrials!.Where(t => t.Retained))
        {
            Assert.True(trial.Metrics.PeakTransmittance >= trial.Bounds.MinimumPeakTransmittance);
            Assert.True(Math.Abs(trial.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) <= trial.Bounds.MaximumWidthErrorNm);
            Assert.True(trial.OpticalDensities.Zip(trial.Bounds.MinimumOpticalDensities).All(p => p.First >= p.Second));
        }
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(d.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(b.Run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run, Experiment.JsonOptions));
            Assert.Equal(b.Metrics, service.Evaluate(reopened).Result!.Metrics);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AlreadyPassedNarrowBandStillImprovesItsPhysicalMargins()
    {
        var c = Case("holdout-violet"); var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)! with
        { Layers = c.GetProperty("PreviousLayers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! };
        var service = new DesignService(); var before = service.Evaluate(d); Assert.True(before.Result!.Passed);
        var output = service.Optimize(d); Assert.True(output.Result!.Passed);
        Assert.Equal(0, output.Result.Merit);
        Assert.True(output.Result.Metrics.PeakTransmittance > before.Result.Metrics.PeakTransmittance);
        Assert.True(output.Result.Run.GeneralWork!.PassbandQualityAcceptedCandidates > 0);
    }
}
