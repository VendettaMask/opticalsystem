using System.Text.Json;
using OptilandWorkbench.CoatingDesign.Engine;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.CoatingDesign.Tests;

public sealed class WorkflowTests
{
    private readonly DesignService _service = new();

    [Theory]
    [InlineData(DesignKind.Antireflection)] [InlineData(DesignKind.HighReflector)] [InlineData(DesignKind.NarrowBand)]
    public void RealExamplesGenerateOptimizeAndVerifyWithoutChangingInput(DesignKind kind)
    {
        var input = Examples.Create(kind);
        var hash = input.Fingerprint();
        var seed = _service.Generate(input);
        var optimized = _service.Optimize(seed with { Settings = seed.Settings with { MaximumIterations = 12 }, Result = null });
        Assert.Equal(hash, input.Fingerprint()); Assert.Empty(input.Layers);
        Assert.NotEmpty(seed.Layers);
        Assert.True(optimized.Result!.SamplingConverged);
        Assert.Equal(CoherentThinFilmSolver.Version, optimized.Result.Run.SolverVersion);
        Assert.True(optimized.Result.Run.Evaluations > 0);
        Assert.Equal(optimized.Result.Checks.All(c => c.Passed), optimized.Result.Passed);
        Assert.All(optimized.Result.Spectrum, x => Assert.InRange(x.Power.Absorptance, -2e-9, 1));
        Assert.Contains(optimized.Result.Spectrum, x => x.Power.Absorptance > 1e-4); // real k is retained
    }

    [Fact]
    public void FixedLayersAndThicknessBoundsArePreserved()
    {
        var d = _service.Generate(Examples.Create(DesignKind.Antireflection));
        var layers = d.Layers.ToArray(); layers[0] = layers[0] with { Variable = false };
        var output = _service.Optimize(d with { Layers = layers, Result = null, Settings = d.Settings with { MaximumIterations = 8 } });
        Assert.Equal(layers[0], output.Layers[0]);
        Assert.All(output.Layers, l => Assert.InRange(l.ThicknessNm, output.Settings.MinimumThicknessNm, output.Settings.MaximumThicknessNm));
    }

    [Fact]
    public void ImpossibleGoalReportsSpecificFailureInsteadOfOptimizerSuccess()
    {
        var d = Examples.Create(DesignKind.HighReflector);
        d = d with { Target = d.Target with { Reflectance = 1, MaximumLayers = 2 }, Settings = d.Settings with { MaximumIterations = 2 } };
        var result = _service.Optimize(_service.Generate(d)).Result!;
        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Name.Contains("最低 R", StringComparison.Ordinal));
        Assert.NotEmpty(result.Run.StopReason);
    }

    [Fact]
    public void InvalidInputsAndOutOfRangeMaterialsAreNotSilentlyCoerced()
    {
        var d = Examples.Create(DesignKind.Antireflection);
        Assert.Throws<ArgumentException>(() => _service.Generate(d with { Target = d.Target with { AngleDegrees = 90 } }));
        Assert.Throws<ArgumentException>(() => _service.Generate(d with { Target = d.Target with { MaximumNm = 2000 } }));
        Assert.Throws<ArgumentException>(() => _service.Generate(d with { Settings = d.Settings with { Optimizer = "BFGS" } }));
        Assert.Throws<ArgumentException>(() => _service.Generate(d with { Target = d.Target with { BlockingOd = 13 } }));
        Assert.Throws<InvalidDataException>(() => (d with { SchemaVersion = 999 }).Validate());
        var incomplete = MaterialLibrary.Capture(new ConstantIndexMaterial("incomplete", 1.7), "test", 300, 800);
        incomplete.Model.Numbers.Remove("index");
        Assert.Throws<InvalidDataException>(() => incomplete.Resolve(new MaterialRegistry()));
        Assert.Throws<InvalidDataException>(() => (d with { Materials = d.Materials.Append(incomplete).ToArray() }).Validate());
    }

    [Fact]
    public void NarrowBandDenseSamplingConvergesAgainstIndependentDenserGrid()
    {
        var d = _service.Generate(Examples.Create(DesignKind.NarrowBand));
        var registry = new MaterialRegistry(); var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(registry));
        var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var dense = ThinFilmSpectrum.Sample(solver, ThinFilmSpectrum.Grid(d.Target.CenterNm - 2 * d.Target.FwhmNm, d.Target.CenterNm + 2 * d.Target.FwhmNm, 24000),
            d.Target.AngleDegrees, d.Target.Polarization);
        var metrics = ThinFilmSpectrum.Measure(dense);
        Assert.True(d.Result!.SamplingConverged);
        Assert.InRange(Math.Abs(metrics.FwhmNanometers!.Value - d.Result.Metrics.FwhmNanometers!.Value), 0, 0.002);
        Assert.InRange(Math.Abs(metrics.PeakTransmittance - d.Result.Metrics.PeakTransmittance), 0, 1e-6);
    }

    [Fact]
    public void OdUsesRawLogTransmissionAndDisplayDoesNotAffectAcceptance()
    {
        var p = new CoherentThinFilmSolver(new AirMaterial(), new ConstantIndexMaterial("glass", 1.5),
            [new(new ConstantIndexMaterial("absorber", 0.3, 4), 100000)]).Evaluate(550, 0, ThinFilmPolarization.S);
        Assert.Equal(0, p.Transmittance); Assert.True(double.IsFinite(p.LogTransmittance));
        Assert.True(p.OpticalDensity > 12);
        Assert.True(p.LogTransmittance < Math.Log(double.Epsilon));
    }

    [Fact]
    public void PreCancelledGenerationOptimizationAndToleranceStop()
    {
        var d = _service.Generate(Examples.Create(DesignKind.Antireflection));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _service.Generate(d, token: cts.Token));
        Assert.Throws<OperationCanceledException>(() => _service.Optimize(d, token: cts.Token));
        Assert.Throws<OperationCanceledException>(() => _service.Tolerance(d, token: cts.Token));
    }

    [Fact]
    public void CancellationDuringOptimizationStopsAtEvaluationBoundary()
    {
        var d = _service.Generate(Examples.Create(DesignKind.HighReflector));
        using var cts = new CancellationTokenSource();
        var progress = new ImmediateProgress(p => { if (p.Stage == "优化厚度") cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => _service.Optimize(d, progress, cts.Token));
        Assert.NotNull(d.Result);
    }

    [Fact]
    public async Task OldTaskCannotOverwriteEditedOrSwitchedExperimentEvenIfItIgnoresCancellation()
    {
        using var session = new ExperimentSession(Examples.Create(DesignKind.Antireflection));
        var completion = new TaskCompletionSource<Experiment>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = session.Current;
        var work = session.RunAsync((_, _) => completion.Task);
        var next = Examples.Create(DesignKind.HighReflector); session.Replace(next);
        completion.SetResult(old with { Name = "旧任务结果" });
        Assert.False(await work); Assert.Equal(next.Id, session.Current.Id); Assert.Equal(next.Name, session.Current.Name);
    }

    [Fact]
    public async Task CancelAndParameterEditInvalidatePendingTask()
    {
        using var session = new ExperimentSession(Examples.Create(DesignKind.Antireflection));
        foreach (var cancel in new[] { true, false })
        {
            var completion = new TaskCompletionSource<Experiment>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = session.Current;
            var work = session.RunAsync((_, _) => completion.Task);
            if (cancel) session.Cancel(); else session.Invalidate();
            completion.SetResult(old with { Name = "不应写入" });
            Assert.False(await work); Assert.NotEqual("不应写入", session.Current.Name);
        }
    }

    [Fact]
    public async Task SaveOpenRecalculatesIdenticallyAndExportPreservesPrecision()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coating-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var d = _service.Generate(Examples.Create(DesignKind.Antireflection));
            var path = Path.Combine(directory, "design.coating.json");
            await ExperimentStore.SaveAsync(d, path);
            var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(d.Fingerprint(), reopened.Fingerprint());
            Assert.Equal(d.Result!.Spectrum, reopened.Result!.Spectrum);
            await ExperimentStore.ExportAsync(d, directory);
            Assert.True(File.Exists(Path.Combine(directory, "layers.csv")));
            Assert.Equal(d.Result.Spectrum.Length + 1, File.ReadAllLines(Path.Combine(directory, "spectrum.csv")).Length);
            var stale = d with { Target = d.Target with { AngleDegrees = 20 } };
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentStore.SaveAsync(stale, path));
            Assert.Equal(d.Fingerprint(), (await ExperimentStore.OpenAsync(path)).Fingerprint());
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExperimentStore.SaveAsync(d with { Name = "cancel" }, path, cts.Token));
            Assert.Equal(d.Name, (await ExperimentStore.OpenAsync(path)).Name);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ToleranceUsesSameSolverAndDeterministicSharedSampler()
    {
        var d = _service.Generate(Examples.Create(DesignKind.Antireflection));
        d = d with { Settings = d.Settings with { ToleranceTrials = 3 }, Result = null };
        Assert.Equal(_service.Tolerance(d), _service.Tolerance(d));
    }

    [Fact]
    public void InputMaterialSnapshotsAreDetachedFromCallerArrays()
    {
        var d = Examples.Create(DesignKind.Antireflection);
        using var session = new ExperimentSession(d);
        var old = session.Current.Fingerprint();
        d.Materials[0].Model.Numbers["arbitrary"] = 9;
        Assert.Equal(old, session.Current.Fingerprint());
    }

    [Fact]
    public void AbsorbingSubstrateFresnelAndThickFilmLimitAreIndependentAnalyticChecks()
    {
        var material = new ConstantIndexMaterial("metal", 0.2, 4);
        var expected = (0.8 * 0.8 + 16) / (1.2 * 1.2 + 16);
        var bare = new CoherentThinFilmSolver(new AirMaterial(), material, []).Evaluate(550, 0, ThinFilmPolarization.S);
        var thick = new CoherentThinFilmSolver(new AirMaterial(), new ConstantIndexMaterial("glass", 1.5), [new(material, 10000)]).Evaluate(550, 0, ThinFilmPolarization.S);
        Assert.Equal(expected, bare.Reflectance, 13); Assert.Equal(1 - expected, bare.Transmittance, 13);
        Assert.Equal(expected, thick.Reflectance, 13); Assert.Equal(1 - expected, thick.Absorptance, 13);
    }

    private sealed class ImmediateProgress(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
}
