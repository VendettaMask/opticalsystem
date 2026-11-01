using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class TargetTradeoffTests
{
    [Theory]
    [InlineData("continued-double-96")]
    [InlineData("holdout-violet")]
    [InlineData("new-ir-double")]
    [InlineData("prior-double-96")]
    [InlineData("real-narrowband-control")]
    public async Task RegressedSpecificationsRecoverWithoutSpendingAnyLegacyTarget(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/target-tradeoffs-20261031.json")));
        var c = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name);
        var input = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var legacy = c.GetProperty("Legacy"); var service = new DesignService();
        var output = service.Optimize(input); var result = output.Result!; var work = result.Run.GeneralWork!;
        TargetPreservationTests.AssertTargetsPreserved(service.Evaluate(input), output);
        Assert.True(c.GetProperty("Previous").GetProperty("Merit").GetDouble() > legacy.GetProperty("Merit").GetDouble());
        // Removing a regression can restore the legacy result exactly. Extra progress is
        // reported separately; it is not obtained by weakening any physical target.
        Assert.True(result.Merit <= legacy.GetProperty("Merit").GetDouble());
        Assert.True(result.Metrics.PeakTransmittance >= Math.Min(input.Target.PeakTransmittance, legacy.GetProperty("Metrics").GetProperty("PeakTransmittance").GetDouble()));
        double WidthViolation(double w) => Math.Max(0, Math.Abs(w - input.Target.FwhmNm) - input.Target.FwhmToleranceNm);
        Assert.True(result.Metrics.FwhmNanometers.HasValue);
        Assert.True(WidthViolation(result.Metrics.FwhmNanometers ?? double.PositiveInfinity) <= WidthViolation(legacy.GetProperty("Metrics").GetProperty("FwhmNanometers").GetDouble()));
        double CenterViolation(double w) => Math.Max(0, Math.Abs(w - input.Target.CenterNm) - input.Target.CenterToleranceNm);
        Assert.True(CenterViolation(result.Metrics.PeakWavelengthNanometers) <= CenterViolation(legacy.GetProperty("Metrics").GetProperty("PeakWavelengthNanometers").GetDouble()));
        var bands = input.Target.StopBands!;
        for (var i = 0; i < bands.Length; i++)
            Assert.True(result.Spectrum.Where(s => s.WavelengthNanometers >= bands[i].MinimumNm && s.WavelengthNanometers <= bands[i].MaximumNm)
                .Min(s => s.Power.OpticalDensity) >= Math.Min(input.Target.BlockingOd, legacy.GetProperty("BlockingOd")[i].GetDouble()));
        Assert.InRange(result.Run.Iterations, 1, input.Settings.MaximumIterations);
        var repairWindow = (int)Math.Ceiling(Math.Sqrt(input.Settings.MaximumIterations));
        Assert.InRange(work.TargetRepairIterations, 1, input.Settings.MaximumIterations);
        Assert.InRange(work.TargetRepairContinuationIterations, 0, Math.Max(0, input.Settings.MaximumIterations - repairWindow));
        Assert.InRange(work.TargetParetoPolishIterations, 0, repairWindow);
        Assert.True(work.TargetRepairIterations + work.TargetParetoPolishIterations + work.PassbandQualityIterations <= input.Settings.MaximumIterations);
        Assert.Equal(1, work.TargetComparisonRuns); Assert.InRange(work.TargetComparisonIterations, 1, input.Settings.MaximumIterations);
        Assert.Equal(2, result.Run.TargetDirectionTrials!.Length);
        Assert.True(work.InequalitySubproblems > 0); Assert.NotNull(result.Run.TargetRepairTrials);
        Assert.Equal(result.SamplingConverged && result.Checks.All(c => c.Passed), result.Passed);
        Assert.Equal(JsonSerializer.Serialize(input.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(input.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(input.Settings, output.Settings);
        for (var i = 0; i < input.Layers.Length; i++)
        {
            Assert.Equal(input.Layers[i].MaterialId, output.Layers[i].MaterialId);
            Assert.Equal(input.Layers[i].Variable, output.Layers[i].Variable);
            if (!input.Layers[i].Variable) Assert.Equal(input.Layers[i], output.Layers[i]);
            Assert.InRange(output.Layers[i].ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm);
        }
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(result.Run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run, Experiment.JsonOptions));
            var recomputed = service.Evaluate(reopened);
            Assert.Equal(result.Metrics, recomputed.Result!.Metrics); Assert.Equal(result.Checks, recomputed.Result.Checks);
        }
        finally { File.Delete(path); }
    }
}
