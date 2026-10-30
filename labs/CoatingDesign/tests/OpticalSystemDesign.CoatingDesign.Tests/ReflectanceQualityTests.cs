using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using System.Text.Json;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class ReflectanceQualityTests
{
    [Theory]
    [InlineData(450, 1.38, 2.3, 0, 0, ThinFilmPolarization.S, DesignKind.Antireflection)]
    [InlineData(800, 1.32, 2.15, .002, 20, ThinFilmPolarization.P, DesignKind.Antireflection)]
    [InlineData(1300, 1.4, 2.25, .008, 35, ThinFilmPolarization.Unpolarized, DesignKind.Antireflection)]
    [InlineData(520, 1.4, 2.3, 0, 0, ThinFilmPolarization.S, DesignKind.HighReflector)]
    [InlineData(940, 1.45, 2.15, .002, 35, ThinFilmPolarization.P, DesignKind.HighReflector)]
    [InlineData(1550, 1.4, 2.25, .01, 25, ThinFilmPolarization.Unpolarized, DesignKind.HighReflector)]
    public void FeasibleReflectanceTargetsContinueImprovingWorstBandQuality(double wave, double nl, double nh, double k,
        double angle, ThinFilmPolarization pol, DesignKind kind)
    {
        var d = Fixture(wave, nl, nh, k, angle, pol, kind);
        var fingerprint = d.Fingerprint(); var service = new DesignService(); var before = service.Evaluate(d);
        Assert.True(before.Result!.Passed);
        var after = service.Optimize(d); Assert.True(after.Result!.Passed);
        Assert.True(Loss(after) < Loss(before), $"Worst loss did not improve: {Loss(before):G17} -> {Loss(after):G17}");
        Assert.InRange(after.Result.Run.Iterations, 1, d.Settings.MaximumIterations);
        Assert.Equal(fingerprint, d.Fingerprint());
        Assert.Equal(JsonSerializer.Serialize(d.Materials, Experiment.JsonOptions), JsonSerializer.Serialize(after.Materials, Experiment.JsonOptions));
        Assert.Equal(DesignService.ReflectanceQualityProtocol, after.Result.Run.GeneralWork!.QualityProtocol);
        Assert.True(after.Result.Run.GeneralWork.QualityPasses > 0);
        Assert.InRange(after.Result.Run.GeneralWork.QualityIterations, 1, after.Result.Run.Iterations);
        Assert.Equal(before.Result.Checks.Select(c => c.Requirement), after.Result.Checks.Select(c => c.Requirement));
        Assert.Equal(after.Result.Checks.All(c => c.Passed) && after.Result.SamplingConverged, after.Result.Passed);
    }

    internal static double Loss(Experiment d) => d.Target.Kind == DesignKind.Antireflection
        ? d.Result!.Metrics.MaximumReflectance : 1 - d.Result!.Metrics.MinimumReflectance;

    [Theory]
    [InlineData("Damped Least Squares")]
    [InlineData("Nelder-Mead")]
    [InlineData("Coordinate Pattern Search")]
    public async Task QualityPhaseUsesTheSelectedSharedOptimizerAndRetainsItsWorkWhenReopened(string optimizer)
    {
        var d = Fixture(780, 1.35, 2.1, .006, 20, ThinFilmPolarization.P, DesignKind.Antireflection);
        d = d with { Settings = d.Settings with { Optimizer = optimizer } };
        var service = new DesignService(); var before = service.Evaluate(d); var after = service.Optimize(d);
        Assert.True(after.Result!.Passed); Assert.True(Loss(after) <= Loss(before));
        Assert.Equal(optimizer, after.Result.Run.Algorithm);
        Assert.Equal(DesignService.ReflectanceQualityProtocol, after.Result.Run.GeneralWork!.QualityProtocol);
        Assert.InRange(after.Result.Run.Iterations, 0, d.Settings.MaximumIterations);
        Assert.Equal(2, after.Result.Run.GeneralWork.DenseValidations);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(after, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(after.Result.Run.GeneralWork, reopened.Result!.Run.GeneralWork);
            Assert.Equal(after.Layers, reopened.Layers); Assert.Equal(after.Result.Spectrum, reopened.Result.Spectrum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FeasibilityAndQualityShareOneIterationBudgetAndKeepTheOriginalRequirement()
    {
        var d = Fixture(550, Math.Sqrt(1.52), 2.2, 0, 0, ThinFilmPolarization.Unpolarized, DesignKind.Antireflection);
        d = d with
        {
            Layers = d.Layers.Select(l => l with { ThicknessNm = l.ThicknessNm * .5 / .9 }).ToArray(),
            Target = d.Target with { Reflectance = .005 },
            Settings = d.Settings with { MaximumIterations = 48 }
        };
        var service = new DesignService(); Assert.False(service.Evaluate(d).Result!.Passed);
        var output = service.Optimize(d); Assert.True(output.Result!.Passed);
        Assert.True(output.Result.Run.GeneralWork!.QualityIterations > 0);
        Assert.True(output.Result.Run.GeneralWork.ConstraintPasses > output.Result.Run.GeneralWork.QualityPasses);
        Assert.InRange(output.Result.Run.Iterations, 1, d.Settings.MaximumIterations);
        Assert.Equal(d.Target, output.Target);
    }

    [Fact]
    public void FixedFilmsRenamedMaterialsAndConstantIndexScaleAreHandledByTheSameRules()
    {
        var d = Fixture(520, 1.4, 2.3, .003, 15, ThinFilmPolarization.S, DesignKind.HighReflector);
        d = d with { Layers = d.Layers.Select((l, i) => l with { Variable = i % 3 != 0 }).ToArray() };
        var map = d.Materials.Select((m, i) => (m.Id, New: "cosmetic-" + i)).ToDictionary(p => p.Id, p => p.New);
        var renamed = d with
        {
            Materials = d.Materials.Select(m => m with { Id = map[m.Id], Name = "changed label" }).ToArray(),
            IncidentId = map[d.IncidentId],
            SubstrateId = map[d.SubstrateId],
            LowId = map[d.LowId],
            HighId = map[d.HighId],
            Layers = d.Layers.Select(l => l with { MaterialId = map[l.MaterialId] }).ToArray()
        };
        var service = new DesignService(); var a = service.Optimize(d); var b = service.Optimize(renamed);
        Assert.Equal(a.Layers.Select(l => l.ThicknessNm), b.Layers.Select(l => l.ThicknessNm));
        Assert.Equal(a.Result!.Spectrum, b.Result!.Spectrum); Assert.Equal(a.Result.Run.GeneralWork, b.Result.Run.GeneralWork);
        for (var i = 0; i < d.Layers.Length; i++) if (!d.Layers[i].Variable) Assert.Equal(d.Layers[i], a.Layers[i]);
        var scaled = d with
        {
            Materials = d.Materials.Select(m => m with { MinimumNm = 3 * m.MinimumNm, MaximumNm = 3 * m.MaximumNm }).ToArray(),
            Layers = d.Layers.Select(l => l with { ThicknessNm = 3 * l.ThicknessNm }).ToArray(),
            Target = d.Target with { CenterNm = 3 * d.Target.CenterNm, MinimumNm = 3 * d.Target.MinimumNm, MaximumNm = 3 * d.Target.MaximumNm },
            Settings = d.Settings with { MinimumThicknessNm = 3 * d.Settings.MinimumThicknessNm, MaximumThicknessNm = 3 * d.Settings.MaximumThicknessNm }
        };
        var c = service.Optimize(scaled);
        Assert.InRange(Math.Abs(Loss(c) - Loss(a)), 0, 1e-9);
        Assert.Equal(a.Result.Passed, c.Result!.Passed);
    }

    [Fact]
    public void AllFixedFilmsRemainUntouchedAndQualityCancellationCannotMutateTheInput()
    {
        var d = Fixture(700, 1.4, 2.2, .01, 25, ThinFilmPolarization.Unpolarized, DesignKind.HighReflector);
        var fixedInput = d with { Layers = d.Layers.Select(l => l with { Variable = false }).ToArray() };
        var service = new DesignService(); var output = service.Optimize(fixedInput); Assert.Equal(fixedInput.Layers, output.Layers);
        Assert.Equal(0, output.Result!.Run.Iterations);
        var fingerprint = d.Fingerprint(); using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => service.Optimize(d, new Immediate(p =>
        { if (p.Stage == "通用反射率质量优化") cancellation.Cancel(); }), cancellation.Token));
        Assert.Equal(fingerprint, d.Fingerprint()); Assert.Null(d.Result);
    }

    private sealed class Immediate(Action<DesignProgress> action) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => action(value); }

    [Fact]
    public void AnExactPhysicalLimitAndASingleIterationCannotInventQualityOrExceedTheBudget()
    {
        var d = Fixture(600, 1, 2, 0, 0, ThinFilmPolarization.S, DesignKind.Antireflection);
        d = d with
        {
            Materials = d.Materials.Select(m => m.Id == d.SubstrateId
            ? MaterialLibrary.Capture(new ConstantIndexMaterial(m.Name, 1), m.Source, m.MinimumNm, m.MaximumNm) with { Id = m.Id } : m).ToArray()
        };
        var service = new DesignService(); var result = service.Optimize(d);
        Assert.True(result.Result!.Passed); Assert.Equal(0, Loss(result)); Assert.Equal(d.Layers, result.Layers);
        Assert.Equal(0, result.Result.Run.Iterations); Assert.Equal("ReflectancePhysicalLimit", result.Result.Run.StopReason);
        var shortRun = Fixture(450, 1.38, 2.3, .003, 15, ThinFilmPolarization.P, DesignKind.Antireflection);
        shortRun = shortRun with { Settings = shortRun.Settings with { MaximumIterations = 1 } };
        var before = service.Evaluate(shortRun); var after = service.Optimize(shortRun);
        Assert.True(after.Result!.Passed); Assert.True(Loss(after) <= Loss(before)); Assert.InRange(after.Result.Run.Iterations, 0, 1);
    }

    internal static Experiment Fixture(double wave, double nl, double nh, double k, double angle,
        ThinFilmPolarization pol, DesignKind kind)
    {
        var d = Examples.Create(kind);
        MaterialSnapshot Capture(string name, double n, double extinction = 0) => MaterialLibrary.Capture(
            new ConstantIndexMaterial(name, n, extinction), "Synthetic cross-specification regression, not measured", wave / 2, wave * 2);
        var inc = Capture("incident", 1); var sub = Capture("substrate", 1.52);
        var low = Capture("low", nl, k / 5); var high = Capture("high", nh, k);
        double Quarter(MaterialSnapshot m) => CoherentThinFilmSolver.QuarterWaveThickness(m.Resolve(new()), inc.Resolve(new()), wave, angle);
        var layers = kind == DesignKind.Antireflection ? new[] { new FilmLayer(low.Id, .9 * Quarter(low)) }
            : Enumerable.Range(0, 16).Select(i => new FilmLayer(i % 2 == 0 ? high.Id : low.Id,
                Quarter(i % 2 == 0 ? high : low) * (i % 2 == 0 ? .94 : 1.04))).ToArray();
        return d with
        {
            Materials = [inc, sub, low, high],
            IncidentId = inc.Id,
            SubstrateId = sub.Id,
            LowId = low.Id,
            HighId = high.Id,
            Layers = layers,
            Target = d.Target with
            {
                CenterNm = wave,
                MinimumNm = .95 * wave,
                MaximumNm = 1.05 * wave,
                AngleDegrees = angle,
                Polarization = pol,
                Reflectance = kind == DesignKind.Antireflection ? .1 : .8,
                MaximumLayers = 32
            },
            Settings = d.Settings with
            {
                DesignProtocolVersion = 4,
                MaximumIterations = 24,
                MinimumThicknessNm = wave * .001,
                MaximumThicknessNm = wave * 2,
                PreviewIntervals = 100,
                OptimizationIntervals = 40
            }
        };
    }
}
