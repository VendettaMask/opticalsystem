using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class InfeasibleCoatingTests
{
    [Theory]
    [InlineData("real-hr-original-target")]
    [InlineData("real-narrowband-control")]
    [InlineData("real-nbf2-96")]
    [InlineData("ir-absorbing-s")]
    public async Task FrozenFailuresImproveAgainstThePreviousEngineWithoutChangingMaterialsTargetsOrBudget(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/coating-infeasible-20261030.json")));
        var c = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name);
        var input = c.GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var fingerprint = input.Fingerprint(); var service = new DesignService(); var output = service.Optimize(input);
        var result = output.Result!; var baseline = c.GetProperty("Baseline");
        if (input.Target.Kind == DesignKind.HighReflector)
        {
            Assert.True(result.Metrics.MinimumReflectance > baseline.GetProperty("Metrics").GetProperty("MinimumReflectance").GetDouble());
            Assert.Equal(DesignService.ReflectanceQualityProtocol, result.Run.GeneralWork!.QualityProtocol);
        }
        else
        {
            if (baseline.GetProperty("Passed").GetBoolean()) Assert.True(result.Passed);
            else Assert.True(result.Merit < baseline.GetProperty("Loss").GetDouble());
            Assert.Equal(DesignService.PassbandConstraintProtocol, result.Run.GeneralWork!.PassbandProtocol);
            Assert.True(result.Run.GeneralWork.BandSensitivityBatches > 0);
        }
        Assert.Equal(result.Checks.All(c => c.Passed) && result.SamplingConverged, result.Passed);
        Assert.Equal(JsonSerializer.Serialize(input.Target, Experiment.JsonOptions), JsonSerializer.Serialize(output.Target, Experiment.JsonOptions));
        Assert.Equal(input.Settings, output.Settings); Assert.Equal(fingerprint, input.Fingerprint());
        Assert.Equal(JsonSerializer.Serialize(input.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(output.Materials, Experiment.JsonOptions));
        Assert.InRange(result.Run.Iterations, 1, input.Settings.MaximumIterations);
        Assert.All(output.Layers, l => Assert.InRange(l.ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(result.Run.GeneralWork, reopened.Result!.Run.GeneralWork);
            Assert.Equal(result.Run.TargetViolationCeilings, reopened.Result.Run.TargetViolationCeilings);
            Assert.Equal(result.Run.TargetEnvelopeSampling, reopened.Result.Run.TargetEnvelopeSampling);
            Assert.Equal(result.Run.TargetProtectionPhase, reopened.Result.Run.TargetProtectionPhase);
            var recomputed = service.Evaluate(reopened);
            Assert.Equal(result.Metrics, recomputed.Result!.Metrics); Assert.Equal(result.Checks, recomputed.Result.Checks);
            Assert.Equal(result.Spectrum, recomputed.Result.Spectrum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NarrowBandTrialRowsRemainStableWhenPeakAndHalfHeightBracketsDisappear()
    {
        var service = new DesignService(); var d = GeneralCoatingTests.Fixture(750, 1.42, 2.25, .005, 23, ThinFilmPolarization.P);
        d = d with { Layers = service.BuildNarrowBandPrototype(d, new([4, 5], [1])) };
        var grid = (double[])Private("ObjectiveGrid").Invoke(null, [d])!;
        var counts = new List<int>(); var metricAvailability = new List<bool>();
        foreach (var factor in new[] { .01, .1, 1, 5, 20 })
        {
            var current = d with { Layers = d.Layers.Select(l => l with { ThicknessNm = l.ThicknessNm * factor }).ToArray() };
            var (values, jacobian, samples) = System(current, grid);
            counts.Add(values.Length); metricAvailability.Add(ThinFilmSpectrum.Measure(samples.Where(s => Math.Abs(s.WavelengthNanometers - d.Target.CenterNm) <= 2 * d.Target.FwhmNm).ToArray()).FwhmNanometers is not null);
            Assert.Equal(values.Length, jacobian.GetLength(0)); Assert.Equal(d.Layers.Length, jacobian.GetLength(1));
            Assert.All(values, v => Assert.False(double.IsNaN(v) || double.IsPositiveInfinity(v)));
            foreach (var v in jacobian) Assert.True(double.IsFinite(v));
        }
        Assert.Single(counts.Distinct()); Assert.Contains(true, metricAvailability); Assert.Contains(false, metricAvailability);
    }

    [Fact]
    public void CompleteMeasuredBandConstraintJacobianMatchesIndependentCentralDifferences()
    {
        var service = new DesignService(); var d = GeneralCoatingTests.Fixture(750, 1.42, 2.25, .005, 23, ThinFilmPolarization.P);
        d = d with { Layers = service.BuildNarrowBandPrototype(d, new([4, 5], [1])) };
        var grid = (double[])Private("ObjectiveGrid").Invoke(null, [d])!;
        var (values, jacobian, _) = System(d, grid); const double h = 1e-4;
        foreach (var column in new[] { 0, d.Layers.Length / 2, d.Layers.Length - 1 })
        {
            Experiment Perturb(double step) => d with { Layers = d.Layers.Select((l, j) => j == column ? l with { ThicknessNm = l.ThicknessNm + step } : l).ToArray() };
            var a = System(Perturb(-h), grid).Values; var b = System(Perturb(h), grid).Values;
            for (var row = 0; row < values.Length; row++)
                if (double.IsFinite(values[row])) Assert.InRange(Math.Abs((b[row] - a[row]) / (2 * h) - jacobian[row, column]), 0, 1e-6);
                else { Assert.Equal(double.NegativeInfinity, a[row]); Assert.Equal(double.NegativeInfinity, b[row]); Assert.Equal(0, jacobian[row, column]); }
        }
    }

    private static MethodInfo Private(string name) => typeof(DesignService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static (double[] Values, double[,] Derivatives, ThinFilmSample[] Samples) System(Experiment d, double[] grid)
    {
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var response = Private("GeneralConstraintSystem").Invoke(null, [d, solver, grid, true, null])!;
        T Property<T>(string name) => (T)response.GetType().GetProperty(name)!.GetValue(response)!;
        return (Property<double[]>("Values"), Property<double[,]>("Derivatives"), Property<ThinFilmSample[]>("Samples"));
    }
}
