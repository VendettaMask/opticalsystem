using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class GeneralCoatingTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    public void InterfaceCanonicalizationAllowsMatchingAtTheLayerLimitAndStillProtectsFixedLayers(int cavities, int mirror)
    {
        var service = new DesignService(); var input = Fixture(700, 1.43, 2.15, .01, 0, ThinFilmPolarization.Unpolarized);
        input = input with { Target = input.Target with { Structure = new(cavities) } };
        var layers = service.BuildNarrowBandPrototype(input, new(Enumerable.Repeat(mirror, cavities + 1).ToArray(), Enumerable.Repeat(1, cavities).ToArray(), false));
        input = input with { Layers = layers, Target = input.Target with { MaximumLayers = layers.Length } };
        var trials = service.MatchIncidentMedium(input);
        Assert.NotEmpty(trials); Assert.True(trials.Length > 1);
        Assert.All(trials, t =>
        {
            Assert.Equal(layers.Length, t.Layers.Length);
            Assert.Equal(layers.Skip(1), t.Layers.Skip(1));
            Assert.All(t.Layers, l => Assert.InRange(l.ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm));
            Assert.True(t.After.Transmittance >= t.Before.Transmittance - 1e-12);
        });
        Assert.Empty(service.MatchIncidentMedium(input with { Layers = layers.Select((l, i) => l with { Variable = i != 0 }).ToArray() }));
    }

    [Theory]
    [InlineData(450, 0, ThinFilmPolarization.S, 0)]
    [InlineData(800, 35, ThinFilmPolarization.P, .02)]
    [InlineData(1300, 25, ThinFilmPolarization.Unpolarized, .01)]
    public void MatchingKeepsDistinctPhaseStartsAndOptimizesRealTransmissionAcrossMaterialsAndAngles(double wave, double angle, ThinFilmPolarization pol, double k)
    {
        var service = new DesignService(); var input = Fixture(wave, 1.43, 2.15, k, angle, pol);
        input = input with { Layers = service.BuildNarrowBandPrototype(input, new([5, 5], [1])) };
        var fingerprint = input.Fingerprint(); var trials = service.MatchIncidentMedium(input);
        Assert.Equal(24, trials.Length);
        var materials = input.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        Assert.All(trials, t =>
        {
            var start = new CoherentThinFilmSolver(materials[input.IncidentId], materials[input.SubstrateId],
                t.InitialMatchingLayers!.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm))).Evaluate(wave, angle, pol);
            Assert.True(t.After.Transmittance >= start.Transmittance - 1e-12);
        });
        Assert.All(trials, t => Assert.Equal(DesignService.GeneralCoatingProtocol, t.Run.DesignProtocol));
        Assert.True(trials.Sum(t => t.Run.GeneralWork!.AnalyticJacobians) > 0);
        Assert.Equal(fingerprint, input.Fingerprint());
    }

    [Theory]
    [InlineData(450)]
    [InlineData(950)]
    [InlineData(1500)]
    public void PhaseOptimizationPreservesAnAlreadyFeasibleDesignAndUsesTheOriginalDenseChecks(double wave)
    {
        var service = new DesignService(); var input = Fixture(wave, 1.45, 2.1, 0, 0, ThinFilmPolarization.Unpolarized);
        input = input with { Layers = service.BuildNarrowBandPrototype(input, new([7, 7], [1])) };
        var measured = service.Evaluate(input).Result!.Metrics;
        Assert.NotNull(measured.FwhmNanometers);
        input = input with
        {
            Target = input.Target with
            {
                FwhmNm = measured.FwhmNanometers!.Value,
                FwhmToleranceNm = measured.FwhmNanometers.Value / 10,
                PeakTransmittance = measured.PeakTransmittance / 2,
                BlockingOd = 0
            }
        };
        var before = service.Evaluate(input); Assert.True(before.Result!.Passed);
        var output = service.Optimize(input);
        Assert.True(output.Result!.Passed);
        Assert.Equal(output.Result.SamplingConverged && output.Result.Checks.All(c => c.Passed), output.Result.Passed);
        Assert.True(output.Result.Run.GeneralWork!.AnalyticJacobians > 0);
        Assert.Equal(2, output.Result.Run.GeneralWork.DenseValidations);
        Assert.Equal(output.Layers, output.Candidates!.Last().Layers);
        Assert.All(output.Layers, l => Assert.InRange(l.ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm));
    }

    [Fact]
    public async Task RenamingMaterialIdsDoesNotChangeCompetitionAndSavedRunsRetainTheirWorkAndProtocol()
    {
        var service = new DesignService(); var input = Fixture(780, 1.38, 2.3, .006, 10, ThinFilmPolarization.S);
        input = input with { Target = input.Target with { MaximumLayers = 11 }, Settings = input.Settings with { MaximumIterations = 1 } };
        var map = input.Materials.Select((m, i) => (m.Id, New: "renamed-" + i)).ToDictionary(p => p.Id, p => p.New);
        var renamed = input with
        {
            IncidentId = map[input.IncidentId],
            SubstrateId = map[input.SubstrateId],
            LowId = map[input.LowId],
            HighId = map[input.HighId],
            Materials = input.Materials.Select(m => m with { Id = map[m.Id], Name = "cosmetic " + map[m.Id] }).ToArray()
        };
        var a = service.Generate(input); var b = service.Generate(renamed);
        Assert.Equal(a.Layers.Select(l => l.ThicknessNm), b.Layers.Select(l => l.ThicknessNm));
        Assert.Equal(a.Result!.Spectrum, b.Result!.Spectrum);
        Assert.Equal(a.Result.Run.GeneralWork, b.Result.Run.GeneralWork);
        Assert.All(a.Candidates!, c => Assert.False(c.Passed));
        var path = Path.Combine(Path.GetTempPath(), "coating-general-" + Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(a, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(a.Fingerprint(), reopened.Fingerprint()); Assert.Equal(a.Result.Spectrum, reopened.Result!.Spectrum);
            Assert.Equal(JsonSerializer.Serialize(a.Result.Run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result.Run, Experiment.JsonOptions));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SimultaneouslyScalingWavelengthThicknessAndTolerancesPreservesConstantIndexOptimization()
    {
        var service = new DesignService();
        Experiment Input(double wave)
        {
            var d = Fixture(wave, 1.43, 2.15, .004, 20, ThinFilmPolarization.Unpolarized);
            return d with { Layers = service.BuildNarrowBandPrototype(d, new([4, 6], [2])) };
        }
        var a = service.Optimize(Input(450)); var b = service.Optimize(Input(1350));
        CoherentThinFilmSolver Solver(Experiment d)
        {
            var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            return new(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        }
        foreach (var ratio in new[] { .9, .97, 1, 1.03, 1.1 })
        {
            var pa = Solver(a).Evaluate(450 * ratio, 20, ThinFilmPolarization.Unpolarized);
            var pb = Solver(b).Evaluate(1350 * ratio, 20, ThinFilmPolarization.Unpolarized);
            Assert.InRange(Math.Abs(pa.Transmittance - pb.Transmittance), 0, 1e-7);
            Assert.InRange(Math.Abs(pa.Reflectance - pb.Reflectance), 0, 1e-7);
        }
        Assert.Equal(a.Result!.Passed, b.Result!.Passed);
    }

    [Theory]
    [InlineData(DesignKind.Antireflection)]
    [InlineData(DesignKind.HighReflector)]
    public void GeneralProtocolAlsoUsesAnalyticConstraintsForReflectanceTargets(DesignKind kind)
    {
        var input = Examples.Create(kind);
        input = input with { Settings = input.Settings with { DesignProtocolVersion = 4, MaximumIterations = 24 } };
        var service = new DesignService(); var output = service.Optimize(service.Generate(input));
        Assert.Equal(DesignService.GeneralCoatingProtocol, output.Result!.Run.DesignProtocol);
        Assert.True(output.Result.Run.GeneralWork!.AnalyticJacobians > 0);
        Assert.Equal(output.Result.SamplingConverged && output.Result.Checks.All(c => c.Passed), output.Result.Passed);
        Assert.All(output.Layers, l => Assert.InRange(l.ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm));
        if (kind == DesignKind.Antireflection) Assert.True(output.Result.Passed);
    }

    [Fact]
    public void ReflectanceSearchRanksFeasibleCandidatesByActualOpticalQuality()
    {
        var d = Examples.Create(DesignKind.Antireflection);
        d = d with { Settings = d.Settings with { DesignProtocolVersion = 4, MaximumIterations = 24, Search = new(3) } };
        var result = new DesignService().Search(d);
        Assert.True(result.Result!.Passed);
        Assert.Equal(3, result.Candidates!.Length);
        Assert.True(result.Result.Run.GeneralWork!.DenseValidations >= 6);
        Assert.All(result.Candidates, candidate =>
        {
            var actual = new DesignService().Evaluate(d with { Layers = candidate.Layers });
            Assert.Equal(actual.Result!.Passed, candidate.Passed);
        });
    }

    [Fact]
    public void PhaseRoundTripAtPhysicalBoundsCannotRejectAnOtherwiseLegalMatchingCandidate()
    {
        var d = Fixture(450, 1.38, 2.3, 0, 0, ThinFilmPolarization.S);
        d = d with { Settings = d.Settings with { MaximumIterations = 2 } };
        var generated = new DesignService().Generate(d);
        Assert.All(generated.Layers, l => Assert.InRange(l.ThicknessNm, d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm));
        Assert.NotEmpty(generated.Result!.Run.NarrowBandCompetition!.MatchingTrials);
    }

    [Fact]
    public void CancellationStopsGenericCompetitionWithoutChangingTheInput()
    {
        var input = Fixture(600, 1.4, 2.2, .01, 20, ThinFilmPolarization.P); var fingerprint = input.Fingerprint();
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new DesignService().Generate(input, new Immediate(p =>
        { if (p.Stage.StartsWith("通用结构约束竞争", StringComparison.Ordinal)) cancellation.Cancel(); }), cancellation.Token));
        Assert.Equal(fingerprint, input.Fingerprint()); Assert.Null(input.Result);
    }

    internal static Experiment Fixture(double wave, double nl, double nh, double k, double angle, ThinFilmPolarization pol)
    {
        var input = Examples.Create(DesignKind.NarrowBand);
        var low = MaterialLibrary.Capture(new ConstantIndexMaterial("generic low", nl, k / 5), "synthetic generic regression", wave / 2, wave * 2);
        var high = MaterialLibrary.Capture(new ConstantIndexMaterial("generic high", nh, k), "synthetic generic regression", wave / 2, wave * 2);
        var substrate = MaterialLibrary.Capture(new ConstantIndexMaterial("generic substrate", 1.52), "synthetic generic regression", wave / 2, wave * 2);
        return input with
        {
            LowId = low.Id,
            HighId = high.Id,
            SubstrateId = substrate.Id,
            Materials = new[] { input.Materials.Single(m => m.Id == input.IncidentId), low, high, substrate },
            Target = input.Target with
            {
                CenterNm = wave,
                MinimumNm = wave * .85,
                MaximumNm = wave * 1.15,
                FwhmNm = wave * .03,
                CenterToleranceNm = wave * .001,
                FwhmToleranceNm = wave * .003,
                AngleDegrees = angle,
                Polarization = pol,
                StopBands = [new(wave * .88, wave * .92), new(wave * 1.08, wave * 1.12)]
            },
            Settings = input.Settings with { DesignProtocolVersion = 4, MinimumThicknessNm = wave * .002, MaximumThicknessNm = wave * 2, MaximumIterations = 12 }
        };
    }
    private sealed class Immediate(Action<DesignProgress> action) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => action(value); }
}
