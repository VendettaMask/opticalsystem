using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class NonlinearCoatingRegressionTests
{
    [Theory]
    [InlineData("holdout-real-hr-red")]
    [InlineData("real-hr-96")]
    [InlineData("real-nbf2-96")]
    [InlineData("new-real-nbf-red")]
    [InlineData("holdout-violet")]
    [InlineData("correction-real-nbf-red")]
    public async Task NonlinearCorrectionImprovesPhysicalQualityWithoutSpendingFrozenTargetsOrMargins(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/nonlinear-correction-20261031.json")));
        var c = json.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name);
        var d = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!; var service = new DesignService();
        var previous = service.Evaluate(d with { Layers = c.GetProperty("PreviousLayers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! });
        var output = service.Optimize(d); var r = output.Result!; var w = r.Run.GeneralWork!; var window = (int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations));
        Assert.True(!previous.Result!.SamplingConverged || r.SamplingConverged);
        if (d.Target.Kind == DesignKind.HighReflector)
        {
            Assert.Equal(.98, output.Target.Reflectance); Assert.True(r.Metrics.MinimumReflectance > previous.Result!.Metrics.MinimumReflectance);
            Assert.InRange(w.ReflectanceMetricIterations, 1, w.QualityIterations);
        }
        else
        {
            TargetPreservationTests.AssertTargetsPreserved(previous, output);
            var control = service.Evaluate(d with { Layers = c.GetProperty("ControlLayers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)! });
            TargetPreservationTests.AssertTargetsPreserved(control, output);
            Assert.True(r.Metrics.PeakTransmittance > previous.Result!.Metrics.PeakTransmittance);
            Assert.True(Math.Abs(r.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) < Math.Abs(previous.Result.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm));
            foreach (var band in d.Target.StopBands!)
            {
                double Od(Experiment e) => e.Result!.Spectrum.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).Min(s => s.Power.OpticalDensity);
                Assert.True(Od(output) >= Od(previous)); Assert.True(Od(output) >= Od(control));
            }
            Assert.InRange(w.PassbandCorrectionIterations, 1, 2 * window);
            Assert.InRange(w.PassbandQualityIterations - w.PassbandCorrectionIterations, 1, 2 * window);
            Assert.True(w.TargetRepairIterations + w.TargetParetoPolishIterations + w.PassbandQualityIterations <= d.Settings.MaximumIterations);
            Assert.NotNull(r.Run.PassbandCorrectionStartLayers); Assert.NotNull(r.Run.PassbandCorrectionAttempts);
            Assert.True(r.Metrics.PeakTransmittance >= control.Result!.Metrics.PeakTransmittance);
            Assert.True(Math.Abs(r.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) <= Math.Abs(control.Result.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm));
            var selected = Assert.Single(r.Run.PassbandDirectionTrials!, t => t.Selected); var bounds = r.Run.PassbandDirectionSelectionBounds!;
            var uncorrected = Assert.Single(r.Run.PassbandDirectionTrials!, t => t.Direction == "Uncorrected");
            Assert.Equal(control.Layers, uncorrected.Layers); Assert.Equal(control.Result.Metrics, uncorrected.Metrics);
            Assert.True(selected.Metrics.PeakTransmittance >= bounds.MinimumPeakTransmittance);
            Assert.True(Math.Abs(selected.Metrics.FwhmNanometers!.Value - d.Target.FwhmNm) <= bounds.MaximumWidthErrorNm);
            Assert.True(selected.OpticalDensities.Zip(bounds.MinimumOpticalDensities).All(p => p.First >= p.Second));
        }
        Assert.True(w.NonlinearConstraintCorrections > 0);
        Assert.Equal(r.SamplingConverged && r.Checks.All(c => c.Passed), r.Passed);
        Assert.True(r.Run.Iterations <= d.Settings.MaximumIterations);
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.Equal(JsonSerializer.Serialize(d.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions)); Assert.Equal(d.Settings, output.Settings);
        foreach (var pair in d.Layers.Zip(output.Layers)) if (!pair.First.Variable) Assert.Equal(pair.First, pair.Second);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(JsonSerializer.Serialize(r.Run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result!.Run, Experiment.JsonOptions));
            var cold = service.Evaluate(reopened); Assert.Equal(r.Metrics, cold.Result!.Metrics); Assert.Equal(r.Checks, cold.Result.Checks);
        }
        finally { File.Delete(path); }
    }
}
