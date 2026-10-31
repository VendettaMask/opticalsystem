using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class ProgressAllocationTests
{
    private static JsonElement Fixture()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/progress-allocation-20261031.json")));
        return json.RootElement.Clone();
    }

    [Fact]
    public void CompletedRecoveryContinuesAcrossTargetHandoffAndBeatsTheFrozenBudgetControl()
    {
        var fixture = Fixture(); var d = fixture.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var service = new DesignService(); var output = service.Optimize(d); var r = output.Result!;
        var old = service.Evaluate(d with { Layers = fixture.GetProperty("PreviousBudgetControl").GetProperty("Layers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! });
        TargetPreservationTests.AssertTargetsPreserved(old, output);
        Assert.True(r.Passed); Assert.Equal(0, r.Merit);
        Assert.True(r.Metrics.PeakTransmittance > old.Result!.Metrics.PeakTransmittance);
        Assert.True(r.Metrics.FwhmNanometers < old.Result.Metrics.FwhmNanometers);
        Assert.Contains(r.Run.TargetRepairWindows!, w => w.Reason == "CertifiedProgress");
        var work = r.Run.GeneralWork!;
        Assert.True(work.TargetRepairIterations + work.TargetParetoPolishIterations + work.PassbandQualityIterations <= d.Settings.MaximumIterations);
        Assert.Equal(DesignService.DenseSamplingProtocol, r.SamplingProtocol);
        Assert.Equal(DesignService.SearchSamplingProtocol, work.SearchSamplingProtocol);
        Assert.True(work.SearchPeakReferenceBatches >= 3);
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(d.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(d.Settings, output.Settings);
    }

    [Fact]
    public void ExactMetricDomainEndpointsRemoveFalseNonconvergenceWithoutChangingGates()
    {
        var fixture = Fixture(); var witness = fixture.GetProperty("SamplingWitness");
        Assert.False(witness.GetProperty("OriginalSamplingConverged").GetBoolean());
        Assert.All(witness.GetProperty("OriginalChecks").EnumerateArray(), c => Assert.True(c.GetProperty("Passed").GetBoolean()));
        var d = fixture.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)! with
        { Layers = witness.GetProperty("Layers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! };
        var service = new DesignService(); var r = service.Evaluate(d).Result!;
        Assert.True(r.SamplingConverged); Assert.True(r.Passed); Assert.All(r.Checks, c => Assert.True(c.Passed));
        Assert.Contains(r.Spectrum, s => s.WavelengthNanometers == Math.Max(d.Target.MinimumNm, d.Target.CenterNm - 2 * d.Target.FwhmNm));
        Assert.Contains(r.Spectrum, s => s.WavelengthNanometers == Math.Min(d.Target.MaximumNm, d.Target.CenterNm + 2 * d.Target.FwhmNm));
        var stillFailing = service.Evaluate(fixture.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!).Result!;
        Assert.False(stillFailing.Passed); Assert.Contains(stillFailing.Checks, c => !c.Passed);
    }

    [Fact]
    public void FasterFormalSamplingDoesNotChangeTheEstablishedSearchPeakBracket()
    {
        var c = Fixture().GetProperty("SamplingDecoupling"); var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var service = new DesignService(); var before = service.Evaluate(d with { Layers = c.GetProperty("PreviousLayers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! });
        var after = service.Optimize(d); var a = before.Result!; var b = after.Result!;
        foreach (var passed in a.Checks.Where(c => c.Passed)) Assert.True(b.Checks.Single(c => c.Name == passed.Name).Passed);
        Assert.True(b.Metrics.PeakTransmittance >= Math.Min(d.Target.PeakTransmittance, a.Metrics.PeakTransmittance));
        // The previous stack is independently measured on the same final grid. Last-bit
        // changes in an unfinished width are compared at the unchanged convergence resolution.
        Assert.True(b.Metrics.FwhmNanometers <= a.Metrics.FwhmNanometers + Math.Min(0.02, d.Target.FwhmToleranceNm / 10));
        Assert.Equal(DesignService.SearchSamplingProtocol, b.Run.GeneralWork!.SearchSamplingProtocol);
    }
}
