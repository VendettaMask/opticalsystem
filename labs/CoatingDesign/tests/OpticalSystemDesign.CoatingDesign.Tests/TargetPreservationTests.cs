using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class TargetPreservationTests
{
    [Theory]
    [InlineData("new-fixed-double")]
    [InlineData("new-oblique-single")]
    public void CompletedPeakAndBlockingTargetsKeepTheirFeasibleShapeRestorationPath(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/target-feasible-20261030.json")));
        var d = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Name").GetString() == name).Deserialize<Experiment>(Experiment.JsonOptions)!;
        var service = new DesignService(); var before = service.Evaluate(d); var after = service.Optimize(d);
        AssertTargetsPreserved(before, after); Assert.True(after.Result!.Passed);
        Assert.Equal(DesignService.TargetProtectionProtocol, after.Result.Run.GeneralWork!.TargetProtectionProtocol);
        Assert.InRange(after.Result.Run.Iterations, 1, d.Settings.MaximumIterations);
        for (var i = 0; i < d.Layers.Length; i++) if (!d.Layers[i].Variable) Assert.Equal(d.Layers[i], after.Layers[i]);
    }

    [Theory]
    [InlineData("real-narrowband-control")]
    [InlineData("real-nbf2-96")]
    [InlineData("ir-absorbing-s")]
    public void OptimizingOneTargetCannotIncreaseAnotherTargetsExistingViolation(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/coating-infeasible-20261030.json")));
        var input = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == name)
            .GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        var service = new DesignService(); var before = service.Evaluate(input); var after = service.Optimize(input);
        AssertTargetsPreserved(before, after);
        Assert.True(after.Result!.Merit < before.Result!.Merit);
        Assert.Equal(DesignService.TargetProtectionProtocol, after.Result.Run.GeneralWork!.TargetProtectionProtocol);
        Assert.InRange(after.Result.Run.GeneralWork.TargetProtectionPasses, 1, after.Result.Run.GeneralWork.ConstraintPasses);
        Assert.NotNull(after.Result.Run.TargetViolationCeilings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerationAndSearchAlsoPreserveTheExistingDesignsIndividualTargets(bool search)
    {
        var service = new DesignService(); var d = GeneralCoatingTests.Fixture(633, 1.41, 2.18, .006, 17, OpticalSystemDesign.Core.Coatings.ThinFilmPolarization.P);
        d = d with { Layers = service.BuildNarrowBandPrototype(d, new([3, 3], [1])), Settings = d.Settings with { MaximumIterations = 1, Search = new(2) } };
        var fingerprint = d.Fingerprint(); var before = service.Evaluate(d);
        var after = search ? service.Search(d) : service.Generate(d);
        AssertTargetsPreserved(before, after); Assert.Equal(fingerprint, d.Fingerprint());
    }

    [Fact]
    public async Task InfeasibleReflectanceCanRestartItsDualPathOnceWithoutIncreasingTheBudget()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/coating-infeasible-20261030.json")));
        var d = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(c => c.GetProperty("Input").GetProperty("Name").GetString() == "real-hr-original-target")
            .GetProperty("Input").Deserialize<Experiment>(Experiment.JsonOptions)!;
        d = d with { Settings = d.Settings with { MaximumIterations = 96 } };
        var output = new DesignService().Optimize(d); var work = output.Result!.Run.GeneralWork!;
        Assert.Equal(1, work.QualityRestarts); Assert.InRange(output.Result.Run.Iterations, 1, 96);
        Assert.True(output.Result.Metrics.MinimumReflectance > .960308505009531);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(output, path); var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(work, reopened.Result!.Run.GeneralWork); Assert.Equal(d.Settings, reopened.Settings);
        }
        finally { File.Delete(path); }
    }

    internal static void AssertTargetsPreserved(Experiment before, Experiment after)
    {
        var t = before.Target; var a = before.Result!; var b = after.Result!;
        Assert.True(b.Metrics.PeakTransmittance >= Math.Min(a.Metrics.PeakTransmittance, t.PeakTransmittance),
            $"Peak T regressed: {a.Metrics.PeakTransmittance:G17} -> {b.Metrics.PeakTransmittance:G17}");
        foreach (var band in t.StopBands!)
        {
            double Od(Experiment d) => d.Result!.Spectrum.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).Min(s => s.Power.OpticalDensity);
            Assert.True(Od(after) >= Math.Min(Od(before), t.BlockingOd), $"OD regressed: {Od(before):G17} -> {Od(after):G17}");
        }
        double WidthViolation(Experiment d) => d.Result!.Metrics.FwhmNanometers is { } w ? Math.Max(0, Math.Abs(w - t.FwhmNm) - t.FwhmToleranceNm) / t.FwhmNm : 1;
        double CenterViolation(Experiment d) => Math.Max(0, Math.Abs(d.Result!.Metrics.PeakWavelengthNanometers - t.CenterNm) - t.CenterToleranceNm) / t.FwhmNm;
        Assert.True(WidthViolation(after) <= WidthViolation(before)); Assert.True(CenterViolation(after) <= CenterViolation(before));
    }

    [Theory]
    [InlineData("Damped Least Squares")]
    [InlineData("Nelder-Mead")]
    [InlineData("Coordinate Pattern Search")]
    public void ProtectionUsesTheSelectedSharedOptimizerAndKeepsDeclaredFixedFilms(string optimizer)
    {
        var service = new DesignService(); var d = GeneralCoatingTests.Fixture(780, 1.4, 2.2, .005, 23, OpticalSystemDesign.Core.Coatings.ThinFilmPolarization.Unpolarized);
        var layers = service.BuildNarrowBandPrototype(d, new([3, 3], [1]));
        d = d with { Layers = layers.Select((l, i) => l with { Variable = i % 3 != 0 }).ToArray(), Settings = d.Settings with { Optimizer = optimizer, MaximumIterations = 8 } };
        var fingerprint = d.Fingerprint(); var before = service.Evaluate(d); var after = service.Optimize(d);
        AssertTargetsPreserved(before, after); Assert.Equal(optimizer, after.Result!.Run.Algorithm);
        Assert.InRange(after.Result.Run.Iterations, 0, 8); Assert.Equal(fingerprint, d.Fingerprint());
        for (var i = 0; i < d.Layers.Length; i++) if (!d.Layers[i].Variable) Assert.Equal(d.Layers[i], after.Layers[i]);
    }
}
