using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class TargetContinuationTests
{
    private static JsonElement Case(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/target-continuation-20261031.json")));
        return fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name).Clone();
    }

    [Theory]
    [InlineData("prior-double-96")]
    [InlineData("holdout-violet")]
    [InlineData("continued-double-96")]
    [InlineData("real-nbf2-96")]
    public async Task FrozenNarrowBandProgressPreservesEveryPreviouslyCertifiedTarget(string name)
    {
        var c = Case(name); var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var previous = c.GetProperty("Previous"); var metrics = previous.GetProperty("Metrics");
        var service = new DesignService(); var fingerprint = d.Fingerprint(); var output = service.Optimize(d); var r = output.Result!;
        TargetPreservationTests.AssertTargetsPreserved(service.Evaluate(d), output);
        Assert.True(r.Merit < previous.GetProperty("Merit").GetDouble());
        Assert.True(r.Metrics.PeakTransmittance >= Math.Min(d.Target.PeakTransmittance, metrics.GetProperty("PeakTransmittance").GetDouble()));
        double WidthViolation(double w) => Math.Max(0, Math.Abs(w - d.Target.FwhmNm) - d.Target.FwhmToleranceNm);
        double CenterViolation(double w) => Math.Max(0, Math.Abs(w - d.Target.CenterNm) - d.Target.CenterToleranceNm);
        Assert.True(WidthViolation(r.Metrics.FwhmNanometers ?? double.PositiveInfinity) <= WidthViolation(metrics.GetProperty("FwhmNanometers").GetDouble()));
        Assert.True(CenterViolation(r.Metrics.PeakWavelengthNanometers) <= CenterViolation(metrics.GetProperty("PeakWavelengthNanometers").GetDouble()));
        foreach (var b in d.Target.StopBands!)
            Assert.True(r.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity) >= d.Target.BlockingOd);
        Assert.Equal(r.SamplingConverged && r.Checks.All(c => c.Passed), r.Passed);
        Assert.Equal(fingerprint, d.Fingerprint()); Assert.Equal(d.Settings, output.Settings);
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        var work = r.Run.GeneralWork!; var window = (int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations));
        Assert.InRange(r.Run.Iterations, 1, d.Settings.MaximumIterations);
        Assert.InRange(work.TargetRepairIterations, 1, d.Settings.MaximumIterations);
        Assert.InRange(work.TargetRepairContinuationIterations, 0, Math.Max(0, d.Settings.MaximumIterations - window));
        Assert.InRange(work.TargetParetoPolishIterations, 0, window);
        Assert.True(work.TargetRepairIterations + work.TargetParetoPolishIterations + work.PassbandQualityIterations <= d.Settings.MaximumIterations);
        Assert.Equal(d.Settings.MaximumIterations, r.Run.TargetRefinementIterationLimit);
        if (previous.GetProperty("BlockingOd").EnumerateArray().Any(x => x.GetDouble() < d.Target.BlockingOd))
        {
            Assert.True(work.TargetRepairContinuationIterations > 0);
            Assert.NotNull(r.Run.TargetRepairContinuationStartLayers); Assert.NotNull(r.Run.TargetRepairContinuationCeilings);
        }
        foreach (var trial in r.Run.TargetRepairTrials!.Where(t => t.Retained))
        { Assert.NotNull(trial.ViolationCeilings); Assert.True(trial.Violations.Zip(trial.ViolationCeilings!).All(v => v.First <= v.Second)); }
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(r.Run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run, Experiment.JsonOptions));
            var recomputed = service.Evaluate(reopened);
            Assert.Equal(r.Metrics, recomputed.Result!.Metrics); Assert.Equal(r.Checks, recomputed.Result.Checks);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("holdout-real-hr-nearir")]
    [InlineData("holdout-real-hr-red")]
    public void ContractingReflectancePreconditioningSharesTheOriginalTotalIterationLimit(string name)
    {
        var c = Case(name); var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var output = new DesignService().Optimize(d); var r = output.Result!; var w = r.Run.GeneralWork!;
        Assert.True(r.Metrics.MinimumReflectance > c.GetProperty("Previous").GetProperty("Metrics").GetProperty("MinimumReflectance").GetDouble());
        Assert.InRange(r.Run.Iterations, 1, d.Settings.MaximumIterations);
        Assert.Equal(r.Run.Iterations, w.ReflectancePreconditionerIterations + w.QualityIterations);
        Assert.True(w.ReflectancePreconditionerIterations > Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations)));
        Assert.Equal(r.SamplingConverged && r.Checks.All(c => c.Passed), r.Passed);
        Assert.Equal(JsonSerializer.Serialize(d.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.InRange(w.ReflectanceMetricIterations, 1, w.QualityIterations);
        Assert.Equal(w.QualityIterations, r.Run.ReflectanceMetricIterationLimit);
        Assert.NotEmpty(r.Run.ReflectanceMetricWindows!);
        var windows = r.Run.ReflectanceMetricWindows!;
        for (var i = 0; i < windows.Length; i++)
        {
            Assert.InRange(windows[i].Allowance, 1, (int)Math.Ceiling(Math.Sqrt(w.QualityIterations)));
            if (windows[i].Accepted) Assert.True(windows[i].CandidateLoss < windows[i].BaselineLoss);
            if (i > 0) Assert.Equal(windows[i - 1].DampingEnd, windows[i].DampingStart);
        }
        var metricStart = new DesignService().Evaluate(d with { Layers = r.Run.ReflectanceMetricStartLayers!, Result = null });
        Assert.True(r.Metrics.MinimumReflectance >= metricStart.Result!.Metrics.MinimumReflectance);
    }

    [Fact]
    public void RenamingActualMaterialIdsDoesNotSelectAnotherReflectancePath()
    {
        var d = Case("holdout-real-hr-nearir").GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var map = d.Materials.Select((m, i) => (m.Id, New: "cosmetic-" + i)).ToDictionary(x => x.Id, x => x.New);
        var renamed = d with { Materials = d.Materials.Select(m => m with { Id = map[m.Id] }).ToArray(),
            IncidentId = map[d.IncidentId], SubstrateId = map[d.SubstrateId], LowId = map[d.LowId], HighId = map[d.HighId],
            Layers = d.Layers.Select(l => l with { MaterialId = map[l.MaterialId] }).ToArray() };
        var service = new DesignService(); var a = service.Optimize(d); var b = service.Optimize(renamed);
        Assert.Equal(a.Result!.Spectrum, b.Result!.Spectrum); Assert.Equal(a.Result.Run.GeneralWork, b.Result.Run.GeneralWork);
    }
}
