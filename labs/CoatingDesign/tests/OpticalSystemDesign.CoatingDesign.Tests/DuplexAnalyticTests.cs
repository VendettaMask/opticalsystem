using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class DuplexAnalyticTests
{
    [Theory]
    [InlineData(CoatingDesignSurface.Front, CoatingEvaluationSurface.Front, false)]
    [InlineData(CoatingDesignSurface.Back, CoatingEvaluationSurface.Back, false)]
    [InlineData(CoatingDesignSurface.Front, CoatingEvaluationSurface.WholePlate, false)]
    [InlineData(CoatingDesignSurface.Back, CoatingEvaluationSurface.WholePlate, false)]
    [InlineData(CoatingDesignSurface.Both, CoatingEvaluationSurface.WholePlate, false)]
    [InlineData(CoatingDesignSurface.Both, CoatingEvaluationSurface.WholePlate, true)]
    public void AnalyticStagesPreserveFixedOppositeFacesAndOriginalDenseGates(CoatingDesignSurface face, CoatingEvaluationSurface evaluation, bool symmetric)
    {
        var d = DuplexCoatingTests.Design(face, evaluation, symmetric);
        d = d with { Duplex = d.Duplex! with { OptimizationVersion = 2 } };
        var service = new DesignService(); var baseline = service.Evaluate(d); var optimized = service.Optimize(d);
        Assert.True(optimized.Result!.Metrics.MaximumReflectance <= baseline.Result!.Metrics.MaximumReflectance + 1e-12);
        // A fixed, uncoated-to-target opposite face can make the whole plate infeasible; keep its original gate.
        if (evaluation != CoatingEvaluationSurface.WholePlate || face == CoatingDesignSurface.Both) Assert.True(optimized.Result.Passed);
        var run = optimized.Result.Run!; Assert.Equal(DesignService.DuplexAnalyticProtocol, run.DesignProtocol);
        var work = run.Duplex!.Analytic!; Assert.True(work.SensitivityEvaluations > 0); Assert.True(work.AnalyticJacobians > 0);
        Assert.Equal(2, work.DenseValidations); Assert.InRange(work.RestorationIterations + work.QualityIterations, 1, work.IterationBudget);
        Assert.True(work.DensePowerEvaluations >= work.DenseSpectrumPoints);
        if (face == CoatingDesignSurface.Front) Assert.Equal(d.BackLayers, optimized.BackLayers);
        if (face == CoatingDesignSurface.Back) Assert.Equal(d.Layers, optimized.Layers);
        if (symmetric) { Assert.Null(optimized.BackLayers); Assert.Equal(optimized.Layers, optimized.EffectiveBackLayers); Assert.Equal(d.Layers.Length, run.Duplex.IndependentVariables); }
    }
    [Theory]
    [InlineData(DesignKind.Antireflection)]
    [InlineData(DesignKind.HighReflector)]
    [InlineData(DesignKind.NarrowBand)]
    public void SameBudgetVersionComparisonUsesRealMaterialsAndRetainsEachOriginalTargetGate(DesignKind kind)
    {
        var single = Examples.Create(kind); var service = new DesignService();
        var layers = CoatingStackFormula.Parse(single, false, kind == DesignKind.NarrowBand ? "(HL)^3 2H (LH)^3" : "(HL)^4").Layers;
        var d = single with
        {
            SchemaVersion = Experiment.DuplexSchema,
            Duplex = new(CoatingDesignSurface.Both, CoatingEvaluationSurface.WholePlate),
            Layers = layers,
            BackLayers = [new(single.LowId, 110)],
            StackGeometry = new(single.IncidentId, 1),
            Settings = single.Settings with { MaximumIterations = 12, OptimizationIntervals = 32, PreviewIntervals = 64 }
        };
        var v1 = service.Optimize(d); var v2 = service.Optimize(d with { Duplex = d.Duplex with { OptimizationVersion = 2 } });
        Assert.Equal(DesignService.DuplexDesignProtocol, v1.Result!.Run!.DesignProtocol);
        Assert.Equal(12, v2.Result!.Run!.Duplex!.Analytic!.IterationBudget);
        var baseline = service.Evaluate(d);
        if (baseline.Result!.Passed) Assert.True(v2.Result.Passed);
        double[] Violations(DesignResult result)
        {
            var t = d.Target; var m = result.Metrics;
            if (t.Kind == DesignKind.Antireflection) return [Math.Max(0, m.MaximumReflectance - t.Reflectance)];
            if (t.Kind == DesignKind.HighReflector) return [Math.Max(0, t.Reflectance - m.MinimumReflectance)];
            return [Math.Max(0, t.PeakTransmittance - m.PeakTransmittance),
                m.FwhmNanometers is { } width ? Math.Max(0, Math.Abs(width - t.FwhmNm) - t.FwhmToleranceNm) : t.FwhmNm,
                Math.Max(0, Math.Abs(m.PeakWavelengthNanometers - t.CenterNm) - t.CenterToleranceNm),
                .. t.StopBands!.Select(b => Math.Max(0, t.BlockingOd - result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)))];
        }
        Assert.All(Violations(v2.Result).Zip(Violations(baseline.Result)), pair => Assert.True(pair.First <= pair.Second + 1e-10));
        Assert.True(double.IsFinite(v2.Result.Merit));
        var evidence = Environment.GetEnvironmentVariable("COATING_UI_EVIDENCE");
        if (evidence is not null)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "duplex-budget-" + kind + ".json"), JsonSerializer.Serialize(new
            { input = d, baseline = baseline.Result, v1 = v1.Result, v2 = v2.Result }, Experiment.JsonOptions));
        }
    }
    [Fact]
    public async Task FixedFilmsAndVersionedRunSurviveSaveRecomputeAndLegacyNullStaysVersionOne()
    {
        var d = DuplexCoatingTests.Design(); d = d with
        {
            Duplex = d.Duplex! with { OptimizationVersion = 2 },
            Layers = [d.Layers[0] with { Variable = false }],
            BackLayers = [d.BackLayers![0] with { Variable = false }]
        };
        var optimized = new DesignService().Optimize(d); var run = optimized.Result!.Run;
        Assert.Equal(0, run.Duplex!.IndependentVariables); Assert.Equal(0, run.Duplex.Analytic!.AnalyticJacobians);
        Assert.Equal(d.Layers, optimized.Layers); Assert.Equal(d.BackLayers, optimized.BackLayers);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(optimized, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(DesignService.DuplexAnalyticProtocol, reopened.Result!.Run.DesignProtocol);
            Assert.Equal(run.Duplex.Analytic, reopened.Result.Run.Duplex!.Analytic);
        }
        finally { File.Delete(path); }
        var old = DuplexCoatingTests.Design(); var clone = old.Snapshot();
        Assert.Null(clone.Duplex!.OptimizationVersion); Assert.Equal(old.Fingerprint(), clone.Fingerprint());
        Assert.DoesNotContain("OptimizationVersion", JsonSerializer.Serialize(old, Experiment.JsonOptions));
    }
}
