using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class TargetBalancingTests
{
    [Theory]
    [InlineData("continued-single-24")]
    [InlineData("real-nbf2-96")]
    public async Task FrozenBlockedTargetsImproveWithTheirOriginalMaterialsGatesAndBudget(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/target-balancing-20261031.json")));
        var c = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(x => x.GetProperty("Input").GetProperty("Name").GetString() == name);
        var input = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var previous = c.GetProperty("Previous"); var service = new DesignService(); var fingerprint = input.Fingerprint();
        var before = service.Evaluate(input); var output = service.Optimize(input); var result = output.Result!;
        TargetPreservationTests.AssertTargetsPreserved(before, output);
        Assert.False(previous.GetProperty("Passed").GetBoolean());
        // This checks actual gains against the sealed previous engine, beyond merely retaining input.
        Assert.True(result.Merit < previous.GetProperty("Merit").GetDouble());
        Assert.True(result.Metrics.FwhmNanometers < previous.GetProperty("Metrics").GetProperty("FwhmNanometers").GetDouble());
        Assert.Contains(previous.GetProperty("BlockingOd").EnumerateArray(), x => x.GetDouble() < input.Target.BlockingOd);
        foreach (var band in input.Target.StopBands!)
        {
            Assert.True(result.Spectrum.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm)
                .Min(s => s.Power.OpticalDensity) >= input.Target.BlockingOd);
        }
        Assert.Equal(result.SamplingConverged && result.Checks.All(check => check.Passed), result.Passed);
        Assert.Equal(DesignService.TargetProtectionProtocol, result.Run.GeneralWork!.TargetProtectionProtocol);
        Assert.InRange(result.Run.Iterations, 1, input.Settings.MaximumIterations);
        Assert.Equal(input.Settings, output.Settings); Assert.Equal(fingerprint, input.Fingerprint());
        Assert.Equal(JsonSerializer.Serialize(input.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(input.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(result.Run.GeneralWork, reopened.Result!.Run.GeneralWork);
            var recalculated = service.Evaluate(reopened);
            Assert.Equal(result.Metrics, recalculated.Result!.Metrics); Assert.Equal(result.Checks, recalculated.Result.Checks);
        }
        finally { File.Delete(path); }
    }
}
